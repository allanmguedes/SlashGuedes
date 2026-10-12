using System.Windows;
using SlashText.Models;

namespace SlashText;

public partial class MainWindow
{
    private bool TryAuthorizeHistoryFile(CaptureRecord record, out string path)
    {
        path = _captureService.ResolveFilePath(record);
        if (!string.IsNullOrEmpty(path)) return true;
        var candidate = record.RequiresFileApproval ? _captureService.GetFileApprovalPath(record) : string.Empty;
        if (candidate.Length > 0)
        {
            if (ShowAppDialog(
                UiText.Format($"Este registro é antigo ou veio de um backup. Confirme o arquivo antes de permitir acesso nesta sessão:\n\n{candidate}\n\nAutorizar este arquivo?"),
                UiText.Get("Confirmar arquivo do histórico"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            if (_captureService.ApproveFileAccess(record, candidate))
            {
                path = _captureService.ResolveFilePath(record);
                if (path.Length > 0) return true;
            }
        }
        ShowAppDialog(UiText.Get("O arquivo não está disponível ou foi bloqueado. O histórico aceita PNG, JPEG, GIF e MP4 locais, sem links ou redirecionamentos. Você pode remover somente a entrada do histórico."),
            UiText.Get("Histórico"), MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
}
