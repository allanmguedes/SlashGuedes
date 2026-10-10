using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

internal static class AuxiliarySmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), Path.Combine(output, "unused-installed"), isCapturePilot: true));
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        var checks = new List<string>();
        foreach (var theme in new[] { "Light", "Dark", "System" })
        {
            ThemeService.Apply(theme);
            var owner = new Window { Width = 780, Height = 720, Content = new TextBox { Text = "Destino da digitação" } };
            AuxiliaryWindowTheme.Apply(owner); owner.Show();
            owner.Activate(); ((TextBox)owner.Content).Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            try
            {
                var fields = new VariableInputWindow(AuxiliaryFixtures.Fields) { Owner = owner };
                var result = Modal(fields, () =>
                {
                    var inputs = Elements<TextBox>(fields).ToArray();
                    Require(inputs.Length == 12 && inputs[0].Text == "Exemplo", "variable defaults and all fields");
                    Require(inputs[0].IsKeyboardFocused, "variable initial focus");
                    inputs[0].Text = "Valor inserido";
                    var scroll = Elements<ScrollViewer>(fields).First();
                    Require(scroll.ScrollableHeight > 0 && scroll.ActualHeight > 150, "many fields scroll inside dialog");
                    Require(Button(fields, "Inserir").TransformToAncestor((Visual)fields.Content).TransformBounds(new Rect(Button(fields, "Inserir").RenderSize)).Bottom <= ((FrameworkElement)fields.Content).ActualHeight + 1, "insert remains visible");
                    Save(fields, output, "fields-" + theme);
                    scroll.ScrollToBottom(); fields.UpdateLayout(); Save(fields, output, "fields-bottom-" + theme);
                    Invoke(Button(fields, "Inserir"));
                });
                Require(result == true && fields.Values["campo_1"] == "Valor inserido", "accepted values preserved");
                var canceled = new VariableInputWindow(AuxiliaryFixtures.Fields) { Owner = owner };
                Require(Modal(canceled, () => Escape(canceled)) != true, "Escape does not insert");
                var prompt = new PromptDialog("Nome do grupo", "Nome", "Inicial") { Owner = owner };
                Require(Modal(prompt, () =>
                {
                    var input = Elements<TextBox>(prompt).Single(); Require(input.IsKeyboardFocused, "prompt focus");
                    Save(prompt, output, "prompt-" + theme); ThemeService.Apply(ThemeService.IsDark ? "Light" : "Dark");
                    Require(ReferenceEquals(input.Background, Application.Current.FindResource("Lab.input")) && ReferenceEquals(prompt.Background, Application.Current.FindResource("Lab.bg")), "runtime theme updates open modal field and window");
                    ThemeService.Apply(theme); Invoke(Button(prompt, "Cancelar"));
                }) != true, "cancel prompt");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Require(((TextBox)owner.Content).IsKeyboardFocused, "modal returns owner keyboard focus");
                foreach (var (text, expected) in new[] { ("Atualizar agora", UpdateDecision.UpdateNow), ("Lembrar depois", UpdateDecision.RemindLater), ("Ignorar esta versão", UpdateDecision.IgnoreVersion) })
                {
                    var update = new UpdateAvailableWindow(AuxiliaryFixtures.Update) { Owner = owner, Width = 500, Height = 390 };
                    Require(Modal(update, () => { Save(update, output, "update-" + theme); Invoke(Button(update, text)); }) == true && update.Decision == expected, "update decision " + text);
                }
                var dismiss = new UpdateAvailableWindow(AuxiliaryFixtures.Update) { Owner = owner };
                Modal(dismiss, () => Escape(dismiss)); Require(dismiss.Decision == UpdateDecision.Cancel, "dismiss update is cancel");
                var progress = new UpdateProgressWindow { Owner = owner }; progress.Show();
                progress.Report(new UpdateProgress("Recebendo o pacote e verificando sua integridade antes da instalação. Esta descrição longa precisa continuar legível, com o botão de cancelamento acessível.", 45, 100));
                progress.UpdateLayout(); var cancel = Button(progress, "Cancelar");
                Require(cancel.IsEnabled && cancel.TransformToAncestor((Visual)progress.Content).TransformBounds(new Rect(cancel.RenderSize)).Bottom <= ((FrameworkElement)progress.Content).ActualHeight + 1, "long stage and cancel fit");
                Save(progress, output, "progress-" + theme);
                progress.Close(); Require(progress.IsVisible, "system close cannot interrupt updater");
                Escape(progress); Require(progress.Cancellation.IsCancellationRequested && !cancel.IsEnabled, "Escape requests cancel once");
                progress.Report(new UpdateProgress("Encerrando download", 45, 100)); Require(!cancel.IsEnabled, "progress cannot re-enable canceled operation"); progress.AllowClose();
                var applying = new UpdateProgressWindow { Owner = owner }; applying.Show(); applying.Report(new UpdateProgress("Aplicando atualização", 100, 100, true));
                Escape(applying); Require(!Button(applying, "Cancelar").IsEnabled && !applying.Cancellation.IsCancellationRequested, "applying cannot cancel"); applying.AllowClose();
                var welcome = new OnboardingWindow { Owner = owner, Width = 620, Height = 500 };
                Require(Modal(welcome, () => { Save(welcome, output, "welcome-" + theme); Elements<ScrollViewer>(welcome).Single().ScrollToBottom(); welcome.UpdateLayout(); Save(welcome, output, "welcome-bottom-" + theme); Invoke(Button(welcome, "Começar a usar")); }) == true, "onboarding finish");
                using (var gif = AuxiliaryFixtures.Gif())
                {
                    var preview = new GifPreviewWindow(gif) { Owner = owner, Width = 640, Height = 480 };
                    Require(Modal(preview, () => { Save(preview, output, "gif-" + theme); Escape(preview); }) != true, "GIF Escape discards");
                    var save = new GifPreviewWindow(gif) { Owner = owner };
                    Require(Modal(save, () => Invoke(Button(save, "Salvar GIF"))) == true, "GIF accept decision; no save service invoked");
                }
                var controller = new Controller(); var recording = new RecordingControlWindow(controller, "GIF"); recording.Show();
                Invoke(Elements<Button>(recording).First(b => b.ToolTip?.ToString()?.StartsWith("Pausar") == true));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Require(controller.IsPaused, "recording pause");
                Invoke(Elements<Button>(recording).First(b => b.ToolTip?.ToString()?.StartsWith("Continuar") == true));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Require(!controller.IsPaused, "recording resume"); Save(recording, output, "recording-" + theme);
                Invoke(Button(recording, "Finalizar")); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); recording.Close(); Require(controller.Stops == 1, "finish and close stop exactly once");
                owner.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var foreground = GetForegroundWindow();
                var accent = new QuickAccentWindow(); accent.UpdateChoices("áàâãä", 2, "BottomCenter", true);
                var suggestions = new SuggestionWindow(); var point = owner.PointToScreen(new Point(25, 60));
                suggestions.UpdateSuggestions(AuxiliaryFixtures.Snippets, point, 8); suggestions.UpdateLayout();
                Require(GetForegroundWindow() == foreground, "accent and suggestions preserve foreground");
                foreach (var floating in new Window[] { accent, suggestions })
                {
                    var handle = new WindowInteropHelper(floating).Handle;
                    Require((FloatingWindowPlacement.GetWindowLong(handle, -20) & 0x08000000) != 0, "native no-activate style");
                    GetWindowRect(handle, out var bounds); var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                    Require(bounds.Left >= area.Left && bounds.Top >= area.Top && bounds.Right <= area.Right && bounds.Bottom <= area.Bottom, "floating fits current monitor");
                }
                Require(Elements<ScrollViewer>(suggestions).Single().VerticalOffset > 0, "selected suggestion scrolls into view");
                Save(accent, output, "accent-" + theme); Save(suggestions, output, "suggestions-" + theme);
                var old = ((SolidColorBrush)((Border)accent.Content).Background).Color;
                ThemeService.Apply(ThemeService.IsDark ? "Light" : "Dark");
                Require(((SolidColorBrush)((Border)accent.Content).Background).Color != old && ReferenceEquals(((Border)suggestions.Content).Background, Application.Current.FindResource("Lab.panel")), "runtime theme updates open floating surfaces");
                accent.Close(); suggestions.Close(); ThemeService.Apply(theme);
                var dialog = new SettingsDialogWindow("Nenhum arquivo será excluído nesta verificação.", "Confirmar exclusão", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, "Excluir arquivo", "Remover do histórico") { Owner = owner };
                LabMotion.SetReduced(dialog, true); Modal(dialog, () => { Save(dialog, output, "dialog-" + theme); Escape(dialog); });
                Require(dialog.Choice == MessageBoxResult.Cancel, "destructive dismissal is cancel");
                var gallery = new AuxiliaryReviewWindow(); gallery.Show(); Save(gallery, output, "gallery-" + theme); gallery.Close();
                checks.Add(theme + ": variables/focus/scroll, prompt cancel, all update choices, protected progress, onboarding, GIF decisions, recording pause/resume/stop-once, floating focus/bounds/selection/runtime theme, destructive cancel: PASS; client renders 100/125/150/200%.");
            }
            finally { owner.Close(); }
        }
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        foreach (var area in new[] { new Rect(-1920, -200, 1920, 1040), new Rect(0, 0, 800, 600) })
        {
            var placed = FloatingWindowPlacement.Calculate(area, new Size(1000 * scale, 300 * scale), new Point(area.Right + 100, area.Bottom + 100));
            Require(area.Contains(placed), "clamp negative origin and oversized floating dimensions");
        }
        var main = new MainWindow(captureEvidence: true); var opened = new List<string>(); main.ConfigureNotificationEvidence(opened.Add);
        var path = Path.Combine(output, "capture-fixture.txt"); await File.WriteAllTextAsync(path, "fixture");
        main.NotifyCaptureEvidence(path); main.OpenNotificationEvidence(); main.OpenNotificationEvidence(); Require(opened.SequenceEqual(new[] { path }), "notification opens correct file once");
        main.NotifyCaptureEvidence(path); main.NotifyCaptureEvidence(null); main.OpenNotificationEvidence(); Require(opened.Count == 1, "notification without file clears old path");
        main.NotifyCaptureEvidence(path); File.Delete(path); main.OpenNotificationEvidence(); Require(opened.Count == 1, "deleted file does not launch"); main.Close();
        checks.Add("Negative monitor origins/oversized bounds; actual notification path routing, consumed once, null replacement and deleted file: PASS.");
        checks.Add("Fixtures only: no updater/download/recording service, shell launch, global hook or official data access. Native toast delivery and physical mixed-DPI monitor tests remain manual (#70). Rendered native windows show client content only; captions belong to Windows.");
        await File.WriteAllLinesAsync(Path.Combine(output, "result.txt"), checks);
    }

    private static bool? Modal(Window window, Action action)
    {
        Exception? failure = null;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try { window.UpdateLayout(); action(); }
            catch (Exception exception) { failure = exception; window.Close(); }
        }));
        var result = window.ShowDialog(); if (failure is not null) throw failure; return result;
    }
    private static IEnumerable<T> Elements<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var element in Elements<T>(child)) yield return element;
    }
    private static Button Button(Window window, string text) => Elements<Button>(window).Single(b => b.Content as string == text);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Escape(Window window) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Save(Window window, string output, string name)
    {
        window.UpdateLayout(); var root = (FrameworkElement)window.Content; var size = root.RenderSize;
        Require(size.Width > 0 && size.Height > 0, "renderable " + name);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.AllowsTransparency ? Brushes.Transparent : window.Background, null, new Rect(size));
            drawing.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null, new Rect(size));
        }
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name + "-" + scale.ToString("0.##", CultureInfo.InvariantCulture) + ".png")); encoder.Save(stream);
        }
    }
    private sealed class Controller : IRecordingController
    {
        public Guid RecordingId { get; } = Guid.NewGuid(); public bool IsPaused { get; private set; }
        public TimeSpan Elapsed => TimeSpan.FromSeconds(12); internal int Stops;
        public event EventHandler<RecordingProgress>? ProgressChanged;
        public void Pause() { IsPaused = true; ProgressChanged?.Invoke(this, new RecordingProgress(Elapsed, true, "Gravação pausada")); }
        public void Resume() { IsPaused = false; ProgressChanged?.Invoke(this, new RecordingProgress(Elapsed, false, "Gravando GIF")); }
        public void Stop() => Stops++;
    }
    private static void Require(bool condition, string scenario) { if (!condition) throw new InvalidOperationException("Auxiliary smoke: " + scenario); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
}
