using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SlashText.Models;
using SlashText.Services;

internal static class SnippetBatchBenchmark
{
    internal static List<Snippet> Corpus(int count) => Enumerable.Range(0, count).Select(index => new Snippet
    {
        Id = new Guid(index + 1, 0, 0, new byte[8]),
        Name = $"Fixture {index:D5}", Trigger = $"/fixture{index:D5}", Category = $"Category {index % 7}",
        Content = $"Fictitious content {index}.\nSecond line. **Text** {{data}}.",
        Format = index % 2 == 0 ? SnippetFormat.Plain : SnippetFormat.Markdown,
        Enabled = index % 3 != 0, IsFavorite = index % 5 == 0, IsPinned = index % 11 == 0
    }).ToList();

    internal static async Task RunAsync(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Benchmark output must be a new or empty directory.");
        Directory.CreateDirectory(output);
        var assembly = typeof(SnippetMarkdownRepository).Assembly;
        var results = new List<object>();
        foreach (var count in new[] { 100, 1_000, 5_000, 10_000 })
        {
            var items = Corpus(count);
            var markdown = SnippetMarkdownRepository.Serialize(items);
            var corpusHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(markdown)));
            await MeasureAsync("read-local", () => ReadAsync(() => SnippetMarkdownRepository.ReadRequired(markdown)));
            await MeasureAsync("read-import", () => ReadAsync(() => SnippetMarkdownRepository.ParseImport(markdown, default)));
            await MeasureAsync("read-restore", () => ReadAsync(() => SnippetMarkdownRepository.ReadRequired(markdown, forRestore: true)));
            var directory = Path.Combine(output, $"corpus-{count}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "snippets.md");
            var repository = new SnippetMarkdownRepository(path, Path.Combine(directory, "Backups"));
            await repository.SaveAsync(items);
            var unchanged = await File.ReadAllBytesAsync(path);
            await MeasureAsync("save-unchanged", () => repository.SaveAsync(items));
            if (!Enumerable.SequenceEqual(unchanged, await File.ReadAllBytesAsync(path))) throw new Exception("Unchanged bytes differ");
            await MeasureAsync("save-changed", () =>
            {
                items[0].Content = items[0].Content.EndsWith("!") ? items[0].Content[..^1] : items[0].Content + "!";
                return repository.SaveAsync(items);
            });
            if ((await repository.LoadAsync()).Count != count) throw new Exception("Saved count differs");

            Task ReadAsync(Func<List<Snippet>> read)
            {
                if (read().Count != count) throw new Exception("Parsed count differs");
                return Task.CompletedTask;
            }
            async Task MeasureAsync(string operation, Func<Task> action)
            {
                await action(); // Warm-up is deliberately excluded.
                var samples = new List<object>();
                for (var sample = 0; sample < 5; sample++)
                {
                    var allocated = GC.GetTotalAllocatedBytes(precise: true);
                    var gen0 = GC.CollectionCount(0); var gen1 = GC.CollectionCount(1); var gen2 = GC.CollectionCount(2);
                    var start = Stopwatch.GetTimestamp();
                    await action();
                    var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    samples.Add(new { ms, allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated,
                        gen0 = GC.CollectionCount(0) - gen0, gen1 = GC.CollectionCount(1) - gen1, gen2 = GC.CollectionCount(2) - gen2 });
                }
                results.Add(new { operation, count, corpusSha256 = corpusHash, samples });
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
                {
                    assemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
                    assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    runtime = RuntimeInformation.FrameworkDescription, os = Environment.OSVersion.VersionString,
                    allocations = "Process-wide managed bytes; includes async continuations and runtime noise, excludes native memory.",
                    warmupsPerOperation = 1, results
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Snippet batch benchmark: {operation}, {count}: 5 samples recorded");
            }
        }
    }
}
