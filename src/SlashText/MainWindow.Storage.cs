using System.IO;
using System.Windows;
using SlashText.Models;
using SlashText.Services;

namespace SlashText;

public partial class MainWindow
{
    private Task SavePreferencesAsync() => SavePreferencesWithCancellationAsync(CancellationToken.None);

    private async Task SavePreferencesWithCancellationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_settingsStore.IsProtected)
        {
            RefreshStorageProtection();
            StatusText.Text = UiText.Get("Preferências protegidas: ajustes desta sessão não serão salvos.");
            return;
        }
        await _settingsStore.SaveAsync(_settings, cancellationToken);
    }

    private void RefreshStorageProtection()
    {
        if (StorageProtectionPanel is null) return;
        var settings = _settingsStore.IsProtected;
        var history = _captureService.IsHistoryProtected;
        var visible = settings || history;
        StorageProtectionPanel.Visibility = StorageProtectionHeaderButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        StorageProtectionHeaderButton.Content = UiText.Get("Dados protegidos · Revisar");
        StorageProtectionTitle.Text = UiText.Get("O original está protegido");
        var messages = new List<string>();
        if (settings) messages.Add(UiText.Get("Não foi possível ler todas as preferências. Ajustes desta sessão não serão salvos."));
        if (history) messages.Add(UiText.Get("O histórico está disponível somente para consulta. Limpeza e remoção estão suspensas. Novas capturas podem ser salvas, mas não entram no histórico até a recuperação."));
        var preserved = (!settings || _settingsStore.CanRecover) && (!history || _captureService.CanRecoverHistory);
        if (visible) messages.Add(UiText.Get(preserved
            ? "Uma cópia do original foi verificada. Você pode tentar ler novamente, restaurar um ZIP válido ou confirmar uma recuperação abaixo."
            : "Não foi possível verificar uma cópia do original. A recuperação continua bloqueada. Verifique a pasta de dados e tente ler novamente; você também pode restaurar um ZIP válido."));
        StorageProtectionDescription.Text = string.Join("\n\n", messages);
        StorageRetryButton.Content = UiText.Get("Tentar ler novamente");
        StorageRecoverHistoryButton.Content = UiText.Get("Recuperar histórico legível");
        StorageRecoverSettingsButton.Content = UiText.Get("Redefinir preferências");
        StorageOpenFolderButton.Content = UiText.Get("Abrir pasta de dados");
        StorageRecoverHistoryButton.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        StorageRecoverSettingsButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        StorageRecoverHistoryButton.IsEnabled = _captureService.CanRecoverHistory;
        StorageRecoverSettingsButton.IsEnabled = _settingsStore.CanRecover;
        SettingsAppearanceCard.IsEnabled = SettingsBehaviorCard.IsEnabled = QuickAccentPage.IsEnabled = !settings;
        CheckUpdatesCheckBox.IsEnabled = !settings && !AppPaths.IsCapturePilot;
        if (settings) StopUpdateMonitor();
    }

    private void ReviewStorage_OnClick(object sender, RoutedEventArgs e)
    {
        ShowView(SettingsView, SettingsTabButton);
        StorageProtectionPanel.BringIntoView();
        StorageRetryButton.Focus();
    }

    private async Task RunStorageRecoveryAsync(Func<Task> action)
    {
        try
        {
            await RunSettingsMaintenanceAsync(UiText.Get("Verificando os dados locais…"), action);
            SyncSettingsControls();
            LoadCaptureSettings();
            LoadQuickAccentControls();
            ApplyQuickAccentSettings();
            RefreshCaptureHistory();
            RefreshStatistics();
            RefreshStorageProtection();
            SettingsOperationText.Text = UiText.Get("Leitura concluída. Confira o estado dos dados abaixo. Preferências recuperadas serão aplicadas por completo na próxima abertura.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshStorageProtection();
            ShowSettingsDialog(UiText.Get("Não foi possível concluir a recuperação. O original continua preservado. Verifique a pasta de dados e tente ler novamente."),
                UiText.Get("Dados protegidos"), image: MessageBoxImage.Warning);
        }
    }

    private async void RetryStorage_OnClick(object sender, RoutedEventArgs e) => await RunStorageRecoveryAsync(async () =>
    {
        if (_settingsStore.IsProtected) _settings = await _settingsStore.LoadAsync();
        if (_captureService.IsHistoryProtected) await _captureService.LoadAsync();
    });

    private async void RecoverHistory_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_captureService.IsHistoryProtected || !_captureService.CanRecoverHistory) return;
        if (ShowSettingsDialog(UiText.Get("Criar um histórico válido usando apenas as entradas legíveis? Entradas incompatíveis não entrarão nessa lista. O arquivo original completo continuará guardado na pasta de dados."),
            UiText.Get("Recuperar histórico"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
            "Recuperar", "Cancelar") != MessageBoxResult.Yes) return;
        await RunStorageRecoveryAsync(_captureService.RecoverHistoryAsync);
    }

    private async void RecoverSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_settingsStore.IsProtected || !_settingsStore.CanRecover) return;
        if (ShowSettingsDialog(UiText.Get("Redefinir as preferências para os valores padrão? Seus atalhos, estatísticas e arquivos de captura não serão alterados. O arquivo original completo continuará guardado na pasta de dados."),
            UiText.Get("Redefinir preferências"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
            "Redefinir", "Cancelar") != MessageBoxResult.Yes) return;
        await RunStorageRecoveryAsync(async () =>
        {
            var defaults = new AppSettings { OnboardingCompleted = true };
            if (AppPaths.IsCapturePilot)
            {
                defaults.Capture.OutputDirectoryTemplate = Path.Combine(AppPaths.DataDirectory, "Captures");
            }
            await _settingsStore.RecoverAsync(defaults);
            _settings = await _settingsStore.LoadAsync();
        });
    }

    internal async Task LoadStorageProtectionEvidenceAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        await _captureService.LoadAsync();
        ConfigureSettingsEvidence(_settings, Width);
    }
    internal Task RecoverStorageHistoryEvidenceAsync() => _captureService.RecoverHistoryAsync();
}
