using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using SlashText.Design;

namespace SlashText;

public partial class MainWindow
{
    internal void ConfigureLanguageEvidence(string view, double width)
    {
        _settings.Language = UiText.Language;
        SyncSettingsControls();
        switch (view)
        {
            case "Shortcuts": ShowView(ShortcutsView, ShortcutsTabButton); UpdateResponsiveLayout(width); break;
            case "Capture": LoadCaptureSettings(); ShowView(CaptureView, CaptureTabButton); break;
            case "Accent": LoadQuickAccentControls(); ShowView(QuickAccentView, QuickAccentTabButton); UpdateQuickAccentLayout(width); break;
            case "Statistics": RefreshStatistics(); ShowView(StatisticsView, StatisticsTabButton); UpdateStatisticsLayout(width); break;
            case "About": ShowView(AboutView, AboutTabButton); UpdateAboutLayout(width); break;
            default: ShowView(SettingsView, SettingsTabButton); UpdateSettingsLayout(width); break;
        }
    }

    private async void LanguageBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _settingsOperationInProgress || _shortcutOperationInProgress || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        await SaveLanguagePreferenceAsync(language);
    }

    internal async Task<bool> SaveLanguagePreferenceAsync(string language)
    {
        if (_settingsOperationInProgress || _shortcutOperationInProgress) return false;
        var previous = _settings.Language;
        _settingsOperationInProgress = true;
        SettingsCardsGrid.IsEnabled = false;
        try
        {
            _settings.Language = UiText.Normalize(language);
            await _settingsStore.SaveAsync(_settings);
            ShowLanguageRestartNotice(_settings.Language != UiText.Language);
            SettingsOperationText.Text = UiText.Get("Preferência salva.");
            return true;
        }
        catch (Exception exception)
        {
            _settings.Language = previous;
            SyncSettingsControls();
            LanguageRestartText.BeginAnimation(OpacityProperty, null);
            LanguageRestartText.Opacity = 1;
            LanguageRestartText.SetResourceReference(TextBlock.ForegroundProperty, "Lab.error");
            LanguageRestartText.Text = UiText.Get("Não foi possível salvar o idioma. A preferência anterior foi mantida.");
            SettingsOperationText.Text = UiText.Get("Não foi possível salvar a preferência.");
            ShowSettingsDialog(exception.Message, UiText.Get("Não foi possível salvar a configuração"), image: MessageBoxImage.Warning);
            return false;
        }
        finally { SettingsCardsGrid.IsEnabled = true; _settingsOperationInProgress = false; }
    }

    private void ShowLanguageRestartNotice(bool pending)
    {
        // Replace an earlier pulse; never hide the restart instruction completely.
        LanguageRestartText.BeginAnimation(OpacityProperty, null);
        LanguageRestartText.Opacity = 1;
        LanguageRestartText.SetResourceReference(TextBlock.ForegroundProperty, pending ? "Lab.error" : "Lab.muted");
        LanguageRestartText.FontWeight = pending ? FontWeights.SemiBold : FontWeights.Normal;
        LanguageRestartText.Text = UiText.Get(pending
            ? "Idioma salvo. Reabra o SlashDesk para aplicar."
            : "O idioma escolhido será aplicado ao reabrir o SlashDesk.");
        if (!pending || !LabMotion.Allowed(LanguageRestartText)) return;
        LanguageRestartText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, .55, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }
}
