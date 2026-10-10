using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

public sealed class RecordingControlWindow : Window
{
    private readonly IRecordingController _service;
    private readonly string _mediaName;
    private readonly TextBlock _time, _status;
    private readonly Border _indicator;
    private readonly Button _pause, _finish, _close;
    private readonly Button? _microphone, _computer, _audioMenu;
    private readonly RecordingAudioPanel? _audioPanel;
    private readonly RecordingSettings? _settings;
    private readonly Popup? _audioPopup;
    private readonly DispatcherTimer _timer;
    private readonly TaskCompletionSource<bool> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _preparing, _checkingAudio, _changingAudio;
    private int _stopRequested;

    internal RecordingControlWindow(IRecordingController service, string mediaName, RecordingSettings? settings = null)
    {
        var targetScreen = FloatingWindowPlacement.ForegroundScreen();
        _service = service; _mediaName = mediaName; _settings = settings;
        _preparing = settings is not null;
        Title = "Controle de gravação";
        Width = settings is null ? 310 : 430;
        Height = 84;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 24;
        var root = new Border
        {
            Background = Brush("PanelBrush"), BorderBrush = Brush("DividerBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 8)
        };
        var layout = new StackPanel();
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(row);
        Grid.SetColumn(actions, 1);
        bar.Children.Add(actions);
        _indicator = new Border
        {
            Width = 9, Height = 9, CornerRadius = new CornerRadius(5),
            Background = Brush(_preparing ? "MutedBrush" : "DangerBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        };
        row.Children.Add(_indicator);
        _time = new TextBlock
        {
            Text = "00:00:00", FontFamily = new FontFamily("Consolas"), FontSize = 16,
            Foreground = Brush("InkBrush"), VerticalAlignment = VerticalAlignment.Center,
            Width = 88, Margin = new Thickness(0, 0, 10, 0)
        };
        row.Children.Add(_time);
        if (settings is not null)
        {
            _audioPanel = new RecordingAudioPanel(settings.Audio ?? new()) { Width = 310 };
            _audioPanel.Changed += (_, _) => UpdateAudioButtons();
            _microphone = IconButton("MicOff", "Microfone", async (_, _) => await ToggleAudioAsync(true));
            _computer = IconButton("Volume2", "Áudio do computador", async (_, _) => await ToggleAudioAsync(false));
            row.Children.Add(_microphone); row.Children.Add(_computer);
            _audioMenu = IconButton("ChevronDown", "Escolher dispositivos e volumes", (_, _) => OpenAudioMenu());
            _audioMenu.Width = 28;
            row.Children.Add(_audioMenu);
            _audioPopup = new Popup
            {
                PlacementTarget = _audioMenu, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true,
                Child = new Border
                {
                    Child = _audioPanel, Background = Brush("PanelBrush"), BorderBrush = Brush("DividerBrush"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16)
                }
            };
            row.Children.Add(new Border { Width = 1, Height = 26, Background = Brush("DividerBrush"), Margin = new Thickness(8, 0, 8, 0) });
        }
        _pause = IconButton("Pause", "Pausar · Espaço", (_, _) => TogglePause());
        _pause.Visibility = _preparing ? Visibility.Collapsed : Visibility.Visible;
        actions.Children.Add(_pause);
        _finish = new Button
        {
            Content = _preparing ? "Iniciar" : "Finalizar", Width = 94, Height = 36,
            Margin = new Thickness(6, 0, 0, 0), Style = (Style)Application.Current.FindResource("Lab.Pilot.PrimaryButton")
        };
        _finish.Click += async (_, _) => { if (_preparing) await RequestStartAsync(); else StopOnce(); };
        actions.Children.Add(_finish);
        _close = IconButton("X", "Cancelar preparação · Esc", (_, _) => CancelPreparation());
        _close.Visibility = _preparing ? Visibility.Visible : Visibility.Collapsed;
        actions.Children.Add(_close);
        _status = new TextBlock
        {
            Text = _preparing ? "Escolha o áudio e clique em Iniciar." : $"Gravando {_mediaName}",
            FontSize = 11, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        layout.Children.Add(bar); layout.Children.Add(_status); root.Child = layout; Content = root;
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ChangedButton != MouseButton.Left || _checkingAudio) return;
            var source = args.OriginalSource as DependencyObject;
            while (source is not null)
            {
                if (source is ButtonBase) return;
                source = VisualTreeHelper.GetParent(source);
            }
            DragMove();
        };
        PreviewKeyDown += async (_, args) =>
        {
            if (_preparing && args.Key == Key.Enter) { args.Handled = true; await RequestStartAsync(); }
            else if (!_preparing && args.Key == Key.Space) { args.Handled = true; TogglePause(); }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                if (_audioPopup?.IsOpen == true) _audioPopup.IsOpen = false;
                else if (_preparing) CancelPreparation(); else StopOnce();
            }
        };
        // Exclude the floating controls from monitor/region recordings.
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        _service.ProgressChanged += OnProgress;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => { if (!_preparing) UpdateElapsed(_service.Elapsed); };
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop(); _service.ProgressChanged -= OnProgress;
            if (_audioPopup is not null) _audioPopup.IsOpen = false;
            _start.TrySetResult(false);
            // A system close also finalizes active MP4/GIF recordings.
            if (!_preparing) StopOnce();
            AppDiagnosticLog.Write("recording.overlay-closed", ("recordingId", _service.RecordingId.ToString("N")),
                ("media", _mediaName), ("elapsedMs", _service.Elapsed.TotalMilliseconds));
        };

        AuxiliaryWindowTheme.Apply(this);
        UpdateAudioButtons();
        if (_audioPopup?.Child is DependencyObject popupRoot) AuxiliaryWindowTheme.ApplyTree(popupRoot);
        Loaded += (_, _) => FloatingWindowPlacement.Place(this, targetScreen, "BottomCenter");
    }

    internal Task<bool> WaitForStartAsync() => _start.Task;
    internal void BeginRecording()
    {
        _preparing = false;
        if (_audioPopup is not null) _audioPopup.IsOpen = false;
        if (_audioMenu is not null) _audioMenu.IsEnabled = false;
        _finish.Content = "Finalizar"; _pause.Visibility = Visibility.Visible; _close.Visibility = Visibility.Collapsed;
        _pause.IsEnabled = _finish.IsEnabled = false;
        _status.Text = "Inicializando MP4…";
        UpdateAudioButtons();
    }

    internal async Task RequestStartAsync()
    {
        if (!_preparing || _checkingAudio || _start.Task.IsCompleted || _audioPanel is null || _settings is null) return;
        _checkingAudio = true; _finish.IsEnabled = false; _audioPanel.IsEnabled = false;
        UpdateAudioButtons();
        try
        {
            var error = await _audioPanel.RefreshDevicesAsync();
            if (!IsVisible) return;
            if (error is not null) { _status.Text = error; Height = 112; return; }
            _settings.Audio = _audioPanel.ReadSettings().Copy();
            _start.TrySetResult(true);
        }
        finally
        {
            _checkingAudio = false; _audioPanel.IsEnabled = !_start.Task.IsCompleted; _finish.IsEnabled = _preparing && !_start.Task.IsCompleted;
            UpdateAudioButtons();
        }
    }

    private void CancelPreparation() { if (_preparing) { _start.TrySetResult(false); Close(); } }
    private void OpenAudioMenu()
    {
        if (_preparing && !_checkingAudio && _audioPopup is not null) _audioPopup.IsOpen = !_audioPopup.IsOpen;
    }
    private async Task ToggleAudioAsync(bool microphone)
    {
        if (_audioPanel is null || _checkingAudio || _changingAudio) return;
        if (_preparing) { _audioPanel.ToggleSource(microphone); return; }
        if (_service is not ScreenRecordingService recorder) return;
        _changingAudio = true; UpdateAudioButtons();
        try
        {
            if (!await recorder.SetAudioMutedAsync(
                microphone ? !recorder.IsMicrophoneMuted : recorder.IsMicrophoneMuted,
                microphone ? recorder.IsComputerMuted : !recorder.IsComputerMuted))
                _status.Text = "Não foi possível alterar o áudio. O vídeo continua gravando.";
        }
        catch { _status.Text = "Não foi possível alterar o áudio. O vídeo continua gravando."; }
        finally { _changingAudio = false; UpdateAudioButtons(); }
    }
    private void UpdateAudioButtons()
    {
        if (_audioPanel is null || _microphone is null || _computer is null) return;
        var audio = _preparing ? _audioPanel.ReadSettings() : _settings!.Audio;
        var recorder = _service as ScreenRecordingService;
        var microphoneOn = _preparing ? audio.CaptureMicrophone : recorder?.IsMicrophoneMuted == false;
        var computerOn = _preparing ? audio.CaptureComputer : recorder?.IsComputerMuted == false;
        SetSource(_microphone, microphoneOn, "Microfone", microphoneOn ? "Mic" : "MicOff", audio.CaptureMicrophone);
        SetSource(_computer, computerOn, "Áudio do PC", computerOn ? "Volume2" : "VolumeX", audio.CaptureComputer);
    }
    private void SetSource(Button button, bool on, string name, string icon, bool configured)
    {
        var glyph = new LabIcon { Kind = icon, Width = 20, Height = 20 };
        glyph.SetResourceReference(LabIcon.ForegroundProperty, on ? "Lab.accent-text" : "Lab.muted"); button.Content = glyph;
        if (on) button.SetResourceReference(Button.BackgroundProperty, "Lab.tint"); else button.Background = Brushes.Transparent;
        var ready = _preparing || _service is ScreenRecordingService { State: ScreenRecordingState.Recording };
        button.IsEnabled = !_checkingAudio && !_changingAudio && ready && (!(_preparing && _start.Task.IsCompleted)) && (_preparing || configured) && Volatile.Read(ref _stopRequested) == 0;
        var label = name + (_preparing ? (on ? " ligado" : " desligado") : (on ? " gravando" : " silenciado"));
        button.ToolTip = !_preparing && !configured ? name + ": selecione esta fonte antes da próxima gravação." :
            label + ". Clique para " + (on ? "silenciar." : "ativar.");
        if (!_preparing && _service.IsPaused && configured) button.ToolTip = name + ": continue a gravação para alterar o áudio.";
        ToolTipService.SetShowOnDisabled(button, true); AutomationProperties.SetName(button, label);
    }
    private static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);
    private static Button IconButton(string icon, string label, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = new LabIcon { Kind = icon, Width = 20, Height = 20 }, Width = 36, Height = 36,
            MinWidth = 0, Padding = new Thickness(4), Margin = new Thickness(2, 0, 0, 0), ToolTip = label
        };
        AutomationProperties.SetName(button, label); button.Click += click; return button;
    }
    private void TogglePause()
    {
        if (_preparing || !_pause.IsEnabled || Volatile.Read(ref _stopRequested) != 0) return;
        if (_service.IsPaused) _service.Resume(); else _service.Pause();
    }
    private void OnProgress(object? sender, RecordingProgress progress)
    {
        void Update()
        {
            if (_preparing) return;
            UpdateElapsed(progress.Elapsed); _status.Text = progress.Status;
            _indicator.SetResourceReference(Border.BackgroundProperty, progress.IsPaused ? "Lab.muted" : "Lab.error");
            _pause.Content = new LabIcon { Kind = progress.IsPaused ? "Play" : "Pause", Width = 20, Height = 20 };
            _pause.ToolTip = progress.IsPaused ? "Continuar · Espaço" : "Pausar · Espaço";
            AutomationProperties.SetName(_pause, progress.IsPaused ? "Continuar" : "Pausar");
            var ready = _service is not ScreenRecordingService recorder || recorder.State is ScreenRecordingState.Recording or ScreenRecordingState.Paused;
            _pause.IsEnabled = _finish.IsEnabled = ready && Volatile.Read(ref _stopRequested) == 0;
            UpdateAudioButtons();
        }
        if (Dispatcher.CheckAccess()) Update(); else _ = Dispatcher.BeginInvoke(Update);
    }
    private void UpdateElapsed(TimeSpan elapsed) => _time.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    private void StopOnce()
    {
        if (_preparing || Interlocked.CompareExchange(ref _stopRequested, 1, 0) != 0) return;
        AppDiagnosticLog.Write("recording.overlay-finish-clicked", ("recordingId", _service.RecordingId.ToString("N")),
            ("media", _mediaName), ("elapsedMs", _service.Elapsed.TotalMilliseconds));
        _service.Stop(); _pause.IsEnabled = _finish.IsEnabled = false;
        _status.Text = $"Finalizando {_mediaName}…"; UpdateAudioButtons();
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
