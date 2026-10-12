using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using SlashText.Models;
using SlashText.Services;

internal static class CaptureHistoryTrustChecks
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("History trust: " + message);
        _checks++;
    }

    internal static async Task RunAsync(string parent)
    {
        var previous = AppPaths.Current;
        var root = Path.Combine(parent, "history-trust");
        var environment = new AppDataEnvironment(DistributionMode.Portable,
            Path.Combine(root, "portable"), Path.Combine(root, "local"));
        AppPaths.Initialize(environment);
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var custom = Path.Combine(root, "custom destination with spaces á");
            Directory.CreateDirectory(custom);
            var png = Path.Combine(custom, "capture.png");
            using (var image = new Bitmap(16, 12)) image.Save(png, ImageFormat.Png);
            var jpeg = Path.Combine(custom, "capture.jpeg");
            var gif = Path.Combine(custom, "capture.gif");
            using (var image = new Bitmap(16, 12))
            { image.Save(jpeg, ImageFormat.Jpeg); image.Save(gif, ImageFormat.Gif); }
            var mp4 = Path.Combine(custom, "capture.mp4");
            // Minimal inert ISO media header; never opened in a player.
            File.WriteAllBytes(mp4, [0,0,0,24,102,116,121,112,105,115,111,109,0,0,0,0,105,115,111,109,109,112,52,50]);
            var store = new CaptureHistoryStore(AppPaths.CaptureHistoryFile);
            var original = new CaptureRecord { RequiresFileApproval = false, Id = "original", FilePath = png, CreatedAt = DateTimeOffset.Now.AddDays(-100) };
            await store.SaveAsync([original]);
            var service = new CaptureService(); await service.LoadAsync();
            Check(service.ResolveFilePath(service.History[0]) == png, "existing custom destination is preserved");
            var legacyRecord = JsonSerializer.Deserialize<CaptureRecord>("{\"FilePath\":\"C:\\\\legacy.png\"}")!;
            Check(legacyRecord.RequiresFileApproval, "unmarked legacy records default to untrusted");

            foreach (var path in new[] { png, jpeg, gif, mp4 })
            {
                var record = new CaptureRecord { RequiresFileApproval = false, FilePath = path, MediaKind = CaptureHistoryFilePolicy.MediaKind(path) };
                Check(service.ResolveFilePath(record) == path, "supported media content matches extension");
            }
            var marker = Path.Combine(custom, "not-media.txt"); File.WriteAllText(marker, "synthetic marker");
            var renamed = Path.Combine(custom, "not-media.png"); File.WriteAllText(renamed, "synthetic text, not pixels");
            foreach (var path in new[] { marker, renamed, png + ":stream", @"\\server\share\capture.png", @"\\?\C:\capture.png", @"C:\CON.png", @"C:\LPT1.png", "relative.png" })
                Check(service.ResolveFilePath(new CaptureRecord { RequiresFileApproval = false, FilePath = path }) == "", "unsafe or incompatible file blocked");
            Check(service.ResolveFilePath(new CaptureRecord { RequiresFileApproval = false, FilePath = png, MediaKind = "video" }) == "", "inconsistent kind blocked");
            Check(service.ResolveFilePath(new CaptureRecord { RequiresFileApproval = false, FilePath = png, PortableRelativePath = @"..\escape.png" }) == "", "portable escape never falls back");
            Check(service.ResolveFilePath(new CaptureRecord { RequiresFileApproval = false, FilePath = png, PortableRelativePath = png }) == "", "absolute portable reference blocked");
            var portableFile = Path.Combine(environment.ExecutableDirectory, "capture.png");
            Directory.CreateDirectory(environment.ExecutableDirectory); File.Copy(png, portableFile);
            var portableRecord = new CaptureRecord { RequiresFileApproval = false, FilePath = @"C:\old-location\capture.png", PortableRelativePath = "capture.png" };
            Check(service.ResolveFilePath(portableRecord) == portableFile, "portable relative reference remaps correctly");
            var moved = new AppDataEnvironment(DistributionMode.Portable, Path.Combine(root, "moved"), Path.Combine(root, "local"));
            Directory.CreateDirectory(moved.ExecutableDirectory); File.Copy(png, Path.Combine(moved.ExecutableDirectory, "capture.png"));
            Check(CapturePathResolver.Resolve(portableRecord, moved) == Path.Combine(moved.ExecutableDirectory, "capture.png"), "portable move retains resolution");

            foreach (var schema in new[] { 1, 2 })
            {
                // A restored false flag must never carry authority from an archive.
                await store.SaveAsync([original, new CaptureRecord { RequiresFileApproval = false, Id = "external", FilePath = marker },
                    new CaptureRecord { RequiresFileApproval = false, Id = "renamed", FilePath = renamed }]);
                var backups = new BackupService();
                string archive;
                if (schema == 2) archive = backups.CreateManualSnapshot();
                else
                {
                    archive = Path.Combine(root, "legacy.zip");
                    using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                    using (var writer = new StreamWriter(zip.CreateEntry("backup-manifest.json").Open())) writer.Write("{\"schemaVersion\":1}");
                    using (var writer = new StreamWriter(zip.CreateEntry("capture-history.json").Open())) writer.Write(File.ReadAllText(AppPaths.CaptureHistoryFile));
                }
                backups.RestoreSnapshot(archive);
                if (schema == 2)
                {
                    var before = File.ReadAllBytes(AppPaths.CaptureHistoryFile);
                    backups.BeforeRestoreApply = (step, _) => { if (step == 1) throw new IOException("synthetic rollback"); };
                    try { backups.RestoreSnapshot(archive); throw new Exception("must roll back"); }
                    catch (IOException) { Check(File.ReadAllBytes(AppPaths.CaptureHistoryFile).SequenceEqual(before), "failed restore rolls history back byte-for-byte"); }
                    finally { backups.BeforeRestoreApply = null; }
                }
                service = new CaptureService(); await service.LoadAsync();
                Check(service.History.All(r => r.RequiresFileApproval), "restore forces approval for every record, schema " + schema);
                var restored = service.History.Single(r => r.Id == "original");
                Check(service.ResolveFilePath(restored) == "", "no automatic thumbnail/editor access");
                Check(!service.ApproveFileAccess(restored, jpeg), "approval is bound to displayed path");
                Check(!service.ApproveFileAccess(service.History.Single(r => r.Id == "external"), marker), "non-media cannot be authorized");
                Check(!service.ApproveFileAccess(service.History.Single(r => r.Id == "renamed"), renamed), "renaming content cannot authorize it");
                try { await service.DeleteAsync("external", true); throw new Exception("must reject deletion"); }
                catch (InvalidOperationException) { Check(File.Exists(marker), "external marker preserved"); }
                using (var image = new Bitmap(16, 12))
                {
                    try { await service.SaveEditedImageAsync(image, png, new(), restored); throw new Exception("must reject write"); }
                    catch (InvalidOperationException) { Check(true, "unapproved overwrite blocked"); }
                }
                Check(service.ApproveFileAccess(restored, png) && service.ResolveFilePath(restored) == png, "explicit approval enables local media");
                File.SetLastWriteTimeUtc(png, File.GetLastWriteTimeUtc(png).AddSeconds(2));
                Check(service.ResolveFilePath(restored) == "", "file change invalidates approval");
                Check(service.ApproveFileAccess(restored, png), "file can be reconfirmed");
                using (var image = new Bitmap(18, 14)) await service.SaveEditedImageAsync(image, png, new(), restored);
                Check(service.ResolveFilePath(restored) == png && restored.RequiresFileApproval, "authorized save retains current session only");
                await service.LoadAsync();
                Check(service.ResolveFilePath(service.History.Single(r => r.Id == "original")) == "", "reload never persists session approval");
                Check(await service.DeleteAsync("external", false) && File.Exists(marker), "metadata-only removal does not delete file");
                await service.CleanOlderThanAsync(1, true);
                Check(File.Exists(png), "retention preserves restored media without implicit approval");
            }
            await store.SaveAsync([new CaptureRecord { RequiresFileApproval = false, Id = "same", FilePath = png }, new CaptureRecord { RequiresFileApproval = false, Id = "same", FilePath = jpeg }]);
            await service.LoadAsync();
            Check(service.History.Select(r => r.Id).Distinct().Count() == 2, "duplicate IDs cannot target another entry");
            var trusted = service.History[0];
            Check(await service.DeleteAsync(trusted.Id, true) && !File.Exists(png), "normal authorized deletion remains functional");
            Console.WriteLine($"Capture history trust: {_checks} checks passed.");
        }
        finally { AppPaths.Initialize(previous); }
    }
}
