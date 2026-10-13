using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Design;

// Synthetic fixtures only; no screen capture, clipboard access, keyboard hooks or production data.
internal static class RetentionSmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), output, isCapturePilot: true));
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var checks = new List<string>();
        using (var bitmap = new System.Drawing.Bitmap(900, 150))
        {
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font("Segoe UI", 32))
            { graphics.Clear(System.Drawing.Color.White); graphics.DrawString("SlashDesk synthetic OCR test", font, System.Drawing.Brushes.Black, 20, 20); }
            var result = await new CaptureOcrService(options: new() { Model = "Fast", Languages = "eng" }).RecognizeAsync(bitmap);
            Require(result.Text.Contains("SlashDesk", StringComparison.OrdinalIgnoreCase), "actual OCR worker result");
            Require(!Directory.EnumerateDirectories(TemporarySession.Root, "ocr-*").Any(), "actual worker normal cleanup");
        }
        using (var bitmap = new System.Drawing.Bitmap(2000, 1600))
        using (var cancellation = new CancellationTokenSource())
        {
            var operation = new CaptureOcrService().RecognizeAsync(bitmap, cancellation.Token);
            // Session creation is synchronous; wait until publication, then cancel the real operation.
            await Task.Delay(80); cancellation.Cancel();
            try { await operation; throw new InvalidOperationException("OCR cancellation ignored."); }
            catch (OperationCanceledException) { }
            Require(!Directory.EnumerateDirectories(TemporarySession.Root, "ocr-*").Any(), "actual cancellation cleanup");
        }
        checks.Add("Actual isolated OCR worker: synthetic text recognized; input/result removed on success and cancellation: PASS");

        var partial = Path.Combine(output, ".fixture.recording.mp4"); File.WriteAllText(partial, "synthetic incomplete video");
        var interruptedId = Guid.NewGuid(); RecordingRecovery.Start(interruptedId, partial); RecordingRecovery.End(interruptedId, false);
        foreach (var language in new[] { "pt-BR", "en", "es" })
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var size in new[] { new Size(1440, 900), new Size(980, 680) })
        {
            UiText.Apply(language); ThemeService.Apply(theme);
            var window = new MainWindow(captureEvidence: true);
            window.ConfigureSettingsEvidence(new AppSettings { Theme = theme }, size.Width);
            var root = (FrameworkElement)window.Content; window.Content = null; LabMotion.SetReduced(root, true);
            var host = new Border { Child = root, Width = size.Width, Height = size.Height };
            host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
            var scroll = (ScrollViewer)window.FindName("SettingsView");
            void Layout() { host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
            Layout(); await Dispatcher.Yield(DispatcherPriority.Loaded); Layout(); scroll.ScrollToBottom(); Layout();
            var toggle = (CheckBox)window.FindName("SettingsDiagnosticsCheckBox");
            toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(SafeDiagnosticLog.Enabled, "real diagnostics toggle enabled");
            toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(!SafeDiagnosticLog.Enabled, "real diagnostics toggle disabled");
            var notice = (Border)window.FindName("SettingsRecordingRecoveryNotice");
            var text = (TextBlock)window.FindName("SettingsRecordingRecoveryText");
            Require(notice.Visibility == Visibility.Visible && text.Text.Contains(".recording.mp4") && text.Text.StartsWith("1 "), "localized interrupted notice");
            Require(scroll.ScrollableWidth == 0, "no horizontal overflow");
            var bounds = toggle.TransformToAncestor(scroll).TransformBounds(new Rect(toggle.RenderSize));
            Require(bounds.Top >= 0 && bounds.Bottom <= scroll.ActualHeight, "toggle reachable at compact width");
            foreach (var scale in new[] { 1d, 1.5 })
            {
                var image = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                image.Render(host); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(output, $"{language}-{theme}-{size.Width}-{scale:0.0}.png")); encoder.Save(file);
            }
            checks.Add($"{language}/{theme}/{size.Width}: toggle persistence, localized recovery, compact scroll, 100/150% rendering: PASS");
            host.Child = null; window.Close();
        }
        Require(File.ReadAllText(partial) == "synthetic incomplete video", "partial remains unchanged");
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("Retention smoke: " + message); }
}
