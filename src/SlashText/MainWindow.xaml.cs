using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using DrawingIcon = System.Drawing.Icon;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using DrawingColor = System.Drawing.Color;

namespace SlashText;

public partial class MainWindow : Window
{
    private readonly SnippetMarkdownRepository _repository = new();
    private readonly KeyboardHookService _keyboardHook = new();
    private readonly TextExpansionService _expansionService = new();
    private readonly UsageService _usageService = new();
    private readonly QuickAccentService _quickAccentService = new();
    private readonly BackupService _backupService = new();
    private readonly AssetMaintenanceService _assetMaintenanceService = new();
    private readonly SnippetImportService _snippetImportService = new();
    private CancellationTokenSource? _captureImageLoadCancellation;
    private CancellationTokenSource? _captureThumbnailCancellation;
    private readonly CaptureService _captureService = new();
    private readonly GifRecordingService _gifRecordingService = new();
    private readonly GlobalCaptureShortcutService _captureShortcuts = new();
    private readonly UpdateService _updateService;
    private readonly PortableUpdateService _portableUpdateService = new();
    private readonly UpdateNotificationGate _updateNotificationGate = new();
    private readonly SingleFlightGate _expansionRequestGate = new();
    private readonly SingleFlightGate _captureGate = new();
    private readonly JsonFileStore<AppSettings> _settingsStore = new(AppPaths.SettingsFile);
    private readonly ObservableCollection<Snippet> _snippets = [];
    private readonly SuggestionWindow _suggestionWindow = new();
    private readonly QuickAccentWindow _quickAccentWindow = new();

    private Forms.NotifyIcon? _trayIcon;
    private string? _pendingCaptureNotificationPath;
    private AppSettings _settings = new();
    private Snippet? _selected;
    private string? _selectedCategory;
    private bool _showMostUsed;
    private bool _exitRequested;
    private bool _servicesDisposed;
    private bool _restoreRecoveryBlocked;
    private bool _initialized;
    private bool _snippetStorageAvailable = true;
    private bool _updatingQuickAccentSets;
    private bool _updatingCaptureRecordingCompact;
    private int _quickAccentPreviewIndex;
    private ScreenRecordingService? _recordingService;
    private GifRecordingSession? _gifRecordingSession;
    private RecordingControlWindow? _recordingControl;
    private string? _lastReleaseUrl;
    private int _updateOfferActive;
    private CancellationTokenSource? _activeUpdateCancellation;
    private CancellationTokenSource? _updateMonitorCancellation;
    private Task? _updateMonitorTask;
    private int _shortcutResponsiveBand = -1;
    private CaptureLauncherMode _captureLauncherMode = CaptureLauncherMode.Region;
    private CaptureMediaMode _captureMediaMode = CaptureMediaMode.Image;
    private CaptureRecord? _captureWorkbenchRecord;
    private string? _captureWorkbenchPath;
    private double _captureWorkbenchZoom = 1d;
    private bool _captureEditorExpanded;
    private bool _captureCommitInProgress;
    private TaskCompletionSource<CaptureImageEditResult?>? _pendingCaptureEdit;
    private CaptureEditorDocument? _completedCaptureDocument;
    private readonly Dictionary<UIElement, Visibility> _capturePageVisibility = new();
    private GridLength[]? _capturePageRowHeights;

    private static readonly DependencyProperty CaptureHistoryOffsetProperty =
        DependencyProperty.Register(
            nameof(CaptureHistoryOffset),
            typeof(double),
            typeof(MainWindow),
            new PropertyMetadata(0d, (target, args) =>
            {
                if (target is MainWindow window && window.CaptureHistoryScroller is not null)
                {
                    window.CaptureHistoryScroller.ScrollToHorizontalOffset((double)args.NewValue);
                }
            }));

    private double CaptureHistoryOffset
    {
        get => (double)GetValue(CaptureHistoryOffsetProperty);
        set => SetValue(CaptureHistoryOffsetProperty, value);
    }

    private const double ShortcutLeftMinimum = 220;
    private const double ShortcutLeftMaximum = 460;
    private const double ShortcutRightMinimum = 240;
    private const double ShortcutRightMaximum = 480;
    private const double ShortcutSplitterStep = 16;
    private const double ShortcutDividerSpace = 32;
    private static readonly TimeSpan UpdateMonitorInterval = TimeSpan.FromMinutes(30);

    private enum CaptureLauncherMode
    {
        ActiveMonitor,
        Region,
        Window,
        Scrolling,
        Gif
    }

    private enum CaptureMediaMode
    {
        Image,
        Video,
        Gif
    }

    public MainWindow() : this(captureEvidence: false) { }

    internal MainWindow(bool captureEvidence, UpdateService? updateService = null)
    {
        _updateService = updateService ?? new UpdateService();
        InitializeComponent();
        CategoryBox.ItemsSource = ShortcutCategories.All;
        SelectShortcutCategory("Geral");
        InitializeShortcutFormatting();
        ShortcutEditorPanel.SizeChanged += (_, _) => { if (_shortcutEditorExpanded) UpdateShortcutEditorHeight(); };
        CaptureInlineEditor.StateChanged += CaptureInlineEditor_OnStateChanged;
        CaptureEditorContext.Attach(CaptureInlineEditor);
        CaptureInlineEditor.SaveCopyRequested += (_, _) => SaveCapturePreview_OnClick(this, new RoutedEventArgs());
        CaptureInlineEditor.ExitRequested += (_, _) => { if (_captureEditorExpanded) SetCaptureEditorExpanded(false); };
        _captureService.ImageEditor = EditNewCaptureInWorkbenchAsync;
        CaptureImageMediaButton.IsChecked = true;
        CaptureRegionModeButton.IsChecked = true;
        InitializeRecordingPresetControls();
        // Unshown UI fixture: no tray, startup/data loading, hooks or updater.
        if (captureEvidence) return;
        InitializeClipboard();
        InitializeTray();
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        Closed += MainWindow_OnClosed;
        StateChanged += MainWindow_OnStateChanged;
        Activated += MainWindow_OnActivated;
        _keyboardHook.ExpansionRequested += KeyboardHook_OnExpansionRequested;
        _keyboardHook.SuggestionsChanged += KeyboardHook_OnSuggestionsChanged;
        _suggestionWindow.SnippetChosen += SuggestionWindow_OnSnippetChosen;
        _quickAccentService.Changed += QuickAccentService_OnChanged;
        _quickAccentService.CharacterInserted += QuickAccentService_OnCharacterInserted;
        _captureShortcuts.Triggered += CaptureShortcuts_OnTriggered;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        var startup = new StartupModuleCoordinator();
        try
        {
            var settingsLoaded = await startup.RunAsync(
                "Configurações",
                async () => _settings = await _settingsStore.LoadAsync());
            if (!settingsLoaded)
            {
                _settings = new AppSettings();
            }

            _settings.Capture ??= new CaptureSettings();
            _settings.Capture.Recording ??= new RecordingSettings();
            if (AppPaths.IsCapturePilot)
            {
                Title = AppPaths.IsLegacyBackupsPilot ? UiText.Get("SlashDesk — Piloto Backups antigos 3.3.0 · #119") : AppPaths.IsProtectedStoragePilot ? UiText.Get("SlashDesk — Piloto Dados protegidos 3.3.0 · #116") : AppPaths.IsBackupCompatibilityPilot ? UiText.Get("SlashDesk — Piloto Backups 3.3.0 · #117") : AppPaths.IsLanguagesPilot ? UiText.Get("SlashDesk — Piloto Idiomas 3.3.0 · #82") : AppPaths.IsAuxiliaryPilot ? UiText.Get("SlashDesk — Piloto Interfaces auxiliares 3.3.0 · #69") : AppPaths.IsAboutPilot ? UiText.Get("SlashDesk — Piloto Sobre 3.3.0 · #68") : AppPaths.IsSettingsPilot ? UiText.Get("SlashDesk — Piloto Configurações 3.3.0 · #67") : AppPaths.IsStatisticsPilot ? UiText.Get("SlashDesk — Piloto Estatísticas 3.3.0 · #66") : AppPaths.IsQuickAccentPilot ? UiText.Get("SlashDesk — Piloto Acento Rápido 3.3.0 · #65") : AppPaths.IsCaptureOcrPilot ? UiText.Get("SlashDesk — Piloto Captura + OCR 3.3.0") : AppPaths.IsCaptureComplementsPilot ? UiText.Get("SlashDesk — Piloto Complementos da Captura 3.3.0 · #74") : AppPaths.IsShortcutsPilot ? UiText.Get("SlashDesk — Piloto Atalhos 3.3.0 · #64") : UiText.Get("SlashDesk — Piloto Captura 3.3.0 · #63");
                if (!AppPaths.IsSettingsPilot && !AppPaths.IsAboutPilot && !AppPaths.IsAuxiliaryPilot && !AppPaths.IsLanguagesPilot && !AppPaths.IsBackupCompatibilityPilot && !AppPaths.IsProtectedStoragePilot && !AppPaths.IsLegacyBackupsPilot)
                {
                    _settings.CheckUpdatesOnStartup = false;
                    _settings.StartWithWindows = false;
                }
                _settings.OnboardingCompleted = true;
                if (!AppPaths.IsSettingsPilot && !AppPaths.IsAboutPilot && !AppPaths.IsAuxiliaryPilot && !AppPaths.IsLanguagesPilot && !AppPaths.IsBackupCompatibilityPilot && !AppPaths.IsProtectedStoragePilot && !AppPaths.IsLegacyBackupsPilot) _settings.CloseToTray = false;
                if (!File.Exists(AppPaths.SettingsFile) || _settingsStore.IsProtected || AppPaths.IsProtectedStoragePilot || AppPaths.IsLegacyBackupsPilot)
                    _settings.Capture.OutputDirectoryTemplate = Path.Combine(AppPaths.DataDirectory, "Capturas", "{year}", "{month}");
                StartWithWindowsCheckBox.IsEnabled = false;
                CheckUpdatesCheckBox.IsEnabled = false;
                CheckUpdatesButton.IsEnabled = false;
                SettingsCheckUpdatesButton.IsEnabled = false;
            }
            if (AppPaths.IsSlashGuedes)
            {
                Title = "SlashGuedes — Preview " + UpdateService.InstalledVersion();
                // Preview 01/02 disabled updates while still pointing to upstream.
                // Enable our own Preview channel once; keep later user preferences.
                if (!_settings.GuedesUpdatesInitialized && !_settingsStore.IsProtected)
                {
                    _settings.GuedesUpdatesInitialized = true;
                    _settings.CheckUpdatesOnStartup = true;
                    try { await _settingsStore.SaveAsync(_settings); }
                    catch (Exception error)
                    {
                        AppDiagnosticLog.Write("guedes.update.preference.save_failed",
                            ("exceptionType", error.GetType().Name));
                    }
                }
            }
            RecordingPresetCatalog.Normalize(_settings.Capture.Recording);
            ThemeService.Apply(_settings.Theme);
            if (_trayIcon?.ContextMenuStrip is { } startupTrayMenu)
            {
                ApplyTrayTheme(startupTrayMenu);
            }

            await startup.RunAsync(
                "Estatísticas",
                _usageService.LoadAsync);

            CloseToTrayCheckBox.IsChecked = _settings.CloseToTray;
            StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
            ShowSuggestionsCheckBox.IsChecked = _settings.ShowSuggestions;
            CheckUpdatesCheckBox.IsChecked = _settings.CheckUpdatesOnStartup;
            RefreshAboutPresentation();
            SettingsVersionText.Text =
                UiText.Format($"Versão atual: {ProductVersion()}");
            UpdateChannelText.Text =
                UiText.Format($"Canal: estável · modo {AppPaths.Mode.ToString().ToLowerInvariant()}");
            BackupLocationText.Text =
                UiText.Format($"Backups em {AppPaths.BackupsDirectory}. Nenhum arquivo é enviado para a nuvem.");

            await startup.RunAsync(
                "Estado de atualização",
                RefreshUpdateStatusAsync);

            SelectComboByTag(ThemeBox, _settings.Theme);
            SelectComboByTag(LanguageBox, UiText.Normalize(_settings.Language));
            LoadQuickAccentControls();

            await startup.RunAsync(
                "Histórico de captura",
                async () =>
                {
                    await _captureService.LoadAsync();
                    await _captureService.CleanOlderThanAsync(
                        _settings.Capture.HistoryRetentionDays,
                        deleteFiles: false);
                });
            LoadCaptureSettings();
            await startup.RunAsync("Área de transferência", StartClipboardAsync);
            _initialized = true;

            IReadOnlyList<Snippet> loaded = [];
            _snippetStorageAvailable = await startup.RunAsync(
                "Atalhos",
                async () => loaded = await _repository.LoadAsync());
            if (_snippetStorageAvailable)
                loaded = await ShortcutShareMessage.RenameUntouchedDefaultAsync(_repository, loaded);
            ReplaceList(loaded);

            startup.Run(
                "Backup diário",
                _backupService.CreateDailySnapshot);
            startup.Run(
                "Monitor de atalhos",
                StartMonitoring);
            startup.Run(
                "Acento Rápido",
                _quickAccentService.Start);
            StartQuickAccentStatusMonitor();

            if (_snippets.Count > 0)
            {
                SelectSnippet(_snippets[0]);
            }
            else
            {
                BeginNewSnippet();
            }

            RefreshStatistics();
            ShowView(ShortcutsView, ShortcutsTabButton);
            StatusText.Text =
                UiText.Format($"{_snippets.Count} atalho(s) carregado(s)");
            startup.Run(
                "Atalhos globais de captura",
                ConfigureCaptureShortcuts);
            RefreshCaptureHistory();
            if (_settingsStore.IsProtected || _captureService.IsHistoryProtected || AppPaths.IsLegacyBackupsPilot || AppPaths.IsProtectedStoragePilot || AppPaths.IsLanguagesPilot || AppPaths.IsBackupCompatibilityPilot) ShowView(SettingsView, SettingsTabButton);
            else if (AppPaths.IsAuxiliaryPilot) ShowView(SettingsView, SettingsTabButton);
            else if (AppPaths.IsAboutPilot) ShowView(AboutView, AboutTabButton);
            else if (AppPaths.IsSettingsPilot) ShowView(SettingsView, SettingsTabButton);
            else if (AppPaths.IsStatisticsPilot) ShowView(StatisticsView, StatisticsTabButton);
            else if (AppPaths.IsQuickAccentPilot) ShowView(QuickAccentView, QuickAccentTabButton);
            else if (AppPaths.IsCapturePilot && !AppPaths.IsShortcutsPilot) ShowView(CaptureView, CaptureTabButton);

            if (!_settingsStore.IsProtected && !_settings.OnboardingCompleted)
            {
                await startup.RunAsync(
                    "Onboarding",
                    async () =>
                    {
                        var onboarding =
                            new OnboardingWindow { Owner = this };
                        onboarding.ShowDialog();
                        _settings.OnboardingCompleted = true;
                        await SavePreferencesAsync();
                    });
            }

            StartUpdateMonitor();
            ShowStartupWarnings(startup);
        }
        catch (Exception exception)
        {
            AppDiagnosticLog.Write(
                "startup.shell.failed",
                ("exceptionType", exception.GetType().FullName),
                ("hresult", $"0x{exception.HResult:X8}"));
            MessageBox.Show(
                UiText.Get("O SlashDesk encontrou uma falha inesperada ao preparar a interface. ") +
                UiText.Get("Nenhum dado foi apagado. Consulte a pasta Logs para o diagnóstico."),
                "SlashDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            BeginNewSnippet();
        }
    }

    private void ShowStartupWarnings(
        StartupModuleCoordinator startup)
    {
        if (startup.Failures.Count == 0)
        {
            return;
        }

        var modules = string.Join(
            Environment.NewLine,
            startup.Failures
                .Select(failure => $"• {UiText.Get(failure.Module)}")
                .Distinct(StringComparer.CurrentCultureIgnoreCase));
        var protectedStorage = _snippetStorageAvailable
            ? string.Empty
            : Environment.NewLine + Environment.NewLine +
              UiText.Get("O arquivo snippets.md não será alterado enquanto estiver indisponível. ") +
              UiText.Get("Use Restaurar ZIP na área de backups para recuperar os atalhos.");

        StatusText.Text =
            UiText.Format($"SlashDesk iniciado em modo reduzido · {startup.Failures.Count} módulo(s) indisponível(is)");
        ShowAppDialog(
            UiText.Get("O SlashDesk iniciou, mas alguns módulos não puderam ser carregados:") +
            Environment.NewLine + Environment.NewLine +
            modules +
            protectedStorage +
            Environment.NewLine + Environment.NewLine +
            UiText.Get("Os demais recursos continuam disponíveis. Consulte a pasta Logs para detalhes técnicos."),
            UiText.Get("SlashDesk iniciado em modo reduzido"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool EnsureSnippetStorageAvailable()
    {
        if (_snippetStorageAvailable)
        {
            return true;
        }

        ShowAppDialog(
            UiText.Get("O arquivo snippets.md não pôde ser carregado e está protegido contra alterações. ") +
            UiText.Get("Restaure um backup válido antes de salvar, excluir ou importar atalhos."),
            UiText.Get("Atalhos em modo protegido"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private void InitializeTray()
    {
        DrawingIcon? icon = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                icon = DrawingIcon.ExtractAssociatedIcon(Environment.ProcessPath);
            }
        }
        catch (Exception)
        {
            // O ícone da janela continua disponível mesmo se o shell não o extrair.
        }

        var menu = CreateTrayMenu();

        _trayIcon = new Forms.NotifyIcon
        {
            Text = UiText.Get("SlashDesk · Monitoramento ativo"),
            Icon = icon ?? DrawingSystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        _trayIcon.BalloonTipClicked += (_, _) =>
            Dispatcher.Invoke(OpenPendingCaptureNotification);
    }

    private void ShowTrayBalloon(
        int timeout,
        string title,
        string text,
        Forms.ToolTipIcon icon,
        string? openFilePath = null)
    {
        _pendingCaptureNotificationPath =
            string.IsNullOrWhiteSpace(openFilePath) ? null : openFilePath;
        _trayIcon?.ShowBalloonTip(timeout, title, text, icon);
    }

    private void OpenPendingCaptureNotification()
    {
        var path = _pendingCaptureNotificationPath;
        _pendingCaptureNotificationPath = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            if (_captureNotificationEvidenceLauncher is not null) _captureNotificationEvidenceLauncher(path);
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowAppDialog(
                exception.Message,
                UiText.Get("Não foi possível abrir a captura"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void ApplyTrayTheme(Forms.ContextMenuStrip menu) => TrayMenuTheme.Apply(menu);

    private void StartMonitoring()
    {
        _keyboardHook.UpdateSnippets(_snippets);
        if (!_keyboardHook.IsRunning)
        {
            _keyboardHook.Start();
        }

        MonitorStatusText.Text = UiText.Get("Monitoramento ativo");
        ShellStatusText.Text = UiText.Get("Monitoramento ativo");
    }

    private void QuickAccentService_OnChanged(object? sender, QuickAccentChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!e.Visible)
            {
                _quickAccentWindow.Hide();
                return;
            }

            _quickAccentWindow.UpdateChoices(
                e.Choices,
                e.SelectedIndex,
                _settings.QuickAccentToolbarPosition,
                _settings.QuickAccentShowUnicode);
        }));
    }

    private void QuickAccentService_OnCharacterInserted(
        object? sender,
        QuickAccentCharacterInsertedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_restoreRecoveryBlocked || _servicesDisposed) return;
            var saved = await TryUpdateAuxiliaryUsageAsync(() => _usageService.RecordQuickAccentAsync(e.Character));
            if (_servicesDisposed || _restoreRecoveryBlocked) return;
            StatusText.Text = UiText.Get("Acento inserido") + AuxiliaryUsageStatusSuffix(saved);
        }));
    }

    private void KeyboardHook_OnSuggestionsChanged(object? sender, SnippetSuggestionsEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_restoreRecoveryBlocked || _servicesDisposed) return;
            if (!_settings.ShowSuggestions)
            {
                _suggestionWindow.Hide();
                return;
            }

            _suggestionWindow.UpdateSuggestions(
                e.Snippets,
                e.ScreenPosition,
                e.SelectedIndex);
        }));
    }

    private void SuggestionWindow_OnSnippetChosen(Snippet snippet) =>
        _keyboardHook.ConfirmSuggestion(snippet, SuggestionConfirmation.Click);

    private void KeyboardHook_OnExpansionRequested(
        object? sender,
        SnippetExpansionRequestedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_restoreRecoveryBlocked || _servicesDisposed) return;
            using var expansionLease = _expansionRequestGate.TryEnter();
            if (expansionLease is null)
            {
                SafeDiagnosticLog.Write("expansion.ignored_busy");
                StatusText.Text = UiText.Get("Outra expansão ainda está em andamento");
                return;
            }
            int inserted;
            try
            {
                SafeDiagnosticLog.Write("expansion.started", new Dictionary<string, object?>
                {
                    ["targetHandleValid"] = e.TargetWindow != IntPtr.Zero,
                    ["typedCharacterCount"] = e.TypedCharacterCount,
                    ["confirmation"] = e.Confirmation.ToString()
                });
                _keyboardHook.ResetBuffer(BufferResetReason.ExpansionStarted);
                _suggestionWindow.Hide();
                IReadOnlyDictionary<string, string> values =
                    new Dictionary<string, string>();
                var fields = _expansionService.GetFillableFields(e.Snippet);

                if (fields.Count > 0)
                {
                    var form = VariableInputWindow.ShowForTarget(fields, e.TargetWindow);
                    if (form.DialogResult != true)
                    {
                        _keyboardHook.ResetBuffer(BufferResetReason.SuggestionCancelled);
                        StatusText.Text = UiText.Get("Expansão cancelada");
                        return;
                    }

                    values = form.Values;
                }

                inserted = await _expansionService.ExpandAsync(
                    e.Snippet,
                    values,
                    e.TargetWindow,
                    e.TypedCharacterCount,
                    CancellationToken.None);
            }
            catch (ExpansionBusyException)
            {
                StatusText.Text = UiText.Get("Outra expansão ainda está em andamento");
                return;
            }
            catch (Exception exception)
            {
                SafeDiagnosticLog.Write("expansion.failed", new Dictionary<string, object?>
                {
                    ["exceptionType"] = exception.GetType().Name,
                    ["reason"] = exception is OperationCanceledException ? "target_changed" : "operation_failed"
                });
                StatusText.Text = UiText.Format($"Falha ao inserir {e.Snippet.Trigger}");
                ShowAppDialog(
                    exception.Message,
                    UiText.Get("Não foi possível inserir o texto"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            finally
            {
                _keyboardHook.ResetBuffer(BufferResetReason.ExpansionFinished);
            }
            await CompleteSuccessfulExpansionAsync(e.Snippet, inserted);
        }));
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) =>
        RefreshNavigation();

    private void RefreshNavigation()
    {
        if (CategoriesPanel is null || SnippetListPanel is null)
        {
            return;
        }

        var query = SearchBox.Text.Trim();
        if (ShortcutSearchHint is not null) ShortcutSearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ShortcutSearchClearButton is not null) ShortcutSearchClearButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        IEnumerable<Snippet> filtered = _snippets;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Trigger.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                ShortcutCategories.Resolve(item.Category).Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Content.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_selectedCategory))
        {
            filtered = filtered.Where(item =>
                ShortcutCategories.Resolve(item.Category).Name.Equals(_selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (_showShortcutFavorites) filtered = filtered.Where(item => item.IsFavorite);
        if (_showShortcutPinned) filtered = filtered.Where(item => item.IsPinned);
        if (_showMostUsed)
        {
            filtered = filtered
                .Select(item => new { Snippet = item, Usage = _usageService.For(item.Id)?.Count ?? 0 })
                .Where(item => item.Usage > 0)
                .OrderByDescending(item => item.Usage)
                .ThenBy(item => item.Snippet.Trigger, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => item.Snippet);
        }
        else
        {
            filtered = filtered.OrderBy(item => item.Trigger, StringComparer.CurrentCultureIgnoreCase);
        }

        var visible = filtered.OrderByDescending(item => item.IsPinned).ToList();

        CategoriesPanel.Children.Clear();
        CategoriesPanel.Children.Add(CreateCategoryButton(null, UiText.Get("Todos"), _snippets.Count));
        foreach (var category in ShortcutCategories.All)
        {
            var count = _snippets.Count(item => ShortcutCategories.Resolve(item.Category).Name == category.Name);
            CategoriesPanel.Children.Add(CreateCategoryButton(category.Name, category.DisplayName, count));
        }

        UpdateShortcutItems(visible);

        SnippetCountText.Text = $"{visible.Count}/{_snippets.Count}";
        ShellCollectionStatusText.Text = UiText.Format($"{_snippets.Count} atalhos · {CategoriesPanel.Children.Count - 1} categorias");
        RefreshMostUsed();
    }

    private void RefreshMostUsed()
    {
        ApplyDisplayFilterState(DisplayAllButton, !_showMostUsed && !_showShortcutFavorites && !_showShortcutPinned);
        ApplyDisplayFilterState(DisplayFavoritesButton, _showShortcutFavorites);
        ApplyDisplayFilterState(DisplayPinnedButton, _showShortcutPinned);
        ApplyDisplayFilterState(DisplayMostUsedButton, _showMostUsed);
    }

    private void DisplayAll_OnClick(object sender, RoutedEventArgs e)
    {
        _showMostUsed = false; _showShortcutFavorites = false; _showShortcutPinned = false;
        RefreshNavigation();
    }

    private void DisplayMostUsed_OnClick(object sender, RoutedEventArgs e)
    {
        _showMostUsed = true; _showShortcutFavorites = false; _showShortcutPinned = false;
        RefreshNavigation();
    }

    private void ApplyDisplayFilterState(Button button, bool selected)
    {
        button.SetResourceReference(Button.BackgroundProperty, selected ? "Lab.tint" : "Lab.shell");
        button.SetResourceReference(Button.BorderBrushProperty, selected ? "Lab.line-strong" : "Lab.line");
        button.SetResourceReference(Button.ForegroundProperty, selected ? "Lab.accent-text" : "Lab.muted");
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void SelectSnippet(Snippet snippet)
    {
        if (ReferenceEquals(snippet, _selected) || !CanDiscardShortcutDraft()) return;
        _loadingShortcutDraft = true;
        _selected = snippet;
        _shortcutDuplicateSource = null;
        NameBox.Text = snippet.Name;
        TriggerBox.Text = snippet.Trigger;
        SelectShortcutCategory(snippet.Category);
        FormatBox.SelectedIndex = snippet.Format == SnippetFormat.Markdown ? 1 : 0;
        RichTextMarkdownConverter.Load(ContentEditor, snippet.Content, snippet.Format);
        StatusText.Text = snippet.HasLegacyIncompatibleTrigger
            ? UiText.Get("Atalho legado preservado, mas incompatível com o monitor atual; corrija o gatilho para reativá-lo")
            : snippet.Enabled ? UiText.Get("Atalho ativo") : UiText.Get("Atalho pausado");
        _loadingShortcutDraft = false;
        ResetShortcutDraftBaseline();
        RefreshShortcutListSelection();
        UpdatePreview();
        if (ShortcutEditorPanel.IsLoaded) LabMotion.PlayEntrance(ShortcutEditorPanel);
    }

    private void NewSnippet_OnClick(object sender, RoutedEventArgs e) =>
        BeginNewSnippet();

    private void BeginNewSnippet(bool discardAlreadyConfirmed = false)
    {
        if (!discardAlreadyConfirmed && !CanDiscardShortcutDraft()) return;
        _shortcutDuplicateSource = null;
        _loadingShortcutDraft = true;
        _selected = null;
        NameBox.Clear();
        TriggerBox.Text = "/";
        SelectShortcutCategory("Geral");
        FormatBox.SelectedIndex = 0;
        RichTextMarkdownConverter.Load(ContentEditor, string.Empty, SnippetFormat.Plain);
        StatusText.Text = UiText.Get("Novo atalho");
        _loadingShortcutDraft = false;
        ResetShortcutDraftBaseline();
        RefreshNavigation();
        NameBox.Focus();
        UpdatePreview();
    }

    private async void SaveSnippet_OnClick(object sender, RoutedEventArgs e) => await TrySaveShortcutAsync();

    private async System.Threading.Tasks.Task<bool> TrySaveShortcutAsync()
    {
        if (!EnsureSnippetStorageAvailable())
        {
            return false;
        }

        if (_shortcutOperationInProgress) return false;
        var previous = _selected;
        var format = FormatBox.SelectedIndex == 1 ? SnippetFormat.Markdown : SnippetFormat.Plain;
        var candidate = new Snippet
        {
            Id = previous?.Id ?? Guid.NewGuid(),
            Name = NameBox.Text.Trim(),
            Trigger = TriggerBox.Text.Trim(),
            Category = SelectedShortcutCategory,
            Content = RichTextMarkdownConverter.Save(ContentEditor, format),
            Format = format,
            Enabled = previous?.Enabled ?? _shortcutDuplicateSource?.Enabled ?? true,
            IsFavorite = previous?.IsFavorite ?? false, IsPinned = previous?.IsPinned ?? false,
            ConfirmKeys = previous?.ConfirmKeys.ToList() ?? _shortcutDuplicateSource?.ConfirmKeys.ToList() ?? ["Enter", "Tab", "Space"]
        };

        if (!TriggerRule.TryValidate(candidate.Trigger, out var triggerError))
        {
            ShowAppDialog(
                triggerError,
                UiText.Get("Atalho incompatível"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            TriggerBox.Focus();
            return false;
        }

        var nextState = _snippets
            .Where(item => !ReferenceEquals(item, previous))
            .Append(candidate)
            .ToList();

        try
        {
            _shortcutOperationInProgress = true;
            ShortcutEditorPanel.IsEnabled = false;
            await _repository.SaveAsync(nextState);
            if (previous is null)
            {
                _snippets.Add(candidate);
            }
            else
            {
                _snippets[_snippets.IndexOf(previous)] = candidate;
            }

            _selected = candidate;
            _keyboardHook.UpdateSnippets(_snippets);
            RefreshNavigation();
            RefreshStatistics();
            ResetShortcutDraftBaseline();
            ShortcutLegacyCategoryHint.Visibility = Visibility.Collapsed;
            StatusText.Text = UiText.Format($"Salvo em {AppPaths.SnippetsFile}");
            return true;
        }
        catch (Exception exception)
        {
            ShowAppDialog(
                exception.Message,
                UiText.Get("Não foi possível salvar"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _shortcutOperationInProgress = false;
            ShortcutEditorPanel.IsEnabled = true;
            RefreshShortcutDraftState();
        }
    }

    private async void DeleteSnippet_OnClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureSnippetStorageAvailable())
        {
            return;
        }

        if (_selected is null || _shortcutOperationInProgress)
        {
            return;
        }

        var confirmation = new ShortcutConfirmationWindow(UiText.Get("Excluir atalho?"), UiText.Get("Este atalho será removido da sua lista."),
            $"{_selected.Name}\n{_selected.Trigger} · {_selected.Category}", UiText.Get("Excluir atalho")) { Owner = this };
        LabMotion.SetReduced(confirmation, LabMotion.GetReduced(this)); confirmation.EnableBackdrop();
        if (!ShowCaptureDialog(confirmation))
        {
            return;
        }

        var removed = _selected;
        try
        {
            _shortcutOperationInProgress = true; ShortcutEditorPanel.IsEnabled = false;
            await _repository.SaveAsync(_snippets.Where(item => !ReferenceEquals(item, removed)).ToList());
            _snippets.Remove(removed);
            _shortcutOperationInProgress = false;
            _keyboardHook.UpdateSnippets(_snippets);
            _shortcutDraftBaseline = null;
            BeginNewSnippet();
            RefreshStatistics();
            StatusText.Text = UiText.Get("Atalho excluído");
        }
        catch (Exception exception)
        {
            RefreshNavigation();
            ShowAppDialog(
                exception.Message,
                UiText.Get("Não foi possível excluir"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _shortcutOperationInProgress = false; ShortcutEditorPanel.IsEnabled = true;
            RefreshShortcutDraftState();
        }
    }

    private void FormatBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormattingToolbar is not null)
        {
            FormattingToolbar.Visibility =
                FormatBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            RefreshShortcutDraftState();
            UpdatePreview();
        }
    }

    private void Bold_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleBold.Execute(null, ContentEditor);

    private void Italic_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleItalic.Execute(null, ContentEditor);

    private void Underline_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleUnderline.Execute(null, ContentEditor);

    private void FontFamilyBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncShortcutFormatting || ContentEditor is null ||
            FontFamilyBox?.SelectedItem is not ComboBoxItem { Tag: string font })
        {
            return;
        }

        ApplyShortcutFormatting(TextElement.FontFamilyProperty, new FontFamily(font));
    }

    private void FontSizeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncShortcutFormatting || ContentEditor is null ||
            FontSizeBox?.SelectedItem is not ComboBoxItem { Tag: string sizeText } ||
            !double.TryParse(sizeText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var size)) return;
        ApplyShortcutFormatting(TextElement.FontSizeProperty, size);
    }

    private void Color_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button anchor) ShowShortcutColorPicker(anchor, TextElement.ForegroundProperty);
    }

    private void Highlight_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button anchor) ShowShortcutColorPicker(anchor, TextElement.BackgroundProperty);
    }

    private void Bullets_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleBullets.Execute(null, ContentEditor);

    private void Numbering_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleNumbering.Execute(null, ContentEditor);

    private void AlignLeft_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.AlignLeft.Execute(null, ContentEditor);

    private void AlignCenter_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.AlignCenter.Execute(null, ContentEditor);

    private void AlignRight_OnClick(object sender, RoutedEventArgs e) =>
        EditingCommands.AlignRight.Execute(null, ContentEditor);

    private void Table_OnClick(object sender, RoutedEventArgs e)
    {
        var rowsText = PromptDialog.Show(this, UiText.Get("Inserir tabela"), UiText.Get("Quantidade de linhas"), "2");
        if (rowsText is null)
        {
            return;
        }

        var columnsText = PromptDialog.Show(this, UiText.Get("Inserir tabela"), UiText.Get("Quantidade de colunas"), "2");
        if (!int.TryParse(rowsText, out var rows) ||
            !int.TryParse(columnsText, out var columns) ||
            rows is < 1 or > 10 ||
            columns is < 1 or > 8)
        {
            ShowAppDialog(
                UiText.Get("Use de 1 a 10 linhas e de 1 a 8 colunas."),
                UiText.Get("Tabela"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 6)
        };
        for (var column = 0; column < columns; column++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        TableCell? firstCell = null;
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var row = new TableRow();
            group.Rows.Add(row);
            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                var cell = new TableCell(new Paragraph(new Run(
                    rowIndex == 0 ? UiText.Format($"Título {columnIndex + 1}") : UiText.Get("Texto"))))
                {
                    BorderBrush = (Brush)FindResource("DividerBrush"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7)
                };
                firstCell ??= cell;
                row.Cells.Add(cell);
            }
        }

        var paragraph = ContentEditor.CaretPosition.Paragraph;
        if (paragraph?.Parent is FlowDocument document)
        {
            document.Blocks.InsertAfter(paragraph, table);
        }
        else
        {
            ContentEditor.Document.Blocks.Add(table);
        }

        if (firstCell is not null)
        {
            ContentEditor.CaretPosition = firstCell.ContentStart;
        }
        ContentEditor.Focus();
        UpdatePreview();
    }

    private void Link_OnClick(object sender, RoutedEventArgs e)
    {
        var urlText = PromptDialog.Show(this, UiText.Get("Inserir hiperlink"), UiText.Get("Endereço (https://...)"));
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri))
        {
            if (urlText is not null)
            {
                ShowAppDialog(UiText.Get("Informe um endereço completo iniciado por http:// ou https://."));
            }
            return;
        }

        if (ContentEditor.Selection.IsEmpty)
        {
            var label = PromptDialog.Show(this, UiText.Get("Texto do link"), UiText.Get("Texto exibido"), urlText);
            if (label is null)
            {
                return;
            }

            ContentEditor.Selection.Text = $"[{label}]({uri})";
        }
        else
        {
            try
            {
                _ = new Hyperlink(ContentEditor.Selection.Start, ContentEditor.Selection.End)
                {
                    NavigateUri = uri,
                    Foreground = (Brush)FindResource("AccentBrush")
                };
            }
            catch (InvalidOperationException)
            {
                var label = ContentEditor.Selection.Text;
                ContentEditor.Selection.Text = $"[{label}]({uri})";
            }
        }

        ContentEditor.Focus();
    }

    private void Image_OnClick(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Get("Adicionar imagem ao atalho"),
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.gif;*.webp",
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        var info = new System.IO.FileInfo(picker.FileName);
        if (info.Length > 5 * 1024 * 1024)
        {
            ShowAppDialog(
                UiText.Get("Escolha uma imagem com até 5 MB."),
                UiText.Get("Imagem muito grande"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        System.IO.Directory.CreateDirectory(AppPaths.AssetsDirectory);
        var safeName = $"{Guid.NewGuid():N}{info.Extension.ToLowerInvariant()}";
        var destination = System.IO.Path.Combine(AppPaths.AssetsDirectory, safeName);
        System.IO.File.Copy(info.FullName, destination);
        ContentEditor.Selection.Text = $"![{System.IO.Path.GetFileNameWithoutExtension(info.Name)}](assets/{safeName})";
        ContentEditor.Focus();
        UpdatePreview();
    }

    private void VariableChip_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token })
        {
            ContentEditor.Selection.Text = token;
            ContentEditor.Focus();
        }
    }

    private void ContentEditor_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ContentEditor is not null)
        {
            foreach (var block in ContentEditor.Document.Blocks.OfType<BlockUIContainer>())
                ShortcutCodeBlockView.AttachBlock(block, view => OpenCodeBlock(view));
            ShortcutEmojiAssets.RestoreDocument(ContentEditor.Document);
        }
        RefreshShortcutDraftState();
        RefreshShortcutFormatting();
        UpdatePreview();
    }

    private void EditorField_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshShortcutDraftState();
    }

    private void UpdatePreview()
    {
        if (PreviewDocument is null || ContentEditor is null || FormatBox is null)
        {
            return;
        }

        var format = FormatBox.SelectedIndex == 1 ? SnippetFormat.Markdown : SnippetFormat.Plain;
        var content = RichTextMarkdownConverter.Save(ContentEditor, format);
        var rendered = new TemplateEngine().Render(content, PreviewValues(content, format), literalCodeBlocks: format == SnippetFormat.Markdown);
        RichTextMarkdownConverter.BuildPreview(PreviewDocument, rendered, format);
    }

    private static IReadOnlyDictionary<string, string> PreviewValues(string template, SnippetFormat format)
    {
        var engine = new TemplateEngine();
        return engine.GetFillableFields(template, literalCodeBlocks: format == SnippetFormat.Markdown)
            .ToDictionary(
                item => item.Name,
                item => item.DefaultValue ?? $"[{item.Name}]",
                StringComparer.CurrentCultureIgnoreCase);
    }

    private void ShortcutsTab_OnClick(object sender, RoutedEventArgs e)
    {
        ShowView(ShortcutsView, ShortcutsTabButton);
    }

    private void QuickAccentTab_OnClick(object sender, RoutedEventArgs e) =>
        ShowView(QuickAccentView, QuickAccentTabButton);

    private void CaptureTab_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshCaptureHistory();
        ShowView(CaptureView, CaptureTabButton);
    }

    private void StatisticsTab_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshStatistics();
        ShowView(StatisticsView, StatisticsTabButton);
    }

    private void SettingsTab_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshBackupSummary();
        ShowView(SettingsView, SettingsTabButton);
    }

    private void AboutTab_OnClick(object sender, RoutedEventArgs e) =>
        ShowView(AboutView, AboutTabButton);

    private void ShowView(UIElement view, Button selectedButton)
    {
        var views = new UIElement[]
        {
            ShortcutsView, CaptureView, QuickAccentView, ClipboardView, StatisticsView, SettingsView, AboutView
        };
        foreach (var item in views)
        {
            item.Visibility = ReferenceEquals(item, view)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        var buttons = new[]
        {
            ShortcutsTabButton, CaptureTabButton, QuickAccentTabButton, ClipboardTabButton, StatisticsTabButton,
            SettingsTabButton, AboutTabButton
        };
        foreach (var button in buttons)
        {
            var selected = ReferenceEquals(button, selectedButton);
            button.Tag = selected ? "Selected" : null;
        }

        (ShellPageTitle.Text, ShellPageDescription.Text) = ReferenceEquals(view, ShortcutsView)
            ? (UiText.Get("Atalhos"), UiText.Get("Crie, organize e edite seus textos prontos."))
            : ReferenceEquals(view, QuickAccentView)
                ? (UiText.Get("Acento Rápido"), UiText.Get("Digite caracteres especiais sem interromper seu fluxo."))
                : ReferenceEquals(view, CaptureView)
                    ? (UiText.Get("Captura"), UiText.Get("Capture, edite e grave sua tela localmente."))
                    : ReferenceEquals(view, ClipboardView)
                        ? ("Área de transferência", "Histórico local, favoritos e categorias. • Win + Shift + V")
                    : ReferenceEquals(view, StatisticsView)
                        ? (UiText.Get("Estatísticas"), UiText.Get("Acompanhe o uso real dos recursos do SlashDesk."))
                        : ReferenceEquals(view, SettingsView)
                            ? (UiText.Get("Configurações"), UiText.Get("Personalize o comportamento do SlashDesk."))
                            : (UiText.Get("Sobre"), UiText.Get("Versão, privacidade, suporte e atualizações."));
        ShortcutHeaderActions.Visibility = ReferenceEquals(view, ShortcutsView)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ShellCollectionStatusText.Visibility = ReferenceEquals(view, ShortcutsView)
            ? Visibility.Visible
            : Visibility.Collapsed;

        var monitorReady = _keyboardHook.IsRunning;
        MonitorStatusText.Text = monitorReady
            ? ReferenceEquals(view, QuickAccentView) ? UiText.Get("Monitor de atalhos ativo") : UiText.Get("Monitoramento ativo")
            : UiText.Get("Monitor de atalhos indisponível");
        ShellStatusText.Text = MonitorStatusText.Text;
        MonitorStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, monitorReady ? "SuccessBrush" : "MutedBrush");
        ShellMonitorStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, monitorReady ? "SuccessBrush" : "MutedBrush");

        if (ReferenceEquals(view, ShortcutsView) && ShortcutsView.IsLoaded) LabMotion.PlayEntrance(ShortcutsView);
        if (ReferenceEquals(view, AboutView))
        {
            RefreshAboutPresentation(); UpdateAboutLayout(ActualWidth);
            if (AboutView.IsLoaded) LabMotion.PlayEntrance(AboutView);
        }
        RefreshStorageProtection();
        if (ReferenceEquals(view, SettingsView))
        {
            RefreshSettingsPresentation(); UpdateSettingsLayout(ActualWidth);
            if (SettingsView.IsLoaded) LabMotion.PlayEntrance(SettingsView);
        }
        if (ReferenceEquals(view, StatisticsView))
        {
            UpdateStatisticsLayout(ActualWidth);
            if (StatisticsView.IsLoaded) LabMotion.PlayEntrance(StatisticsView);
        }
        if (ReferenceEquals(view, QuickAccentView))
        {
            UpdateQuickAccentLayout(ActualWidth);
            UpdateQuickAccentPreviewSelection();
            if (QuickAccentView.IsLoaded) LabMotion.PlayEntrance(QuickAccentView);
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizedState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeWindow_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeWindow_OnClick(object sender, RoutedEventArgs e) =>
        ToggleMaximizedState();

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e) =>
        Close();

    private void ToggleMaximizedState() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private async void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _settingsOperationInProgress || _settingsStore.IsProtected) return;
        var previousClose = _settings.CloseToTray;
        var previousSuggestions = _settings.ShowSuggestions;
        var previousStart = _settings.StartWithWindows;
        var requestedStart = StartWithWindowsCheckBox.IsChecked == true;
        var previousUpdateMonitoring = _settings.CheckUpdatesOnStartup;
        _settings.CloseToTray = CloseToTrayCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.ShowSuggestions = ShowSuggestionsCheckBox.IsChecked == true;
        _settings.CheckUpdatesOnStartup = CheckUpdatesCheckBox.IsChecked == true;
        _settingsOperationInProgress = true; SettingsCardsGrid.IsEnabled = false;

        try
        {
            if (!AppPaths.IsCapturePilot && previousStart != _settings.StartWithWindows)
            {
                StartupService.SetEnabled(_settings.StartWithWindows);
            }
            await SavePreferencesAsync();

            if (previousUpdateMonitoring != _settings.CheckUpdatesOnStartup)
            {
                if (_settings.CheckUpdatesOnStartup)
                {
                    StartUpdateMonitor();
                }
                else
                {
                    StopUpdateMonitor();
                }
            }

            if (!_settings.ShowSuggestions)
            {
                _suggestionWindow.Hide();
            }
            SettingsOperationText.Text = UiText.Get("Preferência salva.");
        }
        catch (Exception exception)
        {
            _settings.CloseToTray = previousClose;
            _settings.ShowSuggestions = previousSuggestions;
            _settings.StartWithWindows = previousStart;
            _settings.CheckUpdatesOnStartup = previousUpdateMonitoring;
            StartWithWindowsCheckBox.IsChecked = previousStart;
            CheckUpdatesCheckBox.IsChecked = previousUpdateMonitoring;
            CloseToTrayCheckBox.IsChecked = previousClose;
            ShowSuggestionsCheckBox.IsChecked = previousSuggestions;
            if (!AppPaths.IsCapturePilot && previousStart != requestedStart)
            {
                try { StartupService.SetEnabled(previousStart); }
                catch (Exception rollback) { SettingsOperationText.Text = UiText.Get("Não foi possível reverter a inicialização: ") + rollback.Message; }
            }
            SettingsOperationText.Text = UiText.Get("Não foi possível salvar a preferência.");
            ShowSettingsDialog(
                exception.Message,
                UiText.Get("Não foi possível salvar a configuração"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally { _settingsOperationInProgress = false; SettingsCardsGrid.IsEnabled = true; }
    }

    private async void ImportSnippets_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settingsOperationInProgress || _shortcutOperationInProgress) return;
        if (!EnsureSnippetStorageAvailable())
        {
            return;
        }

        if (!CanDiscardShortcutDraft()) return;
        var source = ImportSourceBox.SelectedItem is ComboBoxItem { Tag: string sourceTag } &&
                     Enum.TryParse<SnippetImportSource>(sourceTag, out var parsed)
            ? parsed
            : SnippetImportSource.SlashDesk;
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Format($"Importar atalhos do {ImportSourceName(source)}"),
            Filter = source switch
            {
                SnippetImportSource.SlashDesk => UiText.Get("Atalhos do SlashDesk|*.md"),
                SnippetImportSource.TextBlaze => UiText.Get("Exportação do Text Blaze|*.json"),
                SnippetImportSource.Espanso => UiText.Get("Configuração do Espanso|*.yml;*.yaml"),
                _ => UiText.Get("Todos os arquivos|*.*")
            },
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await RunSettingsMaintenanceAsync(UiText.Get("Importando e protegendo os atalhos…"), async () =>
            {
            _shortcutOperationInProgress = true; ShortcutEditorPanel.IsEnabled = false;
            RefreshShortcutDraftState();
            var token = _settingsCancellation?.Token ?? default;
            var result = await _snippetImportService.ImportAsync(picker.FileName, source, token);
            if (result.Snippets.Count == 0)
            {
                ShowSettingsDialog(
                    UiText.Get("Nenhum atalho compatível foi encontrado no arquivo."),
                    UiText.Get("Importação concluída"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                StatusText.Text = SettingsOperationText.Text = UiText.Get("Nenhum atalho compatível encontrado");
                return;
            }

            var conflicts = result.Snippets.Count(incoming =>
                _snippets.Any(current =>
                    current.Trigger.Equals(incoming.Trigger, StringComparison.OrdinalIgnoreCase)));
            var mode = ShowSettingsDialog(
                UiText.Format($"{result.Snippets.Count} atalho(s) compatível(is) encontrado(s).") +
                (conflicts == 0
                    ? UiText.Get("\n\nDeseja adicionar esses atalhos aos atuais?")
                    : UiText.Format($"\n\n{conflicts} conflito(s) de comando. Escolha substituir os conflitos ou manter os atuais e importar somente os demais.")),
                UiText.Format($"Importar do {ImportSourceName(source)}"),
                conflicts == 0 ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question, conflicts == 0 ? UiText.Get("Importar atalhos") : UiText.Get("Substituir conflitos"), UiText.Get("Manter atuais"));
            if (mode is MessageBoxResult.Cancel or MessageBoxResult.None ||
                (conflicts == 0 && mode == MessageBoxResult.No))
            {
                StatusText.Text = SettingsOperationText.Text = UiText.Get("Importação cancelada. Os atalhos atuais foram preservados.");
                return;
            }

            await _usageService.RunMaintenanceAsync(() => _backupService.CreateManualSnapshot(token), token);
            var replaceConflicts = mode == MessageBoxResult.Yes;
            var incomingTriggers = result.Snippets
                .Select(item => item.Trigger)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var merged = _snippets
                .Where(current => !replaceConflicts || !incomingTriggers.Contains(current.Trigger))
                .ToList();
            foreach (var incoming in result.Snippets)
            {
                if (replaceConflicts ||
                    !merged.Any(current =>
                        current.Trigger.Equals(incoming.Trigger, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(incoming);
                }
            }

            _shortcutOperationInProgress = true;
            ShortcutEditorPanel.IsEnabled = false;
            token.ThrowIfCancellationRequested();
            await _repository.SaveAsync(merged, token);
            _settingsProgress?.PreventCancellation();
            ReplaceList(merged);
            _shortcutOperationInProgress = false;
            _shortcutDraftBaseline = null; _selected = null;
            if (_snippets.Count > 0) SelectSnippet(_snippets[0]); else BeginNewSnippet();
            _keyboardHook.UpdateSnippets(_snippets);
            RefreshStatistics();
            var warningSummary = result.Warnings.Count == 0
                ? string.Empty
                : UiText.Format($"\n\n{result.Warnings.Count} aviso(s) de compatibilidade. Os primeiros:\n• ") +
                  string.Join("\n• ", result.Warnings.Take(4));
            ShowSettingsDialog(
                UiText.Format($"{result.Snippets.Count} atalho(s) processado(s). O snippets.md foi atualizado e o estado anterior foi salvo em backup.{warningSummary}"),
                UiText.Get("Importação concluída"),
                MessageBoxButton.OK,
                result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            StatusText.Text = UiText.Format($"Importação do {ImportSourceName(source)} concluída");
            SettingsOperationText.Text = StatusText.Text;
            RefreshBackupSummary();
            }, cancellable: true);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = SettingsOperationText.Text = UiText.Get("Operação cancelada. Os dados atuais foram preservados.");
        }
        catch (Exception exception)
        {
            SettingsOperationText.Text = UiText.Get("Não foi possível importar: ") + exception.Message;
            ShowSettingsDialog(
                exception.Message,
                UiText.Get("Não foi possível importar"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _shortcutOperationInProgress = false;
            ShortcutEditorPanel.IsEnabled = true;
            RefreshShortcutDraftState();
        }
    }

    private async void CreateBackup_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = null;
            await RunSettingsMaintenanceAsync(UiText.Get("Criando e verificando o backup…"), () =>
                _usageService.RunMaintenanceAsync(() => path = _backupService.CreateManualSnapshot()));
            RefreshBackupSummary();
            StatusText.Text = UiText.Format($"Backup criado: {Path.GetFileName(path)}");
            SettingsOperationText.Text = StatusText.Text;
        }
        catch (Exception exception)
        {
            SettingsOperationText.Text = UiText.Get("Não foi possível criar o backup: ") + exception.Message;
            ShowSettingsDialog(
                exception.Message,
                UiText.Get("Não foi possível criar o backup"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void AnalyzeAssets_OnClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureSnippetStorageAvailable())
        {
            return;
        }

        try
        {
            await RunSettingsMaintenanceAsync(UiText.Get("Analisando imagens dos atalhos…"), async () =>
            {
            var snippets = _snippets.ToList();
            var report = await Task.Run(() => _assetMaintenanceService.Analyze(snippets));
            if (report.UnsafeReferenceCount > 0)
            {
                ShowSettingsDialog(
                    UiText.Format($"A análise encontrou {report.UnsafeReferenceCount} referência(s) local(is) inválida(s). ") +
                    UiText.Get("Por segurança, nenhuma imagem pode ser removida até que esses atalhos sejam corrigidos."),
                    UiText.Get("Limpeza de imagens bloqueada"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusText.Text = SettingsOperationText.Text = UiText.Get("Limpeza bloqueada por referências inválidas");
                return;
            }

            if (report.Orphans.Count == 0)
            {
                ShowSettingsDialog(
                    UiText.Get("Nenhuma imagem órfã foi encontrada. Os arquivos em uso foram preservados."),
                    UiText.Get("Análise concluída"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                StatusText.Text = SettingsOperationText.Text = UiText.Get("Nenhuma imagem órfã encontrada");
                return;
            }

            var size = report.OrphanSizeBytes < 1024
                ? $"{report.OrphanSizeBytes} B"
                : $"{report.OrphanSizeBytes / 1024d:N1} KB";
            var preview = string.Join(
                "\n• ",
                report.Orphans.Take(10).Select(item => item.RelativePath));
            var remaining = report.Orphans.Count > 10
                ? UiText.Format($"\n• ... e mais {report.Orphans.Count - 10}")
                : string.Empty;
            var confirmation = ShowSettingsDialog(
                UiText.Format($"{report.Orphans.Count} imagem(ns) sem referência ({size}):\n\n• {preview}{remaining}\n\n") +
                UiText.Get("Deseja criar um backup completo e remover somente esses arquivos?"),
                UiText.Get("Revisar imagens órfãs"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning, UiText.Get("Criar backup e remover"));
            if (confirmation != MessageBoxResult.Yes)
            {
                StatusText.Text = SettingsOperationText.Text = UiText.Get("Limpeza de imagens cancelada");
                return;
            }

            AssetCleanupResult? result = null;
            await _usageService.RunMaintenanceAsync(() => result = _assetMaintenanceService.DeleteOrphansAfterBackup(report, _backupService.CreateManualSnapshot));
            RefreshBackupSummary();
            StatusText.Text = UiText.Format($"{result!.DeletedCount} imagem(ns) órfã(s) removida(s)");
            SettingsOperationText.Text = StatusText.Text;
            ShowSettingsDialog(
                UiText.Format($"{result.DeletedCount} imagem(ns) removida(s).\n\n") +
                UiText.Format($"Backup criado antes da limpeza: {Path.GetFileName(result.BackupPath)}"),
                UiText.Get("Limpeza concluída"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            });
        }
        catch (Exception exception)
        {
            SettingsOperationText.Text = UiText.Get("Não foi possível analisar imagens: ") + exception.Message;
            ShowSettingsDialog(
                exception.Message,
                UiText.Get("Não foi possível analisar as imagens"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void RestoreBackup_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settingsOperationInProgress || _shortcutOperationInProgress) return;
        if (!CanDiscardShortcutDraft()) return;
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Get("Restaurar backup do SlashDesk"),
            InitialDirectory = AppPaths.BackupsDirectory,
            Filter = UiText.Get("Backup do SlashDesk|SlashDesk-backup-*.zip|Arquivo ZIP|*.zip"),
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true ||
            ShowSettingsDialog(
                UiText.Get("Restaurar este backup substituirá os dados incluídos nele, inclusive Meus emojis quando presentes. Um backup de segurança será criado antes de continuar. Salve suas edições de imagem antes de continuar: se a recuperação ficar pendente, o SlashDesk precisará fechar."),
                UiText.Get("Restaurar backup"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning, UiText.Get("Restaurar backup")) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await RunSettingsMaintenanceAsync(UiText.Get("Verificando e restaurando o backup…"), async () =>
            {
            _shortcutOperationInProgress = true; ShortcutEditorPanel.IsEnabled = false;
            var token = _settingsCancellation?.Token ?? default;
            await _usageService.RunMaintenanceAsync(() => _backupService.RestoreSnapshot(picker.FileName, token), token);
            _settingsProgress?.PreventCancellation();
            CaptureStampCatalog.InvalidateCurrent();
            _settings = await _settingsStore.LoadAsync();
            ThemeService.Apply(_settings.Theme);
            SyncSettingsControls();
            var restoredSnippets = await _repository.LoadAsync();
            ReplaceList(restoredSnippets);
            _shortcutDraftBaseline = null; _selected = null;
            _snippetStorageAvailable = true;
            _shortcutOperationInProgress = false;
            if (_snippets.Count > 0) SelectSnippet(_snippets[0]); else BeginNewSnippet();
            _keyboardHook.UpdateSnippets(_snippets);
            await _usageService.LoadAsync();
            await _captureService.LoadAsync();
            RefreshStorageProtection();
            // Preserve edited pixels, but a restore must not retain authority to overwrite the old path.
            _captureWorkbenchPath = null; _captureWorkbenchRecord = null;
            RefreshCaptureHistory();
            RefreshStatistics();
            RefreshBackupSummary();
            ShowSettingsDialog(
                UiText.Get("Backup restaurado. As preferências completas serão aplicadas na próxima abertura do SlashDesk."),
                UiText.Get("Restauração concluída"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SettingsOperationText.Text = UiText.Get("Backup restaurado. Reabra o app para aplicar todas as preferências.");
            }, cancellable: true);
        }
        catch (RestoreRecoveryRequiredException)
        {
            _restoreRecoveryBlocked = true;
            _settingsOperationInProgress = true;
            _shortcutOperationInProgress = true;
            ((FrameworkElement)Content).IsEnabled = false;
            _keyboardHook.Dispose(); _quickAccentService.Enabled = false;
            StopUpdateMonitor(); _captureShortcuts.Dispose();
            _suggestionWindow.Hide(); _quickAccentWindow.Hide();
            var dialog = RestoreRecoveryDialog.Create(blocked: true);
            dialog.Owner = this; dialog.EnableBackdrop(); dialog.ShowDialog();
            try
            {
                if (dialog.Choice == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(AppPaths.BackupsDirectory) { UseShellExecute = true });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                // Close safely even when Explorer cannot open the preserved material.
            }
            finally { _exitRequested = true; Close(); }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = SettingsOperationText.Text = UiText.Get("Operação cancelada. Os dados atuais foram preservados.");
        }
        catch (Exception exception)
        {
            SettingsOperationText.Text = UiText.Get("Não foi possível restaurar: ") + exception.Message;
            ShowSettingsDialog(
                exception.Message,
                UiText.Get("Não foi possível restaurar"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _shortcutOperationInProgress = false; ShortcutEditorPanel.IsEnabled = true;
            RefreshShortcutDraftState();
        }
    }

    private void OpenBackupFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.BackupsDirectory);
        Process.Start(new ProcessStartInfo(AppPaths.BackupsDirectory) { UseShellExecute = true });
    }

    private void RefreshBackupSummary()
    {
        if (BackupSummaryText is null)
        {
            return;
        }

        var snapshots = _backupService.ListSnapshots();
        BackupSummaryText.Text = snapshots.Count == 0
            ? UiText.Get("Nenhum backup criado ainda.")
            : UiText.Format($"{snapshots.Count} backup(s) · último em {snapshots[0].LastWriteTime:g}");
    }

    private static string ImportSourceName(SnippetImportSource source) => source switch
    {
        SnippetImportSource.SlashDesk => "SlashDesk",
        SnippetImportSource.TextBlaze => "Text Blaze",
        SnippetImportSource.Espanso => "Espanso",
        _ => source.ToString()
    };

    private async void ThemeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _settingsOperationInProgress || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme })
        {
            return;
        }

        var previous = _settings.Theme;
        _settings.Theme = theme;
        _settingsOperationInProgress = true; SettingsCardsGrid.IsEnabled = false;
        try
        {
        ThemeService.Apply(theme);
        if (_trayIcon?.ContextMenuStrip is { } trayMenu)
        {
            ApplyTrayTheme(trayMenu);
        }
        await SavePreferencesAsync();
        ShowView(SettingsView, SettingsTabButton);
        SettingsOperationText.Text = UiText.Get("Tema salvo.");
        }
        catch (Exception exception)
        {
            _settings.Theme = previous; ThemeService.Apply(previous); SyncSettingsControls();
            SettingsOperationText.Text = UiText.Get("Não foi possível salvar o tema: ") + exception.Message;
        }
        finally { _settingsOperationInProgress = false; SettingsCardsGrid.IsEnabled = true; }
    }

    private async void QuickAccentCharacterSet_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (!_initialized || _updatingQuickAccentSets)
        {
            return;
        }

        if (SelectedQuickAccentCharacterSets().Count == 0)
        {
            _updatingQuickAccentSets = true;
            QuickAccentPortugueseCheckBox.IsChecked = true;
            _updatingQuickAccentSets = false;
        }

        await SaveQuickAccentSettingsAsync();
    }

    private async void QuickAccentPortugueseOnly_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ApplyQuickAccentCharacterSetSelection(["PortugueseBrazil"]);
        if (_initialized)
        {
            await SaveQuickAccentSettingsAsync();
        }
    }

    private async void QuickAccentSelectAll_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ApplyQuickAccentCharacterSetSelection(
            QuickAccentCharacterSetBoxes()
                .Select(item => item.Tag?.ToString() ?? string.Empty));
        if (_initialized)
        {
            await SaveQuickAccentSettingsAsync();
        }
    }

    private void ApplyQuickAccentSettings()
    {
        _quickAccentService.Enabled = _settings.QuickAccentEnabled;
        _quickAccentService.ActivationKey = _settings.QuickAccentActivationKey;
        _quickAccentService.SortByUsage = _settings.QuickAccentSortByUsage;
        _quickAccentService.InputDelayMs = _settings.QuickAccentInputDelayMs;
        _quickAccentService.ExcludedApps = _settings.QuickAccentExcludedApps;
        _quickAccentService.SetCharacterSets(_settings.QuickAccentCharacterSets);
        _quickAccentService.SetUsage(_usageService.QuickAccentCharacterCounts());
        if (_settings.QuickAccentEnabled && _quickAccentStatusTimer is not null && !_quickAccentService.IsRunning)
        {
            try { _quickAccentService.Start(); }
            catch (Exception exception)
            { AppDiagnosticLog.Write("quick-accent.start_failed", ("exceptionType", exception.GetType().Name)); }
        }
        UpdateQuickAccentCharacterSetPreview();
        UpdateQuickAccentPreviewSelection();
        if (!_settings.QuickAccentEnabled)
        {
            _quickAccentWindow.Hide();
        }
    }

    private void ApplyQuickAccentCharacterSetSelection(IEnumerable<string>? sets)
    {
        var selected = new HashSet<string>(
            sets ?? [],
            StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            selected.Add("PortugueseBrazil");
        }

        _updatingQuickAccentSets = true;
        foreach (var checkBox in QuickAccentCharacterSetBoxes())
        {
            checkBox.IsChecked =
                checkBox.Tag is string tag && selected.Contains(tag);
        }
        if (QuickAccentCharacterSetBoxes().All(item => item.IsChecked != true))
        {
            QuickAccentPortugueseCheckBox.IsChecked = true;
        }
        _updatingQuickAccentSets = false;
        UpdateQuickAccentCharacterSetPreview();
    }

    private List<string> SelectedQuickAccentCharacterSets() =>
        QuickAccentCharacterSetBoxes()
            .Where(item => item.IsChecked == true)
            .Select(item => item.Tag?.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList();

    private CheckBox[] QuickAccentCharacterSetBoxes() =>
    [
        QuickAccentPortugueseCheckBox,
        QuickAccentSpanishCheckBox,
        QuickAccentFrenchCheckBox,
        QuickAccentGermanCheckBox,
        QuickAccentItalianCheckBox,
        QuickAccentNordicCheckBox,
        QuickAccentCentralEuropeanCheckBox,
        QuickAccentCurrencyCheckBox,
        QuickAccentSpecialCheckBox
    ];

    private void UpdateQuickAccentCharacterSetPreview()
    {
        if (QuickAccentSetSummaryText is null)
        {
            return;
        }

        var selected = SelectedQuickAccentCharacterSets();
        QuickAccentSetSummaryText.Text = selected.Count == 1 &&
                                         selected[0] == "PortugueseBrazil"
            ? UiText.Get("Português (Brasil)")
            : selected.Count == 9 ? UiText.Get("Todos os conjuntos") : UiText.Format($"{selected.Count} conjuntos");
    }

    private static string SelectedTag(ComboBox box, string fallback) =>
        box.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : fallback;

    private void InitializeRecordingPresetControls()
    {
        AddPresetItems(RecordingQualityBox, RecordingPresetCatalog.Mp4Quality,
            item => item.Name, item => item.Value);
        AddPresetItems(GifFpsBox, RecordingPresetCatalog.GifFps,
            item => $"{item.Value} FPS — {item.Name}", item => item.Value.ToString());
        AddPresetItems(GifQualityBox, RecordingPresetCatalog.GifQuality,
            item => item.Name, item => item.Value.ToString());
        PopulateCompactRecordingControls();
    }

    private void PopulateCompactRecordingControls()
    {
        if (CaptureCompactFpsBox is null || CaptureCompactQualityBox is null)
        {
            return;
        }
        _updatingCaptureRecordingCompact = true;
        try
        {
            CaptureCompactFpsBox.Items.Clear();
            CaptureCompactQualityBox.Items.Clear();
            if (_captureMediaMode == CaptureMediaMode.Gif)
            {
                CaptureCompactHintText.Text = UiText.Get("Paleta adaptativa · prévia antes de salvar");
                CaptureCompactTargetBox.Items.Clear();
                CaptureCompactTargetBox.Items.Add(new ComboBoxItem { Content = UiText.Get("Região"), Tag = "Region" });
                CaptureCompactTargetBox.SelectedIndex = 0;
                CaptureCompactTargetBox.IsEnabled = false;
                AddPresetItems(CaptureCompactFpsBox, RecordingPresetCatalog.GifFps,
                    item => $"{item.Value}", item => item.Value.ToString());
                AddPresetItems(CaptureCompactQualityBox, RecordingPresetCatalog.GifQuality,
                    item => item.Name, item => item.Value.ToString());
                SelectComboByTag(CaptureCompactFpsBox, _settings.Capture.Recording.GifFps.ToString());
                SelectComboByTag(CaptureCompactQualityBox, _settings.Capture.Recording.GifQuality.ToString());
            }
            else
            {
                CaptureCompactHintText.Text = UiText.Get("H.264 local · pausa e retomada");
                CaptureCompactTargetBox.Items.Clear();
                CaptureCompactTargetBox.Items.Add(new ComboBoxItem { Content = UiText.Get("Monitor ativo"), Tag = "Monitor" });
                CaptureCompactTargetBox.Items.Add(new ComboBoxItem { Content = UiText.Get("Região"), Tag = "Region" });
                CaptureCompactTargetBox.Items.Add(new ComboBoxItem { Content = UiText.Get("Janela"), Tag = "Window" });
                CaptureCompactTargetBox.IsEnabled = true;
                foreach (var fps in new[] { 24, 30, 60 })
                {
                    CaptureCompactFpsBox.Items.Add(new ComboBoxItem { Content = fps.ToString(), Tag = fps.ToString() });
                }
                AddPresetItems(CaptureCompactQualityBox, RecordingPresetCatalog.Mp4Quality,
                    item => item.Name, item => item.Value);
                SelectComboByTag(CaptureCompactTargetBox, SelectedTag(RecordingTargetBox, "Monitor"));
                SelectComboByTag(CaptureCompactFpsBox, SelectedTag(RecordingFpsBox, _settings.Capture.Recording.VideoFps.ToString()));
                SelectComboByTag(CaptureCompactQualityBox, SelectedTag(RecordingQualityBox, _settings.Capture.Recording.VideoQuality));
            }
            CaptureCompactCursorCheckBox.IsChecked = RecordingCursorCheckBox.IsChecked == true;
        }
        finally
        {
            _updatingCaptureRecordingCompact = false;
        }
    }

    private void CaptureCompactRecording_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingCaptureRecordingCompact || RecordingCursorCheckBox is null)
        {
            return;
        }
        if (_captureMediaMode == CaptureMediaMode.Video && ReferenceEquals(sender, CaptureCompactTargetBox))
        {
            _captureLauncherMode = SelectedTag(CaptureCompactTargetBox, "Region") switch
            {
                "Monitor" => CaptureLauncherMode.ActiveMonitor,
                "Window" => CaptureLauncherMode.Window,
                _ => CaptureLauncherMode.Region
            };
            SyncVideoTargetControls();
            UpdateCaptureSelectionSummary();
        }
        RecordingCursorCheckBox.IsChecked = CaptureCompactCursorCheckBox.IsChecked == true;
        if (_captureMediaMode == CaptureMediaMode.Gif)
        {
            SelectComboByTag(GifFpsBox, SelectedTag(CaptureCompactFpsBox, "10"));
            SelectComboByTag(GifQualityBox, SelectedTag(CaptureCompactQualityBox, "128"));
        }
        else
        {
            SelectComboByTag(RecordingTargetBox, SelectedTag(CaptureCompactTargetBox, "Monitor"));
            SelectComboByTag(RecordingFpsBox, SelectedTag(CaptureCompactFpsBox, "30"));
            SelectComboByTag(RecordingQualityBox, SelectedTag(CaptureCompactQualityBox, "Alta"));
        }
    }

    private static void AddPresetItems<T>(
        ComboBox box,
        IReadOnlyList<RecordingPreset<T>> presets,
        Func<RecordingPreset<T>, string> content,
        Func<RecordingPreset<T>, string> tag)
    {
        box.Items.Clear();
        foreach (var preset in presets)
        {
            box.Items.Add(new ComboBoxItem
            {
                Content = content(preset),
                Tag = tag(preset),
                ToolTip = preset.Description
            });
        }
    }

    private void RecordingPreset_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePresetDescription(RecordingQualityBox, RecordingQualityDescriptionText);
        UpdatePresetDescription(GifFpsBox, GifFpsDescriptionText);
        UpdatePresetDescription(GifQualityBox, GifQualityDescriptionText);
    }

    private static void UpdatePresetDescription(ComboBox box, TextBlock description)
    {
        var text = box.SelectedItem is ComboBoxItem { ToolTip: string value }
            ? value
            : string.Empty;
        description.Text = text;
        box.ToolTip = text;
    }

    private static void SelectComboByTag(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag &&
                tag.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private void LoadCaptureSettings()
    {
        var capture = _settings.Capture ??= new CaptureSettings();
        capture.Recording ??= new RecordingSettings();
        RecordingPresetCatalog.Normalize(capture.Recording);
        CaptureMonitorShortcutBox.Text = capture.ActiveMonitorShortcut;
        CaptureRegionShortcutBox.Text = capture.RegionShortcut;
        CaptureWindowShortcutBox.Text = capture.WindowShortcut;
        CaptureScrollingShortcutBox.Text = capture.ScrollingShortcut;
        CaptureMonitorShortcutText.Text = DisplayShortcut(capture.ActiveMonitorShortcut);
        CaptureRegionShortcutText.Text = DisplayShortcut(capture.RegionShortcut);
        CaptureWindowShortcutText.Text = DisplayShortcut(capture.WindowShortcut);
        CaptureScrollingShortcutText.Text = DisplayShortcut(capture.ScrollingShortcut);
        CaptureDirectoryBox.Text = capture.OutputDirectoryTemplate;
        CaptureFileNameBox.Text = capture.FileNameTemplate;
        SelectComboByTag(CaptureFormatBox, capture.ImageFormat);
        CaptureQualityBox.Text = capture.JpegQuality.ToString();
        CaptureClipboardCheckBox.IsChecked = capture.CopyToClipboard;
        CaptureAutoSaveCheckBox.IsChecked = capture.SaveAutomatically;
        CaptureCursorCheckBox.IsChecked = capture.IncludeCursor;
        CaptureHideSlashDeskCheckBox.IsChecked = capture.HideSlashDeskDuringCapture;
        SelectComboByTag(
            CaptureEditorCheckBox,
            capture.OpenEditorForMonitorAndWindow ? "Editor" : "Direct");
        SelectComboByTag(CaptureDelayBox, capture.DelaySeconds.ToString());
        SelectComboByTag(CaptureRetentionBox, capture.HistoryRetentionDays.ToString());
        SelectComboByTag(RecordingTargetBox, _captureMediaMode == CaptureMediaMode.Video ? SelectedVideoTargetKind.ToString() : "Monitor");
        SelectComboByTag(RecordingFpsBox, capture.Recording.VideoFps.ToString());
        SelectComboByTag(RecordingQualityBox, capture.Recording.VideoQuality);
        RecordingCursorCheckBox.IsChecked = capture.Recording.IncludeCursor;
        SelectComboByTag(GifFpsBox, capture.Recording.GifFps.ToString());
        SelectComboByTag(GifQualityBox, capture.Recording.GifQuality.ToString());
        RecordingPreset_OnChanged(this, null!);
        SelectComboByTag(CaptureHistoryFilterBox, "all");
        CaptureQualityBox.IsEnabled =
            capture.ImageFormat.Equals("JPEG", StringComparison.OrdinalIgnoreCase);
        CapturePostModeText.Text = capture.OpenEditorForMonitorAndWindow
            ? UiText.Get("Editor: revisar antes de concluir")
            : UiText.Get("Direta: copiar e salvar");
        PopulateCompactRecordingControls();
    }

    private static string DisplayShortcut(string value) =>
        value.Replace("+", " + ", StringComparison.Ordinal);

    private void BrowseCaptureDestination_OnClick(object sender, RoutedEventArgs e)
    {
        var expandedTemplate =
            Environment.ExpandEnvironmentVariables(CaptureDirectoryBox.Text.Trim());
        var currentDirectory = CaptureService.ResolveDirectoryTemplate(
            expandedTemplate,
            DateTimeOffset.Now);
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = UiText.Get("Selecione a pasta onde as capturas serão salvas."),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(currentDirectory)
                ? currentDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            CaptureDirectoryBox.Text = dialog.SelectedPath;
        }
    }

    private async void SaveCaptureSettings_OnClick(object sender, RoutedEventArgs e)
    {
        using var lease = TryReserveCaptureCommand();
        if (lease is null) return;
        if (!await PrepareCapturePreferencesAsync(UiText.Get("Configuração de captura"))) return;
        ConfigureCaptureShortcuts();
        ShowCapturePreferencesSavedStatus();
    }

    private bool TryReadCaptureSettings(out string error)
    {
        error = string.Empty;
        var shortcuts = new[]
        {
            CaptureMonitorShortcutBox.Text.Trim(),
            CaptureRegionShortcutBox.Text.Trim(),
            CaptureWindowShortcutBox.Text.Trim(),
            CaptureScrollingShortcutBox.Text.Trim()
        };
        if (shortcuts.Any(item => !GlobalCaptureShortcutService.IsValid(item)))
        {
            error = UiText.Get("Clique no campo e pressione uma tecla, roda ou botão do mouse. ") +
                    UiText.Get("A roda sempre exige Ctrl, Alt, Shift ou Win; cliques esquerdo e direito não são aceitos.");
            return false;
        }
        if (shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != shortcuts.Length)
        {
            error = UiText.Get("Cada ação precisa ter um atalho diferente.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(CaptureDirectoryBox.Text) ||
            string.IsNullOrWhiteSpace(CaptureFileNameBox.Text))
        {
            error = UiText.Get("Informe a pasta e o modelo de nome do arquivo.");
            return false;
        }
        var openEditor = SelectedTag(CaptureEditorCheckBox, "Direct")
            .Equals("Editor", StringComparison.OrdinalIgnoreCase);
        var copyToClipboard = CaptureClipboardCheckBox.IsChecked == true;
        var saveAutomatically = CaptureAutoSaveCheckBox.IsChecked == true;
        if (!openEditor && !copyToClipboard && !saveAutomatically)
        {
            error = UiText.Get("No modo Captura direta, ative Copiar após capturar, ") +
                    UiText.Get("Salvar automaticamente ou ambas as opções.");
            return false;
        }

        var gifFps = ParseSelectedInt(GifFpsBox, 10);
        var gifQuality = ParseSelectedInt(GifQualityBox, 128);
        if (!RecordingPresetCatalog.GifFps.Any(item => item.Value == gifFps) ||
            !RecordingPresetCatalog.GifQuality.Any(item => item.Value == gifQuality))
        {
            error = UiText.Get("Selecione um preset disponível de FPS e qualidade do GIF.");
            return false;
        }

        var legacyGifDuration = _settings.Capture.Recording.GifDurationSeconds;
        var legacyGifWidth = _settings.Capture.Recording.GifWidth;

        var quality = int.TryParse(CaptureQualityBox.Text, out var parsedQuality)
            ? Math.Clamp(parsedQuality, 1, 100)
            : 90;
        _settings.Capture = new CaptureSettings
        {
            ActiveMonitorShortcut = shortcuts[0],
            RegionShortcut = shortcuts[1],
            WindowShortcut = shortcuts[2],
            ScrollingShortcut = shortcuts[3],
            OutputDirectoryTemplate = CaptureDirectoryBox.Text.Trim(),
            FileNameTemplate = CaptureFileNameBox.Text.Trim(),
            ImageFormat = SelectedTag(CaptureFormatBox, "PNG"),
            JpegQuality = quality,
            CopyToClipboard = copyToClipboard,
            SaveAutomatically = saveAutomatically,
            HideSlashDeskDuringCapture = CaptureHideSlashDeskCheckBox.IsChecked == true,
            IncludeCursor = CaptureCursorCheckBox.IsChecked == true,
            OpenEditorForMonitorAndWindow = openEditor,
            DelaySeconds = ParseSelectedInt(CaptureDelayBox, 0),
            HistoryRetentionDays = ParseSelectedInt(CaptureRetentionBox, 90),
            Ocr = _settings.Capture.Ocr ?? new(),
            Recording = new RecordingSettings
            {
                Audio = (_settings.Capture.Recording.Audio ?? new()).Copy(),
                VideoFps = ParseSelectedInt(RecordingFpsBox, 30),
                VideoQuality = SelectedTag(RecordingQualityBox, "Alta"),
                IncludeCursor = RecordingCursorCheckBox.IsChecked == true,
                GifFps = gifFps,
                GifDurationSeconds = legacyGifDuration,
                GifWidth = legacyGifWidth,
                GifQuality = gifQuality
            }
        };
        CaptureQualityBox.Text = quality.ToString();
        return true;
    }

    private void ConfigureCaptureShortcuts()
    {
        if (CapturePreparationForEvidence is { } evidence)
        {
            evidence("shortcuts");
            return;
        }
        var capture = _settings.Capture;
        var errors = _captureShortcuts.Configure(
            this,
            capture.ActiveMonitorShortcut,
            capture.RegionShortcut,
            capture.WindowShortcut,
            capture.ScrollingShortcut);
        CaptureShortcutStatusText.Text = errors.Count == 0
            ? UiText.Get("● Atalhos ativos")
            : string.Join(Environment.NewLine, errors);
        CaptureShortcutStatusText.Foreground = errors.Count == 0
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("MutedBrush");
    }

    private void CaptureShortcuts_OnTriggered(
        object? sender,
        CaptureShortcutEventArgs e)
    {
        if (_restoreRecoveryBlocked || _servicesDisposed) return;
        if (e.Action == CaptureShortcutAction.Scrolling)
        {
            _ = RunScrollingCaptureAsync(invokedByShortcut: true);
            return;
        }

        _ = RunCaptureAsync(e.Action, invokedByShortcut: true);
    }

    private void CaptureActiveMonitor_OnClick(object sender, RoutedEventArgs e) =>
        _ = RunCaptureAsync(CaptureShortcutAction.ActiveMonitor, invokedByShortcut: false);

    private void CaptureRegion_OnClick(object sender, RoutedEventArgs e) =>
        _ = RunCaptureAsync(CaptureShortcutAction.Region, invokedByShortcut: false);

    private void CaptureWindow_OnClick(object sender, RoutedEventArgs e) =>
        _ = RunCaptureAsync(CaptureShortcutAction.Window, invokedByShortcut: false);

    private void CaptureScrolling_OnClick(object sender, RoutedEventArgs e) =>
        _ = RunScrollingCaptureAsync(invokedByShortcut: false);

    private void CaptureLauncherMode_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, ignoreCase: true, out CaptureLauncherMode mode))
        {
            return;
        }

        if (_captureMediaMode == CaptureMediaMode.Video && mode == CaptureLauncherMode.Scrolling) return;
        _captureLauncherMode = mode;
        if (mode == CaptureLauncherMode.Gif)
        {
            _captureMediaMode = CaptureMediaMode.Gif;
            CaptureGifMediaButton.IsChecked = true;
        }
        else if (_captureMediaMode == CaptureMediaMode.Gif)
        {
            _captureMediaMode = CaptureMediaMode.Image;
            CaptureImageMediaButton.IsChecked = true;
        }
        UpdateCaptureLauncherSummary();
    }

    private void CaptureMediaMode_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, ignoreCase: true, out CaptureMediaMode mode))
        {
            return;
        }

        _captureMediaMode = mode;
        if (mode == CaptureMediaMode.Video && _captureLauncherMode == CaptureLauncherMode.Scrolling)
        {
            _captureLauncherMode = CaptureLauncherMode.Region;
            CaptureRegionModeButton.IsChecked = true;
        }
        if (mode == CaptureMediaMode.Gif)
        {
            _captureLauncherMode = CaptureLauncherMode.Gif;
            CaptureGifModeButton.IsChecked = true;
        }
        else if (_captureLauncherMode == CaptureLauncherMode.Gif)
        {
            _captureLauncherMode = CaptureLauncherMode.Region;
            CaptureRegionModeButton.IsChecked = true;
        }
        UpdateCaptureLauncherSummary();
    }

    private async void StartSelectedCapture_OnClick(object sender, RoutedEventArgs e)
    {
        // Video/GIF own their preparation, including the preference save.
        switch (_captureMediaMode)
        {
            case CaptureMediaMode.Video:
                StartMp4Recording_OnClick(sender, e);
                return;
            case CaptureMediaMode.Gif:
                StartGifRecording_OnClick(sender, e);
                return;
        }

        using var lease = TryReserveCaptureCommand();
        if (lease is null) return;
        if (!await PrepareCapturePreferencesAsync(UiText.Get("Regra de captura"))) return;
        if (!CanUseRecordingInterface) return;
        ConfigureCaptureShortcuts();
        if (CapturePreparationForEvidence is { } evidence) { evidence("image"); return; }
        // Existing image handlers reserve synchronously before their first await.
        // Transfer on the UI thread without an await or modal call between leases.
        lease.Dispose();
        switch (_captureLauncherMode)
        {
            case CaptureLauncherMode.ActiveMonitor:
                CaptureActiveMonitor_OnClick(sender, e);
                break;
            case CaptureLauncherMode.Window:
                CaptureWindow_OnClick(sender, e);
                break;
            case CaptureLauncherMode.Scrolling:
                CaptureScrolling_OnClick(sender, e);
                break;
            default:
                CaptureRegion_OnClick(sender, e);
                break;
        }
    }

    private void UpdateCaptureLauncherSummary()
    {
        if (CaptureEditorCard is not null)
        {
            var image = _captureMediaMode == CaptureMediaMode.Image;
            CaptureEditorCard.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
            CaptureMediaPreviewPanel.Visibility = image ? Visibility.Collapsed : Visibility.Visible;
        }
        CaptureScrollingModeButton.IsEnabled = _captureMediaMode != CaptureMediaMode.Video;
        CaptureScrollingModeButton.Opacity = CaptureScrollingModeButton.IsEnabled ? 1 : 0.45;
        if (_captureMediaMode == CaptureMediaMode.Video) SyncVideoTargetControls();
        UpdateCaptureSelectionSummary();
        CaptureRecordingCard.Visibility = Visibility.Collapsed;
        CaptureRecordingCompactCard.Visibility = _captureMediaMode == CaptureMediaMode.Image
            ? Visibility.Collapsed
            : Visibility.Visible;
        CaptureRecordingTopGapRow.Height = new GridLength(20);
        CaptureRecordingBottomGapRow.Height = new GridLength(
            _captureMediaMode == CaptureMediaMode.Image ? 0 : 20);
        CaptureVideoConfigurationPanel.Visibility = _captureMediaMode == CaptureMediaMode.Video
            ? Visibility.Visible
            : Visibility.Collapsed;
        CaptureGifConfigurationPanel.Visibility = _captureMediaMode == CaptureMediaMode.Gif
            ? Visibility.Visible
            : Visibility.Collapsed;
        PopulateCompactRecordingControls();
        if (CaptureRecordingCompactCard.IsVisible && CaptureRecordingCompactCard.IsLoaded)
        {
            LabMotion.PlayEntrance(CaptureRecordingCompactCard);
        }
    }

    // The visible launcher mode is the source of truth. Hidden legacy controls
    // are mirrors; they must never silently override the destination on New.
    internal RecordingTargetKind SelectedVideoTargetKind => _captureLauncherMode switch
    {
        CaptureLauncherMode.ActiveMonitor => RecordingTargetKind.Monitor,
        CaptureLauncherMode.Region => RecordingTargetKind.Region,
        CaptureLauncherMode.Window => RecordingTargetKind.Window,
        _ => throw new InvalidOperationException(UiText.Get("O destino do vídeo não é válido."))
    };

    private void SyncVideoTargetControls()
    {
        var kind = SelectedVideoTargetKind;
        var updating = _updatingCaptureRecordingCompact;
        _updatingCaptureRecordingCompact = true;
        try
        {
            SelectComboByTag(RecordingTargetBox, kind.ToString());
            SelectComboByTag(CaptureCompactTargetBox, kind.ToString());
            CaptureMonitorModeButton.IsChecked = kind == RecordingTargetKind.Monitor;
            CaptureRegionModeButton.IsChecked = kind == RecordingTargetKind.Region;
            CaptureWindowModeButton.IsChecked = kind == RecordingTargetKind.Window;
        }
        finally { _updatingCaptureRecordingCompact = updating; }
    }

    private void UpdateCaptureSelectionSummary()
    {
        var area = _captureLauncherMode switch
        {
            CaptureLauncherMode.ActiveMonitor => UiText.Get("Monitor"),
            CaptureLauncherMode.Window => UiText.Get("Janela"),
            CaptureLauncherMode.Scrolling => UiText.Get("Captura longa"),
            _ => UiText.Get("Região")
        };
        var selection = _captureMediaMode switch
        {
            CaptureMediaMode.Video => UiText.Get("Vídeo MP4") + " · " + area,
            CaptureMediaMode.Gif => UiText.Get("GIF · região"),
            _ => area
        };
        CaptureNewButtonText.Text = UiText.Get("Novo");
        CaptureSelectionSummaryText.Text = UiText.Format($"{selection} · pronta");
    }

    private ScreenHelpHighlighter? _captureHelpHighlight;

    private void OpenCaptureHelp_OnClick(object sender, RoutedEventArgs e)
    {
        _captureHelpHighlight?.Remove(); _captureHelpHighlight = null;
        var guide = new ScreenHelpWindow(CaptureHelpContent.Create(_settings.Capture)) { Owner = this };
        LabMotion.SetReduced(guide, LabMotion.GetReduced(this));
        if (sender is Button button && button != CaptureHelpButton) guide.OpenTopic("zoom");
        guide.EnableBackdrop();
        ShowCaptureDialog(guide);
        if (guide.RequestedTarget is not { } name) return;
        if (FindName(name) is not FrameworkElement target) return;
        // Do not switch media, select tools or discard a session just to reveal help.
        if (!target.IsVisible)
        {
            StatusText.Text = UiText.Get("Este recurso aparece no painel de imagem. Sua sessão foi preservada.");
            target = _captureEditorExpanded ? CaptureExpandEditorButton : CaptureImageMediaButton;
        }
        else StatusText.Text = UiText.Get("Recurso destacado: ") + (System.Windows.Automation.AutomationProperties.GetName(target) is { Length: > 0 } label ? label : guide.Title);
        target.BringIntoView();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!target.IsVisible) return;
            target.Focus(); _captureHelpHighlight = ScreenHelpHighlighter.Show(target);
        }), DispatcherPriority.Loaded);
    }

    private async void FocusCaptureRule_OnClick(object sender, RoutedEventArgs e)
    {
        using var lease = TryReserveCaptureCommand();
        if (lease is null) return;
        if (_settingsOperationInProgress || _servicesDisposed || _restoreRecoveryBlocked) return;
        var previous = _settings.Capture;
        if (!TryReadCaptureSettings(out var currentError))
        {
            ShowAppDialog(currentError, UiText.Get("Configurações de captura"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new CaptureRuleDialog(_settings.Capture)
        {
            Owner = this
        };
        LabMotion.SetReduced(dialog, LabMotion.GetReduced(this));
        dialog.ShortcutStatus = CaptureShortcutStatusText.Text;
        var accepted = ShowCaptureDialog(dialog);
        if (!accepted)
        {
            _settings.Capture = previous;
            return;
        }

        _settings.Capture = dialog.Result;
        if (!await TrySaveCapturePreferencesAsync(previous)) return;
        LoadCaptureSettings();
        ConfigureCaptureShortcuts();
        ShowCapturePreferencesSavedStatus();
    }

    private bool ShowCaptureDialog(Window dialog)
    {
        var previousOpacity = ShellRoot.Opacity;
        var previousEffect = ShellRoot.Effect;
        try
        {
            ShellRoot.Opacity = 0.58;
            ShellRoot.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 4 };
            return dialog.ShowDialog() == true;
        }
        finally
        {
            ShellRoot.Effect = previousEffect;
            ShellRoot.Opacity = previousOpacity;
        }
    }

    private async Task RunScrollingCaptureAsync(bool invokedByShortcut)
    {
        if (!CanUseRecordingInterface || _settingsOperationInProgress) return;
        using var captureLease = _captureGate.TryEnter();
        if (captureLease is null)
        {
            SafeDiagnosticLog.Write("capture.ignored_busy");
            StatusText.Text = UiText.Get("Já existe uma captura ou gravação em andamento.");
            return;
        }

        var wasVisible = IsVisible;
        try
        {
            SafeDiagnosticLog.Write("capture.started", new Dictionary<string, object?>
            {
                ["action"] = CaptureShortcutAction.Scrolling.ToString(),
                ["invokedByShortcut"] = invokedByShortcut,
                ["windowWasVisible"] = wasVisible
            });

            if (invokedByShortcut)
            {
                await WaitForCaptureDelayAsync();
                if (_settings.Capture.ShouldHideSlashDesk(wasVisible))
                {
                    Hide();
                    await Task.Delay(180);
                }
            }
            else
            {
                if (wasVisible)
                {
                    Hide();
                }
                var selectionDelaySeconds = Math.Max(
                    3,
                    _settings.Capture.DelaySeconds);
                ShowTrayBalloon(
                    Math.Min(selectionDelaySeconds * 1000, 10000),
                    UiText.Format($"Captura longa em {selectionDelaySeconds}s"),
                    UiText.Get("Posicione o cursor sobre a página que será capturada."),
                    Forms.ToolTipIcon.Info);
                await Task.Delay(TimeSpan.FromSeconds(selectionDelaySeconds));
            }

            var target = _captureService.WindowUnderCursorTarget();
            if (target is null)
            {
                if (wasVisible)
                {
                    ShowFromTray();
                    ShowAppDialog(
                        UiText.Get("Posicione o cursor sobre a janela que será capturada."),
                        UiText.Get("Captura longa"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    ShowTrayBalloon(
                        2500,
                        UiText.Get("Captura longa não iniciada"),
                        UiText.Get("Não foi possível identificar a janela sob o cursor."),
                        Forms.ToolTipIcon.Warning);
                }
                return;
            }

            var result = await _captureService.CaptureScrollingAsync(
                target.WindowHandle,
                target.Bounds,
                _settings.Capture,
                openEditor: _settings.Capture.OpenEditorForMonitorAndWindow,
                owner: IsVisible ? this : null);
            if (wasVisible)
            {
                ShowFromTray();
            }
            if (result is not null)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(result.FilePath)
                    ? UiText.Get("Captura longa copiada")
                    : UiText.Format($"Captura longa salva: {Path.GetFileName(result.FilePath)}");
                AcceptCompletedCapture(result);
                RefreshCaptureHistory();
                ShowTrayBalloon(
                    3500,
                    UiText.Get("Captura longa concluída"),
                    string.IsNullOrWhiteSpace(result.FilePath)
                        ? UiText.Get("Imagem copiada para o clipboard.")
                        : UiText.Get("Clique para abrir a imagem."),
                    Forms.ToolTipIcon.Info,
                    result.FilePath);
            }

            SafeDiagnosticLog.Write("capture.completed", new Dictionary<string, object?>
            {
                ["action"] = CaptureShortcutAction.Scrolling.ToString(),
                ["resultCreated"] = result is not null
            });
        }
        catch (Exception exception)
        {
            SafeDiagnosticLog.Write("capture.failed", new Dictionary<string, object?>
            {
                ["action"] = CaptureShortcutAction.Scrolling.ToString(),
                ["exceptionType"] = exception.GetType().Name
            });
            if (wasVisible)
            {
                ShowFromTray();
                ShowAppDialog(
                    exception.Message,
                    UiText.Get("Captura longa"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                ShowTrayBalloon(
                    2500,
                    UiText.Get("Não foi possível fazer a captura longa"),
                    exception.Message,
                    Forms.ToolTipIcon.Warning);
            }
        }
        finally
        {
            if (wasVisible && !IsVisible)
            {
                ShowFromTray();
            }
            _captureCommitInProgress = false;
            CaptureEditorCard.IsEnabled = true;
            _completedCaptureDocument = null;
            CaptureInlineEditor_OnStateChanged(this, EventArgs.Empty);
        }
    }

    private async Task RunCaptureAsync(
        CaptureShortcutAction action,
        bool invokedByShortcut)
    {
        if (!CanUseRecordingInterface || _settingsOperationInProgress) return;
        using var captureLease = _captureGate.TryEnter();
        if (captureLease is null)
        {
            StatusText.Text = UiText.Get("Já existe uma captura ou gravação em andamento.");
            return;
        }

        if (action == CaptureShortcutAction.Region && AppPaths.IsCapturePilot && !CanReplaceCaptureSession()) return;
        CaptureRegionSession? regionSession = null;

        var wasVisible = IsVisible;
        var shouldHide = _settings.Capture.ShouldHideSlashDesk(wasVisible);
        try
        {
            SafeDiagnosticLog.Write("capture.started", new Dictionary<string, object?>
            {
                ["action"] = action.ToString(),
                ["invokedByShortcut"] = invokedByShortcut,
                ["windowWasVisible"] = wasVisible
            });
            await WaitForCaptureDelayAsync();
            System.Drawing.Rectangle? bounds = null;
            System.Drawing.Bitmap? editedRegion = null;
            var requestedRegionOutput = CaptureEditorOutput.Default;
            var type = action switch
            {
                CaptureShortcutAction.ActiveMonitor => "monitor",
                CaptureShortcutAction.Region => "regiao",
                _ => "janela"
            };

            if (action == CaptureShortcutAction.ActiveMonitor)
            {
                bounds = _captureService.ActiveMonitorBounds();
            }
            else if (action == CaptureShortcutAction.Window)
            {
                bounds = _captureService.WindowUnderCursorBounds();
            }

            if (shouldHide)
            {
                Hide();
                await Task.Delay(180);
            }

            if (action == CaptureShortcutAction.Region)
            {
                editedRegion = _captureService.SelectAndEditRegion(
                    null,
                    _settings.Capture.IncludeCursor,
                    out requestedRegionOutput, out regionSession, _settings.Capture.Ocr);
            }
            CaptureRecord? result = null;
            if (editedRegion is not null)
            {
                using (editedRegion)
                {
                    result = await _captureService.ProcessEditedRegionAsync(
                        editedRegion,
                        type,
                        _settings.Capture,
                        requestedRegionOutput);
                }
            }
            else if (bounds is not null)
            {
                result = await _captureService.CaptureAndProcessAsync(
                    bounds.Value,
                    type,
                    _settings.Capture,
                    openEditor: _settings.Capture.OpenEditorForMonitorAndWindow,
                    owner: shouldHide ? null : this);
            }

            if (result is not null)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(result.FilePath)
                    ? UiText.Format($"Captura de {type} copiada")
                    : UiText.Format($"Captura salva: {Path.GetFileName(result.FilePath)}");
                if (regionSession is not null) LoadCompletedRegionSession(regionSession);
                AcceptCompletedCapture(result);
                RefreshCaptureHistory();
                ShowTrayBalloon(
                    3500,
                    UiText.Get("Captura concluída"),
                    string.IsNullOrWhiteSpace(result.FilePath)
                        ? UiText.Get("Imagem copiada para o clipboard.")
                        : UiText.Get("Clique para abrir a imagem."),
                    Forms.ToolTipIcon.Info,
                    result.FilePath);
            }

            if (shouldHide)
            {
                ShowFromTray();
            }
            SafeDiagnosticLog.Write("capture.completed", new Dictionary<string, object?>
            {
                ["action"] = action.ToString(),
                ["resultCreated"] = result is not null
            });
        }
        catch (Exception exception)
        {
            SafeDiagnosticLog.Write("capture.failed", new Dictionary<string, object?>
            {
                ["action"] = action.ToString(),
                ["exceptionType"] = exception.GetType().Name
            });
            if (wasVisible)
            {
                ShowFromTray();
                ShowAppDialog(
                    exception.Message,
                    UiText.Get("Não foi possível capturar"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                ShowTrayBalloon(
                    2500,
                    UiText.Get("Não foi possível capturar"),
                    exception.Message,
                    Forms.ToolTipIcon.Warning);
            }
        }
        finally
        {
            regionSession?.Dispose();
            if (shouldHide && wasVisible && !IsVisible)
            {
                ShowFromTray();
            }
            _captureCommitInProgress = false;
            CaptureEditorCard.IsEnabled = true;
            _completedCaptureDocument = null;
            CaptureInlineEditor_OnStateChanged(this, EventArgs.Empty);
        }
    }

    private async Task WaitForCaptureDelayAsync()
    {
        var seconds = _settings.Capture.DelaySeconds;
        if (seconds <= 0)
        {
            return;
        }
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            StatusText.Text = UiText.Format($"Captura em {remaining}s…");
            await Task.Delay(1000);
        }
    }

    private async void StartMp4Recording_OnClick(object sender, RoutedEventArgs e)
        => await RunRecordingOperationAsync("MP4", RunMp4RecordingAsync);

    private async Task RunMp4RecordingAsync(RecordingOperation operation)
    {
        try
        {
            var targetKind = SelectedVideoTargetKind;
            if (!await PrepareCapturePreferencesAsync(UiText.Get("Gravação"), operation.Token)) return;
            CheckRecordingOperation(operation);
            var capture = _settings.Capture;
            ConfigureCaptureShortcuts();
            if (RecordingPipelineForEvidence is { } pipeline) { await pipeline("MP4", operation); CheckRecordingOperation(operation); return; }
            if (CapturePreparationForEvidence is { } evidence) { evidence("mp4"); return; }
            operation.InterfaceHidden = true;
            Hide();
            await Task.Delay(250, operation.Token);
            CheckRecordingOperation(operation);
            var target = ResolveRecordingTarget(targetKind, operation.Token);
            if (target is null)
            {
                operation.Cancel();
                ShowFromTray();
                return;
            }

            if (IsVisible)
            {
                Hide();
            }
            await Task.Delay(180, operation.Token);
            CheckRecordingOperation(operation);
            var recorder = new ScreenRecordingService();
            _recordingService = recorder;
            recorder.RecordingFailed += (_, message) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    _ = Dispatcher.BeginInvoke(() => { if (CanUseRecordingInterface && ReferenceEquals(_recordingOperation, operation)) StatusText.Text = message; });
            };
            var previousAudio = capture.Recording.Audio.Copy();
            var control = new RecordingControlWindow(recorder, "MP4", capture.Recording);
            _recordingControl = control;
            using var cancellation = WatchPreparedRecordingControl(control, operation);
            control.Show();
            var shouldStart = await control.WaitForStartAsync();
            CheckRecordingOperation(operation);
            if (shouldStart)
            {
                shouldStart = await TrySaveCapturePreferencesCoreAsync(capture, previousAudio, ClosePreparedMp4Recording, operation.Token);
                CheckRecordingOperation(operation);
                shouldStart = shouldStart && control.IsVisible;
            }
            if (!shouldStart || _recordingControl is null)
            {
                if (_recordingControl is not null || _recordingService is not null) ClosePreparedMp4Recording();
                return;
            }
            CheckRecordingOperation(operation);
            operation.MarkRecording();
            control.BeginRecording();
            var completion = recorder.StartAsync(
                target,
                capture,
                capture.Recording);
            var path = await completion;
            CheckRecordingOperation(operation);
            operation.MarkFinalizing();
            var elapsed = recorder.Elapsed;
            var completedRecordingId = recorder.RecordingId.ToString("N");
            CleanupRecordingResources();
            AppDiagnosticLog.Write("recording.interface-restored",
                ("recordingId", completedRecordingId), ("media", "MP4"), ("result", "success"));
            await _captureService.AddMediaRecordAsync(new CaptureRecord
            {
                CreatedAt = DateTimeOffset.Now,
                Type = target.Type,
                MediaKind = "video",
                FilePath = path,
                Width = target.Bounds.Width,
                Height = target.Bounds.Height,
                DurationSeconds = elapsed.TotalSeconds
            });
            CheckRecordingOperation(operation);
            ShowFromTray();
            StatusText.Text = UiText.Format($"Vídeo salvo: {Path.GetFileName(path)}");
            RefreshCaptureHistory();
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !CanUseRecordingInterface) { throw; }
        catch (Exception exception)
        {
            if (!CanUseRecordingInterface) return;
            operation.Token.ThrowIfCancellationRequested();
            var failedRecordingId = _recordingService?.RecordingId.ToString("N") ?? "unknown";
            CleanupRecordingResources();
            ShowFromTray();
            AppDiagnosticLog.Write("recording.interface-restored",
                ("recordingId", failedRecordingId), ("media", "MP4"), ("result", "failure"));
            ShowAppDialog(
                exception.Message +
                UiText.Get("\n\nO SlashDesk usa H.264/Media Foundation. Em edições N/KN, instale o Media Feature Pack do Windows.") +
                $"\nLogs: {AppPaths.LogsDirectory}",
                UiText.Get("Não foi possível gravar"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void StartGifRecording_OnClick(object sender, RoutedEventArgs e)
        => await RunRecordingOperationAsync("GIF", RunGifRecordingAsync);

    private async Task RunGifRecordingAsync(RecordingOperation operation)
    {
        try
        {
            if (!await PrepareCapturePreferencesAsync("GIF", operation.Token)) return;
            CheckRecordingOperation(operation);
            var capture = _settings.Capture;
            ConfigureCaptureShortcuts();
            if (RecordingPipelineForEvidence is { } pipeline) { await pipeline("GIF", operation); CheckRecordingOperation(operation); return; }
            if (CapturePreparationForEvidence is { } evidence) { evidence("gif"); return; }
            var target = _captureService.SelectRecordingRegion(this, UiText.Get("Selecione a região do GIF"), operation.Token);
            CheckRecordingOperation(operation);
            if (target is null) { operation.Cancel(); return; }
            operation.InterfaceHidden = true;
            Hide();
            await Task.Delay(180, operation.Token);
            CheckRecordingOperation(operation);
            operation.MarkRecording();
            var session = _gifRecordingService.StartRecording(
                target.Bounds,
                capture.Recording);
            _gifRecordingSession = session;
            _recordingControl = new RecordingControlWindow(session, "GIF");
            _recordingControl.Show();
            using var recording = await session.Completion;
            CheckRecordingOperation(operation);
            operation.MarkFinalizing();
            var completedRecordingId = session.RecordingId.ToString("N");
            CleanupRecordingResources();
            ShowFromTray();
            AppDiagnosticLog.Write("recording.interface-restored",
                ("recordingId", completedRecordingId), ("media", "GIF"), ("result", "captured"));
            if (recording.Metrics?.DroppedFrames > 0)
            {
                ShowAppDialog(
                    UiText.Format($"O pipeline descartou {recording.Metrics.DroppedFrames:N0} quadro(s) por sobrecarga. ") +
                    UiText.Get("O tempo foi preservado no GIF e o evento foi registrado nos logs."),
                    UiText.Get("GIF finalizado com sobrecarga"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            var preview = new GifPreviewWindow(recording) { Owner = this };
            using var previewCancellation = new RecordingWindowCancellation(preview, operation.Token);
            if (preview.ShowDialog() != true)
            {
                CheckRecordingOperation(operation);
                StatusText.Text = UiText.Get("GIF descartado");
                return;
            }
            CheckRecordingOperation(operation);
            var path = await _gifRecordingService.SaveAsync(
                recording,
                capture,
                target.Type,
                capture.Recording.GifQuality,
                operation.Token);
            CheckRecordingOperation(operation);
            await _captureService.AddMediaRecordAsync(new CaptureRecord
            {
                CreatedAt = DateTimeOffset.Now,
                Type = target.Type,
                MediaKind = "gif",
                FilePath = path,
                Width = recording.Width,
                Height = recording.Height,
                DurationSeconds = recording.Duration.TotalSeconds
            });
            CheckRecordingOperation(operation);
            StatusText.Text = UiText.Format($"GIF salvo: {Path.GetFileName(path)}");
            RefreshCaptureHistory();
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !CanUseRecordingInterface) { throw; }
        catch (OperationCanceledException)
        {
            var cancelledRecordingId = _gifRecordingSession?.RecordingId.ToString("N") ?? "unknown";
            CleanupRecordingResources();
            ShowFromTray();
            AppDiagnosticLog.Write("recording.interface-restored",
                ("recordingId", cancelledRecordingId), ("media", "GIF"), ("result", "cancelled"));
            StatusText.Text = UiText.Get("Gravação de GIF cancelada");
        }
        catch (Exception exception)
        {
            if (!CanUseRecordingInterface) return;
            operation.Token.ThrowIfCancellationRequested();
            var failedRecordingId = _gifRecordingSession?.RecordingId.ToString("N") ?? "unknown";
            CleanupRecordingResources();
            ShowFromTray();
            AppDiagnosticLog.Write("recording.interface-restored",
                ("recordingId", failedRecordingId), ("media", "GIF"), ("result", "failure"));
            ShowAppDialog(
                exception.Message,
                UiText.Get("Não foi possível criar o GIF"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private RecordingTarget? ResolveRecordingTarget(RecordingTargetKind kind, CancellationToken cancellationToken = default) => kind switch
    {
        RecordingTargetKind.Monitor => _captureService.ActiveMonitorTarget(),
        RecordingTargetKind.Region => _captureService.SelectRecordingRegion(this, UiText.Get("Selecione a região do vídeo"), cancellationToken),
        RecordingTargetKind.Window => _captureService.SelectRecordingWindow(this, UiText.Get("Clique na janela que deseja gravar"), cancellationToken),
        _ => throw new InvalidOperationException(UiText.Get("O destino do vídeo não é válido."))
    };

    private static int ParseSelectedInt(ComboBox box, int fallback) =>
        int.TryParse(SelectedTag(box, fallback.ToString()), out var value)
            ? value
            : fallback;

    private async void OpenCaptureImage_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Get("Abrir imagem no editor"),
            Filter = UiText.Get("Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os arquivos|*.*")
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (await LoadCaptureWorkbenchImageAsync(dialog.FileName, null,
                UiText.Format($"Imagem · {Path.GetFileName(dialog.FileName)}")))
        {
            StatusText.Text = UiText.Format($"Imagem aberta: {Path.GetFileName(dialog.FileName)}");
        }
    }

    private async Task<bool> LoadCaptureWorkbenchImageAsync(
        string path,
        CaptureRecord? record,
        string details, bool showProgress = true)
    {
        if (record is not null && !string.Equals(_captureService.ResolveFilePath(record), path, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }
        if (_captureWorkbenchPath == path && CaptureInlineEditor.HasImage) return true;
        if (!CanReplaceCaptureSession()) return false;
        try
        {
            _captureImageLoadCancellation?.Cancel();
            using var cancellation = new CancellationTokenSource();
            _captureImageLoadCancellation = cancellation;
            var originalDocument = CaptureInlineEditor.Document;
            var originalRevision = originalDocument?.Revision;
            var progress = showProgress ? new ExternalOperationWindow(UiText.Get("Abrindo imagem…"), cancellation) { Owner = this } : null;
            progress?.Show();
            try
            {
                using var bitmap = await ExternalImageReader.DecodeAsync(() => ExternalImageReader.Load(path, cancellation.Token), cancellation.Token);
                if (cancellation.IsCancellationRequested || _servicesDisposed || _settingsOperationInProgress ||
                    !ReferenceEquals(originalDocument, CaptureInlineEditor.Document) || originalRevision != CaptureInlineEditor.Document?.Revision ||
                    (record is not null && (!string.Equals(_captureService.ResolveFilePath(record), path, StringComparison.OrdinalIgnoreCase) || !_captureService.History.Contains(record)))) return false;
                CaptureInlineEditor.LoadImage(bitmap);
            }
            finally
            {
                progress?.Complete();
                if (ReferenceEquals(_captureImageLoadCancellation, cancellation)) _captureImageLoadCancellation = null;
            }
            CapturePreviewEmptyPanel.Visibility = Visibility.Collapsed;
            CaptureWorkbenchToolbar.Visibility = Visibility.Visible;
            CaptureWorkbenchFooter.Visibility = Visibility.Visible;
            CaptureEditorContext.Visibility = Visibility.Visible;
            _captureMediaMode = CaptureMediaMode.Image;
            CaptureImageMediaButton.IsChecked = true;
            UpdateCaptureLauncherSummary();
            CapturePreviewDetailsText.Text = details;
            _captureWorkbenchPath = path;
            _captureWorkbenchRecord = record;
            UpdateCaptureWorkbenchToolState(null);
            if (CaptureWorkbenchToolbar.IsLoaded)
            {
                LabMotion.PlayEntrance(CaptureWorkbenchToolbar);
            }
            return true;
        }
        catch (OperationCanceledException) { StatusText.Text = UiText.Get("Operação cancelada. Os dados atuais foram preservados."); return false; }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = UiText.Get("Não foi possível abrir a imagem selecionada") + ": " + exception.Message;
            return false;
        }
    }

    private void SelectCaptureWorkbenchTool_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CaptureInlineEditor.HasImage)
        {
            ShowAppDialog(UiText.Get("Abra uma imagem ou faça uma captura antes de editar."), UiText.Get("Edição rápida"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CaptureAnnotationKind? selected = null;
        if (sender is Button { CommandParameter: string tool } &&
            Enum.TryParse(tool, true, out CaptureAnnotationKind parsed))
        {
            selected = parsed;
        }
        CaptureInlineEditor.SelectTool(selected);
        UpdateCaptureWorkbenchToolState(selected);
        StatusText.Text = selected is null
            ? UiText.Get("Seleção ativada")
            : UiText.Format($"{CaptureWorkbenchToolLabel(selected.Value)} ativado — arraste sobre a imagem");
    }

    private static string CaptureWorkbenchToolLabel(CaptureAnnotationKind tool) => tool switch
    {
        CaptureAnnotationKind.Pencil => UiText.Get("Caneta"),
        CaptureAnnotationKind.Highlighter => UiText.Get("Marca-texto"),
        CaptureAnnotationKind.Arrow => UiText.Get("Seta"),
        CaptureAnnotationKind.Rectangle => UiText.Get("Retângulo"),
        CaptureAnnotationKind.Text => UiText.Get("Texto"),
        CaptureAnnotationKind.Stamp => "Emoji",
        CaptureAnnotationKind.Ellipse => UiText.Get("Elipse"),
        CaptureAnnotationKind.Line => UiText.Get("Linha"),
        CaptureAnnotationKind.Number => UiText.Get("Número"),
        CaptureAnnotationKind.Blur => UiText.Get("Desfocar"),
        CaptureAnnotationKind.Pixelate => UiText.Get("Pixelizar"),
        _ => UiText.Get("Ferramenta")
    };

    private void UpdateCaptureWorkbenchToolState(CaptureAnnotationKind? selected)
    {
        CaptureSelectToolButton.Tag = selected is null && !CaptureInlineEditor.IsCropTool ? "Selected" : null;
        CapturePencilToolButton.Tag = selected == CaptureAnnotationKind.Pencil ? "Selected" : null;
        CaptureHighlighterToolButton.Tag = selected == CaptureAnnotationKind.Highlighter ? "Selected" : null;
        CaptureArrowToolButton.Tag = selected == CaptureAnnotationKind.Arrow ? "Selected" : null;
        CaptureRectangleToolButton.Tag = selected == CaptureAnnotationKind.Rectangle ? "Selected" : null;
        CaptureTextToolButton.Tag = selected == CaptureAnnotationKind.Text ? "Selected" : null;
        CaptureStampToolButton.Tag = selected == CaptureAnnotationKind.Stamp ? "Selected" : null;
        CaptureEllipseToolButton.Tag = selected == CaptureAnnotationKind.Ellipse ? "Selected" : null;
        CaptureLineToolButton.Tag = selected == CaptureAnnotationKind.Line ? "Selected" : null;
        CaptureNumberToolButton.Tag = selected == CaptureAnnotationKind.Number ? "Selected" : null;
        CaptureBlurToolButton.Tag = selected == CaptureAnnotationKind.Blur ? "Selected" : null;
        CapturePixelateToolButton.Tag = selected == CaptureAnnotationKind.Pixelate ? "Selected" : null;
        CaptureCropToolButton.Tag = CaptureInlineEditor.IsCropTool ? "Selected" : null;
    }

    private void CaptureInlineEditor_OnStateChanged(object? sender, EventArgs e)
    {
        if (CaptureWorkbenchUndoButton is null || CaptureWorkbenchRedoButton is null) return;
        var ink = DrawingColor.FromArgb(CaptureInlineEditor.InkArgb);
        CaptureWorkbenchColorSwatch.Background = new SolidColorBrush(Color.FromArgb(ink.A, ink.R, ink.G, ink.B));
        var size = CaptureInlineEditor.InkThickness.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        var sizeItem = CaptureWorkbenchThicknessBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == size);
        if (sizeItem is null)
        {
            sizeItem = new ComboBoxItem { Content = size, Tag = size };
            var index = CaptureWorkbenchThicknessBox.Items.OfType<ComboBoxItem>().Count(i => int.Parse(i.Tag!.ToString()!) < (int)CaptureInlineEditor.InkThickness);
            CaptureWorkbenchThicknessBox.Items.Insert(index, sizeItem);
        }
        CaptureWorkbenchThicknessBox.SelectedItem = sizeItem;
        CaptureWorkbenchUndoButton.IsEnabled = CaptureInlineEditor.CanUndo;
        CaptureWorkbenchRedoButton.IsEnabled = CaptureInlineEditor.CanRedo;
        CaptureEditorContext.Refresh();
        if (_captureEditorExpanded) Dispatcher.BeginInvoke(new Action(() => UpdateCaptureEditorViewport()));
        UpdateCaptureWorkbenchToolState(CaptureInlineEditor.SelectedTool);
        var ready = CaptureInlineEditor.HasImage && !CaptureInlineEditor.HasPendingCrop && !_captureCommitInProgress;
        CaptureCopyImageButton.IsEnabled = ready;
        CaptureSaveImageButton.IsEnabled = ready;
        CaptureCompleteImageButton.IsEnabled = ready;
        CaptureExpandEditorButton.IsEnabled = CaptureInlineEditor.HasImage;
        CaptureDiscardChangesButton.IsEnabled = CaptureInlineEditor.HasUnsavedChanges && !_captureCommitInProgress;
        CaptureEditorSessionText.Text = CaptureInlineEditor.Document is { } document
            ? UiText.Format($"{document.Dimensions.Width} × {document.Dimensions.Height} px · {document.OperationCount} edição(ões) · ") +
              (document.HasUnsavedChanges ? UiText.Get("Alterações não salvas") : UiText.Get("Sem alterações pendentes"))
            : UiText.Get("Uma sessão · edição não destrutiva");
        foreach (ComboBoxItem item in CaptureWorkbenchZoomBox.Items)
            if (item.Tag is string value && double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var zoom) && Math.Abs(zoom - CaptureInlineEditor.Zoom) < .001)
            { CaptureWorkbenchZoomBox.SelectedItem = item; break; }
    }

    private void CaptureWorkbenchUndo_OnClick(object sender, RoutedEventArgs e) =>
        CaptureInlineEditor.Undo();

    private void CaptureWorkbenchRedo_OnClick(object sender, RoutedEventArgs e) =>
        CaptureInlineEditor.Redo();

    private void CaptureWorkbenchThickness_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CaptureWorkbenchThicknessBox?.SelectedItem is ComboBoxItem { Tag: string value } &&
            float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var thickness))
        {
            CaptureInlineEditor?.SetThickness(thickness);
        }
    }

    private void CycleCaptureWorkbenchColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button anchor) CaptureInkPicker.Show(anchor, CaptureInlineEditor);
    }

    private void OpenAdvancedCaptureEditor_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CaptureInlineEditor.HasImage)
        {
            ShowAppDialog(UiText.Get("Abra uma imagem ou faça uma captura antes de editar."), UiText.Get("Editor avançado"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetCaptureEditorExpanded(!_captureEditorExpanded);
    }

    private void CaptureMoreTools_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button anchor) CaptureEditorContext.ShowMoreTools(anchor);
    }

    private void CaptureCropTool_OnClick(object sender, RoutedEventArgs e) => CaptureInlineEditor.SelectCrop();
    private void CaptureResizeTool_OnClick(object sender, RoutedEventArgs e) => CaptureEditorContext.ShowResize();

    private void UpdateCaptureToolbarLayout()
    {
        if (CaptureOutputCommands is null || CaptureToolCommands is null) return;
        // Full tools need their own row on smaller viewports; never hide them again.
        var compact = CaptureWorkbenchToolbar.ActualWidth < (_captureEditorExpanded ? 1250 : 1060);
        Grid.SetRow(CaptureOutputCommands, compact ? 1 : 0);
        Grid.SetColumn(CaptureOutputCommands, compact ? 0 : 1);
        Grid.SetColumnSpan(CaptureOutputCommands, compact ? 2 : 1);
        Grid.SetColumnSpan(CaptureToolCommands, compact ? 2 : 1);
        CaptureOutputCommands.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void CaptureWorkbenchToolbar_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCaptureToolbarLayout();
    }

    private void SetCaptureEditorExpanded(bool expanded)
    {
        if (_captureEditorExpanded == expanded) return;
        _captureEditorExpanded = expanded;
        CaptureMoreToolsButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        foreach (var tool in new[] { CaptureEllipseToolButton, CaptureLineToolButton, CaptureNumberToolButton,
                     CaptureBlurToolButton, CapturePixelateToolButton, CaptureCropToolButton, CaptureResizeToolButton })
            tool.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateCaptureToolbarLayout();
        CaptureExpandEditorButton.Content = expanded ? UiText.Get("Voltar ao painel · Esc") : UiText.Get("Expandir editor");
        if (expanded)
        {
            _capturePageVisibility.Clear();
            _capturePageRowHeights = CapturePageLayout.RowDefinitions.Select(r => r.Height).ToArray();
            foreach (UIElement child in CapturePageLayout.Children)
                if (Grid.GetRow(child) != 8) { _capturePageVisibility[child] = child.Visibility; child.Visibility = Visibility.Collapsed; }
            for (var i = 0; i < CapturePageLayout.RowDefinitions.Count; i++)
                if (i != 8) CapturePageLayout.RowDefinitions[i].Height = new GridLength(0);
        }
        else
        {
            foreach (var pair in _capturePageVisibility) pair.Key.Visibility = pair.Value;
            if (_capturePageRowHeights is not null)
                for (var i = 0; i < _capturePageRowHeights.Length; i++) CapturePageLayout.RowDefinitions[i].Height = _capturePageRowHeights[i];
        }
        CaptureShellHeader.Visibility = CaptureShellNavigation.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ShellRoot.RowDefinitions[1].Height = new GridLength(expanded ? 0 : 66);
        ShellRoot.RowDefinitions[2].Height = new GridLength(expanded ? 0 : 52);
        UpdateCaptureEditorViewport(animate: true);
        if (expanded) Dispatcher.BeginInvoke(new Action(() => UpdateCaptureEditorViewport(animate: true)), DispatcherPriority.ApplicationIdle);
        CaptureInlineEditor.Focus();
        CaptureInlineEditor.BringIntoView();
    }

    private void UpdateCaptureEditorViewport(bool animate = false)
    {
        if (CaptureEditorViewport is null) return;
        // Desired size excludes the unused stretch space of the page. ActualHeight
        // would count that blank area as chrome and keep the viewport at its minimum.
        var outside = Math.Max(0, CapturePageLayout.DesiredSize.Height - CaptureEditorViewport.DesiredSize.Height);
        var height = _captureEditorExpanded
            ? Math.Max(240, CaptureView.ActualHeight > 0 ? CaptureView.ActualHeight - outside - 2 : Height - 330)
            : 480;
        if (animate && LabMotion.Allowed(CaptureEditorViewport))
            CaptureEditorViewport.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(height, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { CaptureEditorViewport.BeginAnimation(FrameworkElement.HeightProperty, null); CaptureEditorViewport.Height = height; }
    }

    private bool CanReplaceCaptureSession()
    {
        if (_pendingCaptureEdit is not null || _captureCommitInProgress)
        { StatusText.Text = UiText.Get("Conclua ou cancele a captura atual antes de abrir outra imagem."); return false; }
        if (!CaptureInlineEditor.HasUnsavedChanges && !CaptureInlineEditor.HasPendingCrop) return true;
        return ShowAppDialog(UiText.Get("Há alterações ou uma seleção de recorte pendentes. Descartá-las e abrir outra imagem?"),
            UiText.Get("Editor de imagem"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    internal void PrepareCaptureEvidence(DrawingBitmap source, CaptureAnnotationKind tool, bool expanded)
    {
        ShowView(CaptureView, CaptureTabButton);
        if (!CaptureInlineEditor.HasImage)
        {
            CaptureInlineEditor.LoadImage(source);
            CaptureInlineEditor.InsertStamp("⭐", new Point(300, 180));
        }
        CapturePreviewEmptyPanel.Visibility = Visibility.Collapsed;
        CaptureWorkbenchToolbar.Visibility = CaptureWorkbenchFooter.Visibility = CaptureEditorContext.Visibility = Visibility.Visible;
        CapturePreviewDetailsText.Text = UiText.Get("Imagem de teste local · sessão única");
        CaptureInlineEditor.SelectTool(tool);
        SetCaptureEditorExpanded(expanded);
    }

    internal CaptureEditorDocument? CaptureEvidenceDocument => CaptureInlineEditor.Document;
    internal void ResizeCaptureEvidenceViewport() => UpdateCaptureEditorViewport();
    internal void DisposeCaptureEvidence() => CaptureInlineEditor.Dispose();

    private void CaptureDiscardChanges_OnClick(object sender, RoutedEventArgs e)
    {
        if (ShowAppDialog(UiText.Get("Voltar à última versão salva nesta sessão?"), UiText.Get("Descartar alterações"), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes) CaptureInlineEditor.DiscardChanges();
    }

    private async Task<CaptureImageEditResult?> EditNewCaptureInWorkbenchAsync(DrawingBitmap source, CaptureAnnotationKind initialTool)
    {
        if (!CanReplaceCaptureSession()) return null;
        CaptureInlineEditor.LoadImage(source);
        _captureWorkbenchPath = null; _captureWorkbenchRecord = null;
        _captureMediaMode = CaptureMediaMode.Image; CaptureImageMediaButton.IsChecked = true;
        UpdateCaptureLauncherSummary();
        CapturePreviewEmptyPanel.Visibility = Visibility.Collapsed;
        CaptureWorkbenchToolbar.Visibility = CaptureWorkbenchFooter.Visibility = CaptureEditorContext.Visibility = Visibility.Visible;
        CaptureInlineEditor.SelectTool(initialTool);
        CapturePreviewDetailsText.Text = UiText.Get("Nova captura · conclua para aplicar a regra");
        _pendingCaptureEdit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CaptureCancelPendingButton.Visibility = Visibility.Visible;
        ShowFromTray(); ShowView(CaptureView, CaptureTabButton); CaptureInlineEditor.BringIntoView(); CaptureInlineEditor.Focus();
        try { return await _pendingCaptureEdit.Task; }
        finally { _pendingCaptureEdit = null; CaptureCancelPendingButton.Visibility = Visibility.Collapsed; }
    }

    private void CaptureCancelPending_OnClick(object sender, RoutedEventArgs e)
    {
        _pendingCaptureEdit?.TrySetResult(null);
        CaptureInlineEditor.Clear();
        CapturePreviewEmptyPanel.Visibility = Visibility.Visible;
        CaptureWorkbenchToolbar.Visibility = CaptureWorkbenchFooter.Visibility = CaptureEditorContext.Visibility = Visibility.Collapsed;
        StatusText.Text = UiText.Get("Captura cancelada sem salvar");
    }

    private void AcceptCompletedCapture(CaptureRecord result)
    {
        _captureCommitInProgress = false;
        if (_completedCaptureDocument is not null && ReferenceEquals(_completedCaptureDocument, CaptureInlineEditor.Document))
        {
            _captureWorkbenchRecord = result;
            _captureWorkbenchPath = _captureService.ResolveFilePath(result);
            if (!string.IsNullOrWhiteSpace(_captureWorkbenchPath)) CaptureInlineEditor.MarkSaved();
        }
        else if (!CaptureInlineEditor.HasUnsavedChanges && !string.IsNullOrWhiteSpace(result.FilePath))
            _ = LoadCaptureWorkbenchImageAsync(_captureService.ResolveFilePath(result), result, $"{CaptureTypeLabel(result)} · {result.Width}×{result.Height}", showProgress: false);
        _completedCaptureDocument = null;
        CaptureInlineEditor_OnStateChanged(this, EventArgs.Empty);
    }

    private Microsoft.Win32.SaveFileDialog CreateCapturePreviewSaveDialog(string title)
    {
        var sourcePath = _captureWorkbenchPath ?? "captura.png";
        var extension = Path.GetExtension(sourcePath);
        var jpeg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        extension = jpeg ? ".jpg" : ".png";
        return new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            FileName = Path.GetFileNameWithoutExtension(sourcePath) + "-editada" + extension,
            DefaultExt = extension,
            Filter = jpeg
                ? "JPEG|*.jpg|PNG|*.png"
                : "PNG|*.png|JPEG|*.jpg"
        };
    }

    private async Task<bool> CommitCaptureWorkbenchImageAsync(DrawingBitmap bitmap, string path)
    {
        _captureCommitInProgress = true;
        CaptureEditorCard.IsEnabled = false;
        CaptureInlineEditor_OnStateChanged(this, EventArgs.Empty);
        try
        {
            var record = await _captureService.SaveEditedImageAsync(bitmap, path, _settings.Capture, _captureWorkbenchRecord);
            _captureWorkbenchPath = path;
            _captureWorkbenchRecord = record;
            CaptureInlineEditor.MarkSaved();
            CapturePreviewDetailsText.Text = $"{CaptureTypeLabel(record)} · {record.Width}×{record.Height}";
            RefreshCaptureHistory();
            StatusText.Text = UiText.Format($"Edição salva e atualizada nos Recentes: {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            ShowAppDialog(exception.Message, UiText.Get("Salvar captura"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _captureCommitInProgress = false;
            CaptureEditorCard.IsEnabled = true;
            CaptureInlineEditor_OnStateChanged(this, EventArgs.Empty);
        }
    }

    private void CopyCapturePreview_OnClick(object sender, RoutedEventArgs e)
    {
        if (CaptureInlineEditor.HasPendingCrop || _captureCommitInProgress) return;
        if (!CaptureInlineEditor.HasImage)
        {
            ShowAppDialog(UiText.Get("Não há uma imagem no editor."), UiText.Get("Copiar"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        using var rendered = CaptureInlineEditor.Render();
        try
        {
            Clipboard.SetImage(ToBitmapSource(rendered));
            StatusText.Text = UiText.Get("Imagem copiada para a área de transferência");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { StatusText.Text = UiText.Get("A área de transferência está ocupada. Tente copiar novamente."); }
    }

    private async void SaveCapturePreview_OnClick(object sender, RoutedEventArgs e)
    {
        if (CaptureInlineEditor.HasPendingCrop || _captureCommitInProgress) return;
        if (!CaptureInlineEditor.HasImage)
        {
            ShowAppDialog(UiText.Get("Não há uma imagem no editor."), UiText.Get("Salvar"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = CreateCapturePreviewSaveDialog(UiText.Get("Salvar uma cópia"));
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        using var rendered = CaptureInlineEditor.Render();
        await CommitCaptureWorkbenchImageAsync(rendered, dialog.FileName);
    }

    private async void CompleteCapturePreview_OnClick(object sender, RoutedEventArgs e)
    {
        if (CaptureInlineEditor.HasPendingCrop || _captureCommitInProgress) return;
        if (_pendingCaptureEdit is not null)
        {
            _completedCaptureDocument = CaptureInlineEditor.Document;
            _captureCommitInProgress = true;
            CaptureEditorCard.IsEnabled = false;
            _pendingCaptureEdit.TrySetResult(new CaptureImageEditResult(CaptureInlineEditor.Render(), CaptureEditorOutput.Default));
            CaptureInlineEditor_OnStateChanged(this, EventArgs.Empty);
            return;
        }
        if (!CaptureInlineEditor.HasImage)
        {
            ShowAppDialog(UiText.Get("Abra uma imagem ou faça uma captura antes de concluir."), UiText.Get("Concluir"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        using var rendered = CaptureInlineEditor.Render();
        var saved = false;
        if (_captureWorkbenchRecord?.MediaKind.Equals("image", StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrWhiteSpace(_captureWorkbenchPath) &&
            File.Exists(_captureWorkbenchPath))
        {
            if (!await CommitCaptureWorkbenchImageAsync(rendered, _captureWorkbenchPath)) return;
            saved = true;
        }
        else if (_settings.Capture.SaveAutomatically)
        {
            var dialog = CreateCapturePreviewSaveDialog(UiText.Get("Salvar edição"));
            if (dialog.ShowDialog(this) != true || !await CommitCaptureWorkbenchImageAsync(rendered, dialog.FileName)) return;
            saved = true;
        }
        if (_settings.Capture.CopyToClipboard)
        {
            try { Clipboard.SetImage(ToBitmapSource(rendered)); }
            catch (System.Runtime.InteropServices.ExternalException)
            { StatusText.Text = saved ? UiText.Get("Edição salva; a área de transferência está ocupada. Tente copiar novamente.") : UiText.Get("A área de transferência está ocupada. Tente copiar novamente."); return; }
        }
        StatusText.Text = saved
            ? UiText.Get("Edição concluída e atualizada nos Recentes")
            : _settings.Capture.CopyToClipboard ? UiText.Get("Captura copiada; a sessão permanece disponível") : UiText.Get("Resultado mantido apenas na sessão — copiar e salvar estão desativados na regra");
    }

    private void CaptureWorkbenchZoom_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CaptureWorkbenchZoomBox?.SelectedItem is not ComboBoxItem { Tag: string value } ||
            !double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var zoom))
        {
            return;
        }
        _captureWorkbenchZoom = zoom;
        if (CaptureInlineEditor is not null)
        {
            CaptureInlineEditor.SetZoom(zoom);
        }
    }

    private static BitmapSource ToBitmapSource(DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void CaptureFormat_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CaptureQualityBox is not null && CaptureFormatBox is not null)
        {
            CaptureQualityBox.IsEnabled =
                SelectedTag(CaptureFormatBox, "PNG").Equals(
                    "JPEG",
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RefreshCaptureHistory()
    {
        if (CaptureHistoryPanel is null)
        {
            return;
        }

        CaptureHistoryPanel.Children.Clear();
        _captureThumbnailCancellation?.Cancel(); _captureThumbnailCancellation?.Dispose();
        _captureThumbnailCancellation = new CancellationTokenSource();
        CaptureHistoryScroller.ScrollToLeftEnd();

        var mostRecent = _captureService.History.FirstOrDefault();
        if (!CaptureInlineEditor.HasImage && mostRecent is not null)
        {
            var mostRecentPath = _captureService.ResolveFilePath(mostRecent);
            if (mostRecent.MediaKind.Equals("image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(mostRecentPath) &&
                File.Exists(mostRecentPath))
            {
                _ = LoadCaptureWorkbenchImageAsync(
                    mostRecentPath,
                    mostRecent,
                    $"{CaptureTypeLabel(mostRecent)} · {mostRecent.Width}×{mostRecent.Height}", showProgress: false);
            }
        }

        var filter = CaptureHistoryFilterBox is null
            ? "all"
            : SelectedTag(CaptureHistoryFilterBox, "all");
        var filtered = CaptureHistoryQuery.Filter(_captureService.History, filter, CaptureHistorySearchBox?.Text ?? "");
        foreach (var item in filtered.Take(40))
        {
            CaptureHistoryPanel.Children.Add(BuildCaptureHistoryCard(item));
        }
        if (CaptureHistoryPanel.Children.Count == 0)
        {
            var empty = new Border
            {
                Padding = new Thickness(18),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Child = new TextBlock
                {
                    Text = _captureService.History.Count > 0 ? UiText.Get("Nenhuma captura corresponde à busca e ao filtro.") : UiText.Get("As últimas capturas aparecerão aqui."),
                    Foreground = (Brush)FindResource("MutedBrush")
                }
            };
            empty.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
            empty.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
            CaptureHistoryPanel.Children.Add(empty);
        }
        RefreshStorageProtection();
        CaptureHistoryStatusText.Text = filtered.Count > 40 ? UiText.Format($"40 de {filtered.Count:N0} · Ver todas") : filtered.Count.ToString("N0");
        Dispatcher.BeginInvoke(new Action(UpdateCaptureHistoryNavigationState));
    }

    private Border BuildCaptureHistoryCard(CaptureRecord record)
    {
        var path = _captureService.ResolveFilePath(record);
        var card = new Border
        {
            Width = 370,
            Height = 96,
            Margin = new Thickness(0, 0, 12, 2),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            ToolTip = UiText.Get("Selecionar no editor"),
            Tag = record,
            Cursor = Cursors.Hand,
            RenderTransform = new TranslateTransform(),
            RenderTransformOrigin = new Point(.5, .5)
        };
        card.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        LabMotion.SetEntrance(card, "Page");
        card.MouseLeftButtonUp += CaptureHistoryCard_OnClick;
        card.MouseEnter += (_, _) =>
        {
            card.SetResourceReference(Border.BorderBrushProperty, "Lab.accent");
            if (card.RenderTransform is TranslateTransform transform)
            {
                AnimateHistoryCard(transform, -3, card);
            }
        };
        card.MouseLeave += (_, _) =>
        {
            card.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
            if (card.RenderTransform is TranslateTransform transform)
            {
                AnimateHistoryCard(transform, 0, card);
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumbnail = new Border
        {
            Margin = new Thickness(0, 0, 12, 0),
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true
        };
        thumbnail.SetResourceReference(Border.BackgroundProperty, "Lab.canvas");
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource(record.MediaKind.Equals("video", StringComparison.OrdinalIgnoreCase)
                    ? "Lab.Icon.Video"
                    : "Lab.Icon.Image"),
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Lab.muted");
            thumbnail.Child = icon;
        }
        if (!record.MediaKind.Equals("video", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(path))
            _ = LoadHistoryThumbnailAsync(thumbnail, path, record, _captureThumbnailCancellation?.Token ?? default);
        grid.Children.Add(thumbnail);

        // Display metadata without granting access for a thumbnail or other file action.
        var displayPath = CapturePathResolver.Resolve(record, AppPaths.Current);
        var title = string.IsNullOrWhiteSpace(displayPath)
            ? CaptureTypeLabel(record)
            : Path.GetFileNameWithoutExtension(displayPath);
        var duration = record.DurationSeconds > 0
            ? $" · {TimeSpan.FromSeconds(record.DurationSeconds):mm\\:ss}"
            : string.Empty;
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 170
        });
        copy.Children.Add(new TextBlock
        {
            Text = record.RequiresFileApproval && string.IsNullOrEmpty(path)
                ? UiText.Get("Confirme o arquivo do histórico")
                : $"{CaptureTypeLabel(record)} · {record.CreatedAt:dd/MM HH:mm}{duration}",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(HistoryButton("ExternalLink", UiText.Get("Abrir"), record, OpenHistoryItem_OnClick));
        actions.Children.Add(HistoryButton("Copy", UiText.Get("Copiar"), record, CopyHistoryItem_OnClick));
        if (record.MediaKind.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            actions.Children.Add(HistoryButton("PenLine", UiText.Get("Editar"), record, EditHistoryItem_OnClick));
        }
        actions.Children.Add(HistoryButton("X", UiText.Get("Excluir"), record, DeleteHistoryItem_OnClick));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private async void CaptureHistoryCard_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: CaptureRecord record })
        {
            return;
        }
        if (!TryAuthorizeHistoryFile(record, out var path)) return;
        if (!record.MediaKind.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            OpenHistoryItem_OnClick(new Button { Tag = record }, new RoutedEventArgs());
            return;
        }
        if (await LoadCaptureWorkbenchImageAsync(path, record,
                $"{CaptureTypeLabel(record)} · {record.Width}×{record.Height}"))
        {
            CaptureInlineEditor.BringIntoView();
            StatusText.Text = UiText.Get("Captura selecionada no editor");
        }
    }

    private static string CaptureTypeLabel(CaptureRecord record) => record.MediaKind.ToLowerInvariant() switch
    {
        "gif" => "GIF",
        "video" => UiText.Get("Vídeo MP4"),
        _ => record.Type.ToLowerInvariant() switch
        {
            "monitor" => "Monitor",
            "regiao" => UiText.Get("Região"),
            "janela" => UiText.Get("Janela"),
            "rolagem" => UiText.Get("Captura longa"),
            _ => UiText.Get("Imagem")
        }
    };

    private async Task LoadHistoryThumbnailAsync(Border thumbnail, string path, CaptureRecord record, CancellationToken token)
    {
        try
        {
            var image = await ExternalImageReader.DecodeAsync(() => TryLoadImage(path, out var result) ? result : null, token);
            if (image is not null && !token.IsCancellationRequested && !_servicesDisposed &&
                _captureService.History.Contains(record) && string.Equals(_captureService.ResolveFilePath(record), path, StringComparison.OrdinalIgnoreCase))
                thumbnail.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
        }
        catch (OperationCanceledException) { }
    }

    private static bool TryLoadImage(string path, out BitmapImage? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var dimensions = ExternalImageReader.CheckHeader(stream);
            var loaded = new BitmapImage();
            loaded.BeginInit();
            loaded.CacheOption = BitmapCacheOption.OnLoad;
            var scale = Math.Min(1d, Math.Min(240d / dimensions.Width, 240d / dimensions.Height));
            loaded.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(dimensions.Width * scale));
            loaded.DecodePixelHeight = Math.Max(1, (int)Math.Ceiling(dimensions.Height * scale));
            loaded.StreamSource = stream;
            loaded.EndInit();
            loaded.Freeze();
            image = loaded;
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static Button HistoryButton(
        string iconKind,
        string toolTip,
        CaptureRecord record,
        RoutedEventHandler handler)
    {
        var button = new Button
        {
            Tag = record,
            ToolTip = toolTip,
            MinWidth = 28,
            Width = 28,
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(2)
        };
        button.SetResourceReference(StyleProperty, "Lab.Pilot.ToolButton");
        var icon = new LabIcon { Kind = iconKind, Width = 15, Height = 15 };
        icon.SetResourceReference(LabIcon.ForegroundProperty, "Lab.text");
        button.Content = icon;
        button.Click += (sender, args) =>
        {
            args.Handled = true;
            handler(sender, args);
        };
        return button;
    }

    private static void AnimateHistoryCard(
        TranslateTransform transform,
        double target,
        FrameworkElement owner)
    {
        if (!SystemParameters.ClientAreaAnimation || LabMotion.GetReduced(owner))
        {
            transform.Y = target;
            return;
        }
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    private void ScrollCaptureHistoryPrevious_OnClick(object sender, RoutedEventArgs e) =>
        ScrollCaptureHistoryBy(-Math.Max(300, CaptureHistoryScroller.ViewportWidth * .82));

    private void ScrollCaptureHistoryNext_OnClick(object sender, RoutedEventArgs e) =>
        ScrollCaptureHistoryBy(Math.Max(300, CaptureHistoryScroller.ViewportWidth * .82));

    private void ScrollCaptureHistoryBy(double delta)
    {
        var start = CaptureHistoryScroller.HorizontalOffset;
        var target = Math.Clamp(
            start + delta,
            0,
            CaptureHistoryScroller.ScrollableWidth);
        BeginAnimation(CaptureHistoryOffsetProperty, null);
        if (!SystemParameters.ClientAreaAnimation || LabMotion.GetReduced(this))
        {
            CaptureHistoryOffset = target;
            return;
        }
        CaptureHistoryOffset = start;
        var animation = new DoubleAnimation
        {
            From = start,
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.Completed += (_, _) =>
        {
            BeginAnimation(CaptureHistoryOffsetProperty, null);
            CaptureHistoryOffset = target;
        };
        BeginAnimation(CaptureHistoryOffsetProperty, animation);
    }

    private void CaptureHistoryScroller_OnScrollChanged(object sender, ScrollChangedEventArgs e) =>
        UpdateCaptureHistoryNavigationState();

    private void UpdateCaptureHistoryNavigationState()
    {
        if (CaptureHistoryPreviousButton is null || CaptureHistoryNextButton is null) return;
        CaptureHistoryPreviousButton.IsEnabled = CaptureHistoryScroller.HorizontalOffset > 1;
        CaptureHistoryNextButton.IsEnabled =
            CaptureHistoryScroller.HorizontalOffset < CaptureHistoryScroller.ScrollableWidth - 1;
    }

    private void CaptureHistoryFilter_OnChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshCaptureHistory();

    private void OpenHistoryItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CaptureRecord record } || !TryAuthorizeHistoryFile(record, out var path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception)
        { ShowAppDialog(exception.Message, UiText.Get("Histórico"), MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void CopyHistoryItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CaptureRecord record })
        {
            return;
        }
        try
        {
            if (!TryAuthorizeHistoryFile(record, out var path)) return;
            CaptureService.CopyFileToClipboard(path);
            StatusText.Text = UiText.Format($"Captura copiada: {Path.GetFileName(path)}");
        }
        catch (Exception exception)
        {
            ShowAppDialog(
                exception.Message,
                UiText.Get("Histórico"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void EditHistoryItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CaptureRecord record })
        {
            return;
        }
        if (!TryAuthorizeHistoryFile(record, out var path)) return;
        if (await LoadCaptureWorkbenchImageAsync(path, record,
                $"{CaptureTypeLabel(record)} · {record.Width}×{record.Height}"))
        {
            ShowView(CaptureView, CaptureTabButton);
            CaptureInlineEditor.BringIntoView(); CaptureInlineEditor.Focus();
            StatusText.Text = UiText.Get("Imagem aberta na sessão única de edição");
        }
    }

    private async void DeleteHistoryItem_OnClick(object sender, RoutedEventArgs e)
    {
        using var lease = TryReserveCaptureCommand();
        if (lease is null) return;
        if (_captureService.IsHistoryProtected) { ReviewStorage_OnClick(sender, e); return; }
        if (sender is not Button { Tag: CaptureRecord record })
        {
            return;
        }
        var choice = ShowAppDialog(
            UiText.Get("Sim: excluir também o arquivo local.\nNão: remover somente do histórico."),
            UiText.Get("Excluir captura"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }
        if (choice == MessageBoxResult.Yes && !TryAuthorizeHistoryFile(record, out _)) return;
        try
        {
            await _captureService.DeleteAsync(record.Id, deleteFile: choice == MessageBoxResult.Yes);
            RefreshCaptureHistory();
        }
        catch (Exception exception)
        { ShowAppDialog(exception.Message, UiText.Get("Histórico"), MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void CleanCaptureHistory_OnClick(object sender, RoutedEventArgs e)
    {
        using var lease = TryReserveCaptureCommand();
        if (lease is null) return;
        if (_captureService.IsHistoryProtected) { ReviewStorage_OnClick(sender, e); return; }
        var days = ParseSelectedInt(CaptureRetentionBox, 90);
        if (days <= 0)
        {
            CaptureHistoryStatusText.Text = UiText.Get("Limpeza automática desativada");
            return;
        }
        var count = await _captureService.CleanOlderThanAsync(days, deleteFiles: false);
        CaptureHistoryStatusText.Text = UiText.Format($"{count:N0} entrada(s) antiga(s) removida(s)");
        RefreshCaptureHistory();
    }

    private void OpenCaptureFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = CaptureService.ResolveDirectoryTemplate(
            Environment.ExpandEnvironmentVariables(_settings.Capture.OutputDirectoryTemplate),
            DateTimeOffset.Now);
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        if (AppPaths.IsCapturePilot || _manualUpdateCheckInProgress) return;
        _manualUpdateCheckInProgress = true;
        CheckUpdatesButton.IsEnabled = false;
        SettingsCheckUpdatesButton.IsEnabled = false;
        try
        {
            var result = await CheckUpdatesAndRefreshAsync();
            if (result.UpdateAvailable && result.Release is not null)
            {
                _updateNotificationGate.Mark(result.Release.Version);
                await OfferUpdateAsync(result);
            }
            else
            {
                ShowSettingsDialog(
                    UiText.UpdateStatus(result.Message),
                    UiText.Get("Atualizações do SlashDesk"),
                    MessageBoxButton.OK,
                    result.Status == UpdateCheckStatus.Offline
                        ? MessageBoxImage.Information
                        : MessageBoxImage.None);
            }
        }
        catch (Exception exception)
        {
            ShowSettingsDialog(
                UiText.Format($"Não foi possível consultar o GitHub.\n\n{exception.Message}"),
                UiText.Get("Atualizações do SlashDesk"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _manualUpdateCheckInProgress = false;
            CheckUpdatesButton.IsEnabled = true;
            SettingsCheckUpdatesButton.IsEnabled = true;
        }
    }

    private void StartUpdateMonitor()
    {
        StopUpdateMonitor();
        if (_settingsStore.IsProtected || AppPaths.IsCapturePilot || !_settings.CheckUpdatesOnStartup || _servicesDisposed)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _updateMonitorCancellation = cancellation;
        _updateMonitorTask = MonitorUpdatesAsync(cancellation);
    }

    private void StopUpdateMonitor()
    {
        Interlocked.Exchange(ref _updateMonitorCancellation, null)?.Cancel();
        _updateMonitorTask = null;
    }

    private async Task MonitorUpdatesAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await CheckUpdatesSilentlyAsync(cancellation.Token);
            using var timer = new PeriodicTimer(UpdateMonitorInterval);
            while (await timer.WaitForNextTickAsync(cancellation.Token))
            {
                await CheckUpdatesSilentlyAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Encerramento normal ao desativar a opção ou sair do aplicativo.
        }
        catch (Exception exception)
        {
            SafeDiagnosticLog.Write("update.monitor.failed", new Dictionary<string, object?>
            {
                ["exceptionType"] = exception.GetType().Name
            });
        }
        finally
        {
            Interlocked.CompareExchange(ref _updateMonitorCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private async Task CheckUpdatesSilentlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _updateService.CheckAsync(cancellationToken: cancellationToken);
            UpdateUpdateDisplay(await _updateService.LoadStateAsync(cancellationToken));
            if (result.UpdateAvailable &&
                result.Release is not null &&
                _updateNotificationGate.TryMark(result.Release.Version))
            {
                StatusText.Text = UiText.UpdateStatus(result.Message);
                ShowTrayBalloon(
                    2500,
                    UiText.Get("Atualização disponível"),
                    UiText.UpdateStatus(result.Message),
                    Forms.ToolTipIcon.Info);
                await OfferUpdateAsync(result);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Falhas de rede permanecem silenciosas no monitor automático.
        }
    }

    private async Task OfferUpdateAsync(UpdateCheckResult result)
    {
        if (AppPaths.IsCapturePilot) return;
        if (result.Release is null || Interlocked.Exchange(ref _updateOfferActive, 1) != 0)
        {
            return;
        }
        try
        {
            var dialog = new UpdateAvailableWindow(result) { Owner = this };
            dialog.ShowDialog();
            switch (dialog.Decision)
            {
                case UpdateDecision.UpdateNow:
                    if (AppPaths.IsPortable)
                    {
                        await DownloadAndApplyUpdateAsync(result.Release);
                    }
                    else
                    {
                        ShowAppDialog(
                            UiText.Get("Esta compilação instalada ainda não possui um instalador transacional. ") +
                            UiText.Get("A página oficial será aberta para uma atualização manual que preserva ") +
                            "%LocalAppData%\\SlashDesk.",
                            UiText.Get("Atualização da versão instalada"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        Process.Start(new ProcessStartInfo(result.Release.PageUrl) { UseShellExecute = true });
                    }
                    break;
                case UpdateDecision.RemindLater:
                    await _updateService.RemindLaterAsync(result.Release.Version);
                    break;
                case UpdateDecision.IgnoreVersion:
                    await _updateService.IgnoreVersionAsync(result.Release.Version);
                    break;
            }
            UpdateUpdateDisplay(await _updateService.LoadStateAsync());
        }
        finally
        {
            Interlocked.Exchange(ref _updateOfferActive, 0);
        }
    }

    private async Task DownloadAndApplyUpdateAsync(ReleaseInfo release)
    {
        using var lease = TryReserveCaptureCommand();
        if (lease is null) return;
        var progressWindow = new UpdateProgressWindow { Owner = this };
        _activeUpdateCancellation = progressWindow.Cancellation;
        progressWindow.Show();
        try
        {
            var prepared = await _portableUpdateService.PrepareAsync(
                release,
                progressWindow,
                progressWindow.Cancellation.Token);
            progressWindow.Report(new UpdateProgress(
                UiText.Get(AppPaths.IsSlashGuedes
                    ? "Download validado. Atualizando somente SlashGuedes.exe..."
                    : "Download validado. Encerrando para substituir somente SlashDesk.exe..."),
                0,
                null,
                IsApplying: true));
            PortableUpdateService.LaunchHelper(prepared);
            progressWindow.AllowClose();
            _exitRequested = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            progressWindow.AllowClose();
            StatusText.Text = UiText.Get("Atualização cancelada; nenhum arquivo do aplicativo foi alterado");
        }
        catch (Exception exception)
        {
            progressWindow.AllowClose();
            AppDiagnosticLog.WriteException("update.prepare.failed", exception);
            ShowAppDialog(
                UiText.Get("A atualização não foi aplicada. O executável atual e SlashDeskData foram ") +
                "preservados.\n\n" + exception.Message,
                UiText.Get("Falha na atualização"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _activeUpdateCancellation = null;
        }
    }

    private async Task RefreshUpdateStatusAsync() =>
        UpdateUpdateDisplay(await _updateService.LoadStateAsync());

    private void UpdateUpdateDisplay(UpdateState state)
    {
        LastUpdateCheckText.Text = state.LastCheckedUtc is null
            ? UiText.Get("Última verificação: ainda não verificado")
            : UiText.Format($"Última verificação: {state.LastCheckedUtc.Value.ToLocalTime():g}");
        LastUpdateResultText.Text = UiText.Format($"Resultado: {UiText.UpdateStatus(state.LastResult)}");
        SettingsLastUpdateCheckText.Text = LastUpdateCheckText.Text;
        SettingsLastUpdateResultText.Text = LastUpdateResultText.Text;
        _lastReleaseUrl = state.LastReleaseUrl;
        ReleaseNotesButton.Visibility = string.IsNullOrWhiteSpace(_lastReleaseUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ReleaseNotes_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReleaseUrl))
        {
            OpenAboutLink(_lastReleaseUrl);
        }
    }

    private static string ProductVersion() =>
        AppPaths.IsSlashGuedes ? UpdateService.InstalledVersion() :
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetName().Version?.ToString(3) ?? "0.0.0";

    private void OpenGitHub_OnClick(object sender, RoutedEventArgs e) => OpenAboutLink(ProjectUrl);

    private void ReplaceList(IEnumerable<Snippet> snippets)
    {
        _snippets.Clear();
        foreach (var snippet in snippets)
        {
            _snippets.Add(snippet);
        }
        RefreshNavigation();
    }

    private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
        UpdateQuickAccentLayout(e.NewSize.Width);
        UpdateStatisticsLayout(e.NewSize.Width);
        UpdateSettingsLayout(e.NewSize.Width);
        UpdateAboutLayout(e.NewSize.Width);
        if (_captureEditorExpanded) UpdateCaptureEditorViewport();
    }

    private void UpdateResponsiveLayout(double width)
    {
        if (ShortcutSidebarPanel is null)
        {
            return;
        }

        var band = width <= 1050 ? 0 : width < 1180 ? 1 : 2;
        var bandChanged = band != _shortcutResponsiveBand;
        _shortcutResponsiveBand = band;

        var horizontalMargin = band < 2 ? 20d : 32d;
        WorkspaceHost.Margin = new Thickness(horizontalMargin, band == 0 ? 12 : 20,
            horizontalMargin, band == 0 ? 12 : 20);
        ShortcutSecondaryRow.Height = new GridLength(0);
        ShortcutLeftDividerColumn.Width = new GridLength(16);
        ShortcutRightDividerColumn.Width = new GridLength(16);

        Grid.SetRow(ShortcutSidebarPanel, 0);
        Grid.SetColumn(ShortcutSidebarPanel, 0);
        Grid.SetColumnSpan(ShortcutSidebarPanel, 1);
        Grid.SetRow(ShortcutEditorPanel, 0);
        Grid.SetColumn(ShortcutEditorPanel, _shortcutEditorExpanded ? 0 : 2);
        Grid.SetColumnSpan(ShortcutEditorPanel, _shortcutEditorExpanded ? 5 : 1);
        Grid.SetRow(ShortcutVariablesPanel, 0);
        Grid.SetColumn(ShortcutVariablesPanel, 4);
        Grid.SetColumnSpan(ShortcutVariablesPanel, 1);

        ShortcutLeftDivider.Visibility = _shortcutEditorExpanded ? Visibility.Collapsed : Visibility.Visible;
        ShortcutRightDivider.Visibility = !_shortcutEditorExpanded && _shortcutVariablesVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_shortcutEditorExpanded) UpdateShortcutEditorHeight();
        NormalizeShortcutColumns(width, bandChanged);
    }

    private void NormalizeShortcutColumns(double windowWidth, bool restoreDefaults = false)
    {
        var band = _shortcutResponsiveBand < 0
            ? windowWidth <= 1050 ? 0 : windowWidth < 1180 ? 1 : 2
            : _shortcutResponsiveBand;
        var margin = band < 2 ? 20d : 32d;
        var editorMinimum = band == 0 ? 420d : band == 1 ? 440d : 520d;
        var available = Math.Max(0, windowWidth - (margin * 2));
        var rightMinimum = _shortcutVariablesVisible ? ShortcutRightMinimum : 0d;
        var sideSpace = Math.Max(
            ShortcutLeftMinimum + rightMinimum,
            available - (_shortcutVariablesVisible ? ShortcutDividerSpace : 16d) - editorMinimum);

        ShortcutEditorColumn.MinWidth = editorMinimum;
        ShortcutLeftColumn.MinWidth = ShortcutLeftMinimum;
        ShortcutRightColumn.MinWidth = rightMinimum;
        ShortcutLeftColumn.MaxWidth = Math.Max(
            ShortcutLeftMinimum,
            Math.Min(ShortcutLeftMaximum, sideSpace - rightMinimum));
        ShortcutRightColumn.MaxWidth = Math.Max(
            ShortcutRightMinimum,
            Math.Min(ShortcutRightMaximum, sideSpace - ShortcutLeftMinimum));

        var defaultLeft = band == 0 ? 240d : band == 1 ? 260d : 300d;
        var defaultRight = band == 0 ? 240d : 280d;
        var left = restoreDefaults ? defaultLeft : ShortcutLeftColumn.ActualWidth;
        var right = restoreDefaults ? defaultRight : ShortcutRightColumn.ActualWidth;
        if (left <= 0) left = defaultLeft;
        if (right <= 0) right = defaultRight;

        left = Math.Clamp(left, ShortcutLeftMinimum, ShortcutLeftColumn.MaxWidth);
        right = _shortcutVariablesVisible ? Math.Clamp(right, rightMinimum, ShortcutRightColumn.MaxWidth) : 0d;
        ShortcutRightDividerColumn.Width = new GridLength(_shortcutVariablesVisible ? 16 : 0);
        var overflow = Math.Max(0, left + right - sideSpace);
        if (overflow > 0)
        {
            var rightReduction = Math.Min(overflow, right - rightMinimum);
            right -= rightReduction;
            overflow -= rightReduction;
            left -= Math.Min(overflow, left - ShortcutLeftMinimum);
        }

        ShortcutLeftColumn.Width = new GridLength(left);
        ShortcutRightColumn.Width = new GridLength(right);
        ShortcutLeftColumn.MaxWidth = Math.Max(
            ShortcutLeftMinimum,
            Math.Min(ShortcutLeftMaximum, sideSpace - right));
        ShortcutRightColumn.MaxWidth = Math.Max(
            ShortcutRightMinimum,
            Math.Min(ShortcutRightMaximum, sideSpace - left));
    }

    private void ShortcutSplitter_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Home)
        {
            RestoreShortcutSplitter((GridSplitter)sender);
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        var direction = e.Key == Key.Right ? 1d : -1d;
        if (ReferenceEquals(sender, ShortcutLeftDivider))
        {
            ShortcutLeftColumn.Width = new GridLength(
                Math.Clamp(ShortcutLeftColumn.ActualWidth + (direction * ShortcutSplitterStep),
                    ShortcutLeftMinimum, ShortcutLeftColumn.MaxWidth));
        }
        else
        {
            ShortcutRightColumn.Width = new GridLength(
                Math.Clamp(ShortcutRightColumn.ActualWidth - (direction * ShortcutSplitterStep),
                    ShortcutRightMinimum, ShortcutRightColumn.MaxWidth));
        }
        NormalizeShortcutColumns(ActualWidth);
        e.Handled = true;
    }

    private void ShortcutSplitter_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        RestoreShortcutSplitter((GridSplitter)sender);
        e.Handled = true;
    }

    private void RestoreShortcutSplitter(GridSplitter splitter)
    {
        var band = _shortcutResponsiveBand;
        if (ReferenceEquals(splitter, ShortcutLeftDivider))
        {
            ShortcutLeftColumn.Width = new GridLength(band == 0 ? 240 : band == 1 ? 260 : 300);
        }
        else
        {
            ShortcutRightColumn.Width = new GridLength(band == 0 ? 240 : 280);
        }
        NormalizeShortcutColumns(ActualWidth);
    }

    private void ShortcutSplitter_OnDragStarted(object sender, DragStartedEventArgs e) =>
        ((GridSplitter)sender).Tag = "Dragging";

    private void ShortcutSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        ((GridSplitter)sender).Tag = null;
        NormalizeShortcutColumns(ActualWidth);
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void MainWindow_OnActivated(object? sender, EventArgs e)
    {
        if (_initialized &&
            _settings.Theme.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            ThemeService.Apply(_settings.Theme);
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_servicesDisposed) return;
        if (_recordingExitPending) { e.Cancel = true; return; }
        if (_restoreRecoveryBlocked) { _pendingCaptureEdit?.TrySetResult(null); DisposeServices(); return; }
        if (_settingsOperationInProgress) { e.Cancel = true; _exitRequested = false; StatusText.Text = UiText.Get("Aguarde a manutenção em andamento antes de sair."); return; }
        if (!_exitRequested && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            ShowTrayBalloon(
                1800,
                UiText.Get("SlashDesk continua ativo"),
                UiText.Get("Use o ícone da bandeja para abrir ou sair."),
                Forms.ToolTipIcon.Info);
            return;
        }

        if (!CanDiscardShortcutDraft()) { e.Cancel = true; _exitRequested = false; return; }
        _activeUpdateCancellation?.Cancel();
        if (CaptureInlineEditor.HasUnsavedChanges && ShowAppDialog(UiText.Get("Há alterações de imagem não salvas. Sair sem salvar?"), UiText.Get("Editor de imagem"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        { e.Cancel = true; _exitRequested = false; return; }
        if (_recordingOperation is { Phase: RecordingOperationPhase.Preparing } operation)
        {
            e.Cancel = true;
            _recordingExitPending = true;
            _exitRequested = true;
            operation.Cancel();
            _ = CloseAfterRecordingPreparationAsync(operation);
            return;
        }
        _pendingCaptureEdit?.TrySetResult(null);
        DisposeServices();
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        DisposeServices();
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowFromTray()
    {
        if (!CanUseRecordingInterface) return;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    private void DisposeServices()
    {
        if (_servicesDisposed)
        {
            return;
        }

        _servicesDisposed = true;
        _recordingOperation?.Cancel();
        _captureImageLoadCancellation?.Cancel();
        _captureThumbnailCancellation?.Cancel();
        StopQuickAccentStatusMonitor();
        StopUpdateMonitor();
        Activated -= MainWindow_OnActivated;
        _keyboardHook.ExpansionRequested -= KeyboardHook_OnExpansionRequested;
        _keyboardHook.SuggestionsChanged -= KeyboardHook_OnSuggestionsChanged;
        _keyboardHook.Dispose();
        _quickAccentService.Changed -= QuickAccentService_OnChanged;
        _quickAccentService.CharacterInserted -= QuickAccentService_OnCharacterInserted;
        _quickAccentService.Dispose();
        _captureShortcuts.Triggered -= CaptureShortcuts_OnTriggered;
        _captureShortcuts.Dispose();
        DisposeClipboard();
        CleanupRecordingResources();
        CaptureInlineEditor.Dispose();
        _suggestionWindow.Close();
        _quickAccentWindow.Close();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }
}
