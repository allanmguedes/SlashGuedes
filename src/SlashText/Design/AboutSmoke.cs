using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SlashText.Services;

namespace SlashText.Design;

internal static class AboutSmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), Path.Combine(output, "unused-installed"), isCapturePilot: true));
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        var checks = new List<string>();
        var settings = AppPaths.SettingsFile;
        Directory.CreateDirectory(AppPaths.DataDirectory);
        await File.WriteAllTextAsync(settings, "{\"Theme\":\"Dark\",\"CheckUpdatesOnStartup\":true}");
        var originalSettings = await File.ReadAllBytesAsync(settings);
        foreach (var theme in new[] { "Light", "Dark", "System" })
        foreach (var size in new[] { new Size(1440, 900), new Size(980, 680) })
        {
            ThemeService.Apply(theme);
            var launched = new List<Uri>();
            var window = new MainWindow(captureEvidence: true);
            window.ConfigureAboutEvidence(size.Width, launched.Add);
            T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            var root = (FrameworkElement)window.Content; window.Content = null;
            LabMotion.SetReduced(root, true);
            var host = new Border { Child = root, Width = size.Width, Height = size.Height };
            host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
            void Layout() { host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
            Layout(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded); Layout();
            var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version!.ToString(3);
            Require(Control<TextBlock>("AboutVersionText").Text == "Versão " + version, "Actual assembly version");
            Require(Control<TextBlock>("AboutDistributionText").Text.Contains("portátil"), "Actual distribution");
            Require(!Control<Button>("CheckUpdatesButton").IsEnabled, "Pilot cannot check or apply updates");
            Require(Grid.GetRow(Control<Border>("AboutProjectCard")) == (size.Width < 1100 ? 2 : 0), "Responsive columns");
            foreach (var name in new[] { "AboutProjectButton", "AboutCurrentReleaseNotesButton", "AboutReportIssueButton", "AboutLicenseButton" })
            {
                var button = Control<Button>(name);
                Require(button.Focusable && button.Content is string { Length: > 0 }, "Keyboard and accessible button name: " + name);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Require(launched.Count == 4 && launched.All(uri => uri.Host == "github.com"), "Actual link handlers");
            Require(launched[1].AbsolutePath.EndsWith("/releases/tag/v" + version) && launched[3].AbsolutePath.EndsWith("/LICENSE"), "Installed notes and MIT destinations");
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 }) SaveImage(host, output, $"about-{theme}-{size.Width}", size, scale);
            var scroll = Control<ScrollViewer>("AboutView"); scroll.ScrollToBottom(); Layout();
            var card = Control<Border>("AboutUpdatesCard");
            var bounds = card.TransformToAncestor(scroll).TransformBounds(new Rect(card.RenderSize));
            Require(scroll.HorizontalOffset == 0 && bounds.Bottom <= scroll.ActualHeight + 1, "Updates reachable without horizontal scroll");
            SaveImage(host, output, $"about-{theme}-{size.Width}-bottom", size, 1);
            window.ConfigureAboutEvidence(size.Width, _ => throw new IOException("Browser unavailable fixture"));
            Control<Button>("AboutProjectButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Control<TextBlock>("AboutLinkStatusText").Visibility == Visibility.Visible, "Browser failure is readable inline");
            scroll.ScrollToBottom(); Layout(); SaveImage(host, output, $"about-{theme}-{size.Width}-link-error", size, 1);
            checks.Add($"{theme} {size}: real version, distribution, icon, links, keyboard, responsive scroll, browser error; 100/125/150/200% renders: PASS");
            host.Child = null; window.Close();
        }
        var savedSettings = await File.ReadAllBytesAsync(settings);
        Require(originalSettings.SequenceEqual(savedSettings), "Viewing and link handlers preserve preferences");
        var transport = new ReleaseTransport();
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Installed, Path.Combine(output, "fixture"), Path.Combine(output, "installed-fixture")));
        var stateFile = Path.Combine(output, "update-fixture.json");
        var service = new UpdateService(new HttpClient(transport), stateFile, "3.2.0");
        var updatesWindow = new MainWindow(service);
        updatesWindow.ConfigureAboutEvidence(1440, _ => { });
        Require(((TextBlock)updatesWindow.FindName("AboutDistributionText")).Text.Contains("instalada") && ((Button)updatesWindow.FindName("CheckUpdatesButton")).IsEnabled, "Installed distribution and real manual action enabled outside pilot");
        var result = await updatesWindow.CheckAboutUpdatesEvidenceAsync();
        Require(result.Status == Models.UpdateCheckStatus.UpToDate && transport.Requests == 1, "Real shared service checks current release once");
        var aboutResult = ((TextBlock)updatesWindow.FindName("LastUpdateResultText")).Text;
        Require(aboutResult == ((TextBlock)updatesWindow.FindName("SettingsLastUpdateResultText")).Text, "Both screens share result");
        transport.Offline = true;
        result = await updatesWindow.CheckAboutUpdatesEvidenceAsync();
        Require(result.Status == Models.UpdateCheckStatus.Offline && transport.Requests == 2, "Real service offline response");
        Require(((TextBlock)updatesWindow.FindName("LastUpdateCheckText")).Text == ((TextBlock)updatesWindow.FindName("SettingsLastUpdateCheckText")).Text, "Both screens share timestamp");
        updatesWindow.Close();
        checks.Add("Shared UpdateService with isolated HTTP transport: current release, offline, single request per forced check, same state/timestamp in About and Settings: PASS");
        await File.WriteAllLinesAsync(Path.Combine(output, "result.txt"), checks.Append("No live network, browser launch, user-data edits, hooks or registry writes. Render scales are not physical monitor DPI. Manual update button stays disabled in the pilot; its shared service is exercised through the evidence entrypoint."));
    }

    private sealed class ReleaseTransport : HttpMessageHandler
    {
        internal int Requests;
        internal bool Offline;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline) throw new HttpRequestException("Offline fixture");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"tag_name\":\"v3.2.0\",\"name\":\"3.2.0\",\"html_url\":\"https://github.com/lucasllira/SlashText/releases/tag/v3.2.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"SlashDesk-3.2.0-portable-win-x64.zip\",\"browser_download_url\":\"https://github.com/lucasllira/SlashText/releases/download/v3.2.0/SlashDesk-3.2.0-portable-win-x64.zip\",\"size\":100},{\"name\":\"SlashDesk-3.2.0-portable-win-x64.zip.sha256\",\"browser_download_url\":\"https://github.com/lucasllira/SlashText/releases/download/v3.2.0/SlashDesk-3.2.0-portable-win-x64.zip.sha256\",\"size\":64}]}]") });
        }
    }

    private static void SaveImage(FrameworkElement host, string output, string name, Size size, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, $"{name}-{scale:0.##}.png")); encoder.Save(stream);
    }
    private static void Require(bool condition, string scenario) { if (!condition) throw new InvalidOperationException("About smoke: " + scenario); }
}
