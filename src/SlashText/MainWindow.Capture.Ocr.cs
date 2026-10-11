using System.Windows;
using SlashText.Design;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private void ExtractCaptureText_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CaptureInlineEditor.HasImage) return;
        if (CaptureInlineEditor.HasPendingCrop)
        { StatusText.Text = UiText.Get("Aplique ou cancele o recorte antes de extrair texto."); return; }
        using var snapshot = CaptureInlineEditor.Render();
        var dialog = new CaptureOcrWindow(snapshot, CaptureInlineEditor.SetOcrReading,
            new Services.CaptureOcrService(options: _settings.Capture.Ocr)) { Owner = this };
        LabMotion.SetReduced(dialog, LabMotion.GetReduced(this));
        try { ShowCaptureDialog(dialog); }
        finally { CaptureInlineEditor.SetOcrReading(false); }
        if (dialog.CopiedText) StatusText.Text = UiText.Get("Texto da imagem copiado.");
    }
}
