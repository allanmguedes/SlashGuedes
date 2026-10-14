using SlashText.Models;
using SlashText.Services;

internal static class UsageWriteFailureChecks
{
    internal static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Require(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("Usage writes: " + label); checks.Add(label + ": PASS"); }
        var snippet = new Snippet { Id = Guid.Parse("12600000-0000-4000-8000-000000000001"), Trigger = "/fixture126" };
        foreach (var block in new[] { "locked", "readonly" })
        {
            var path = Path.Combine(root, block + ".json");
            var store = new JsonFileStore<UsageSnapshot>(path);
            await store.SaveAsync(new UsageSnapshot { Snippets = [new() { SnippetId = snippet.Id, Count = 3, CharactersSaved = 30 }],
                QuickAccent = new() { Count = 2, Characters = new() { ["á"] = 2 } } });
            var before = await File.ReadAllBytesAsync(path);
            var usage = new UsageService(path); await usage.LoadAsync();
            Require(usage.IsAvailable && !usage.HasPendingChanges, block + " healthy load");
            FileStream? locked = null;
            try
            {
                if (block == "locked") locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                else File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                await usage.RecordQuickAccentAsync('é');
                await usage.RecordAsync(snippet, snippet.Trigger.Length + 10);
                await usage.RecordQuickAccentAsync('á');
                Require(usage.HasPendingChanges && usage.IsAvailable, block + " write failure is pending, not a read failure");
                var after=await File.ReadAllBytesAsync(path);
                Require(before.SequenceEqual(after), block + " old bytes preserved");
                Require(usage.For(snippet.Id)!.Count == 4 && usage.For(snippet.Id)!.CharactersSaved == 40 &&
                    usage.QuickAccent.Count == 4 && usage.QuickAccent.Characters["á"] == 3 && usage.QuickAccent.Characters["é"] == 1,
                    block + " each failed-write insertion counted once in this session");
                var reopened = new UsageService(path); await reopened.LoadAsync();
                Require(reopened.For(snippet.Id)!.Count == 3 && reopened.QuickAccent.Count == 2 && !reopened.HasPendingChanges,
                    block + " new session sees only previously persisted counters");
                Require(!Directory.EnumerateFiles(root, "." + Path.GetFileName(path) + "-*.tmp").Any(), block + " temporary write cleaned");
            }
            finally { locked?.Dispose(); File.SetAttributes(path, FileAttributes.Normal); }
            await usage.RecordAsync(snippet, snippet.Trigger.Length + 5);
            Require(!usage.HasPendingChanges, block + " next actual insertion retries complete snapshot");
            var saved = new UsageService(path); await saved.LoadAsync();
            Require(saved.For(snippet.Id)!.Count == 5 && saved.For(snippet.Id)!.CharactersSaved == 45 && saved.QuickAccent.Count == 4 &&
                saved.QuickAccent.Characters["á"] == 3 && saved.QuickAccent.Characters["é"] == 1, block + " no replay or duplicated increments after retry");
            Require(saved.IsAvailable && !saved.HasPendingChanges, block + " cold reader sees valid saved data");
        }

        var invalid = Path.Combine(root,"invalid.json"); await File.WriteAllTextAsync(invalid,"{invalid");
        var invalidBytes = await File.ReadAllBytesAsync(invalid);
        var unavailable = new UsageService(invalid); await unavailable.LoadAsync();
        await unavailable.RecordAsync(snippet, 50); await unavailable.RecordQuickAccentAsync('é');
        var invalidAfter=await File.ReadAllBytesAsync(invalid);
        Require(!unavailable.IsAvailable && !unavailable.HasPendingChanges && invalidBytes.SequenceEqual(invalidAfter),
            "unreadable data never replaced by a pending empty snapshot");

        var concurrentPath = Path.Combine(root,"concurrent.json");
        var concurrent = new UsageService(concurrentPath); await concurrent.LoadAsync();
        await Task.WhenAll(Enumerable.Range(0,20).Select(_=>concurrent.RecordQuickAccentAsync('á')));
        await Task.WhenAll(Enumerable.Range(0,20).Select(_=>concurrent.RecordAsync(snippet,20)));
        var concurrentReload = new UsageService(concurrentPath); await concurrentReload.LoadAsync();
        Require(concurrentReload.QuickAccent.Count==20 && concurrentReload.QuickAccent.Characters["á"]==20 &&
            concurrentReload.For(snippet.Id)!.Count==20 && !concurrent.HasPendingChanges, "concurrent observers serialize exact counts");

        var missing = Path.Combine(root,"missing.json");
        var first = new UsageService(missing); await first.LoadAsync(); await first.RecordQuickAccentAsync('ã');
        Require(File.Exists(missing) && !first.HasPendingChanges, "first use creates valid statistics normally");
        File.WriteAllLines(Path.Combine(root,"result.txt"),checks);
        Console.WriteLine($"Usage writes: {checks.Count} checks PASS");
    }
}
