using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlashText.Models;
using SlashText.Services;

internal static class RestoreRecoveryChecks
{
    private static int _checks;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Restore recovery: " + label); _checks++; }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string> Hashes(string data) => Directory.GetFiles(data, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(data, path).StartsWith("Backups" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !Path.GetRelativePath(data, path).StartsWith('.'))
        .ToDictionary(path => Path.GetRelativePath(data, path), Hash);
    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) => before.Count == after.Count && before.All(p => after.GetValueOrDefault(p.Key) == p.Value);
    private static AppDataEnvironment EnvironmentFor(string root) => new(DistributionMode.Portable, Path.Combine(root, "exe"), Path.Combine(root, "local"), true);
    private static BackupService Service(string data) => new(Path.Combine(data, "Backups"),
        new[] { "snippets.md", "settings.json", "usage.json", "capture-history.json" }.Select(n => Path.Combine(data, n)).ToArray(), Path.Combine(data, "assets"), data, Path.Combine(data, "capture-emotes"));
    private static void Seed(string data, string text)
    {
        Directory.CreateDirectory(data);
        new SnippetMarkdownRepository(Path.Combine(data, "snippets.md"), Path.Combine(data, "Backups"))
            .SaveAsync([new Snippet { Id = Guid.Parse("77926bbe-0c96-4188-b17c-92dbf48030aa"), Trigger = "/restore", Name = text, Content = "Fictitious " + text }]).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(new AppSettings { Theme = text == "before" ? "System" : "Dark", Language = "pt-BR" }, Json));
        File.WriteAllText(Path.Combine(data, "usage.json"), JsonSerializer.Serialize(new UsageSnapshot { QuickAccent = new() { Count = text == "before" ? 1 : 9 } }, Json));
        File.WriteAllText(Path.Combine(data, "capture-history.json"), JsonSerializer.Serialize(new[] { new CaptureRecord { Id = text, Type = "region", CreatedAt = DateTimeOffset.Parse("2026-10-13T00:00:00Z"), Width = 2, Height = 2 } }, Json));
        Directory.CreateDirectory(Path.Combine(data, "assets", "empty"));
        using var bitmap = new Bitmap(3, 2); bitmap.SetPixel(0, 0, text == "before" ? Color.Blue : Color.Red);
        var png = Path.Combine(data, "assets", "fixture.png"); bitmap.Save(png, ImageFormat.Png);
        new CaptureStampCatalog(Path.Combine(data, "capture-emotes")).Import(png, "Fictitious " + text);
    }
    private static (string Data, Dictionary<string, string> Before, string Archive) Fixture(string root)
    {
        AppPaths.Initialize(EnvironmentFor(root));
        Seed(AppPaths.DataDirectory, "before");
        var incoming = Path.Combine(root, "incoming"); Seed(incoming, "after");
        var source = Service(incoming).CreateManualSnapshot(); var archive = Path.Combine(root, "incoming.zip"); File.Copy(source, archive);
        return (AppPaths.DataDirectory, Hashes(AppPaths.DataDirectory), archive);
    }

    internal static void Worker(string[] args)
    {
        var phase = args[1]; var index = int.Parse(args[2]); var root = args[3];
        AppPaths.Initialize(EnvironmentFor(root));
        var service = Service(AppPaths.DataDirectory);
        service.RestoreCheckpoint = (current, step) =>
        {
            if (phase.StartsWith("rollback-", StringComparison.Ordinal) || phase == "after-rollback")
            {
                if (current == "after-apply" && step == 4) throw new IOException("controlled original failure");
            }
            if (current == phase && step == index) System.Environment.Exit(73);
        };
        service.RestoreSnapshot(Path.Combine(root, "incoming.zip"));
        System.Environment.Exit(74);
    }

    private static async Task Interrupt(string root, string phase, int index)
    {
        using var child = new Process { StartInfo = new ProcessStartInfo(System.Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } };
        if (string.Equals(Path.GetFileNameWithoutExtension(System.Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) child.StartInfo.ArgumentList.Add(typeof(RestoreRecoveryChecks).Assembly.Location);
        foreach (var argument in new[] { "--restore-recovery-worker", phase, index.ToString(), root }) child.StartInfo.ArgumentList.Add(argument);
        Check(child.Start(), "inert worker starts");
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!child.HasExited) child.Kill(); }
        var error = await child.StandardError.ReadToEndAsync();
        Check(child.ExitCode == 73, "worker reaches " + phase + "/" + index + ": " + error);
    }

    internal static async Task RunAsync(string parent)
    {
        var previous = AppPaths.Current; var root = Path.Combine(parent, "restore-recovery"); Directory.CreateDirectory(root);
        try
        {
            foreach (var phase in new[] { "before-apply", "after-apply" })
            foreach (var index in Enumerable.Range(0, 6))
            {
                var box = Path.Combine(root, phase + index); var fixture = Fixture(box);
                await Interrupt(box, phase, index); AppPaths.Initialize(EnvironmentFor(box));
                AppPaths.EnsureDataLayout();
                Check(AppPaths.LastRestoreRecovery.RecoveredCount == 1 && Same(fixture.Before, Hashes(fixture.Data)), "startup recovers all hashes at " + phase + index);
                Check(Directory.Exists(Path.Combine(fixture.Data, "assets", "empty")), "empty original directory restored");
                AppPaths.EnsureDataLayout(); Check(AppPaths.LastRestoreRecovery.RecoveredCount == 0 && Same(fixture.Before, Hashes(fixture.Data)), "recovery is idempotent");
            }
            foreach (var index in new[] { 4, 5 })
            {
                var box = Path.Combine(root, "detached" + index); var fixture = Fixture(box);
                await Interrupt(box, "directory-detached", index); AppPaths.Initialize(EnvironmentFor(box)); AppPaths.EnsureDataLayout();
                Check(Same(fixture.Before, Hashes(fixture.Data)), "interruption after directory detach preserves assets/emojis " + index);
            }
            foreach (var phase in new[] { "after-rollback", "rollback-directory-detached" })
            {
                var box = Path.Combine(root, phase); var fixture = Fixture(box);
                await Interrupt(box, phase, 4); AppPaths.Initialize(EnvironmentFor(box)); AppPaths.EnsureDataLayout();
                Check(Same(fixture.Before, Hashes(fixture.Data)), "recovery itself may be interrupted and repeated: " + phase);
            }
            foreach (var phase in new[] { "prepared", "committed" })
            {
                var box = Path.Combine(root, phase); var fixture = Fixture(box);
                await Interrupt(box, phase, phase == "prepared" ? -1 : 5); AppPaths.Initialize(EnvironmentFor(box)); AppPaths.EnsureDataLayout();
                if (phase == "prepared") Check(Same(fixture.Before, Hashes(fixture.Data)), "prepared never mutated active data");
                else Check(Hashes(Path.Combine(box, "incoming")).Where(p => p.Key != "capture-history.json").All(p => Hashes(fixture.Data).GetValueOrDefault(p.Key) == p.Value), "confirmed commit never rolled back on restart");
            }
            var lockBox = Path.Combine(root, "second-failure"); var lockedFixture = Fixture(lockBox); var lockedService = Service(lockedFixture.Data);
            FileStream? held = null;
            lockedService.BeforeRestoreApply = (step, _) => { if (step == 1) { held = new FileStream(Path.Combine(lockedFixture.Data, "capture-history.json"), FileMode.Open, FileAccess.Read, FileShare.None); throw new IOException("controlled apply failure"); } };
            try { lockedService.RestoreSnapshot(lockedFixture.Archive); throw new InvalidOperationException("rollback must become protected"); }
            catch (RestoreRecoveryRequiredException) { _checks++; }
            finally { held?.Dispose(); }
            var transaction = Directory.GetDirectories(Path.Combine(lockedFixture.Data, "Backups"), ".restore-transaction-*").Single();
            Check(Hash(Path.Combine(transaction, "originals", "capture-history.json")) == lockedFixture.Before["capture-history.json"], "rollback failure retains verified original");
            await RefuseWrites(lockedFixture.Data);
            var recovery = RestoreRecovery.RecoverPending(lockedFixture.Data);
            Check(recovery.RecoveredCount == 1 && Same(lockedFixture.Before, Hashes(lockedFixture.Data)), "second failure recovers after lock released");

            var commitBox = Path.Combine(root, "commit-journal-write-failure"); var commitFixture = Fixture(commitBox);
            var commitService = Service(commitFixture.Data); FileStream? commitLock = null;
            commitService.RestoreCheckpoint = (phase, _) =>
            {
                if (phase != "before-commit") return;
                var folder = Directory.GetDirectories(Path.Combine(commitFixture.Data, "Backups"), ".restore-transaction-*").Single();
                commitLock = new FileStream(Path.Combine(folder, "journal.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            };
            try { commitService.RestoreSnapshot(commitFixture.Archive); throw new InvalidOperationException("unrecorded commit must not discard recovery"); }
            catch (RestoreRecoveryRequiredException) { _checks++; }
            finally { commitLock?.Dispose(); }
            var commitFolder = Directory.GetDirectories(Path.Combine(commitFixture.Data, "Backups"), ".restore-transaction-*").Single();
            Check(Hash(Path.Combine(commitFolder, "originals", "snippets.md")) == commitFixture.Before["snippets.md"], "unrecorded commit retains original bytes");
            await RefuseWrites(commitFixture.Data);
            AppPaths.EnsureDataLayout();
            Check(AppPaths.LastRestoreRecovery.RecoveredCount == 1 && Same(commitFixture.Before, Hashes(commitFixture.Data)), "failed commit journal write recovers previous data on restart");

            foreach (var failure in new[] { "original", "journal", "changed-target", "missing-backups", "identity", "pointer", "journal-null", "journal-path" })
            {
                var box = Path.Combine(root, failure); var fixture = Fixture(box); await Interrupt(box, "after-apply", 2);
                var tx = Directory.GetDirectories(Path.Combine(fixture.Data, "Backups"), ".restore-transaction-*").Single();
                if (failure == "original") File.WriteAllText(Path.Combine(tx, "originals", "snippets.md"), "changed recovery original");
                if (failure == "journal") File.WriteAllText(Path.Combine(tx, "journal.json"), "{malformed");
                if (failure == "changed-target") File.WriteAllText(Path.Combine(fixture.Data, "snippets.md"), "manual fictitious correction");
                if (failure == "missing-backups") Directory.Move(Path.Combine(fixture.Data, "Backups"), Path.Combine(box, "unavailable-backups"));
                if (failure == "identity") File.WriteAllText(Path.Combine(fixture.Data, ".restore-layout-id"), Guid.NewGuid().ToString("N"));
                if (failure == "pointer") File.WriteAllText(Path.Combine(fixture.Data, ".restore-pending-id"), "malformed");
                if (failure is "journal-null" or "journal-path")
                {
                    var journalPath = Path.Combine(tx, "journal.json"); var node = JsonNode.Parse(File.ReadAllText(journalPath))!;
                    if (failure == "journal-null") node["items"] = null;
                    else { node["items"]![0]!["label"] = "../outside.txt"; File.WriteAllText(Path.Combine(box, "outside.txt"), "unrelated fictitious sentinel"); }
                    File.WriteAllText(journalPath, node.ToJsonString());
                }
                var beforeRecovery = Hashes(fixture.Data);
                AppPaths.Initialize(EnvironmentFor(box));
                try { AppPaths.EnsureDataLayout(); throw new InvalidOperationException("must block unresolved recovery"); }
                catch (RestoreRecoveryRequiredException) { _checks++; }
                // A changed target is inspected before undoing any earlier step in this fixture.
                Check(Same(beforeRecovery, Hashes(fixture.Data)), "unverifiable recovery preserves current contents: " + failure);
                await RefuseWrites(fixture.Data);
                if (failure == "journal-path") Check(File.ReadAllText(Path.Combine(box, "outside.txt")) == "unrelated fictitious sentinel", "journal never targets an unrelated path");
            }
            var absentBox = Path.Combine(root, "original-absent"); AppPaths.Initialize(EnvironmentFor(absentBox));
            Directory.CreateDirectory(AppPaths.DataDirectory);
            await new SnippetMarkdownRepository().SaveAsync([new Snippet { Trigger = "/only", Name = "before", Content = "previous" }]);
            var absentData = AppPaths.DataDirectory; var absentBefore = Hashes(absentData);
            var absentIncoming = Path.Combine(absentBox, "incoming"); Seed(absentIncoming, "after"); File.Copy(Service(absentIncoming).CreateManualSnapshot(), Path.Combine(absentBox, "incoming.zip"));
            await Interrupt(absentBox, "after-apply", 5); AppPaths.Initialize(EnvironmentFor(absentBox)); AppPaths.EnsureDataLayout();
            Check(Same(absentBefore, Hashes(absentData)) && !Directory.Exists(Path.Combine(absentData, "assets")) && !Directory.Exists(Path.Combine(absentData, "capture-emotes")), "recovery removes only verified new files/directories originally absent");
            var relocated = Path.Combine(root, "relocated"); var movedFixture = Fixture(relocated); await Interrupt(relocated, "after-apply", 2);
            var movedEnv = EnvironmentFor(Path.Combine(root, "moved ç")); Directory.CreateDirectory(movedEnv.ExecutableDirectory); Directory.Move(movedFixture.Data, movedEnv.DataDirectory);
            AppPaths.Initialize(movedEnv); AppPaths.EnsureDataLayout(); Check(Same(movedFixture.Before, Hashes(movedEnv.DataDirectory)), "portable relocation keeps relative recovery binding");

            var cancelledBox = Path.Combine(root, "cancel"); var cancelledFixture = Fixture(cancelledBox); using var cancellation = new CancellationTokenSource();
            var cancelledService = Service(cancelledFixture.Data); cancelledService.BeforeRestoreApply = (step, _) => { if (step == 3) cancellation.Cancel(); };
            try { cancelledService.RestoreSnapshot(cancelledFixture.Archive, cancellation.Token); throw new InvalidOperationException("must cancel"); }
            catch (OperationCanceledException) { _checks++; }
            Check(Same(cancelledFixture.Before, Hashes(cancelledFixture.Data)), "cancellation restores complete old set");
            var normalBox = Path.Combine(root, "normal"); var normalFixture = Fixture(normalBox); Service(normalFixture.Data).RestoreSnapshot(normalFixture.Archive);
            Check(!File.Exists(Path.Combine(normalFixture.Data, ".restore-pending-id")), "normal commit clears pending marker");
            AppPaths.EnsureDataLayout(); Check(AppPaths.LastRestoreRecovery.RecoveredCount == 0, "normal commit does not show recovery warning");
            var legacyBox = Path.Combine(root, "legacy-orphan"); var legacyFixture = Fixture(legacyBox);
            var legacy = Path.Combine(legacyFixture.Data, "Backups", ".restore-rollback-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "sentinel.txt"), "legacy recovery fixture");
            try { AppPaths.EnsureDataLayout(); throw new InvalidOperationException("legacy recovery must not be guessed"); }
            catch (RestoreRecoveryRequiredException) { _checks++; }
            Check(File.ReadAllText(Path.Combine(legacy, "sentinel.txt")) == "legacy recovery fixture", "legacy orphan preserved without guessed replay");
            File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { checks = _checks, fixturesOnly = true, sourceAssembly = Hash(typeof(BackupService).Assembly.Location) }, Json));
            Console.WriteLine($"Restore recovery: {_checks} checks passed (controlled subprocesses; no personal data).");
        }
        finally { AppPaths.Initialize(previous); }
    }

    private static async Task RefuseWrites(string data)
    {
        var before = Hashes(data);
        try { await new JsonFileStore<AppSettings>(Path.Combine(data, "settings.json")).SaveAsync(new()); throw new InvalidOperationException("settings write must be protected"); }
        catch (RestoreRecoveryRequiredException) { _checks++; }
        try { await new CaptureHistoryStore(Path.Combine(data, "capture-history.json")).SaveAsync([]); throw new InvalidOperationException("history write must be protected"); }
        catch (RestoreRecoveryRequiredException) { _checks++; }
        try { Service(data).CreateManualSnapshot(); throw new InvalidOperationException("backup of partial set must be protected"); }
        catch (RestoreRecoveryRequiredException) { _checks++; }
        Check(Same(before, Hashes(data)), "blocked writers never replace mixed/uncertain originals");
    }
}
