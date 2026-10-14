using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlashText.Models;
using SlashText.Services;

internal static class BackupCompatibilityChecks
{
    private static int _checks;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Backup compatibility: " + label); _checks++; }
    private static AppDataEnvironment EnvironmentFor(string root) => new(DistributionMode.Portable, Path.Combine(root, "app"), Path.Combine(root, "unused"), true);

    internal static void Archive(string path, Dictionary<string, byte[]> payload, int schema = 2)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var pair in payload)
        {
            using var stream = zip.CreateEntry(pair.Key).Open(); stream.Write(pair.Value);
        }
        using var manifest = zip.CreateEntry("backup-manifest.json").Open();
        JsonSerializer.Serialize(manifest, new
        {
            schemaVersion = schema, includesAssets = payload.Keys.Any(k => k.StartsWith("assets/")),
            includesCaptureEmotes = payload.Keys.Any(k => k.StartsWith("capture-emotes/")),
            files = payload.Select(p => new { path = p.Key, size = p.Value.LongLength, sha256 = Convert.ToHexString(SHA256.HashData(p.Value)).ToLowerInvariant() }).ToArray()
        }, Json);
    }
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static string Markdown(string trigger = "/restaurado117", string name = "Backup válido 117", string id = "e782332d-3665-43e6-8099-c5c077964110") =>
        $"# Meus atalhos\n\n## {trigger}\n<!-- slashtext:{{\"id\":\"{id}\",\"name\":\"{name}\",\"category\":\"Geral\",\"enabled\":true}} -->\n```text\nBACKUP VÁLIDO #117\n```\n";
    private static Dictionary<string, byte[]> Payload() => new()
    {
        ["snippets.md"] = Bytes(Markdown()),
        ["settings.json"] = Bytes("{\"theme\":\"Dark\",\"language\":\"pt-BR\",\"shortcutCategoryIcons\":{\"Geral\":\"folder\",\"geral\":\"star\"}}"),
        ["usage.json"] = Bytes("{\"snippets\":[{\"snippetId\":\"e782332d-3665-43e6-8099-c5c077964110\",\"count\":4,\"charactersSaved\":220}],\"quickAccent\":{\"count\":2,\"characters\":{\"á\":1,\"Á\":1}}}"),
        ["capture-history.json"] = Bytes("[{\"id\":\"fixture117\",\"type\":\"region\",\"filePath\":\"C:\\\\fixture117-unavailable.png\",\"width\":2,\"height\":2,\"requiresFileApproval\":false}]")
    };
    private static Dictionary<string, string> Hashes(string data)
    {
        // Hash persisted application targets, not Windows File.Replace's transient RF*.TMP scratch.
        var files = new[] { "snippets.md", "settings.json", "usage.json", "capture-history.json" }
            .Select(name => Path.Combine(data, name)).Where(File.Exists)
            .Concat(new[] { "assets", "capture-emotes" }.Select(name => Path.Combine(data, name))
                .Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)));
        return files.ToDictionary(p => Path.GetRelativePath(data, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    }
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
    private static async Task Seed(string root)
    {
        AppPaths.Initialize(EnvironmentFor(root)); AppPaths.EnsureDataLayout();
        await new SnippetMarkdownRepository().SaveAsync([new Snippet { Name = "Atual 117", Trigger = "/atual117", Content = "DADOS ATUAIS 117" }]);
        await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).SaveAsync(new() { Theme = "Light" });
        await new JsonFileStore<UsageSnapshot>(AppPaths.UsageFile).SaveAsync(new() { QuickAccent = new() { Count = 7 } });
        await new CaptureHistoryStore(AppPaths.CaptureHistoryFile).SaveAsync([new() { Id = "current117", FilePath = "C:\\unavailable-current117.png", RequiresFileApproval = false }]);
    }

    internal static async Task RunAsync(string parent)
    {
        var previous = AppPaths.Current; var root = Path.Combine(parent, "backup-compatibility"); Directory.CreateDirectory(root);
        try
        {
            var invalid = new (string File, string Text)[]
            {
                ("snippets.md", "## /fixture\n<!-- slashtext:{bad -->\n```text\nx\n```\n"),
                ("snippets.md", "## /fixture\n<!-- slashtext:{\"name\":null} -->\n```text\nx\n```\n"),
                ("snippets.md", "## /fixture\n<!-- slashtext:{\"name\":\"n\"} -->\n```text\nx\n"),
                ("snippets.md", Markdown() + Markdown()),
                ("snippets.md", Markdown() + Markdown("/outro117")),
                ("snippets.md", Markdown() + "\n<!-- slashtext:{bad -->\n"),
                ("snippets.md", "## /fixture\n<!-- slashtext:{\"name\":\"a\",\"Name\":\"b\"} -->\n```text\nx\n```\n"),
                ("settings.json", "{\"theme\":{\"unexpected\":true}}"),
                ("settings.json", "null"), ("settings.json", "[]"), ("settings.json", "{"),
                ("settings.json", "{\"theme\":null}"), ("settings.json", "{\"capture\":null}"),
                ("settings.json", "{\"quickAccentCharacterSets\":[null]}"),
                ("settings.json", "{\"capture\":{\"recording\":{\"audio\":null}}}"),
                ("settings.json", "{\"capture\":{\"jpegQuality\":\"wrong\"}}"),
                ("settings.json", "{\"theme\":\"Dark\",\"Theme\":\"Light\"}"),
                ("usage.json", "null"), ("usage.json", "{\"snippets\":[null]}"),
                ("usage.json", "{\"snippets\":[{\"count\":\"wrong\"}]}"),
                ("usage.json", "{\"quickAccent\":{\"characters\":null}}"),
                ("usage.json", "{\"snippets\":[{\"count\":-1}]}"),
                ("usage.json", "[{\"lastUsedAt\":\"wrong\"}]"),
                ("usage.json", "{\"quickAccent\":{\"characters\":{\"á\":1,\"á\":2}}}"),
                ("capture-history.json", "{}"), ("capture-history.json", "[null]"),
                ("capture-history.json", "[{\"id\":\"good\"},{\"width\":\"wrong\"}]"),
                ("capture-history.json", "[{\"createdAt\":\"wrong\"}]"),
                ("capture-history.json", "[{\"id\":null}]"),
                ("capture-history.json", "[{\"id\":\"dup\"},{\"id\":\"dup\"}]"),
                ("capture-history.json", "[{\"width\":-1}]"),
                ("capture-history.json", "[{\"durationSeconds\":1e999}]")
            };
            foreach (var schema in new[] { 1, 2 })
            for (var i = 0; i < invalid.Length; i++)
            {
                var box = Path.Combine(root, $"invalid-{schema}-{i}"); await Seed(box);
                var payload = Payload(); payload[invalid[i].File] = Bytes(invalid[i].Text);
                var archive = Path.Combine(box, "incoming.zip"); Archive(archive, payload, schema);
                BackupService.ValidateSnapshot(archive); Check(true, "archive integrity alone is valid");
                var before = Hashes(AppPaths.DataDirectory); var backup = new BackupService(); var applyCalls = 0;
                backup.BeforeRestoreApply = (_, _) => applyCalls++;
                try { backup.RestoreSnapshot(archive); throw new InvalidOperationException("incompatible archive applied"); }
                catch (InvalidDataException error)
                {
                    Check(error.Message.Contains(invalid[i].File) && error.InnerException is null, "localized refusal without raw document content");
                }
                Check(applyCalls == 0 && Same(before, Hashes(AppPaths.DataDirectory)), "refusal precedes every active mutation");
                Check(Directory.GetFiles(AppPaths.BackupsDirectory, "*.zip").Length == 0 && !Directory.GetDirectories(AppPaths.BackupsDirectory, ".restore-transaction-*").Any(), "no fallback loader or pending restore on validation failure");
                Check((await new SnippetMarkdownRepository().LoadAsync()).Single().Trigger == "/atual117", "current module remains loadable");
            }
            foreach (var schema in new[] { 1, 2 })
            {
                var box = Path.Combine(root, "valid-" + schema); await Seed(box); var payload = Payload();
                var archive = Path.Combine(box, "incoming.zip"); Archive(archive, payload, schema); new BackupService().RestoreSnapshot(archive);
                foreach (var name in new[] { "snippets.md", "settings.json", "usage.json" })
                    Check(File.ReadAllBytes(Path.Combine(AppPaths.DataDirectory, name)).SequenceEqual(payload[name]), "valid bytes stay unchanged, no normalization/default rewrite");
                Check((await new SnippetMarkdownRepository().LoadAsync()).Single().Trigger == "/restaurado117", "real snippet reader reloads");
                var settings = await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).LoadAsync(); Check(settings.Theme == "Dark" && settings.ShortcutCategoryIcons.Count == 2, "real settings reader keeps case-distinct dictionary data");
                var usage = new UsageService(); await usage.LoadAsync(); Check(usage.IsAvailable && usage.Records.Single().Count == 4 && usage.QuickAccent.Characters.Count == 2, "real usage reader keeps uppercase/lowercase accents distinct");
                var history = await new CaptureHistoryStore(AppPaths.CaptureHistoryFile).LoadAsync(); Check(history.Single().RequiresFileApproval && history[0].FilePath.Contains("fixture117"), "history reloads with local trust transformation, no reference opened");
                Check(new BackupService().ListSnapshots().Any(p => p.Name.StartsWith("SlashDesk-backup-before-restore-")), "verified safety backup exists");
            }
            foreach (var schema in new[] { 1, 2 })
            {
                var box = Path.Combine(root, "local-trust-" + schema); await Seed(box); var payload = Payload();
                payload["capture-history.json"] = Bytes("[{\"id\":\"local-trust\",\"requiresFileApproval\":{\"ignored\":true},\"RequiresFileApproval\":false}]");
                var archive = Path.Combine(box, "trust.zip"); Archive(archive, payload, schema); new BackupService().RestoreSnapshot(archive);
                var records = await new CaptureHistoryStore(AppPaths.CaptureHistoryFile).LoadAsync();
                Check(records.Single().RequiresFileApproval, "archive approval fields are discarded before semantic validation, never trusted");
            }
            foreach (var legacy in new[] { "[]", "{\"snippets\":[],\"quickAccent\":{\"count\":2}}" })
            {
                var box = Path.Combine(root, "legacy-" + Guid.NewGuid().ToString("N")); await Seed(box);
                var payload = new Dictionary<string, byte[]> { ["usage.json"] = Bytes(legacy), ["settings.json"] = Bytes("{}") };
                var beforeSnippet = File.ReadAllBytes(AppPaths.SnippetsFile); var beforeHistory = File.ReadAllBytes(AppPaths.CaptureHistoryFile);
                var archive = Path.Combine(box, "legacy.zip"); Archive(archive, payload, 1); new BackupService().RestoreSnapshot(archive);
                Check(File.ReadAllBytes(AppPaths.SnippetsFile).SequenceEqual(beforeSnippet) && File.ReadAllBytes(AppPaths.CaptureHistoryFile).SequenceEqual(beforeHistory), "partial legacy archive leaves omitted modules untouched");
                var service = new UsageService(); await service.LoadAsync(); Check(service.IsAvailable, "legacy usage array/partial object reloads");
                Check(File.ReadAllText(AppPaths.SettingsFile) == "{}", "omitted legacy preferences remain omitted on disk");
            }
            var cancelBox = Path.Combine(root, "cancel"); await Seed(cancelBox); var beforeCancel = Hashes(AppPaths.DataDirectory);
            var validArchive = Path.Combine(cancelBox, "valid.zip"); Archive(validArchive, Payload());
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { new BackupService().RestoreSnapshot(validArchive, cancelled.Token); throw new InvalidOperationException("must cancel"); }
            catch (OperationCanceledException) { Check(Same(beforeCancel, Hashes(AppPaths.DataDirectory)), "cancelled restore preserves active data"); }
            Console.WriteLine($"Backup compatibility: {_checks} checks passed (schemas 1/2; staged metadata; real reader reloads).");
        }
        finally { AppPaths.Initialize(previous); }
    }

    internal static void CreatePilotFixtures(string output)
    {
        Directory.CreateDirectory(output);
        var payload = Payload(); Archive(Path.Combine(output, "117-backup-valido.zip"), payload);
        var badSnippet = Payload(); badSnippet["snippets.md"] = Bytes("## /fixture117\n<!-- slashtext:{bad -->\n```text\nFICTÍCIO\n```\n");
        Archive(Path.Combine(output, "117-atalhos-incompativeis.zip"), badSnippet);
        var badSettings = Payload(); badSettings["settings.json"] = Bytes("{\"theme\":{\"unexpected\":true}}");
        Archive(Path.Combine(output, "117-preferencias-incompativeis.zip"), badSettings);
        var badHistory = Payload(); badHistory["capture-history.json"] = Bytes("[{\"id\":\"good\"},{\"width\":\"wrong\"}]");
        Archive(Path.Combine(output, "117-historico-incompativel.zip"), badHistory);
        Archive(Path.Combine(output, "117-legado-valido.zip"), new() { ["snippets.md"] = Bytes(Markdown("/legado117", "Legado válido 117")), ["usage.json"] = Bytes("[]") }, 1);
        using var blue = new System.Drawing.Bitmap(160, 100); using var graphics = System.Drawing.Graphics.FromImage(blue);
        graphics.Clear(System.Drawing.Color.RoyalBlue); blue.Save(Path.Combine(output, "azul117.png"), System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("Pilot fixtures generated: 5 inert archives and one blue PNG.");
    }
}
