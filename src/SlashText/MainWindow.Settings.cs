using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private bool _settingsOperationInProgress;
    private CancellationTokenSource? _settingsCancellation;
    private ExternalOperationWindow? _settingsProgress;
    private ScreenHelpHighlighter? _settingsHelpHighlight;

    private MessageBoxResult ShowSettingsDialog(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information, string yesLabel = "Continuar", string noLabel = "Cancelar")
    {
        var dialog = new SettingsDialogWindow(message, title, buttons, image, yesLabel, noLabel) { Owner = this };
        LabMotion.SetReduced(dialog, LabMotion.GetReduced(this)); dialog.EnableBackdrop(); ShowCaptureDialog(dialog);
        return dialog.Choice;
    }

    private void UpdateSettingsLayout(double width)
    {
        if (SettingsCardsGrid is null) return;
        var narrow = width < 1100;
        SettingsCardsGrid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 20);
        SettingsCardsGrid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        var cards = new[] { SettingsAppearanceCard, SettingsBehaviorCard, SettingsImportCard, SettingsBackupCard, SettingsUpdatesCard, SettingsDataCard };
        for (var index = 0; index < cards.Length; index++)
        {
            Grid.SetRow(cards[index], narrow ? index : index / 2);
            Grid.SetColumn(cards[index], narrow ? 0 : index % 2 * 2);
        }
    }

    private void RefreshSettingsPresentation()
    {
        SettingsDataPathText.Text = AppPaths.DataDirectory;
        SettingsDataPathText.ToolTip = AppPaths.DataDirectory;
        SettingsVersionText.Text = UiText.Format($"Versão atual: {ProductVersion()}");
        SettingsPilotNoteText.Text = AppPaths.IsCapturePilot ? UiText.Get("Neste piloto, inicialização com o Windows e atualizações ficam desativadas. Suas preferências são preservadas.") : "";
        foreach (var button in new[] { SettingsLightThemeButton, SettingsDarkThemeButton, SettingsSystemThemeButton })
        {
            var selected = (string)button.Tag == _settings.Theme;
            button.SetResourceReference(Button.BorderBrushProperty, selected ? "Lab.accent" : "Lab.line");
            button.SetResourceReference(Button.BackgroundProperty, selected ? "Lab.tint" : "Lab.shell");
            AutomationProperties.SetHelpText(button, selected ? UiText.Get("Tema selecionado") : UiText.Get("Selecionar este tema"));
        }
    }

    private void SettingsTheme_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string theme })
            ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == theme);
    }

    private void SyncSettingsControls()
    {
        var initialized = _initialized;
        _initialized = false;
        try
        {
            CloseToTrayCheckBox.IsChecked = _settings.CloseToTray;
            StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
            ShowSuggestionsCheckBox.IsChecked = _settings.ShowSuggestions;
            CheckUpdatesCheckBox.IsChecked = _settings.CheckUpdatesOnStartup;
            ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == _settings.Theme);
            SelectComboByTag(LanguageBox, UiText.Normalize(_settings.Language));
            RefreshSettingsPresentation();
        }
        finally { _initialized = initialized; }
    }

    private async Task RunSettingsMaintenanceAsync(string message, Func<Task> action, bool cancellable = false)
    {
        if (_settingsOperationInProgress || _shortcutOperationInProgress) throw new InvalidOperationException(UiText.Get("Aguarde a operação em andamento."));
        if (_recordingControl is not null || _gifRecordingSession is not null || _activeUpdateCancellation is not null)
            throw new InvalidOperationException(UiText.Get("Finalize a gravação ou atualização antes da manutenção."));
        using var capture = _captureGate.TryEnter();
        using var expansion = _expansionRequestGate.TryEnter();
        if (capture is null || expansion is null) throw new InvalidOperationException(UiText.Get("Finalize a captura ou expansão em andamento antes de continuar."));
        _captureImageLoadCancellation?.Cancel();
        using var cancellation = cancellable ? new CancellationTokenSource() : null;
        _settingsCancellation = cancellation;
        var progress = cancellation is null ? null : new ExternalOperationWindow(message, cancellation) { Owner = this };
        _settingsProgress = progress;
        var accentEnabled = _quickAccentService.Enabled;
        var root = (FrameworkElement)Content;
        var wasEnabled = root.IsEnabled;
        _settingsOperationInProgress = true;
        _shortcutOperationInProgress = true;
        _quickAccentService.Enabled = false;
        _suggestionWindow.Hide();
        root.IsEnabled = false;
        SettingsOperationText.Text = message;
        StatusText.Text = message;
        try { progress?.Show(); await action(); }
        finally
        {
            progress?.Complete(); _settingsCancellation = null; _settingsProgress = null;
            if (SettingsOperationText.Text == message) SettingsOperationText.Text = UiText.Get("Pronto.");
            root.IsEnabled = wasEnabled;
            _quickAccentService.Enabled = accentEnabled;
            _shortcutOperationInProgress = false;
            _settingsOperationInProgress = false;
            RefreshShortcutDraftState();
        }
    }

    private void OpenSettingsData_OnClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(AppPaths.DataDirectory); Process.Start(new ProcessStartInfo(AppPaths.DataDirectory) { UseShellExecute = true }); }
        catch (Exception exception) { SettingsOperationText.Text = UiText.Get("Não foi possível abrir a pasta: ") + exception.Message; }
    }

    private void OpenSettingsHelp_OnClick(object sender, RoutedEventArgs e)
    {
        _settingsHelpHighlight?.Remove();
        var guide = new ScreenHelpWindow(SettingsHelpContent.Create()) { Owner = this };
        LabMotion.SetReduced(guide, LabMotion.GetReduced(this)); guide.EnableBackdrop(); ShowCaptureDialog(guide);
        if (guide.RequestedTarget is not { } name || FindName(name) is not FrameworkElement target) return;
        target.BringIntoView();
        Dispatcher.BeginInvoke(new Action(() => { if (target.IsVisible) _settingsHelpHighlight = ScreenHelpHighlighter.Show(target); }), DispatcherPriority.Loaded);
    }

    internal void ConfigureSettingsEvidence(AppSettings settings, double width)
    {
        _settings = settings; SyncSettingsControls(); RefreshBackupSummary();
        ShowView(SettingsView, SettingsTabButton); UpdateSettingsLayout(width);
    }
    internal async Task SaveSettingsPreferenceEvidenceAsync()
    { await _settingsStore.SaveAsync(_settings); }
    internal async Task ApplySettingsControlsEvidenceAsync(string theme)
    {
        _initialized = true;
        try
        {
            ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == theme);
            await WaitSettingsEvidenceAsync();
            ShowSuggestionsCheckBox.IsChecked = !ShowSuggestionsCheckBox.IsChecked;
            Settings_OnClick(ShowSuggestionsCheckBox, new RoutedEventArgs());
            await WaitSettingsEvidenceAsync();
        }
        finally { _initialized = false; }
    }
    private async Task WaitSettingsEvidenceAsync()
    {
        for (var index = 0; _settingsOperationInProgress && index < 200; index++) await Task.Delay(10);
        if (_settingsOperationInProgress) throw new TimeoutException("Preference save did not complete");
    }
    internal async Task TestSettingsBusyEvidenceAsync()
    {
        await RunSettingsMaintenanceAsync(UiText.Get("Verificando estado de operação…"), async () =>
        {
            if (((FrameworkElement)Content).IsEnabled || !_settingsOperationInProgress) throw new InvalidOperationException("Maintenance gate missing");
            if (await SetQuickAccentEnabledFromTrayAsync(!_settings.QuickAccentEnabled)) throw new InvalidOperationException("Tray bypassed maintenance gate");
            await Task.Yield();
        });
        try { await RunSettingsMaintenanceAsync(UiText.Get("Falha de teste"), () => throw new IOException("Injected maintenance failure")); }
        catch (IOException) { }
        if (!((FrameworkElement)Content).IsEnabled || _settingsOperationInProgress) throw new InvalidOperationException("Failure left maintenance locked");
        SettingsOperationText.Text = UiText.Get("As preferências são salvas automaticamente.");
        StatusText.Text = UiText.Get("Pronto.");
    }
}
