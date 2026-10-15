using System.Globalization;
using System.Text;
using SlashText.Localization;
using SlashText.Models;
using SlashText.Services;

internal static class SnippetBatchChecks
{
    internal static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var checks = 0;
        void Require(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("Snippet batch: " + label);
            checks++;
        }

        var path = Path.Combine(root, "snippets.md");
        var repository = new SnippetMarkdownRepository(path, Path.Combine(root, "Backups"));
        var fixture = new[]
        {
            new Snippet { Name = "Ação", Trigger = "/AÇÃO", Category = "Equipe", Enabled = false,
                IsFavorite = true, IsPinned = true, ConfirmKeys = ["Tab"], Format = SnippetFormat.Markdown,
                Content = "**Olá**\n````\n{{campo:Nome}}\n{{tab}}fim" },
            new Snippet { Name = "Colon legacy", Trigger = ":antigo", HasLegacyIncompatibleTrigger = true },
            new Snippet { Name = "Decomposed legacy", Trigger = "/cafe\u0301", HasLegacyIncompatibleTrigger = true },
            new Snippet { Name = "Punctuation legacy", Trigger = "/com.ponto", HasLegacyIncompatibleTrigger = true },
            new Snippet { Name = "Prefix", Trigger = "/a" },
            new Snippet { Name = "Longer prefix", Trigger = "/ab" }
        };
        await repository.SaveAsync(fixture);
        var original = await File.ReadAllBytesAsync(path);
        var markdown = Encoding.UTF8.GetString(original);
        foreach (var parsed in new[]
        {
            await repository.LoadAsync(),
            SnippetMarkdownRepository.ParseImport(markdown, default),
            SnippetMarkdownRepository.ReadRequired(markdown, forRestore: true)
        })
        {
            Require(parsed.Count == fixture.Length, "all records in local/import/restore");
            foreach (var expected in fixture)
            {
                var actual = parsed.Single(item => item.Id == expected.Id);
                Require(actual.Trigger == expected.Trigger && actual.Name == expected.Name &&
                    actual.Category == expected.Category && actual.Content == expected.Content &&
                    actual.Format == expected.Format && actual.Enabled == expected.Enabled &&
                    actual.IsFavorite == expected.IsFavorite && actual.IsPinned == expected.IsPinned &&
                    actual.HasLegacyIncompatibleTrigger == expected.HasLegacyIncompatibleTrigger &&
                    actual.ConfirmKeys.SequenceEqual(expected.ConfirmKeys), "round-trip metadata and legacy trigger");
            }
        }
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var unchangedTime = File.GetLastWriteTimeUtc(path);
        await repository.SaveAsync(fixture.Reverse());
        Require(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(path)) &&
            File.GetLastWriteTimeUtc(path) == unchangedTime, "unchanged save avoids replacement");

        foreach (var (left, right) in new[]
        {
            ("/TESTE", "/teste"), ("/café", "/cafe\u0301"),
            (":ANTIGO", ":antigo"), ("/com.PONTO", "/com.ponto")
        })
        {
            foreach (var reverse in new[] { false, true })
            {
                var pair = new[]
                {
                    new Snippet { Name = "First", Trigger = left, HasLegacyIncompatibleTrigger = true },
                    new Snippet { Name = "Second", Trigger = right, Enabled = false, HasLegacyIncompatibleTrigger = true }
                };
                if (reverse) Array.Reverse(pair);
                var duplicateMarkdown = SnippetMarkdownRepository.Serialize(pair);
                foreach (var read in new Func<List<Snippet>>[]
                {
                    () => SnippetMarkdownRepository.ReadRequired(duplicateMarkdown),
                    () => SnippetMarkdownRepository.ParseImport(duplicateMarkdown, default),
                    () => SnippetMarkdownRepository.ReadRequired(duplicateMarkdown, forRestore: true)
                })
                {
                    try { read(); throw new Exception("Duplicate was accepted"); }
                    catch (InvalidDataException exception)
                    {
                        Require(exception.Message == UiText.Format($"O atalho '{pair[1].Trigger}' está duplicado."),
                            "same duplicate message in all read paths");
                    }
                }
                await RejectSaveAsync(pair, "duplicate save refuses replacement");
            }
        }

        await RejectSaveAsync([new Snippet { Name = "Invalid", Trigger = "/bad!" }], "new invalid trigger");
        await RejectSaveAsync([new Snippet { Name = " ", Trigger = "/blank" }], "empty name");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            await RejectSaveAsync([
                new Snippet { Name = "One", Trigger = "/I" }, new Snippet { Name = "Two", Trigger = "/i" }
            ], "normalization remains invariant under Turkish culture");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await CancelledAsync(() => repository.SaveAsync(fixture, cancelled.Token));
        await CancelledAsync(async () => { await repository.LoadAsync(cancelled.Token); });
        foreach (var read in new Action[]
        {
            () => SnippetMarkdownRepository.ReadRequired(markdown, cancelled.Token),
            () => SnippetMarkdownRepository.ParseImport(markdown, cancelled.Token),
            () => SnippetMarkdownRepository.ReadRequired(markdown, cancelled.Token, forRestore: true)
        }) await CancelledAsync(() => { read(); return Task.CompletedTask; });
        using var duringEnumeration = new CancellationTokenSource();
        await CancelledAsync(() => repository.SaveAsync(CancelDuringEnumeration(), duringEnumeration.Token));
        Require(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(path)), "cancelled operations leave original bytes");

        // An exclusive reader permits reads but prevents the atomic replacement on Windows.
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                await repository.SaveAsync([new Snippet { Name = "Changed", Trigger = "/changed" }]);
                throw new Exception("Locked replacement was accepted");
            }
            catch (IOException) { Require(true, "failed atomic replacement reported"); }
        }
        Require(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(path)), "failed replacement preserves original");
        Require(!Directory.EnumerateFiles(root, ".*.tmp").Any(), "no pending temporary files");

        var incomplete = markdown[..markdown.LastIndexOf('`')];
        await File.WriteAllTextAsync(path, incomplete);
        var incompleteBytes = await File.ReadAllBytesAsync(path);
        try { await repository.LoadAsync(); throw new Exception("Incomplete file was accepted"); }
        catch (InvalidDataException) { Require(true, "incomplete file refused"); }
        Require(Enumerable.SequenceEqual(incompleteBytes, await File.ReadAllBytesAsync(path)), "load never rewrites invalid input");

        var large = SnippetBatchBenchmark.Corpus(ExternalInputBudget.SnippetCount);
        var largeMarkdown = SnippetMarkdownRepository.Serialize(large);
        Require(SnippetMarkdownRepository.ParseImport(largeMarkdown, default).Count == large.Count,
            "existing import limit remains accepted");
        var tooMany = largeMarkdown + SnippetMarkdownRepository.Serialize([
            new Snippet { Name = "Extra", Trigger = "/extra" }
        ]);
        try { SnippetMarkdownRepository.ParseImport(tooMany, default); throw new Exception("Import budget was bypassed"); }
        catch (InvalidDataException exception)
        { Require(exception.Message == ExternalInputBudget.Exceeded().Message, "import limit still enforced"); }
        Console.WriteLine($"Snippet batch: {checks} checks passed");

        async Task RejectSaveAsync(IEnumerable<Snippet> items, string label)
        {
            try { await repository.SaveAsync(items); throw new Exception("Invalid save was accepted: " + label); }
            catch (InvalidDataException) { Require(true, label); }
            Require(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(path)), label + " preserves bytes");
        }
        async Task CancelledAsync(Func<Task> operation)
        {
            try { await operation(); throw new Exception("Cancellation was ignored"); }
            catch (OperationCanceledException) { Require(true, "cancellation observed"); }
        }
        IEnumerable<Snippet> CancelDuringEnumeration()
        {
            yield return fixture[0];
            duringEnumeration.Cancel();
            yield return fixture[1];
        }
    }
}
