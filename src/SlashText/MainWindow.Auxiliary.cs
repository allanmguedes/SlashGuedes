using System.Windows;
using SlashText.Design;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private Action<string>? _captureNotificationEvidenceLauncher;

    internal void ConfigureNotificationEvidence(Action<string> launcher) => _captureNotificationEvidenceLauncher = launcher;
    internal void NotifyCaptureEvidence(string? path) => ShowTrayBalloon(1, "Teste", "Teste", System.Windows.Forms.ToolTipIcon.Info, path);
    internal void OpenNotificationEvidence() => OpenPendingCaptureNotification();
    private MessageBoxResult ShowAppDialog(string message, string title = "SlashDesk",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dialog = new SettingsDialogWindow(message, title, buttons, image,
            yesLabel: buttons == MessageBoxButton.YesNoCancel ? UiText.Get("Excluir arquivo") : UiText.Get("Continuar"),
            noLabel: UiText.Get("Remover do histórico"));
        LabMotion.SetReduced(dialog, LabMotion.GetReduced(this));
        if (IsVisible)
        {
            dialog.Owner = this; dialog.EnableBackdrop(); ShowCaptureDialog(dialog);
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowDialog();
        }
        return dialog.Choice;
    }
}
