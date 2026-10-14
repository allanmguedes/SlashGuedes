using System.Windows;
using System.Windows.Automation;
using SlashText.Services;

namespace SlashText.Views;

internal static class RestoreRecoveryDialog
{
    internal static SettingsDialogWindow Create(bool blocked)
    {
        var dialog = new SettingsDialogWindow(
            blocked
                ? UiText.Get("O SlashDesk será fechado para impedir alterações em dados cuja restauração não foi confirmada. O material de recuperação disponível será mantido. Alterações não salvas podem ser perdidas. Reabra o app para tentar recuperar os dados anteriores. Não altere nem apague a pasta de backups.")
                : UiText.Get("Uma restauração interrompida foi desfeita antes de carregar o aplicativo. Os dados anteriores foram restabelecidos; o backup de segurança continua disponível."),
            blocked ? UiText.Get("Recuperação pendente") : UiText.Get("Dados anteriores restabelecidos"),
            blocked ? MessageBoxButton.YesNo : MessageBoxButton.OK,
            blocked ? MessageBoxImage.Warning : MessageBoxImage.Information,
            UiText.Get("Abrir pasta de backups"));
        if (blocked)
        {
            var text = UiText.Get("Fechar SlashDesk");
            dialog.DefaultButton.Content = text; AutomationProperties.SetName(dialog.DefaultButton, text);
            dialog.Actions.Last().IsEnabled = System.IO.Directory.Exists(AppPaths.BackupsDirectory);
        }
        return dialog;
    }
}
