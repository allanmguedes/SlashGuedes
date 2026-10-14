using System.IO.Compression;
using System.Security.Cryptography;
using SlashText.Models;
using SlashText.Services;

internal static class LegacyBackupChecks
{
    internal static async Task RunAsync(string output)
    {
        var previous = AppPaths.Current;
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), Path.Combine(output, "unused"), true));
        try
        {
            AppPaths.EnsureDataLayout();
            var checks = new List<string>();
            void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Legacy backups: " + label); checks.Add(label + ": PASS"); }
            static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var current = new Snippet { Name = "Atual", Trigger = "/atual119", Content = "ATUAL 119" };
            await new SnippetMarkdownRepository().SaveAsync([current]);
            var legacy = new Dictionary<string, string>();
            for (var index = 0; index < 12; index++)
            {
                var path = Path.Combine(AppPaths.BackupsDirectory, $"snippets-202001{index + 1:00}-000000.md");
                var row = new Snippet { Name = "Histórico " + index, Trigger = "/legado119", Content = "VERSÃO HISTÓRICA " + index };
                File.WriteAllText(path, SnippetMarkdownRepository.Serialize([row]));
                legacy[path] = Hash(path);
            }
            var duplicate = Path.Combine(AppPaths.BackupsDirectory, "snippets-duplicado.md");
            File.Copy(legacy.Keys.First(), duplicate); legacy[duplicate] = Hash(duplicate);
            var unreadable = Path.Combine(AppPaths.BackupsDirectory, "snippets-incompativel.md");
            File.WriteAllText(unreadable, "FICTÍCIO: versão incompleta, manter os bytes"); legacy[unreadable] = Hash(unreadable);
            var unrelated = Path.Combine(AppPaths.BackupsDirectory, "pessoal-backup-20200101.zip");
            File.WriteAllText(unrelated, "FICTÍCIO NÃO GERENCIADO");
            var unknown = Path.Combine(AppPaths.BackupsDirectory, "SlashDesk-backup-importado.zip");
            File.WriteAllText(unknown, "FICTÍCIO COM NOME NÃO RECONHECIDO");
            var invalidDate = Path.Combine(AppPaths.BackupsDirectory, "SlashDesk-backup-20200231.zip");
            File.WriteAllText(invalidDate, "FICTÍCIO DATA INVÁLIDA");
            var unmanaged = new[] { unrelated, unknown, invalidDate }.ToDictionary(p => p, Hash);
            var external = Path.Combine(output, "external"); Directory.CreateDirectory(external);
            var outside = Path.Combine(external, "snippets-fora.md"); File.WriteAllText(outside, "FICTÍCIO EXTERNO");
            var outsideHash = Hash(outside);
            var service = new BackupService();
            var seed = service.CreateManualSnapshot();
            Check(legacy.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), "first snapshot retains every legacy version including duplicates and invalid bytes");
            using (var archive = ZipFile.OpenRead(seed))
            {
                Check(archive.Entries.All(e => !e.FullName.StartsWith("Backups/", StringComparison.Ordinal)), "new ZIP does not absorb legacy or arbitrary backup files");
                using var reader = new StreamReader(archive.GetEntry("snippets.md")!.Open());
                Check(reader.ReadToEnd().Contains("ATUAL 119", StringComparison.Ordinal), "new ZIP still contains current shortcut data");
            }
            foreach (var item in legacy.Where(p => p.Key != unreadable))
            {
                var restored = await new SnippetImportService().ImportAsync(item.Key, SnippetImportSource.SlashDesk);
                Check(restored.Snippets.Single().Content.StartsWith("VERSÃO HISTÓRICA", StringComparison.Ordinal), "historical content remains recoverable " + Path.GetFileName(item.Key));
            }
            var candidates = new Dictionary<string, DateTime>();
            for (var index = 0; index < 10; index++)
            {
                var name = index == 0 ? "SlashText-backup-20200101.zip" : $"SlashDesk-backup-202001{index + 1:00}.zip";
                var path = Path.Combine(AppPaths.BackupsDirectory, name); File.Copy(seed, path);
                var date = new DateTime(2020, 1, 1, 0, 0, index, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, date); candidates[path] = date;
            }
            var beforeRestore = Path.Combine(AppPaths.BackupsDirectory, "SlashDesk-backup-before-restore-20200111-120000-000-11900000000040008000000000000001.zip");
            File.Copy(seed, beforeRestore); File.SetLastWriteTimeUtc(beforeRestore, new DateTime(2020, 1, 11, 0, 0, 0, DateTimeKind.Utc));
            candidates[beforeRestore] = File.GetLastWriteTimeUtc(beforeRestore);
            service.CreateDailySnapshot();
            var recent = service.ListSnapshots();
            Check(recent.Count == 7, "retention remains seven managed ZIPs across daily manual and before-restore names");
            Check(recent.Take(2).All(p => !candidates.ContainsKey(p.FullName)), "new daily and manual snapshots remain available");
            Check(candidates.OrderByDescending(p => p.Value).Take(5).All(p => File.Exists(p.Key)) && candidates.OrderByDescending(p => p.Value).Skip(5).All(p => !File.Exists(p.Key)), "only oldest managed versions are removed");
            var hashes = recent.ToDictionary(p => p.FullName, p => Hash(p.FullName));
            service.CreateDailySnapshot();
            Check(hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), "repeated daily cleanup is idempotent");
            Check(legacy.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), "more than seven legacy MDs keep exact original bytes after pruning and repeat");
            Check(unmanaged.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), "unknown prefixed and unrelated ZIP files stay untouched");
            Check(Hash(outside) == outsideHash, "external file stays outside backup and cleanup");
            foreach (var snapshot in recent) BackupService.ValidateSnapshot(snapshot.FullName);
            Check(true, "all retained ZIPs have verified manifests and hashes");
            foreach (var name in new[] { "SlashDesk-backup-20260101.zip", "SlashText-backup-20200101.zip", "SlashDesk-backup-manual-20260101-235959-123.zip", Path.GetFileName(beforeRestore) })
                Check(BackupService.IsManagedSnapshotName(name), "writer name recognized " + name);
            foreach (var name in new[] { "other-backup-20260101.zip", "SlashDesk-backup-foo.zip", "SlashDesk-backup-20260101.zip.bak", "SlashDesk-backup-20261301.zip", "SlashDesk-backup-manual-20260101-246000-123.zip", "SlashDesk-backup-before-restore-20260101-120000-123-not-a-guid.zip", "SlashText-backup-manual-20260101-120000-123.zip" })
                Check(!BackupService.IsManagedSnapshotName(name), "unknown name preserved " + name);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { service.CreateManualSnapshot(cancelled.Token); throw new InvalidOperationException("Expected cancellation"); }
            catch (OperationCanceledException) { Check(legacy.All(p => Hash(p.Key) == p.Value), "cancellation does not discard legacy history"); }
            File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
            Console.WriteLine($"Legacy backups: {checks.Count} checks PASS");
        }
        finally { AppPaths.Initialize(previous); }
    }
}
