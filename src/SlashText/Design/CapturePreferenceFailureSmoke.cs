using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

/// <summary>Real action handlers and filesystem failures; synthetic native boundaries only.</summary>
internal static class CapturePreferenceFailureSmoke
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message + ": PASS"); }
        var unhandled = 0;
        DispatcherUnhandledExceptionEventHandler onError = (_, args) =>
        { unhandled++; File.AppendAllText(Path.Combine(output, "unhandled.txt"), args.Exception.ToString()); args.Handled = true; };
        Application.Current.DispatcherUnhandledException += onError;
        try
        {
            foreach (var language in new[] { "pt-BR", "en", "es" })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var root = Path.Combine(output, language + "-" + theme);
                AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, root, Path.Combine(root, "unused"), true));
                AppPaths.EnsureDataLayout(); UiText.Apply(language); ThemeService.Apply(theme);
                var store = new JsonFileStore<AppSettings>(AppPaths.SettingsFile);
                var settings = new AppSettings { OnboardingCompleted = true, CloseToTray = false, CheckUpdatesOnStartup = false };
                settings.Capture.FileNameTemplate = "previous-{date}";
                settings.Capture.OutputDirectoryTemplate = Path.Combine(root, "synthetic-captures");
                await store.SaveAsync(settings);
                var window = new MainWindow(captureEvidence: true);
                var captureGate = Field<SingleFlightGate>(window, "_captureGate");
                var windowStore = Field<JsonFileStore<AppSettings>>(window, "_settingsStore");
                Set(window, "_settings", await windowStore.LoadAsync());
                Invoke(window, "LoadCaptureSettings");
                var continuations = new List<string>();
                window.CapturePreparationForEvidence = continuations.Add;
                // WPF requires a previously shown owner for the actual rules modal.
                LabMotion.SetReduced(window, true); window.Show(); window.Hide();
                Control<TextBlock>(window, "CaptureShortcutStatusText").Text = "● fixture";
                var dialogs = 0;
                var acceptRule = true;
                var desiredName = "candidate-{date}";
                var screenshot = false;
                var cleanupBeforeDialog = true;
                Action? dialogObserved = null;
                Exception? timerFailure = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
                timer.Tick += (_, _) =>
                {
                    try
                    {
                    foreach (var rule in Application.Current.Windows.OfType<CaptureRuleDialog>().Where(item => item.IsVisible).ToArray())
                    {
                        if (!acceptRule) { rule.DialogResult = false; continue; }
                        Control<TextBox>(rule, "FileNameBox").Text = desiredName;
                        Invoke(rule, "Save_OnClick", rule, new RoutedEventArgs());
                    }
                    foreach (var dialog in Application.Current.Windows.OfType<SettingsDialogWindow>().Where(item => item.IsVisible).ToArray())
                    {
                        dialogs++; dialogObserved?.Invoke();
                        cleanupBeforeDialog &= Field<object?>(window, "_recordingControl") is null && Field<object?>(window, "_recordingService") is null;
                        Require(dialog.Title == UiText.Get("Não foi possível salvar as preferências de captura"), "localized action feedback " + language + theme);
                        Require(FindText(dialog).Contains(UiText.Get("Não foi possível salvar as preferências de captura. Os ajustes anteriores foram mantidos e a captura ou gravação não foi iniciada. Verifique o acesso à pasta de dados, ajuste as opções e tente novamente."), StringComparison.Ordinal),
                            "feedback explains rollback and retry " + language + theme);
                        if (!screenshot)
                        {
                            LabMotion.SetReduced(dialog, true); dialog.UpdateLayout();
                            var image = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                            image.Render(dialog);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                            using var stream = File.Create(Path.Combine(output, language + "-" + theme + "-failure.png")); encoder.Save(stream);
                            screenshot = true;
                        }
                        dialog.DefaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    }
                    catch (Exception error)
                    {
                        timerFailure = error;
                        foreach (var dialog in Application.Current.Windows.OfType<SettingsDialogWindow>().ToArray()) dialog.Close();
                        foreach (var rule in Application.Current.Windows.OfType<CaptureRuleDialog>().ToArray()) rule.Close();
                    }
                };
                timer.Start();
                try
                {
                    foreach (var (handler, media, signal) in new[]
                    {
                        ("SaveCaptureSettings_OnClick", "Image", "shortcuts"),
                        ("StartSelectedCapture_OnClick", "Image", "image"),
                        ("StartSelectedCapture_OnClick", "Video", "mp4"),
                        ("StartSelectedCapture_OnClick", "Gif", "gif"),
                        ("StartMp4Recording_OnClick", "Video", "mp4"),
                        ("StartGifRecording_OnClick", "Gif", "gif"),
                        ("FocusCaptureRule_OnClick", "Image", "shortcuts")
                    })
                    {
                        var label = handler + media + language + theme;
                        File.WriteAllText(Path.Combine(output, "progress.txt"), label);
                        var before = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                        var previous = Field<AppSettings>(window, "_settings").Capture;
                        desiredName = "candidate-" + media + "-{date}";
                        SetMedia(window, media); Control<TextBox>(window, "CaptureFileNameBox").Text = desiredName;
                        continuations.Clear(); var oldDialogs = dialogs;
                        using (var locked = File.Open(AppPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            Invoke(window, handler, window, new RoutedEventArgs());
                            await UntilAsync(() => dialogs > oldDialogs && !captureGate.IsActive);
                            Require(ReferenceEquals(Field<AppSettings>(window, "_settings").Capture, previous) &&
                                Control<TextBox>(window, "CaptureFileNameBox").Text == previous.FileNameTemplate, "memory and controls roll back " + label);
                            var after = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                            Require(before.SequenceEqual(after), "original bytes preserved " + label);
                            Require(continuations.Count == 0 && Field<object?>(window, "_recordingService") is null && Field<object?>(window, "_gifRecordingSession") is null && !window.IsVisible,
                                "no shortcut reconfiguration, picker or recording after aborted save " + label);
                            Require(Control<TextBlock>(window, "StatusText").Text == UiText.Get("Preferências de captura não salvas. A ação foi interrompida."), "no false success " + label);
                        }
                        Control<TextBox>(window, "CaptureFileNameBox").Text = desiredName;
                        Invoke(window, handler, window, new RoutedEventArgs());
                        await UntilAsync(() => continuations.Contains(signal) && !captureGate.IsActive);
                        var fresh = new JsonFileStore<AppSettings>(AppPaths.SettingsFile);
                        var reopened = await fresh.LoadAsync();
                        Require(!fresh.IsProtected && reopened.Capture.FileNameTemplate == desiredName, "retry persists via real fresh reader " + label);
                        Require(dialogs == oldDialogs + 1 && continuations.Count(item => item == "shortcuts") == 1, "normal action prepares only once " + label);
                    }

                    // Cancellation of the real rules dialog must not commit staged settings.
                    var cancelledBefore = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                    var cancelledPrevious = Field<AppSettings>(window, "_settings").Capture;
                    acceptRule = false; continuations.Clear();
                    Control<TextBox>(window, "CaptureFileNameBox").Text = "cancelled-draft";
                    Invoke(window, "FocusCaptureRule_OnClick", window, new RoutedEventArgs());
                    acceptRule = true;
                    var cancelledAfter = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                    Require(ReferenceEquals(Field<AppSettings>(window, "_settings").Capture, cancelledPrevious) && continuations.Count == 0 && cancelledBefore.SequenceEqual(cancelledAfter),
                        "cancelled rules preserve memory and disk " + language + theme);
                    Invoke(window, "LoadCaptureSettings");

                    // The second MP4 checkpoint persists audio chosen on its preparation toolbar.
                    var capture = Field<AppSettings>(window, "_settings").Capture;
                    var audio = capture.Recording.Audio.Copy();
                    capture.Recording.Audio.CaptureMicrophone = !audio.CaptureMicrophone;
                    var checkpointCleanup = false;
                    dialogObserved = () => Require(checkpointCleanup, "MP4 cleanup occurs before error feedback " + language + theme);
                    var checkpointBefore = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                    using (var locked = File.Open(AppPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var result = await (Task<bool>)Invoke(window, "TrySaveCapturePreferencesAsync", capture, audio, (Action)(() => checkpointCleanup = true))!;
                        var checkpointAfter = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                        Require(!result && capture.Recording.Audio.CaptureMicrophone == audio.CaptureMicrophone && checkpointBefore.SequenceEqual(checkpointAfter),
                            "audio checkpoint rolls back and refuses start " + language + theme);
                    }
                    dialogObserved = null;
                    capture.Recording.Audio.CaptureMicrophone = !audio.CaptureMicrophone;
                    Require(await (Task<bool>)Invoke(window, "TrySaveCapturePreferencesAsync", capture, audio, null)!, "audio checkpoint retry accepted " + language + theme);
                    Require((await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).LoadAsync()).Capture.Recording.Audio.CaptureMicrophone != audio.CaptureMicrophone,
                        "audio checkpoint persisted on retry " + language + theme);

                    File.SetAttributes(AppPaths.SettingsFile, FileAttributes.ReadOnly);
                    var oldReadOnlyDialogs = dialogs;
                    try
                    {
                        Control<TextBox>(window, "CaptureFileNameBox").Text = "readonly-candidate";
                        Invoke(window, "SaveCaptureSettings_OnClick", window, new RoutedEventArgs());
                        await UntilAsync(() => dialogs > oldReadOnlyDialogs && !captureGate.IsActive);
                        Require(Field<AppSettings>(window, "_settings").Capture.FileNameTemplate != "readonly-candidate", "read-only failure remains local " + language + theme);
                    }
                    finally { File.SetAttributes(AppPaths.SettingsFile, FileAttributes.Normal); }

                    await File.WriteAllTextAsync(AppPaths.SettingsFile, "{invalid");
                    Set(window, "_settings", await windowStore.LoadAsync());
                    Invoke(window, "LoadCaptureSettings");
                    foreach (var (handler, media, signal) in new[]
                    {
                        ("SaveCaptureSettings_OnClick", "Image", "shortcuts"),
                        ("StartSelectedCapture_OnClick", "Image", "image"),
                        ("StartMp4Recording_OnClick", "Video", "mp4"),
                        ("StartGifRecording_OnClick", "Gif", "gif"),
                        ("FocusCaptureRule_OnClick", "Image", "shortcuts")
                    })
                    {
                        continuations.Clear(); SetMedia(window, media);
                        desiredName = "session-only-" + media;
                        Control<TextBox>(window, "CaptureFileNameBox").Text = desiredName;
                        Invoke(window, handler, window, new RoutedEventArgs());
                        await UntilAsync(() => continuations.Contains(signal) && !captureGate.IsActive);
                        Require(windowStore.IsProtected && await File.ReadAllTextAsync(AppPaths.SettingsFile) == "{invalid" && Field<AppSettings>(window, "_settings").Capture.FileNameTemplate == desiredName,
                            "protected reads retain session-only capture and original " + handler + language + theme);
                        Require(Control<TextBlock>(window, "StatusText").Text == UiText.Get("Preferências protegidas: ajustes desta sessão não serão salvos."),
                            "protected session never claims saved " + handler + language + theme);
                    }
                    Require(cleanupBeforeDialog && !Directory.Exists(settings.Capture.OutputDirectoryTemplate), "fixture never starts native capture " + language + theme);
                    if (timerFailure is not null) throw new InvalidOperationException("Dialog fixture failed.", timerFailure);
                }
                finally { timer.Stop(); window.DisposeCaptureEvidence(); window.Close(); }
            }
            Require(unhandled == 0, "no preference failure reaches global Dispatcher");
        }
        finally { Application.Current.DispatcherUnhandledException -= onError; UiText.Apply("pt-BR"); }
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    private static T Control<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void SetMedia(MainWindow window, string media) => Set(window, "_captureMediaMode", Enum.Parse(typeof(MainWindow).GetNestedType("CaptureMediaMode", BindingFlags.NonPublic)!, media));
    private static string FindText(DependencyObject target)
    {
        var text = target is TextBlock block ? block.Text : string.Empty;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(target); index++) text += "\n" + FindText(VisualTreeHelper.GetChild(target, index));
        return text;
    }
    private static async Task UntilAsync(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        if (!ready()) throw new TimeoutException("Actual capture action did not complete.");
    }
}
