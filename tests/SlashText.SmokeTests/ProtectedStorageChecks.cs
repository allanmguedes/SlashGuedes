using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlashText.Models;
using SlashText.Services;

internal static class ProtectedStorageChecks
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), Path.Combine(output, "unused"), isCapturePilot: true));
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var checks = new List<string>();
        void Require(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("Protected storage: " + label); checks.Add(label + ": PASS"); }
        async Task Refused(Func<Task> action, string label)
        {
            try { await action(); } catch (IOException) { Require(true, label); return; }
            throw new InvalidOperationException("Expected refusal: " + label);
        }
        static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        static string CopyPath(string path, byte[] bytes) => path + ".corrupt-" + Digest(bytes)[..16] + ".bak";
        var settingsPath = AppPaths.SettingsFile;
        var settings = new JsonFileStore<AppSettings>(settingsPath);
        await settings.LoadAsync();
        Require(settings.ReadState == StoredReadState.Missing && !settings.IsProtected, "new profile missing is writable");
        await settings.SaveAsync(new AppSettings { Theme = "Dark" });
        Require((await settings.LoadAsync()).Theme == "Dark" && settings.ReadState == StoredReadState.Valid, "valid settings round trip");
        foreach (var text in new[] { "{", "null", "[]", "{\"capture\":null}", "{\"theme\":null}", "{\"Theme\":\"Dark\",\"theme\":\"Light\"}" })
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await File.WriteAllBytesAsync(settingsPath, bytes);
            await settings.LoadAsync();
            Require(settings.IsProtected && settings.ReadState == StoredReadState.Invalid, "invalid settings protected " + Digest(bytes)[..8]);
            Require(File.ReadAllBytes(CopyPath(settingsPath, bytes)).SequenceEqual(bytes), "exact preservation " + Digest(bytes)[..8]);
            await Refused(() => new JsonFileStore<AppSettings>(settingsPath).SaveAsync(new AppSettings()), "new writer cannot replace invalid settings " + Digest(bytes)[..8]);
            Require(File.ReadAllBytes(settingsPath).SequenceEqual(bytes), "original unchanged " + Digest(bytes)[..8]);
            await settings.RecoverAsync(new AppSettings { Theme = "Light" });
            Require(!settings.IsProtected && (await settings.LoadAsync()).Theme == "Light", "explicit settings recovery " + Digest(bytes)[..8]);
        }
        var blocked = Encoding.UTF8.GetBytes("{\"fixture\":");
        await File.WriteAllBytesAsync(settingsPath, blocked);
        Directory.CreateDirectory(CopyPath(settingsPath, blocked));
        await settings.LoadAsync();
        Require(settings.IsProtected && !settings.CanRecover, "blocked preservation keeps protection");
        await Refused(() => settings.RecoverAsync(new AppSettings()), "blocked preservation refuses recovery");
        Require(File.ReadAllBytes(settingsPath).SequenceEqual(blocked), "blocked preservation original unchanged");
        Directory.Delete(CopyPath(settingsPath, blocked));
        await settings.LoadAsync();
        Require(settings.CanRecover && settings.IsProtected, "retry verifies copy without silently unblocking");
        await File.WriteAllTextAsync(CopyPath(settingsPath, blocked), "tampered copy");
        await Refused(() => settings.RecoverAsync(new AppSettings()), "tampered preservation refuses recovery");
        await settings.LoadAsync();
        Require(!settings.CanRecover, "existing bad copy is never trusted");
        Require(File.ReadAllText(CopyPath(settingsPath, blocked)) == "tampered copy", "existing bad copy is never overwritten");
        File.Delete(CopyPath(settingsPath, blocked));
        await settings.LoadAsync();
        var changed = Encoding.UTF8.GetBytes("{\"theme\":\"Dark\"}");
        await File.WriteAllBytesAsync(settingsPath, changed);
        await Refused(() => settings.RecoverAsync(new AppSettings()), "external change requires reread");
        Require(File.ReadAllBytes(settingsPath).SequenceEqual(changed), "external edit retained");
        await settings.LoadAsync();
        Require(!settings.IsProtected, "complete reread releases settings");
        using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await settings.LoadAsync();
            Require(settings.ReadState == StoredReadState.Unavailable && settings.IsProtected && !settings.CanRecover, "locked source unavailable");
            await Refused(() => settings.SaveAsync(new AppSettings()), "locked source cannot become defaults");
        }
        await settings.LoadAsync();
        Require(!settings.IsProtected, "unlocked valid settings recover by reread");
        var oversized = Path.Combine(AppPaths.DataDirectory, "large.json");
        using (var large = File.Create(oversized)) large.SetLength(BackupInputBudget.MetadataBytes + 1);
        var largeStore = new JsonFileStore<AppSettings>(oversized);
        await largeStore.LoadAsync();
        Require(largeStore.IsProtected && !largeStore.CanRecover, "oversized input refused before allocation");
        File.Delete(oversized);

        var media = Path.Combine(AppPaths.DataDirectory, "expired.png");
        using (var bitmap = new System.Drawing.Bitmap(8, 8)) bitmap.Save(media, System.Drawing.Imaging.ImageFormat.Png);
        var entry = new CaptureRecord { Id = "legible", CreatedAt = DateTimeOffset.UtcNow.AddDays(-180), FilePath = media, Width = 8, Height = 8 };
        var historyPath = AppPaths.CaptureHistoryFile;
        var mixed = Encoding.UTF8.GetBytes("[" + JsonSerializer.Serialize(entry) + ", {\"width\":\"invalid\"}, null]");
        await File.WriteAllBytesAsync(historyPath, mixed);
        var capture = new CaptureService(); await capture.LoadAsync();
        Require(capture.HistoryReadState == StoredReadState.Partial && capture.IsHistoryProtected && capture.History.Single().Id == "legible", "partial history stays legible and protected");
        Require(await capture.CleanOlderThanAsync(90, true) == 0 && File.Exists(media), "automatic retention cannot remove legible file");
        await Refused(() => capture.DeleteAsync("legible", true), "manual delete refused before file mutation");
        await capture.AddMediaRecordAsync(new CaptureRecord { FilePath = "new-recording.mp4" });
        Require(capture.History.Count == 1, "recording registration cannot alter protected history");
        var editedPath = Path.Combine(AppPaths.DataDirectory, "saved-explicitly.png");
        using (var bitmap = new System.Drawing.Bitmap(6, 6))
        {
            await capture.SaveEditedImageAsync(bitmap, editedPath, new CaptureSettings());
            Require(File.Exists(editedPath) && capture.History.Count == 1, "explicit image save works without replacing protected history");
        }
        Require(File.ReadAllBytes(historyPath).SequenceEqual(mixed), "all maintenance and capture operations keep original bytes");
        Require(File.ReadAllBytes(CopyPath(historyPath, mixed)).SequenceEqual(mixed), "partial history exact original preserved");
        await Refused(() => new CaptureHistoryStore(historyPath).SaveAsync([]), "new history writer cannot bypass partial read");
        var healthy = Path.Combine(AppPaths.DataDirectory, "healthy.json");
        await new JsonFileStore<AppSettings>(healthy).SaveAsync(new AppSettings());
        Require(File.Exists(healthy), "healthy store remains usable");
        await capture.RecoverHistoryAsync();
        Require(!capture.IsHistoryProtected && capture.History.Single().Id == "legible", "explicit recovery retains every legible history row");
        Require(File.ReadAllBytes(CopyPath(historyPath, mixed)).SequenceEqual(mixed), "full original still retained after recovery");
        Require(Directory.GetFiles(AppPaths.DataDirectory, "*.recovered-original-*.bak").Any(), "atomic recovery retains displaced originals");
        await capture.AddMediaRecordAsync(new CaptureRecord { Id = "new", FilePath = "new-recording.mp4" });
        Require(capture.History.Count == 2, "registration resumes only after complete recovery");
        foreach (var text in new[] { "{", "{}", "null" })
        {
            await File.WriteAllTextAsync(historyPath, text);
            await capture.LoadAsync();
            Require(capture.IsHistoryProtected && capture.HistoryReadState == StoredReadState.Invalid && capture.History.Count == 0, "invalid history root protected " + text);
            await Refused(() => new CaptureHistoryStore(historyPath).SaveAsync([]), "invalid root empty fallback cannot overwrite " + text);
        }
        File.Delete(historyPath);
        await capture.LoadAsync();
        Require(capture.IsHistoryProtected && capture.HistoryReadState == StoredReadState.Unavailable, "disappearing protected source is not a fresh profile");
        await File.WriteAllTextAsync(historyPath, "[]"); await capture.LoadAsync();
        Require(!capture.IsHistoryProtected, "externally repaired valid history rereads cleanly");
        await File.WriteAllTextAsync(settingsPath, "{\"theme\":\"Dark\"}");
        Require((await settings.LoadAsync()).Theme == "Dark" && !settings.IsProtected, "legacy omitted settings fields retain defaults");
        await File.WriteAllTextAsync(settingsPath, "{"); await settings.LoadAsync();
        var failure = StoredReadProtection.Get(settingsPath)!;
        await Refused(() => AtomicFilePersistence.WriteAsync(settingsPath, async (stream, token) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{}"), token);
            await File.WriteAllBytesAsync(settingsPath, changed, token);
        }, recovery: failure), "edit racing recovery serialization refused");
        Require(File.ReadAllBytes(settingsPath).SequenceEqual(changed), "serialization race preserves external data");
        await settings.LoadAsync();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await settings.LoadAsync(cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { Require(true, "cancellation remains cancellation"); }
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
        Console.WriteLine($"Protected storage: {checks.Count} checks PASS");
    }
}
