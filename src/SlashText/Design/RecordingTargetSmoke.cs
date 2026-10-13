using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenRecorderLib;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

// Exercise real launcher clicks and real recorder options, without recording the
// interactive desktop, changing the clipboard, installing hooks or opening user files.
internal static class RecordingTargetSmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), output, isCapturePilot: true));
        var checks = new List<string>();
        var monitor = System.Windows.Forms.Screen.PrimaryScreen!;
        var region = new System.Drawing.Rectangle(monitor.Bounds.Left + 20, monitor.Bounds.Top + 20, 240, 160);
        foreach (var language in new[] { "pt-BR", "en", "es" })
        foreach (var theme in new[] { "Light", "Dark" })
        {
            UiText.Apply(language); ThemeService.Apply(theme);
            var window = new MainWindow(captureEvidence: true); window.ConfigureLanguageEvidence("Capture", 1100);
            T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            void Click(string name) { var button = Control<RadioButton>(name); button.IsChecked = true; button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }
            void Select(string control, string tag)
            { var box = Control<ComboBox>(control); box.SelectedItem = box.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag); }
            Click("CaptureVideoMediaButton");
            Select("CaptureCompactFpsBox", "60"); Select("CaptureCompactQualityBox", "Muito alta");
            var cursorBefore = Control<CheckBox>("RecordingCursorCheckBox").IsChecked;
            foreach (var kind in new[] { RecordingTargetKind.Window, RecordingTargetKind.Region, RecordingTargetKind.Monitor, RecordingTargetKind.Region })
            {
                var tile = kind switch { RecordingTargetKind.Window => "CaptureWindowModeButton", RecordingTargetKind.Monitor => "CaptureMonitorModeButton", _ => "CaptureRegionModeButton" };
                Click(tile);
                Require(window.SelectedVideoTargetKind == kind, "launcher click determines start target");
                foreach (var control in new[] { "CaptureCompactTargetBox", "RecordingTargetBox" })
                    Require(Control<ComboBox>(control).SelectedItem is ComboBoxItem item && item.Tag as string == kind.ToString(), "selectors mirror clicked tile");
                var target = kind switch
                {
                    RecordingTargetKind.Window => new RecordingTarget(kind, region, new IntPtr(1)),
                    RecordingTargetKind.Region => new RecordingTarget(kind, region, DisplayDeviceName: monitor.DeviceName),
                    _ => new RecordingTarget(kind, monitor.Bounds, DisplayDeviceName: monitor.DeviceName)
                };
                var options = ScreenRecordingService.BuildOptions(target, new RecordingSettings { VideoFps = 60 }, "unused.log");
                var source = options.SourceOptions.RecordingSources.Single();
                Require(kind == RecordingTargetKind.Window ? source is WindowRecordingSource w && w.Handle == target.WindowHandle : source is DisplayRecordingSource, "source class matches requested destination");
                if (kind == RecordingTargetKind.Region)
                {
                    Require(source.SourceRect is { } rect && rect.Left == 20 && rect.Top == 20 && rect.Width == 240 && rect.Height == 160, "region retains exact pixel crop");
                    Require(options.OutputOptions.OutputFrameSize.Width == 240 && options.OutputOptions.OutputFrameSize.Height == 160, "region output frame is not monitor sized");
                }
                Require(Control<ComboBox>("CaptureCompactFpsBox").SelectedItem is ComboBoxItem fps && fps.Tag as string == "60" &&
                    Control<ComboBox>("CaptureCompactQualityBox").SelectedItem is ComboBoxItem quality && quality.Tag as string == "Muito alta" &&
                    Control<CheckBox>("RecordingCursorCheckBox").IsChecked == cursorBefore, "target changes preserve FPS quality and cursor");
            }
            foreach (var kind in new[] { RecordingTargetKind.Window, RecordingTargetKind.Monitor, RecordingTargetKind.Region })
            {
                Select("CaptureCompactTargetBox", kind.ToString());
                var tile = kind switch { RecordingTargetKind.Window => "CaptureWindowModeButton", RecordingTargetKind.Monitor => "CaptureMonitorModeButton", _ => "CaptureRegionModeButton" };
                Require(window.SelectedVideoTargetKind == kind && Control<RadioButton>(tile).IsChecked == true, "dropdown mirrors into tile and actual start target");
            }
            Click("CaptureImageMediaButton"); Click("CaptureScrollingModeButton"); Click("CaptureVideoMediaButton");
            Require(window.SelectedVideoTargetKind == RecordingTargetKind.Region && !Control<RadioButton>("CaptureScrollingModeButton").IsEnabled, "unsupported long capture cannot silently become fullscreen MP4");
            Click("CaptureWindowModeButton");
            window.ConfigureLanguageEvidence("Capture", 1100);
            Require(window.SelectedVideoTargetKind == RecordingTargetKind.Window &&
                Control<ComboBox>("RecordingTargetBox").SelectedItem is ComboBoxItem legacy && legacy.Tag as string == "Window" &&
                Control<ComboBox>("CaptureCompactTargetBox").SelectedItem is ComboBoxItem compact && compact.Tag as string == "Window",
                "reloading capture preferences retains the selected target");
            var root = (FrameworkElement)window.Content; window.Content = null; LabMotion.SetReduced(root, true);
            var size = new Size(1100, 840); var host = new Border { Child = root, Width = size.Width, Height = size.Height };
            host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, language + "-" + theme + "-window.png"))) encoder.Save(file);
            host.Child = null; window.Close();
            checks.Add($"{language}/{theme}: tiles, dropdown, start target, recorder source/crop/frame, FPS/quality preserved: PASS");
        }
        using (var selector = new DisposableSelector(new RegionSelectionWindow("fixture", _ => null)))
            Require(!selector.Window.TrySelectWindow(new(10, 10)) && selector.Window.SelectedWindowTarget is null, "no window selection produces no target");
        using (var selector = new DisposableSelector(new RegionSelectionWindow("fixture", _ => new(RecordingTargetKind.Monitor, region))))
            Require(!selector.Window.TrySelectWindow(new(10, 10)), "picker cannot return monitor as a window");
        using (var selector = new DisposableSelector(new RegionSelectionWindow("fixture", _ => new(RecordingTargetKind.Window, region, new IntPtr(42)))))
            Require(selector.Window.TrySelectWindow(new(10, 10)) && selector.Window.SelectedWindowTarget!.WindowHandle == new IntPtr(42), "picker preserves chosen window identity");
        foreach (var invalid in new[] { new RecordingTarget(RecordingTargetKind.Window, region), new RecordingTarget(RecordingTargetKind.Region, new(monitor.Bounds.Left - 20, monitor.Bounds.Top, 200, 100)) })
        {
            try { ScreenRecordingService.BuildOptions(invalid, new(), "unused.log"); throw new Exception("Invalid target accepted."); }
            catch (InvalidOperationException) { }
        }
        checks.Add("Cancelled/invalid window, monitor substitution and out-of-monitor region refused: PASS. No native recording requested; real MP4 content remains pilot validation.");
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException("Recording targets: " + label); }
    private sealed record DisposableSelector(RegionSelectionWindow Window) : IDisposable { public void Dispose() => Window.Close(); }
}
