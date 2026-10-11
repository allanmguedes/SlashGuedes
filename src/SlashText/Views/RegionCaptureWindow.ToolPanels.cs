using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SlashText.Design;
using SlashText.Services;

namespace SlashText.Views;

public sealed partial class RegionCaptureWindow
{
    private FrameworkElement BuildPilotToolContext(CaptureAnnotationKind tool)
    {
        if (tool == CaptureAnnotationKind.Stamp) return BuildPilotStampContext();
        if (tool is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse or CaptureAnnotationKind.Line or
            CaptureAnnotationKind.Arrow or CaptureAnnotationKind.Number) return BuildPilotShapesContext();
        if (tool is CaptureAnnotationKind.Pencil or CaptureAnnotationKind.Highlighter)
            return CaptureInkPicker.CreateContent(() => _color, color => { _color = color; _outlineArgb = color; },
                () => _thickness, value => _thickness = value, title: tool == CaptureAnnotationKind.Pencil ? UiText.Get("Caneta") : UiText.Get("Marca-texto"),
                compactPalette: true, highlighter: tool == CaptureAnnotationKind.Highlighter,
                getOpacity: () => tool == CaptureAnnotationKind.Highlighter ? Math.Min(_opacity, .38f) : _opacity,
                setOpacity: value => _opacity = value);

        var panel = ContextStack(320);
        if (tool is CaptureAnnotationKind.Blur or CaptureAnnotationKind.Pixelate)
        {
            panel.Children.Add(ContextTitle(tool == CaptureAnnotationKind.Blur ? UiText.Get("Desfocar") : UiText.Get("Pixelizar")));
            panel.Children.Add(LabeledSlider(UiText.Get("Intensidade"), 6, 40, _privacyStrength, value => _privacyStrength = (int)value, ""));
            var hint = ContextTitle(tool == CaptureAnnotationKind.Blur ? UiText.Get("Suaviza a imagem dentro da área desenhada.") : UiText.Get("Transforma a área desenhada em blocos de cor."));
            hint.FontWeight = FontWeights.Normal; hint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(hint); return panel;
        }

        panel.Children.Add(ContextTitle(UiText.Get("Texto")));
        var preview = new TextBlock { Text = UiText.Get("Seu texto aqui"), TextWrapping = TextWrapping.Wrap, MaxHeight = 90,
            Margin = new Thickness(0, 10, 0, 10) };
        void RefreshText()
        {
            preview.FontFamily = new System.Windows.Media.FontFamily(_textFont); preview.FontSize = _annotationSize;
            preview.FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal; preview.Foreground = WpfBrush(_color, _opacity);
        }
        var font = new ComboBox { ItemsSource = new[] { "Segoe UI", "Arial", "Verdana", "Georgia", "Consolas" },
            SelectedItem = _textFont, Margin = new Thickness(0, 6, 0, 6) };
        font.SetResourceReference(StyleProperty, "Lab.Combo"); AutomationProperties.SetName(font, UiText.Get("Fonte da anotação"));
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string value) { _textFont = value; RefreshText(); } };
        panel.Children.Add(font);
        panel.Children.Add(LabeledSlider(UiText.Get("Tamanho"), 12, 64, _annotationSize, value => { _annotationSize = (float)value; RefreshText(); }, " px"));
        var bold = new CheckBox { Content = UiText.Get("Negrito"), IsChecked = _textBold, Margin = new Thickness(0, 6, 0, 6) };
        bold.SetResourceReference(Control.ForegroundProperty, "Lab.text");
        bold.Checked += (_, _) => { _textBold = true; RefreshText(); }; bold.Unchecked += (_, _) => { _textBold = false; RefreshText(); };
        panel.Children.Add(bold);
        panel.Children.Add(CaptureInkPicker.CreateColorContent(() => _color, color => { _color = color; _outlineArgb = color; RefreshText(); }, UiText.Get("Cor"), compactPalette: true));
        panel.Children.Add(LabeledSlider(UiText.Get("Opacidade"), 10, 100, _opacity * 100, value => { _opacity = (float)(value / 100); RefreshText(); }, "%"));
        RefreshText(); panel.Children.Add(preview); return panel;
    }

    private FrameworkElement BuildPilotShapesContext()
    {
        var panel = ContextStack(320); panel.Children.Add(ContextTitle(UiText.Get("Formas")));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 8) };
        foreach (var (label, tool, icon) in new[] {
            (UiText.Get("Retângulo"), CaptureAnnotationKind.Rectangle, "Square"), (UiText.Get("Elipse"), CaptureAnnotationKind.Ellipse, "Circle"),
            (UiText.Get("Linha"), CaptureAnnotationKind.Line, "Minus"), (UiText.Get("Seta"), CaptureAnnotationKind.Arrow, "ArrowUpRight"),
            (UiText.Get("Número"), CaptureAnnotationKind.Number, "Hash") })
        {
            var button = ContextTool(label, tool); button.Content = new LabIcon { Kind = icon, Width = 20, Height = 20 };
            button.ToolTip = label; button.MinWidth = 0; button.Width = 44; button.Height = 40; button.Padding = new Thickness(8);
            row.Children.Add(button);
        }
        panel.Children.Add(row);
        if (_tool == CaptureAnnotationKind.Number)
        {
            panel.Children.Add(CaptureInkPicker.CreateColorContent(() => _color, color => { _color = color; _outlineArgb = color; }, UiText.Get("Cor"), compactPalette: true));
            panel.Children.Add(LabeledSlider(UiText.Get("Tamanho"), 12, 64, _annotationSize, value => _annotationSize = (float)value, " px"));
            panel.Children.Add(ContextAction(UiText.Get("Reiniciar numeração"), () => _nextNumber = 1));
        }
        else
        {
            var colors = new StackPanel { Orientation = Orientation.Horizontal };
            if (_tool is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse)
                colors.Children.Add(ContextAction(UiText.Get("Preenchimento ▾"), () => ShowShapeInk(fill: true)));
            colors.Children.Add(ContextAction(UiText.Get("Contorno ▾"), () => ShowShapeInk(fill: false))); panel.Children.Add(colors);
            panel.Children.Add(LabeledSlider(UiText.Get("Espessura"), 1, 24, _thickness, value => _thickness = (float)value, " px"));
        }
        panel.Children.Add(LabeledSlider(UiText.Get("Opacidade"), 10, 100, _opacity * 100, value => _opacity = (float)(value / 100), "%"));
        return panel;
    }

}
