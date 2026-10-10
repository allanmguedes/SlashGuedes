using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

/// <summary>Real usage/history readers and WPF; no hooks, collection or user data.</summary>
internal static class StatisticsSmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable,
            Path.Combine(output, "data-fixture"), Path.Combine(output, "unused-installed"), isCapturePilot: true));
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        var snippets = new[]
        {
            new Snippet { Trigger = "/assinatura", Name = "Assinatura" },
            new Snippet { Trigger = "/boasvindas", Name = "Boas-vindas" },
            new Snippet { Trigger = "/retorno", Name = "Retorno" }
        };
        var captures = new[]
        {
            Capture("regiao"), Capture("REGIAO"), Capture("monitor"), Capture("janela"),
            Capture("regiao", "gif"), Capture("monitor", "video"), Capture("longa"), Capture("arquivo")
        };
        var usage = new UsageSnapshot
        {
            Snippets =
            [
                new() { SnippetId = snippets[0].Id, Count = 96, CharactersSaved = 10000 },
                new() { SnippetId = snippets[1].Id, Count = 71, CharactersSaved = 5000 },
                new() { SnippetId = snippets[2].Id, Count = 53, CharactersSaved = 3000 },
                new() { SnippetId = Guid.NewGuid(), Count = 28, CharactersSaved = 420 }
            ],
            QuickAccent = new() { Count = 612, Characters = new() { ["á"] = 302, ["ã"] = 204, ["é"] = 106 } }
        };
        var checks = new List<string>();
        foreach (var theme in new[] { "Light", "Dark", "System" })
        foreach (var size in new[] { new Size(1440, 900), new Size(980, 680) })
        {
            ThemeService.Apply(theme);
            var window = new MainWindow(captureEvidence: true);
            window.SetStatisticsSnippetsForEvidence(snippets);
            var root = (FrameworkElement)window.Content; window.Content = null;
            LabMotion.SetReduced(root, true);
            var host = new Border { Child = root, Width = size.Width, Height = size.Height };
            host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
            void Layout() { host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
            T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            var store = new JsonFileStore<UsageSnapshot>(AppPaths.UsageFile);
            var history = new CaptureHistoryStore(AppPaths.CaptureHistoryFile);
            await store.SaveAsync(new UsageSnapshot()); await history.SaveAsync([]);
            await window.ReloadStatisticsForEvidenceAsync(size.Width); Layout();
            Require(Control<TextBlock>("TotalExpansionsText").Text == "0" && Control<TextBlock>("TimeSavedText").Text == "0 min", "Empty data stays zero, not illustrative metrics");
            Require(Control<TextBlock>("QuickAccentFavoriteText").Text == "—" && Control<StackPanel>("StatisticsRankingPanel").Children.Count == 1, "Empty ranking and accents have guidance");
            Require(Control<ProgressBar>("StatisticsGifBar").Value == 0 && Control<Border>("StatisticsErrorPanel").Visibility == Visibility.Collapsed, "Empty history is available with zero bars");
            SaveImage(host, output, $"stats-{theme}-{size.Width}-empty", size, 1);

            await store.SaveAsync(usage); await history.SaveAsync(captures);
            var usageBefore = await File.ReadAllBytesAsync(AppPaths.UsageFile);
            var historyBefore = await File.ReadAllBytesAsync(AppPaths.CaptureHistoryFile);
            await window.ReloadStatisticsForEvidenceAsync(size.Width); Layout();
            Require(Control<TextBlock>("TotalExpansionsText").Text == "248" && Control<TextBlock>("CharactersSavedText").Text == "18.420", "Same snapshot retains 3.2.0 totals, including removed shortcut records");
            Require(Control<TextBlock>("TimeSavedText").Text == "93 min" && Control<TextBlock>("AverageCharactersText").Text == "74", "Existing estimate and mean, not the Lab's illustrative seconds");
            Require(Control<TextBlock>("UsedSnippetsText").Text == "4" && Control<TextBlock>("QuickAccentTotalText").Text == "612" && Control<TextBlock>("QuickAccentFavoriteText").Text == "á", "Used records and actual accent counters preserved");
            Require(Control<TextBlock>("CaptureTotalText").Text == "8" && Control<TextBlock>("CaptureRegionTotalText").Text == "3" && Control<TextBlock>("CaptureMonitorTotalText").Text == "2" && Control<TextBlock>("CaptureWindowTotalText").Text == "1" && Control<TextBlock>("CaptureGifTotalText").Text == "1" && Control<TextBlock>("CaptureMp4TotalText").Text == "1", "History classifications remain case-insensitive and overlapping");
            Require(Control<ProgressBar>("StatisticsRegionBar").Value == 37.5 && Control<ProgressBar>("StatisticsGifBar").Value == 12.5, "Bars use the actual history denominator");
            Require(Control<StackPanel>("StatisticsRankingPanel").Children.Count == 3 && Control<WrapPanel>("QuickAccentCharactersPanel").Children.Count == 2, "Rankings preserve real existing shortcuts and eight-character limit without duplicate favorite");
            var firstRank = (Grid)Control<StackPanel>("StatisticsRankingPanel").Children[0];
            var firstBody = (StackPanel)firstRank.Children[1];
            Require(((ProgressBar)firstBody.Children[1]).Value == 100, "Leader is the ranking reference, not a fictitious percentage");
            var usageAfter = await File.ReadAllBytesAsync(AppPaths.UsageFile);
            var historyAfter = await File.ReadAllBytesAsync(AppPaths.CaptureHistoryFile);
            Require(usageBefore.SequenceEqual(usageAfter) && historyBefore.SequenceEqual(historyAfter), "Opening/viewing statistics never writes either input file");
            Require(Control<System.Windows.Controls.Primitives.UniformGrid>("StatisticsMetricsGrid").Columns == (size.Width < 1100 ? 2 : 4) && Grid.GetRow(Control<Border>("StatisticsCaptureCard")) == (size.Width < 1100 ? 1 : 0), "Responsive layout retains every section");
            foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d }) SaveImage(host, output, $"stats-{theme}-{size.Width}", size, scale);
            var scroll = Control<ScrollViewer>("StatisticsView"); scroll.ScrollToBottom(); Layout();
            Require(scroll.HorizontalOffset == 0 && Control<Border>("StatisticsPrivacyCard").TransformToAncestor(host).Transform(new Point()).Y < size.Height, "Bottom cards remain reachable without horizontal scrolling");
            SaveImage(host, output, $"stats-{theme}-{size.Width}-bottom", size, 1); scroll.ScrollToTop(); Layout();

            await window.RecordStatisticsForEvidenceAsync(snippets[0]);
            Require(Control<TextBlock>("TotalExpansionsText").Text == "249" && Control<TextBlock>("QuickAccentTotalText").Text == "613", "One real service insertion increments each counter once");
            var afterRecord = await new JsonFileStore<UsageSnapshot>(AppPaths.UsageFile).LoadAsync();
            Require(afterRecord.Snippets.Single(r => r.SnippetId == snippets[0].Id).Count == 97 && afterRecord.QuickAccent.Characters["é"] == 107, "Increment survives existing JSON persistence");
            await history.SaveAsync(captures.Take(7).ToArray()); await window.ReloadStatisticsForEvidenceAsync(size.Width);
            Require(Control<TextBlock>("CaptureTotalText").Text == "7", "Capture totals follow retained history, not a fabricated lifetime counter");

            var longSnippet = new Snippet { Trigger = "/" + new string('a', 120), Name = new string('B', 150) };
            window.SetStatisticsSnippetsForEvidence([longSnippet]);
            await File.WriteAllTextAsync(AppPaths.UsageFile, JsonSerializer.Serialize(new[] { new UsageRecord { SnippetId = longSnippet.Id, Count = long.MaxValue, CharactersSaved = long.MaxValue } }));
            await window.ReloadStatisticsForEvidenceAsync(size.Width); Layout();
            Require(Control<TextBlock>("TotalExpansionsText").Text == long.MaxValue.ToString("N0", culture) && Control<TextBlock>("TotalExpansionsText").FontSize == 22, "Legacy arrays and large valid counts preserve full values");
            Require(AutomationProperties.GetName(Control<TextBlock>("TotalExpansionsText")).Contains(long.MaxValue.ToString("N0", culture)), "Large totals have full textual accessibility");
            SaveImage(host, output, $"stats-{theme}-{size.Width}-large", size, 1);
            await File.WriteAllTextAsync(AppPaths.UsageFile, "{invalid-json");
            var invalidBefore = await File.ReadAllBytesAsync(AppPaths.UsageFile);
            await window.ReloadStatisticsForEvidenceAsync(size.Width); Layout();
            Require(Control<Border>("StatisticsErrorPanel").Visibility == Visibility.Visible && Control<TextBlock>("TotalExpansionsText").Text == "—" && Control<TextBlock>("CaptureTotalText").Text == "7", "Unavailable usage never claims zero/success or blocks history");
            await window.RecordStatisticsForEvidenceAsync(snippets[0]);
            var invalidAfter = await File.ReadAllBytesAsync(AppPaths.UsageFile);
            Require(invalidBefore.SequenceEqual(invalidAfter), "Unavailable data is not overwritten by viewing or auxiliary counter updates");
            SaveImage(host, output, $"stats-{theme}-{size.Width}-unavailable", size, 1);
            await File.WriteAllTextAsync(AppPaths.UsageFile, "[null]");
            await window.ReloadStatisticsForEvidenceAsync(size.Width);
            Require(Control<Border>("StatisticsErrorPanel").Visibility == Visibility.Visible, "Malformed legacy records cannot interrupt rendering or other consumers");
            await store.SaveAsync(new UsageSnapshot { Snippets =
                [new() { SnippetId = longSnippet.Id, Count = long.MaxValue }, new() { Count = 1 }] });
            await window.ReloadStatisticsForEvidenceAsync(size.Width);
            Require(Control<TextBlock>("TotalExpansionsText").Text == "—", "Overflow in an edited usage file is contained within statistics");
            await store.SaveAsync(usage);
            using (var lockedFile = File.Open(AppPaths.UsageFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await window.ReloadStatisticsForEvidenceAsync(size.Width);
                Require(Control<Border>("StatisticsErrorPanel").Visibility == Visibility.Visible, "IO failure stays local to the unavailable counters");
            }
            window.SetStatisticsSnippetsForEvidence(snippets);
            await window.ReloadStatisticsForEvidenceAsync(size.Width); Layout();
            Require(Control<Border>("StatisticsErrorPanel").Visibility == Visibility.Collapsed && Control<TextBlock>("TotalExpansionsText").Text == "248", "Successful subsequent loading restores the real counts");
            foreach (var topic in StatisticsHelpContent.Create().Topics)
                Require(window.FindName(topic.Target!) is FrameworkElement, "Help target exists: " + topic.Id);
            var guide = new ScreenHelpWindow(StatisticsHelpContent.Create()); guide.SearchForEvidence("estimativa");
            Require(guide.ResultCount > 0, "Statistics help is searchable");
            foreach (var topic in StatisticsHelpContent.Create().Topics) guide.OpenTopic(topic.Id);
            guide.Close();
            checks.Add($"{theme} {size}: real JSON/history, immutable viewing, 3.2.0 calculations, empty/unavailable/large states, increments, responsive sections and 100/125/150/200% render OK");
            root = null!; host.Child = null; window.Close();
        }
        await File.WriteAllLinesAsync(Path.Combine(output, "result.txt"), checks.Append("Fixture uses isolated real storage. No native hook, capture, content collection or physical DPI simulation."));
    }

    private static CaptureRecord Capture(string type, string media = "image") => new()
    { Type = type, MediaKind = media, FilePath = "fixture." + (media == "video" ? "mp4" : media == "gif" ? "gif" : "png"), Width = 800, Height = 600 };
    private static void SaveImage(FrameworkElement host, string output, string name, Size size, double scale)
    {
        host.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        image.Render(host);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output, $"{name}-{(int)(scale * 100)}.png")); encoder.Save(file);
    }
    private static void Require(bool condition, string scenario)
    { if (!condition) throw new InvalidOperationException("Statistics smoke: " + scenario); }
}
