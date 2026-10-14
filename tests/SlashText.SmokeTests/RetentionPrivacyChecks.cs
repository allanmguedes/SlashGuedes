using System.Diagnostics;
using System.Text;
using SlashText.Services;
using SlashText.Models;

internal static class RetentionPrivacyChecks
{
    private static int _checks;
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("Retention: " + label); _checks++; }
    private static void Initialize(string path) => AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, path, path, isCapturePilot: true));

    internal static void Worker(string path)
    {
        Initialize(path);
        using var ocr = TemporarySession.Create("ocr");
        using var gif = TemporarySession.Create("gif");
        File.WriteAllText(Path.Combine(ocr.DirectoryPath, "region.png"), "synthetic OCR pixels");
        File.WriteAllText(Path.Combine(ocr.DirectoryPath, "result.json"), "synthetic OCR result");
        File.WriteAllText(Path.Combine(gif.DirectoryPath, "000000.png"), "synthetic GIF pixels");
        var partial = Path.Combine(path, ".crash.recording.mp4"); File.WriteAllText(partial, "synthetic incomplete MP4");
        RecordingRecovery.Start(Guid.NewGuid(), partial);
        // Publish readiness only after the writer has closed; File.Exists alone
        // cannot distinguish an in-progress WriteAllLines from a completed signal.
        var readyTemporary = Path.Combine(path, "ready.tmp");
        File.WriteAllLines(readyTemporary, [ocr.DirectoryPath, gif.DirectoryPath]);
        File.Move(readyTemporary, Path.Combine(path, "ready.txt"));
        Thread.Sleep(Timeout.Infinite);
    }

    internal static async Task RunAsync(string parent)
    {
        var root = Path.Combine(parent, "retention-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = AppPaths.Current;
        var diagnosticsEnvironment = Environment.GetEnvironmentVariable("SLASHDESK_DIAGNOSTICS");
        try
        {
            Environment.SetEnvironmentVariable("SLASHDESK_DIAGNOSTICS", null);
            Initialize(root);
            AppDiagnosticLog.Initialize();
            Check(!SafeDiagnosticLog.Enabled, "additional diagnostics off by default");
            SafeDiagnosticLog.Write("expansion.started");
            Check(!Directory.EnumerateFiles(AppPaths.LogsDirectory, "functional-*").Any(), "opt-in gate prevents file creation");
            const string secret = "SYNTHETIC_SECRET_DO_NOT_LOG";
            AppDiagnosticLog.Write("recording.start-requested", ("message", secret), ("file", secret + ".mp4"),
                ("dataDirectory", "C:\\fixture\\" + secret), ("deviceId", secret), ("nested", new { text = secret }),
                ("width", 1920), ("fps", 30), ("computerAudio", true), ("recordingId", Guid.NewGuid()),
                ("target", secret), ("hresult", secret));
            AppDiagnosticLog.WriteException("gif.capture-failed", new IOException(secret));
            AppDiagnosticLog.Write(secret, ("width", 1));
            SafeDiagnosticLog.SetEnabled(true);
            SafeDiagnosticLog.Write("expansion.started", new Dictionary<string, object?>
                { ["message"] = secret, ["typedCharacterCount"] = 12, ["exceptionType"] = "Private." + secret });
            var logs = string.Join("\n", Directory.EnumerateFiles(AppPaths.LogsDirectory, "*.jsonl").Select(File.ReadAllText));
            Check(!logs.Contains(secret) && !logs.Contains("C:\\fixture") && !logs.Contains("dataDirectory"), "operational and optional logs omit sensitive fields/unknown stages");
            Check(logs.Contains("1920") && logs.Contains("typedCharacterCount") && logs.Contains("IOException"), "useful bounded metadata retained");
            SafeDiagnosticLog.SetEnabled(false);
            Check(!SafeDiagnosticLog.Enabled, "opt-out persisted");

            var current = AppDiagnosticLog.CurrentLogPath;
            File.WriteAllBytes(current, new byte[DiagnosticPolicy.MaximumFileBytes - 40]);
            AppDiagnosticLog.Write("recording.stop-requested", ("elapsedMs", 1200));
            Check(File.Exists(current + ".1") && new FileInfo(current).Length < 4096, "rotate before exceeding single-file budget");
            var old = Path.Combine(AppPaths.LogsDirectory, "functional-20000101.jsonl");
            File.WriteAllText(old, secret); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
            var foreign = Path.Combine(AppPaths.LogsDirectory, "user-document.txt"); File.WriteAllText(foreign, secret);
            var native = Path.Combine(AppPaths.LogsDirectory, "screenrecorderlib-20000101-120000-001.log"); File.WriteAllText(native, secret);
            for (var i = 1; i <= 14; i++) File.WriteAllBytes(Path.Combine(AppPaths.LogsDirectory, $"functional-202601{i:00}.jsonl"), new byte[DiagnosticPolicy.MaximumFileBytes]);
            DiagnosticPolicy.Prune();
            Check(!File.Exists(old) && !File.Exists(native), "age policy and obsolete raw native logs cleaned");
            Check(Directory.EnumerateFiles(AppPaths.LogsDirectory, "*.jsonl*").Sum(p => new FileInfo(p).Length) <= DiagnosticPolicy.MaximumTotalBytes, "aggregate budget enforced");
            Check(File.ReadAllText(foreign) == secret, "foreign file never pruned");
            AppDiagnosticLog.Write("recording.stop");
            using (var locked = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.None))
            { AppDiagnosticLog.Write("recording.stop"); Check(locked.Length < 4096, "busy log cannot interrupt app or be replaced"); }
            File.SetAttributes(current, FileAttributes.ReadOnly);
            var before = File.ReadAllBytes(current);
            AppDiagnosticLog.Write("recording.stop");
            Check(File.ReadAllBytes(current).SequenceEqual(before), "write-denied log safely skipped");
            File.SetAttributes(current, FileAttributes.Normal);

            using (var alive = TemporarySession.Create("ocr"))
            {
                File.WriteAllText(Path.Combine(alive.DirectoryPath, "region.png"), secret);
                Check(TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddDays(1)) == 0 && Directory.Exists(alive.DirectoryPath), "alive parent survives orphan sweep");
                using (TemporarySession.AttachWorker(Path.Combine(alive.DirectoryPath, "region.png"), Path.Combine(alive.DirectoryPath, "result.json")))
                { alive.Dispose(); Check(Directory.Exists(alive.DirectoryPath), "shared worker lease survives parent release"); }
                // The parent is still alive; startup must conservatively retain this session.
                Check(TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddDays(1)) == 0, "owner liveness needed as well as an unlocked lease");
            }
            using (var normal = TemporarySession.Create("gif"))
            {
                File.WriteAllText(Path.Combine(normal.DirectoryPath, "000000.png"), secret);
                var folder = normal.DirectoryPath; normal.Dispose();
                Check(!Directory.Exists(folder), "successful/cancelled session flat cleanup");
            }
            var foreignSession = Path.Combine(TemporarySession.Root, "ocr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(foreignSession); File.WriteAllText(Path.Combine(foreignSession, "region.png"), secret);
            TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddDays(1));
            Check(File.Exists(Path.Combine(foreignSession, "region.png")), "unmarked session preserved");

            var childRoot = Path.Combine(root, "crash-child"); Directory.CreateDirectory(childRoot);
            using var child = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true } };
            child.StartInfo.ArgumentList.Add("--retention-worker"); child.StartInfo.ArgumentList.Add(childRoot);
            Check(child.Start(), "controlled worker launched");
            try
            {
                for (var i = 0; i < 200 && !File.Exists(Path.Combine(childRoot, "ready.txt")); i++) await Task.Delay(50);
                Check(File.Exists(Path.Combine(childRoot, "ready.txt")), "synthetic OCR/GIF ready");
                var folders = File.ReadAllLines(Path.Combine(childRoot, "ready.txt"));
                Initialize(childRoot);
                Check(TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddDays(1)) == 0 && folders.All(Directory.Exists), "live subprocess protected");
                using var sharedWorker = TemporarySession.AttachWorker(Path.Combine(folders[0], "region.png"), Path.Combine(folders[0], "result.json"));
                var childStartedTicks = child.StartTime.ToUniversalTime().Ticks;
                child.Kill(); await child.WaitForExitAsync();
                Check(TemporarySession.CleanupOrphans() == 0, "startup grace protects delayed worker attachment");
                var removedWhileLeased = 0;
                var cleanupClock = Stopwatch.StartNew();
                do
                {
                    removedWhileLeased += TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddMinutes(3));
                    if (!Directory.Exists(folders[0])) throw new InvalidOperationException("Retention: live worker lease was not preserved.");
                    if (removedWhileLeased == 0) await Task.Delay(50);
                } while (removedWhileLeased == 0 && cleanupClock.Elapsed < TimeSpan.FromSeconds(5));
                // Cleanup intentionally retains files temporarily occupied by Windows/AV.
                // Retry the eligible GIF, but require the OCR worker's folder on every sweep.
                Check(removedWhileLeased == 1 && Directory.Exists(folders[0]),
                    $"dead parent with live worker lease preserved (removed={removedWhileLeased}; ownerExited={TemporarySession.OwnerHasExited(child.Id, childStartedTicks)})");
                sharedWorker.Dispose();
                File.SetAttributes(Path.Combine(folders[0], "region.png"), FileAttributes.ReadOnly);
                Check(TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddMinutes(3)) == 0 && Directory.Exists(folders[0]), "delete-denied orphan retained without crashing");
                File.SetAttributes(Path.Combine(folders[0], "region.png"), FileAttributes.Normal);
                Check(TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddMinutes(3)) == 1 && folders.All(p => !Directory.Exists(p)), "abruptly killed owned OCR/GIF removed after worker release");
                Check(RecordingRecovery.InterruptedCount() == 1 && File.ReadAllText(Path.Combine(childRoot, ".crash.recording.mp4")) == "synthetic incomplete MP4", "crash metadata detects preserved MP4 without scanning outputs");
                File.Delete(Path.Combine(childRoot, ".crash.recording.mp4"));
                Check(RecordingRecovery.InterruptedCount() == 0 && !Directory.GetFiles(Path.Combine(AppPaths.DataDirectory, "RecordingRecovery-v1")).Any(), "missing partial removes only stale private metadata for dead owner");
            }
            finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            Check(TemporarySession.OwnerHasExited(Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks - 1), "PID reuse distinguished by start identity");
            Check(!TemporarySession.OwnerHasExited(-1, 0), "invalid/unprovable ownership preserved");

            Initialize(root);
            var external = Path.Combine(root, "outside-managed-root"); Directory.CreateDirectory(external);
            var sentinel = Path.Combine(external, "region.png"); File.WriteAllText(sentinel, secret);
            var junction = Path.Combine(TemporarySession.Root, "ocr-" + Guid.NewGuid().ToString("N"));
            CreateJunction(junction, external);
            TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddDays(1));
            Check(File.ReadAllText(sentinel) == secret && Directory.Exists(junction), "junction session never traversed");
            Directory.Delete(junction, recursive: false);
            using (var unexpected = TemporarySession.Create("ocr"))
            {
                var input = Path.Combine(unexpected.DirectoryPath, "region.png"); File.WriteAllText(input, secret);
                File.WriteAllText(Path.Combine(unexpected.DirectoryPath, "user-document.txt"), secret);
                unexpected.Dispose();
                Check(File.ReadAllText(input) == secret && File.Exists(Path.Combine(unexpected.DirectoryPath, "user-document.txt")), "unexpected contents veto whole-session deletion");
            }
            var data = AppPaths.DataDirectory;
            var logJunctionHome = Path.Combine(root, "log-junction"); Initialize(logJunctionHome);
            Directory.CreateDirectory(AppPaths.DataDirectory); CreateJunction(AppPaths.LogsDirectory, external);
            AppDiagnosticLog.Initialize(); AppDiagnosticLog.Write("recording.stop");
            Check(Directory.GetFiles(external).Length == 1 && File.ReadAllText(sentinel) == secret, "log root junction neither written nor pruned");
            Directory.Delete(AppPaths.LogsDirectory, recursive: false);
            CreateJunction(TemporarySession.Root, external);
            try { using var invalid = TemporarySession.Create("ocr"); throw new InvalidOperationException("Junction root accepted."); }
            catch (IOException) { }
            Check(TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddDays(1)) == 0 && Directory.GetFiles(external).Length == 1, "temporary root junction cannot create or remove content");
            Directory.Delete(TemporarySession.Root, recursive: false);
            Initialize(root);
            var model = Path.Combine(data, "model.traineddata"); File.WriteAllText(model, secret);
            var partial = Path.Combine(external, ".synthetic.recording.mp4"); File.WriteAllText(partial, secret);
            var backup = Path.Combine(AppPaths.BackupsDirectory, "user.zip"); Directory.CreateDirectory(AppPaths.BackupsDirectory); File.WriteAllText(backup, secret);
            var saved = Path.Combine(external, "saved.png"); File.WriteAllText(saved, secret);
            var interruptedId = Guid.NewGuid(); RecordingRecovery.Start(interruptedId, partial); RecordingRecovery.End(interruptedId, false);
            Check(RecordingRecovery.InterruptedCount() == 1, "preserved partial shown as interrupted");
            TemporarySession.CleanupOrphans(DateTimeOffset.UtcNow.AddDays(1)); DiagnosticPolicy.Prune();
            Check(new[] { model, partial, backup, saved }.All(p => File.ReadAllText(p) == secret), "models/backups/captures/MP4 excluded from cleanup");
            var completed = Path.Combine(external, ".completed.recording.mp4"); File.WriteAllText(completed, secret);
            var completedId = Guid.NewGuid(); RecordingRecovery.Start(completedId, completed); RecordingRecovery.End(completedId, true);
            Check(RecordingRecovery.InterruptedCount() == 1 && File.Exists(completed), "successful completion clears only its metadata");
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            RecordingRecovery.Start(first, completed); RecordingRecovery.Start(second, completed);
            RecordingRecovery.End(first, true);
            Check(File.Exists(Path.Combine(AppPaths.DataDirectory, "RecordingRecovery-v1", $"{second:N}.json")), "older completion cannot clear a newer recording marker");
            RecordingRecovery.End(second, true);
            var otherRoot = Path.Combine(root, "other-instance"); Initialize(otherRoot); AppDiagnosticLog.Initialize();
            Check(AppDiagnosticLog.CurrentLogPath.StartsWith(AppPaths.DataDirectory) && AppDiagnosticLog.CurrentLogPath != current, "data-root changes do not retain static log/marker paths");
            Console.WriteLine($"Retention/privacy: {_checks} checks passed (synthetic, no personal clipboard).");
        }
        finally { Environment.SetEnvironmentVariable("SLASHDESK_DIAGNOSTICS", diagnosticsEnvironment); AppPaths.Initialize(previous); }
    }

    private static void CreateJunction(string path, string target)
    {
        using var command = new Process { StartInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in new[] { "/c", "mklink", "/J", path, target }) command.StartInfo.ArgumentList.Add(argument);
        command.Start(); command.WaitForExit();
        if (command.ExitCode != 0) throw new IOException("Synthetic junction fixture unavailable: " + command.StandardError.ReadToEnd());
    }
}
