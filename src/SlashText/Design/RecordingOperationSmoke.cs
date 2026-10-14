using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;
using Forms = System.Windows.Forms;

namespace SlashText.Design;

/// <summary>Actual commands, controlled awaits and owned WPF windows; never native recording.</summary>
internal static class RecordingOperationSmoke
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void CreateFixtures(string data)
    {
        if (Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any()) throw new IOException("Choose an empty fixture directory.");
        Directory.CreateDirectory(data);
        var settings = new AppSettings { CloseToTray = false, OnboardingCompleted = true, CheckUpdatesOnStartup = false, Theme = "Dark" };
        settings.Capture.OutputDirectoryTemplate = Path.Combine(data, "Captures");
        settings.Capture.CopyToClipboard = false;
        settings.Capture.Recording.Audio = new() { CaptureComputer = false, CaptureMicrophone = false };
        File.WriteAllText(Path.Combine(data, "settings.json"), System.Text.Json.JsonSerializer.Serialize(settings));
        var snippet = new Snippet { Id = Guid.Parse("12800000-0000-4000-8000-000000000001"), Trigger = "/teste128", Name = "Teste fictício 128", Content = "ÁREA FICTÍCIA — TESTE 128" };
        var markdown = SnippetMarkdownRepository.Serialize([snippet]);
        if (SnippetMarkdownRepository.ReadRequired(markdown).Single().Id != snippet.Id) throw new InvalidDataException("Invalid fixture.");
        File.WriteAllText(Path.Combine(data, "snippets.md"), markdown);
        File.WriteAllText(Path.Combine(data, "fixture-128.txt"), "SLASHDESK FICTITIOUS RECORDING PREPARATION 128");
    }
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name + ": PASS"); }
        var unhandled = 0;
        DispatcherUnhandledExceptionEventHandler onError = (_, args) =>
        { unhandled++; File.AppendAllText(Path.Combine(output, "unhandled.txt"), args.Exception.ToString()); args.Handled = true; };
        Application.Current.DispatcherUnhandledException += onError;
        var dialogs = 0;
        var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        dismiss.Tick += (_, _) =>
        {
            foreach (var dialog in Application.Current.Windows.OfType<SettingsDialogWindow>().Where(item => item.IsVisible).ToArray())
            { dialogs++; dialog.DefaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
        dismiss.Start();
        try
        {
            foreach (var language in new[] { "pt-BR", "en", "es" })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var root = Path.Combine(output, language + "-" + theme);
                AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, root, Path.Combine(root, "unused"), true));
                AppPaths.EnsureDataLayout(); UiText.Apply(language); ThemeService.Apply(theme);
                var store = new JsonFileStore<AppSettings>(AppPaths.SettingsFile);
                var settings = new AppSettings { CloseToTray = false, OnboardingCompleted = true, CheckUpdatesOnStartup = false };
                settings.Capture.OutputDirectoryTemplate = Path.Combine(root, "no-native-captures");
                settings.Capture.Recording.Audio = new() { CaptureComputer = false, CaptureMicrophone = false };
                await store.SaveAsync(settings);
                var window = new MainWindow(captureEvidence: true);
                Set(window, "_settings", await store.LoadAsync()); Invoke(window, "LoadCaptureSettings");
                Invoke(window, "ShowView", Control<FrameworkElement>(window, "CaptureView"), Control<Button>(window, "CaptureTabButton"));
                var signals = new List<string>(); window.CapturePreparationForEvidence = signals.Add;
                LabMotion.SetReduced(window, true); window.Show(); window.Hide();
                using var menu = window.CreateTrayMenu();
                var recordings = menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Name == "Recordings");
                var cancel = recordings.DropDownItems.OfType<Forms.ToolStripMenuItem>().Single(item => item.Name == "CancelPreparation");
                var gate = Field<SingleFlightGate>(window, "_captureGate");
                RecordingOperation? Current() => Field<RecordingOperation?>(window, "_recordingOperation");
                var preparationCalls = 0;

                void Start(string media, string entry)
                {
                    SetMedia(window, media == "MP4" ? "Video" : "Gif");
                    if (entry == "tray")
                    { window.RefreshTrayForEvidence(menu); ((Forms.ToolStripMenuItem)recordings.DropDownItems[media == "MP4" ? 0 : 1]).PerformClick(); }
                    else Invoke(window, entry == "new" ? "StartSelectedCapture_OnClick" : media == "MP4" ? "StartMp4Recording_OnClick" : "StartGifRecording_OnClick", window, new RoutedEventArgs());
                }

                async Task BlockedCommandsAsync(RecordingOperation owner, string label)
                {
                    var beforeCalls = preparationCalls; var beforeDialogs = dialogs;
                    Invoke(window, "StartMp4Recording_OnClick", window, new RoutedEventArgs());
                    Invoke(window, "StartGifRecording_OnClick", window, new RoutedEventArgs());
                    foreach (var media in new[] { "Image", "Video", "Gif" })
                    { SetMedia(window, media); Invoke(window, "StartSelectedCapture_OnClick", window, new RoutedEventArgs()); }
                    Invoke(window, "SaveCaptureSettings_OnClick", window, new RoutedEventArgs());
                    Invoke(window, "FocusCaptureRule_OnClick", window, new RoutedEventArgs());
                    Invoke(window, "CleanCaptureHistory_OnClick", window, new RoutedEventArgs());
                    Invoke(window, "DeleteHistoryItem_OnClick", new Button { Tag = new CaptureRecord() }, new RoutedEventArgs());
                    await (Task)Invoke(window, "RunCaptureAsync", CaptureShortcutAction.Region, false)!;
                    await (Task)Invoke(window, "RunScrollingCaptureAsync", false)!;
                    var maintenanceRan = false;
                    try
                    {
                        await (Task)Invoke(window, "RunSettingsMaintenanceAsync", "Fixture maintenance", (Func<Task>)(() => { maintenanceRan = true; return Task.CompletedTask; }), false)!;
                        throw new InvalidOperationException("Maintenance bypassed reservation.");
                    }
                    catch (InvalidOperationException) when (!maintenanceRan && gate.IsActive) { }
                    window.RefreshTrayForEvidence(menu);
                    ((Forms.ToolStripMenuItem)recordings.DropDownItems[0]).PerformClick();
                    ((Forms.ToolStripMenuItem)recordings.DropDownItems[1]).PerformClick();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Require(ReferenceEquals(Current(), owner) && preparationCalls == beforeCalls, "repeated screen/tray commands keep one preparation " + label);
                    Require(!maintenanceRan && !Field<bool>(window, "_settingsOperationInProgress") && ((FrameworkElement)window.Content).IsEnabled,
                        "maintenance does not run or disable owner " + label);
                    Require(!((Forms.ToolStripMenuItem)recordings.DropDownItems[0]).Enabled && !((Forms.ToolStripMenuItem)recordings.DropDownItems[1]).Enabled &&
                        cancel.Enabled == (owner.Phase == RecordingOperationPhase.Preparing) && !Control<Button>(window, "CaptureNewButton").IsEnabled,
                        "screen/tray expose correct busy and cancellation state " + label);
                    Require(dialogs == beforeDialogs && Field<object?>(window, "_recordingService") is null && Field<object?>(window, "_gifRecordingSession") is null,
                        "blocked actions never open dialogs or native recording " + label);
                }

                async Task RetryAsync(string media, string label)
                {
                    window.Hide();
                    window.RecordingPreparationForEvidence = (_, _) => { preparationCalls++; return Task.CompletedTask; };
                    window.RecordingPipelineForEvidence = null; signals.Clear();
                    Start(media, "new");
                    await UntilAsync(() => signals.Contains(media == "MP4" ? "mp4" : "gif") && Current() is null);
                    Require(!gate.IsActive && Control<Button>(window, "CaptureNewButton").IsEnabled, "retry admitted after release " + label);
                }

                try
                {
                    foreach (var media in new[] { "MP4", "GIF" })
                    foreach (var entry in new[] { "direct", "new", "tray" })
                    {
                        var label = media + "-" + entry + "-" + language + theme;
                        File.WriteAllText(Path.Combine(output, "progress.txt"), label);
                        var delay = NewSignal(); signals.Clear(); window.RecordingPipelineForEvidence = null;
                        window.RecordingPreparationForEvidence = (_, _) => { preparationCalls++; return delay.Task; }; // deliberately ignores cancellation
                        var before = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                        Start(media, entry); await UntilAsync(() => Current() is not null);
                        var operation = Current()!;
                        Require(gate.IsActive && operation.Phase == RecordingOperationPhase.Preparing && !operation.Completion.IsCompleted && signals.Count == 0,
                            "reservation precedes first preparation await and save " + label);
                        await BlockedCommandsAsync(operation, label);
                        if (media == "MP4" && entry == "direct")
                        {
                            Control<RadioButton>(window, "CaptureImageMediaButton").IsChecked = false;
                            Control<RadioButton>(window, "CaptureVideoMediaButton").IsChecked = true;
                            Save(window, Path.Combine(output, language + "-" + theme + "-preparing.png"));
                        }
                        window.RefreshTrayForEvidence(menu); cancel.PerformClick();
                        await UntilAsync(() => operation.Token.IsCancellationRequested);
                        Require(gate.IsActive && !operation.Completion.IsCompleted, "cancel retains ownership until delayed work unwinds " + label);
                        delay.SetResult(); await UntilAsync(() => Current() is null); await operation.Completion;
                        var after = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                        Require(before.SequenceEqual(after) && signals.Count == 0 && !window.IsVisible && !gate.IsActive,
                            "late cancelled continuation never saves, selects or reopens " + label);
                        Require(Control<TextBlock>(window, "StatusText").Text == UiText.Get("Preparação da gravação cancelada."), "cancellation status localized " + label);
                        await RetryAsync(media, label);
                        operation.Dispose(); Require(!gate.IsActive, "old cleanup remains idempotent " + label);
                    }

                    foreach (var media in new[] { "MP4", "GIF" })
                    {
                        var label = media + "-lifecycle-" + language + theme;
                        var recording = NewSignal(); var finishing = NewSignal(); signals.Clear();
                        window.RecordingPipelineForEvidence = async (_, operation) =>
                        { operation.MarkRecording(); await recording.Task; operation.MarkFinalizing(); await finishing.Task; };
                        Start(media, "new"); await UntilAsync(() => Current()?.Phase == RecordingOperationPhase.Recording);
                        var owner = Current()!; await BlockedCommandsAsync(owner, label + "-recording");
                        Invoke(window, "CancelRecordingPreparation"); Require(!owner.Token.IsCancellationRequested, "preparation cancel cannot stop an active recording " + label);
                        recording.SetResult(); await UntilAsync(() => owner.Phase == RecordingOperationPhase.Finalizing);
                        await BlockedCommandsAsync(owner, label + "-finalization");
                        finishing.SetResult(); await UntilAsync(() => Current() is null);
                        Require(!gate.IsActive && !window.IsVisible, "completion releases after finalization " + label);
                        await RetryAsync(media, label);

                        var beforeDialogs = dialogs;
                        signals.Clear();
                        window.RecordingPreparationForEvidence = (_, _) => Task.FromException(new IOException("Synthetic preparation failure"));
                        Start(media, "direct"); await UntilAsync(() => dialogs > beforeDialogs && Current() is null);
                        Require(!gate.IsActive && signals.Count == 0, "preparation error releases reservation before save/selection " + label);
                        await RetryAsync(media, label);
                        beforeDialogs = dialogs;
                        window.RecordingPipelineForEvidence = (_, operation) => { operation.MarkRecording(); return Task.FromException(new IOException("Synthetic recording failure")); };
                        Start(media, "direct"); await UntilAsync(() => dialogs > beforeDialogs && Current() is null);
                        Require(!gate.IsActive, "recording error releases reservation " + label);
                        await RetryAsync(media, label);
                    }

                    foreach (var media in new[] { "MP4", "GIF" })
                    {
                        var delayedFailure = NewSignal(); var beforeDialogs = dialogs; signals.Clear();
                        window.RecordingPreparationForEvidence = async (_, _) => { await delayedFailure.Task; throw new IOException("Late synthetic failure after cancel"); };
                        Start(media, "direct"); var owner = Current()!;
                        Invoke(window, "CancelRecordingPreparation"); delayedFailure.SetResult();
                        await UntilAsync(() => Current() is null);
                        Require(owner.Token.IsCancellationRequested && !gate.IsActive && dialogs == beforeDialogs && signals.Count == 0 && !window.IsVisible,
                            "late fault after cancel produces no dialog or resurrection " + media + language + theme);
                        await RetryAsync(media, "late-failure-" + language + theme);
                    }

                    // Hold the actual file gate: this reproduces the original race in SavePreferencesAsync.
                    var releaseFile = NewSignal(); var fileEntered = NewSignal();
                    var blockedFile = AtomicFilePersistence.WithPathGateAsync(AppPaths.SettingsFile, async () => { fileEntered.SetResult(); await releaseFile.Task; return true; }, CancellationToken.None);
                    await fileEntered.Task;
                    try
                    {
                        window.RecordingPreparationForEvidence = null; window.RecordingPipelineForEvidence = null; signals.Clear();
                        var before = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                        Control<TextBox>(window, "CaptureFileNameBox").Text = "cancelled-settings-{date}";
                        Start("MP4", "direct"); var owner = Current()!;
                        await BlockedCommandsAsync(owner, "real-save-gate-" + language + theme);
                        Invoke(window, "CancelRecordingPreparation"); await UntilAsync(() => Current() is null);
                        var after = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                        Require(before.SequenceEqual(after) && signals.Count == 0 && !gate.IsActive, "cancelled real save preserves original and aborts capture " + language + theme);
                        Require(Control<TextBox>(window, "CaptureFileNameBox").Text != "cancelled-settings-{date}", "cancelled real save restores controls " + language + theme);
                    }
                    finally { releaseFile.TrySetResult(); await blockedFile; }
                    await RetryAsync("MP4", "real-save-gate-" + language + theme);

                    // Actual toolbar closure and its pending start task use the same ownership watcher.
                    window.RecordingPreparationForEvidence = null;
                    RecordingControlWindow? toolbar = null; var controller = new StubRecorder();
                    window.RecordingPipelineForEvidence = async (_, operation) =>
                    {
                        toolbar = new RecordingControlWindow(controller, "MP4", new RecordingSettings { Audio = new() { CaptureComputer = false, CaptureMicrophone = false } });
                        Set(window, "_recordingControl", toolbar);
                        using var cancellation = (RecordingWindowCancellation)Invoke(window, "WatchPreparedRecordingControl", toolbar, operation)!;
                        toolbar.Show(); await toolbar.WaitForStartAsync();
                    };
                    Start("MP4", "direct"); await UntilAsync(() => toolbar?.IsVisible == true);
                    var toolbarOwner = Current()!; toolbar!.Close(); await UntilAsync(() => Current() is null);
                    Require(toolbarOwner.Token.IsCancellationRequested && controller.StopCalls == 0 && !gate.IsActive, "closing real preparation toolbar cancels without native Stop " + language + theme);
                    await RetryAsync("GIF", "toolbar-" + language + theme);

                    // Cancel while the *second* save is pending: the successful start click
                    // must not turn a late preference continuation into native recording.
                    var audioReady = NewSignal(); var audioRelease = NewSignal();
                    var audioBefore = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                    var nativeStartMarkers = 0;
                    var audioController = new StubRecorder();
                    window.RecordingPreparationForEvidence = null;
                    window.RecordingPipelineForEvidence = async (_, operation) =>
                    {
                        var capture = Field<AppSettings>(window, "_settings").Capture;
                        var previousAudio = capture.Recording.Audio.Copy();
                        var control = new RecordingControlWindow(audioController, "MP4", capture.Recording);
                        Set(window, "_recordingControl", control);
                        using var cancellation = (RecordingWindowCancellation)Invoke(window, "WatchPreparedRecordingControl", control, operation)!;
                        control.Show();
                        Field<TaskCompletionSource<bool>>(control, "_start").SetResult(true);
                        await control.WaitForStartAsync();
                        var entered = NewSignal();
                        var blocked = AtomicFilePersistence.WithPathGateAsync(AppPaths.SettingsFile, async () => { entered.SetResult(); await audioRelease.Task; return true; }, CancellationToken.None);
                        await entered.Task;
                        capture.Recording.Audio.CaptureMicrophone = true;
                        audioReady.SetResult();
                        try
                        {
                            await (Task<bool>)Invoke(window, "TrySaveCapturePreferencesCoreAsync", capture, previousAudio, null, operation.Token)!;
                            nativeStartMarkers++;
                        }
                        finally { audioRelease.TrySetResult(); await blocked; }
                    };
                    Start("MP4", "direct"); await audioReady.Task;
                    Invoke(window, "CancelRecordingPreparation"); await UntilAsync(() => Current() is null);
                    var audioAfter = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
                    Require(nativeStartMarkers == 0 && audioController.StopCalls == 0 && !gate.IsActive &&
                        !Field<AppSettings>(window, "_settings").Capture.Recording.Audio.CaptureMicrophone && audioBefore.SequenceEqual(audioAfter),
                        "cancelled audio checkpoint cannot start and restores audio/file " + language + theme);
                    await RetryAsync("MP4", "audio-checkpoint-" + language + theme);

                    // Closing during a delayed preparation must wait for cleanup and suppress resurrection.
                    var closingDelay = NewSignal(); signals.Clear(); window.RecordingPipelineForEvidence = null;
                    window.RecordingPreparationForEvidence = (_, _) => closingDelay.Task;
                    Start("MP4", "direct"); var closingOwner = Current()!;
                    var args = new CancelEventArgs(); Invoke(window, "MainWindow_OnClosing", window, args);
                    Require(args.Cancel && closingOwner.Token.IsCancellationRequested && gate.IsActive && Field<bool>(window, "_recordingExitPending"),
                        "exit cancels and waits for delayed preparation cleanup " + language + theme);
                    closingDelay.SetResult(); await UntilAsync(() => Field<bool>(window, "_servicesDisposed"));
                    Invoke(window, "ShowFromTray"); Invoke(window, "StartGifRecording_OnClick", window, new RoutedEventArgs());
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Require(signals.Count == 0 && !window.IsVisible && !gate.IsActive && Current() is null,
                        "late exit continuation cannot reopen or start a new operation " + language + theme);
                    Require(!Directory.Exists(settings.Capture.OutputDirectoryTemplate), "all fixture commands avoid native captures " + language + theme);
                }
                finally { if (!Field<bool>(window, "_servicesDisposed")) { window.DisposeCaptureEvidence(); window.Close(); } }
            }

            // Owned selector cancellation works both before Loaded and inside its nested modal loop.
            foreach (var beforeLoaded in new[] { true, false })
            {
                using var cancellation = new CancellationTokenSource();
                var selector = new Window { Width = 250, Height = 100, Title = "Synthetic recording selector", Content = new TextBlock { Text = "No screen capture" } };
                using var watcher = new RecordingWindowCancellation(selector, cancellation.Token);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                timer.Tick += (_, _) => { timer.Stop(); cancellation.Cancel(); };
                if (beforeLoaded) cancellation.Cancel(); else timer.Start();
                var result = selector.ShowDialog(); timer.Stop();
                Require(result != true && !selector.IsVisible && cancellation.IsCancellationRequested, "owned modal cancellation beforeLoaded=" + beforeLoaded);
            }
            Require(unhandled == 0, "no preparation/recording failure reaches global Dispatcher");
        }
        finally { dismiss.Stop(); Application.Current.DispatcherUnhandledException -= onError; UiText.Apply("pt-BR"); }
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    private static T Control<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void SetMedia(MainWindow window, string media) => Set(window, "_captureMediaMode", Enum.Parse(typeof(MainWindow).GetNestedType("CaptureMediaMode", BindingFlags.NonPublic)!, media));
    private static async Task UntilAsync(Func<bool> ready)
    { var clock = Stopwatch.StartNew(); while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10); if (!ready()) throw new TimeoutException("Recording command did not complete."); }
    private static void Save(MainWindow window, string path)
    {
        var content = (FrameworkElement)window.Content; window.Content = null;
        var host = new Border { Child = content, Width = 980, Height = 680 };
        try
        {
            host.Measure(new Size(980, 680)); host.Arrange(new Rect(0, 0, 980, 680)); host.UpdateLayout();
            var image = new RenderTargetBitmap(980, 680, 96, 96, PixelFormats.Pbgra32); image.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path); encoder.Save(file);
        }
        finally { host.Child = null; window.Content = content; }
    }
    private sealed class StubRecorder : IRecordingController
    {
        public Guid RecordingId { get; } = Guid.NewGuid();
        public bool IsPaused => false;
        public TimeSpan Elapsed => TimeSpan.Zero;
        public int StopCalls { get; private set; }
        public event EventHandler<RecordingProgress>? ProgressChanged { add { } remove { } }
        public void Pause() { } public void Resume() { } public void Stop() => StopCalls++;
    }
}
