using System.Windows;
using SlashText.Services;

namespace SlashText;

public partial class MainWindow
{
    private void LoadCompletedRegionSession(CaptureRegionSession session)
    {
        CaptureInlineEditor.LoadSession(session);
        _captureWorkbenchPath = null; _captureWorkbenchRecord = null;
        _captureMediaMode = CaptureMediaMode.Image; CaptureImageMediaButton.IsChecked = true;
        CapturePreviewEmptyPanel.Visibility = Visibility.Collapsed;
        CaptureWorkbenchToolbar.Visibility = CaptureWorkbenchFooter.Visibility = CaptureEditorContext.Visibility = Visibility.Visible;
        CapturePreviewDetailsText.Text = UiText.Get("Região · textos e emojis continuam editáveis nesta sessão");
        UpdateCaptureLauncherSummary();
        _completedCaptureDocument = CaptureInlineEditor.Document;
    }

    internal void AcceptRegionSessionForEvidence(CaptureRegionSession session, SlashText.Models.CaptureRecord record)
    { LoadCompletedRegionSession(session); AcceptCompletedCapture(record); }
}
