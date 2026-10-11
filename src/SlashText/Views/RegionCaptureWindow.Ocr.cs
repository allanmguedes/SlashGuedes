using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SlashText.Design;

namespace SlashText.Views;

public sealed partial class RegionCaptureWindow
{
    private readonly CaptureOcrPulse _ocrPulse = new();
    public Models.CaptureOcrSettings OcrSettings { get; set; } = new();
    private Button OcrButton()
    {
        var button = new Button { Content = new LabIcon { Kind = "ScanText", Width = 20, Height = 20 }, ToolTip = UiText.Get("Extrair texto da região (OCR)") };
        button.SetResourceReference(StyleProperty, _pilotVisuals ? "Lab.Pilot.ToolButton" : "CaptureToolbarIconButton");
        AutomationProperties.SetName(button, UiText.Get("Extrair texto da região")); button.Click += (_, _) => ExtractRegionText(); return button;
    }
    private void ExtractRegionText()
    {
        if (!_selectionReady || _drawing || _dragging || _objectDragStart.HasValue) return;
        // Moving keyboard focus commits the existing inline text through its own handler.
        Focus(); HideContextWindow(reactivateOverlay: false);
        // Recognize exactly the composed, visible region, never the original under privacy effects.
        using var crop = CropFrozenSelection();
        using var snapshot = _pilotVisuals ? RenderSelection() : Services.CaptureAnnotationRenderer.Render(
            crop, _annotationHistory.Items, _localSelection.Width, _localSelection.Height);
        _ocrPulse.Width = _localSelection.Width; _ocrPulse.Height = _localSelection.Height;
        Canvas.SetLeft(_ocrPulse, _localSelection.Left); Canvas.SetTop(_ocrPulse, _localSelection.Top);
        if (!_canvas.Children.Contains(_ocrPulse)) _canvas.Children.Add(_ocrPulse);
        LabMotion.SetReduced(_ocrPulse, LabMotion.GetReduced(this)); _toolbarWindow.Hide();
        var dialog = new CaptureOcrWindow(snapshot, busy => { if (busy) _ocrPulse.Start(); else _ocrPulse.Stop(); },
            new Services.CaptureOcrService(options: OcrSettings)) { Owner = this, Topmost = true };
        LabMotion.SetReduced(dialog, LabMotion.GetReduced(this));
        try { dialog.ShowDialog(); }
        finally { _ocrPulse.Stop(); if (_selectionReady) { _toolbarWindow.Show(); Activate(); RequestToolbarPosition(); } }
    }
}
