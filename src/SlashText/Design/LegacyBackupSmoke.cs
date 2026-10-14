using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

internal static class LegacyBackupSmoke
{
    internal static void CreateFixtures(string root)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Choose an empty fixture folder.");
        var data = Path.Combine(root, "SlashDeskPilotData");
        var backups = Path.Combine(data, "Backups"); Directory.CreateDirectory(backups);
        var current = new Snippet { Id = Guid.Parse("11900000-0000-4000-8000-000000000001"), Name = "Atual 119", Trigger = "/atual119", Content = "VERSÃO ATUAL 119" };
        File.WriteAllText(Path.Combine(data, "snippets.md"), SnippetMarkdownRepository.Serialize([current]));
        var settings = new AppSettings { OnboardingCompleted = true, CloseToTray = false, CheckUpdatesOnStartup = false };
        settings.Capture.OutputDirectoryTemplate = Path.Combine(data, "Captures");
        File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(settings));
        var manifest = new Dictionary<string, string>();
        for (var index = 1; index <= 3; index++)
        {
            var snippet = new Snippet { Id = Guid.Parse($"11900000-0000-4000-8000-{index + 1:000000000000}"), Name = "Versão antiga " + index, Trigger = "/legado119", Content = "TEXTO ANTIGO 119 · VERSÃO " + index };
            var path = Path.Combine(backups, $"snippets-2020010{index}-000000.md");
            File.WriteAllText(path, SnippetMarkdownRepository.Serialize([snippet]));
            if (SnippetMarkdownRepository.ReadRequired(File.ReadAllText(path)).Single().Content != snippet.Content) throw new InvalidDataException("Invalid fixture shortcut.");
            manifest[Path.GetFileName(path)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }
        var external = Path.Combine(backups, "pessoal-backup-ficticio.zip");
        File.WriteAllText(external, "ARQUIVO FICTÍCIO NÃO GERENCIADO — NÃO ABRIR COMO ZIP");
        manifest[Path.GetFileName(external)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(external))).ToLowerInvariant();
        var service = new BackupService(backups, [Path.Combine(data, "snippets.md"), Path.Combine(data, "settings.json")], dataDirectory: data);
        var seed = service.CreateManualSnapshot();
        for (var index = 1; index <= 10; index++)
        {
            var path = Path.Combine(backups, $"SlashDesk-backup-202001{index:00}.zip");
            File.Copy(seed, path); File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, index, 0, 0, 0, DateTimeKind.Utc));
        }
        File.WriteAllText(Path.Combine(root, "fixture-119.json"), JsonSerializer.Serialize(manifest));
    }

    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label + ": PASS"); }
        foreach (var language in new[] { "pt-BR", "en", "es" })
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var root = Path.Combine(output, language + "-" + theme); CreateFixtures(root);
            AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, root, Path.Combine(root, "unused"), true));
            AppPaths.EnsureDataLayout(); UiText.Apply(language); ThemeService.Apply(theme);
            var service = new BackupService(); service.CreateDailySnapshot();
            var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "fixture-119.json")))!;
            void Verify()
            {
                Check(service.ListSnapshots().Count == 7, "seven generated ZIPs " + language + theme);
                Check(manifest.All(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppPaths.BackupsDirectory, p.Key)))).Equals(p.Value, StringComparison.OrdinalIgnoreCase)), "historical and unmanaged bytes unchanged " + language + theme);
            }
            Verify(); service.CreateDailySnapshot(); Verify();
            var window = new MainWindow(captureEvidence: true) { Width = 980, Height = 680 };
            window.ConfigureSettingsEvidence(await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).LoadAsync(), 980);
            var notice = (TextBlock)window.FindName("LegacyBackupPolicyText");
            Check(notice.Text == UiText.Get("Backups antigos de atalhos (.md) são preservados até você removê-los. Para recuperar uma versão, use Importar atalhos → SlashDesk."), "localized preservation policy " + language);
            Check(SettingsHelpContent.Create().Topics.Single(t => t.Id == "backup").Target == "SettingsBackupCard", "existing backup help route retained");
            var element = (FrameworkElement)window.Content; window.Content = null; LabMotion.SetReduced(element, true);
            var host = new Border { Child = element, Width = 980, Height = 680 };
            host.Measure(new Size(980, 680)); host.Arrange(new Rect(0, 0, 980, 680)); host.UpdateLayout();
            var scroll = (ScrollViewer)window.FindName("SettingsView");
            var bounds = notice.TransformToAncestor(scroll).TransformBounds(new Rect(notice.RenderSize));
            scroll.ScrollToVerticalOffset(bounds.Top - 100); host.UpdateLayout();
            Check(notice.ActualWidth > 0 && notice.ActualHeight > 0 && notice.ActualWidth < 980, "policy wraps in compact layout");
            var image = new RenderTargetBitmap(980, 680, 96, 96, PixelFormats.Pbgra32); image.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(output, language + "-" + theme + ".png"))) encoder.Save(stream);
            foreach (var name in manifest.Keys.Where(n => n.EndsWith(".md", StringComparison.Ordinal)))
            {
                var imported = await new SnippetImportService().ImportAsync(Path.Combine(AppPaths.BackupsDirectory, name), SnippetImportSource.SlashDesk);
                Check(imported.Snippets.Single().Trigger == "/legado119" && imported.Snippets.Single().Content.StartsWith("TEXTO ANTIGO 119", StringComparison.Ordinal), "historical version import " + name);
            }
            Check((await new SnippetMarkdownRepository().LoadAsync()).Single().Trigger == "/atual119", "inspection and import preview do not alter current shortcuts");
            var toRecover = await new SnippetImportService().ImportAsync(Path.Combine(AppPaths.BackupsDirectory, "snippets-20200101-000000.md"), SnippetImportSource.SlashDesk);
            await new SnippetMarkdownRepository().SaveAsync([new Snippet { Name = "Atual 119", Trigger = "/atual119", Content = "VERSÃO ATUAL 119" }, ..toRecover.Snippets]);
            Check((await new SnippetMarkdownRepository().LoadAsync()).Any(s => s.Trigger == "/legado119" && s.Content == "TEXTO ANTIGO 119 · VERSÃO 1"), "explicit imported historical content reloads");
            service.CreateManualSnapshot(); Verify(); host.Child = null;
        }
        UiText.Apply("pt-BR"); File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }
}
