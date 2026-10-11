using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SlashText.Design;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>Contextual controls for the same image document, not a second editor.</summary>
public sealed class CaptureEditorContextPanel : UserControl
{
    private readonly WrapPanel _panel = new() { Margin = new Thickness(14, 9, 14, 9), VerticalAlignment = VerticalAlignment.Center };
    private CaptureWorkbenchEditor? _editor;
    private string? _shown;
    private Button? _applyCrop;
    public CaptureEditorContextPanel()
    {
        SetResourceReference(BackgroundProperty, "Lab.input");
        SetResourceReference(ForegroundProperty, "Lab.text");
        Content = _panel;
        MinHeight = 52;
    }
    public void Attach(CaptureWorkbenchEditor editor) { _editor = editor; Refresh(); }
    public void Refresh()
    {
        if (_editor is null) return;
        if (_applyCrop is not null) _applyCrop.IsEnabled = _editor.HasPendingCrop;
        var selected = _editor.SelectedAnnotation;
        var shown = selected?.Annotation.Kind.ToString() ?? (_editor.IsCropTool ? "crop" : _editor.SelectedTool?.ToString() ?? "navigate");
        var key = selected is null ? shown : $"selected:{selected.Id}:{_editor.Document?.Revision}";
        if (_shown == key) return;
        _shown = key; _applyCrop = null; _panel.Children.Clear(); Content = _panel;
        if (selected is not null) AddLabel(UiText.Get("Anotação selecionada"), emphasis: true);
        AddLabel(shown switch
        {
            "crop" => UiText.Get("Recortar"), "navigate" => UiText.Get("Navegar"), "Text" => UiText.Get("Texto"), "Stamp" => "Emotes",
            "Blur" => UiText.Get("Desfocar"), "Pixelate" => UiText.Get("Pixelizar"), "Ellipse" => UiText.Get("Elipse"), "Number" => UiText.Get("Número"),
            "Rectangle" => UiText.Get("Retângulo"), "Pencil" => UiText.Get("Caneta"), "Highlighter" => UiText.Get("Marca-texto"), "Line" => UiText.Get("Linha"), _ => UiText.Get("Seta")
        }, emphasis: true);
        if (shown == "Text")
        {
            AddLabel(UiText.Get("Fonte"));
            var preferred = new[] { "Segoe UI", "Arial", "Verdana", "Georgia", "Consolas" };
            var installed = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var fonts = preferred.Where(p => installed.Contains(p, StringComparer.OrdinalIgnoreCase))
                .Concat(installed.Except(preferred, StringComparer.OrdinalIgnoreCase).OrderBy(f => f)).ToArray();
            var fontBox = Combo(fonts, _editor.TextFontFamily, value => _editor.TextFontFamily = value, 155);
            AddLabel(UiText.Get("Tamanho"));
            Combo(new[] { "12", "16", "20", "24", "32", "40", "48", "64", "80", "96", "120" },
                _editor.TextSize.ToString(CultureInfo.InvariantCulture), value => _editor.TextSize = float.Parse(value, CultureInfo.InvariantCulture), 68);
            var input = new TextBox { Text = _editor.AnnotationText, MinWidth = 210, MaxLength = 120, Margin = new Thickness(0, 0, 10, 0) };
            input.SetResourceReference(StyleProperty, "Lab.Field");
            input.FontFamily = new FontFamily(_editor.TextFontFamily);
            fontBox.SelectionChanged += (_, _) => input.FontFamily = new FontFamily(_editor.TextFontFamily);
            input.TextChanged += (_, _) => _editor.AnnotationText = input.Text;
            _panel.Children.Add(input);
            Toggle(UiText.Get("Negrito"), _editor.TextBold, value => _editor.TextBold = value);
            Toggle(UiText.Get("Itálico"), _editor.TextItalic, value => _editor.TextItalic = value);
            Combo(new[] { UiText.Get("Esquerda"), UiText.Get("Centro"), UiText.Get("Direita") }, _editor.TextAlignment switch { "Center" => UiText.Get("Centro"), "Right" => UiText.Get("Direita"), _ => UiText.Get("Esquerda") },
                value => _editor.TextAlignment = value switch { "Centro" => "Center", "Direita" => "Right", _ => "Left" }, 116);
        }
        else if (shown == "Number" && selected is not null)
        {
            AddLabel(UiText.Get("Número"));
            var input = new TextBox { Text = _editor.AnnotationText, Width = 100, Margin = new Thickness(0, 0, 12, 0) };
            input.SetResourceReference(StyleProperty, "Lab.Field");
            input.TextChanged += (_, _) => _editor.AnnotationText = input.Text; _panel.Children.Add(input);
        }
        else if (shown == "Stamp" && selected is not null)
        {
            AddLabel(UiText.Get("Tamanho"));
            Combo(new[] { "32", "48", "64", "96", "128" }, _editor.StampSize.ToString(CultureInfo.InvariantCulture),
                value => _editor.StampSize = float.Parse(value, CultureInfo.InvariantCulture), 90);
            AddButton(UiText.Get("Trocar emoji"), "Smile", () => { if (CaptureEmojiPicker.Show(Window.GetWindow(this)) is { } value) _editor.SelectedStamp = value; });
        }
        else if (shown == "Stamp")
        {
            _panel.Children.Clear();
            var row = new Grid { Margin = new Thickness(14, 9, 14, 9), MinHeight = 48 };
            foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star),
                         GridLength.Auto, GridLength.Auto, GridLength.Auto })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            void Place(UIElement control, int column) { Grid.SetColumn(control, column); row.Children.Add(control); }
            var label = new TextBlock { Text = "Emotes", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Lab.accent-text"); Place(label, 0);
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            var scroller = new ScrollViewer
            {
                Content = strip, Height = 48, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 8, 0)
            };
            var buttons = new Dictionary<string, Button>();
            var quick = NotoEmojiCatalog.QuickItems.AsEnumerable();
            if (!quick.Any(i => i.Value == _editor.SelectedStamp) && CaptureStampCatalog.Current.TryGet(_editor.SelectedStamp, out var current))
                quick = new[] { current }.Concat(quick);
            foreach (var item in quick)
            {
                var button = new Button
                {
                    Content = new Image { Source = CaptureStampCatalog.Current.CreateImageSource(item.Value), Width = 26, Height = 26 },
                    ToolTip = NotoEmojiCatalog.DisplayName(item), Width = 38, Height = 40, MinWidth = 38, MinHeight = 40, Padding = new Thickness(3),
                    Margin = new Thickness(0, 4, 3, 4), VerticalAlignment = VerticalAlignment.Center,
                    Tag = item.Value == _editor.SelectedStamp ? "Selected" : null
                };
                System.Windows.Automation.AutomationProperties.SetName(button, NotoEmojiCatalog.DisplayName(item));
                button.SetResourceReference(StyleProperty, "Lab.Pilot.ToolButton");
                button.Click += (_, _) => { _editor.SelectedStamp = item.Value; foreach (var pair in buttons) pair.Value.Tag = pair.Key == item.Value ? "Selected" : null; };
                buttons[item.Value] = button; strip.Children.Add(button);
            }
            Button NavigationButton(string label, bool forward)
            {
                var icon = new LabIcon { Kind = "ArrowLeft", Width = 16, Height = 16,
                    RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(forward ? 180 : 0) };
                icon.SetResourceReference(LabIcon.ForegroundProperty, "Lab.text");
                var button = new Button { Content = icon, ToolTip = label, Width = 38, Height = 40,
                    Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
                button.SetResourceReference(StyleProperty, "Lab.Pilot.ToolButton");
                System.Windows.Automation.AutomationProperties.SetName(button, label);
                button.Click += (_, _) => scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + (forward ? 328 : -328));
                return button;
            }
            var previous = NavigationButton(UiText.Get("Emotes anteriores"), false);
            var next = NavigationButton(UiText.Get("Próximos emotes"), true);
            scroller.ScrollChanged += (_, _) => { previous.IsEnabled = scroller.HorizontalOffset > 0; next.IsEnabled = scroller.HorizontalOffset < scroller.ScrollableWidth; };
            Place(previous, 1); Place(scroller, 2); Place(next, 3);
            var all = MakeButton(UiText.Get("Ver todos"), "Smile", () =>
            {
                var selected = CaptureEmojiPicker.Show(Window.GetWindow(this));
                if (selected is null) return;
                _editor.SelectedStamp = selected;
                _shown = null; Refresh();
            });
            all.VerticalAlignment = VerticalAlignment.Center; Place(all, 4);
            var size = new ComboBox { ItemsSource = new[] { "32", "48", "64", "96", "128" },
                SelectedItem = _editor.StampSize.ToString(CultureInfo.InvariantCulture), Width = 90,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = UiText.Get("Tamanho do emote (px)") };
            size.SetResourceReference(StyleProperty, "Lab.Combo");
            System.Windows.Automation.AutomationProperties.SetName(size, UiText.Get("Tamanho do emote em pixels"));
            size.SelectionChanged += (_, _) => { if (size.SelectedItem is string value) _editor.StampSize = float.Parse(value, CultureInfo.InvariantCulture); };
            Place(size, 5); Content = row;
            LabMotion.SetEntrance(row, "Page");
            if (row.IsLoaded) LabMotion.PlayEntrance(row);
        }
        else if (shown == "crop")
        {
            AddLabel(UiText.Get("Arraste a área e confirme."));
            AddButton(UiText.Get("Cancelar seleção"), "X", () => _editor.CancelCrop());
            _applyCrop = AddButton(UiText.Get("Aplicar recorte"), "Check", () =>
            {
                if (!_editor.ApplyCrop()) AuxiliaryWindowTheme.ShowMessage(Window.GetWindow(this), UiText.Get("Selecione ao menos 8 × 8 pixels."), UiText.Get("Recorte"), MessageBoxButton.OK, MessageBoxImage.Information);
            }, primary: true);
            _applyCrop.IsEnabled = _editor.HasPendingCrop;
        }
        else if (shown is "Blur" or "Pixelate")
        {
            AddLabel(UiText.Get("Intensidade"));
            var value = new TextBlock { Text = _editor.PrivacyStrength.ToString(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 16, 0) };
            var slider = new Slider { Minimum = 6, Maximum = 40, Value = _editor.PrivacyStrength, Width = 140, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, _) => { _editor.PrivacyStrength = (int)slider.Value; value.Text = _editor.PrivacyStrength.ToString(); };
            _panel.Children.Add(slider); _panel.Children.Add(value);
            AddLabel(UiText.Get("Efeito real na prévia e no arquivo. Confira antes de compartilhar."));
        }
        else if (shown is "Rectangle" or "Ellipse")
        {
            Toggle(UiText.Get("Contorno"), _editor.ShapeOutline, value => _editor.ShapeOutline = value);
            Toggle(UiText.Get("Preenchimento"), _editor.ShapeFill.HasValue, value => _editor.ShapeFill = value ? _editor.InkArgb : null);
            AddLabel(UiText.Get("Opacidade"));
            Combo(new[] { "25%", "50%", "75%", "100%" }, ((int)(_editor.AnnotationOpacity * 100)) + "%", value => _editor.AnnotationOpacity = int.Parse(value.TrimEnd('%')) / 100f, 80);
        }
        else AddLabel(shown == "navigate" ? UiText.Get("Clique em uma anotação para editar · arraste fora para navegar · Ctrl+roda para zoom") : selected is not null ? UiText.Get("Arraste para mover · setas ajustam · Delete exclui") : UiText.Get("Arraste na imagem para aplicar a ferramenta"));
        if (selected is not null)
        {
            if (shown is not ("Rectangle" or "Ellipse" or "Blur" or "Pixelate"))
            {
                AddLabel(UiText.Get("Opacidade"));
                Combo(new[] { "25%", "50%", "75%", "100%" }, ((int)(_editor.AnnotationOpacity * 100)) + "%",
                    value => _editor.AnnotationOpacity = int.Parse(value.TrimEnd('%')) / 100f, 80);
            }
            TextBox? width = null, height = null;
            if (selected.Annotation.Kind is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse or CaptureAnnotationKind.Blur or CaptureAnnotationKind.Pixelate)
            {
                AddLabel(UiText.Get("Largura/altura (px)"));
                width = DimensionField(Math.Abs(selected.Annotation.End.X - selected.Annotation.Start.X));
                height = DimensionField(Math.Abs(selected.Annotation.End.Y - selected.Annotation.Start.Y));
            }
            AddButton(UiText.Get("Aplicar alterações"), "Check", () =>
            {
                double? w = null, h = null;
                if (width is not null && height is not null)
                {
                    if (!double.TryParse(width.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedWidth) ||
                        !double.TryParse(height.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedHeight) ||
                        parsedWidth < 1 || parsedHeight < 1 || parsedWidth > 32000 || parsedHeight > 32000)
                    { width.ToolTip = height.ToolTip = UiText.Get("Use um valor entre 1 e 32.000 pixels."); width.Focus(); return; }
                    w = parsedWidth; h = parsedHeight;
                }
                _editor.ApplySelectedProperties(w, h);
            }, primary: true);
            AddButton(UiText.Get("Excluir anotação"), "Trash2", () => _editor.DeleteSelectedAnnotation());
        }
        LabMotion.SetEntrance(_panel, "Page");
        if (_panel.IsLoaded) LabMotion.PlayEntrance(_panel);
    }

    public void ShowMoreTools(Button anchor)
    {
        if (_editor is null || !_editor.HasImage) return;
        var panel = new StackPanel();
        var border = new Border { Padding = new Thickness(7), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = panel };
        border.SetResourceReference(Border.BackgroundProperty, "Lab.raised");
        border.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = border, VerticalOffset = 6 };
        LabMotion.SetReduced(border, LabMotion.GetReduced(this));
        border.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
        popup.Closed += (_, _) => anchor.Focus();
        popup.Opened += (_, _) => { LabMotion.PlayEntrance(border); if (panel.Children.OfType<Button>().FirstOrDefault() is { } first) first.Focus(); };
        foreach (var (label, icon, tool) in new[]
        {
            (UiText.Get("Elipse"), "Circle", CaptureAnnotationKind.Ellipse), (UiText.Get("Linha"), "Minus", CaptureAnnotationKind.Line),
            (UiText.Get("Número"), "Hash", CaptureAnnotationKind.Number), (UiText.Get("Desfocar"), "ScanLine", CaptureAnnotationKind.Blur),
            (UiText.Get("Pixelizar"), "Grid2X2", CaptureAnnotationKind.Pixelate)
        })
        {
            var button = MakeButton(label, icon, () => { popup.IsOpen = false; _editor.SelectTool(tool); });
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.MinWidth = 180; panel.Children.Add(button);
        }
        panel.Children.Add(MakeButton(UiText.Get("Recortar"), "Crop", () => { popup.IsOpen = false; _editor.SelectCrop(); }));
        panel.Children.Add(MakeButton(UiText.Get("Redimensionar"), "Scaling", () => { popup.IsOpen = false; ShowResize(); }));
        LabMotion.SetEntrance(border, "Popup"); popup.IsOpen = true;
    }

    public void ShowResize()
    {
        if (_editor?.Document is not { } document) return;
        var size = document.Dimensions;
        var dialog = new Window { Title = UiText.Get("Redimensionar imagem"), Owner = Window.GetWindow(this), Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(Window.BackgroundProperty, "Lab.raised"); dialog.SetResourceReference(Window.ForegroundProperty, "Lab.text");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = UiText.Get("Redimensionar imagem"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        var width = new TextBox { Text = size.Width.ToString(), Margin = new Thickness(0, 6, 0, 12) };
        var height = new TextBox { Text = size.Height.ToString(), Margin = new Thickness(0, 6, 0, 12) };
        width.SetResourceReference(StyleProperty, "Lab.Field"); height.SetResourceReference(StyleProperty, "Lab.Field");
        panel.Children.Add(new TextBlock { Text = UiText.Get("Largura (px)"), Margin = new Thickness(0, 18, 0, 0) }); panel.Children.Add(width);
        panel.Children.Add(new TextBlock { Text = UiText.Get("Altura (px)") }); panel.Children.Add(height);
        var ratio = new CheckBox { Content = UiText.Get("Manter proporção"), IsChecked = true, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(ratio);
        bool syncing = false;
        width.TextChanged += (_, _) => { if (syncing || ratio.IsChecked != true || !int.TryParse(width.Text, out var w)) return; syncing = true; height.Text = Math.Max(1, Math.Round((double)w * size.Height / size.Width)).ToString(); syncing = false; };
        height.TextChanged += (_, _) => { if (syncing || ratio.IsChecked != true || !int.TryParse(height.Text, out var h)) return; syncing = true; width.Text = Math.Max(1, Math.Round((double)h * size.Width / size.Height)).ToString(); syncing = false; };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; error.SetResourceReference(TextBlock.ForegroundProperty, "Lab.error"); panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton(UiText.Get("Cancelar"), "X", () => dialog.DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        actions.Children.Add(MakeButton(UiText.Get("Aplicar tamanho"), "Check", () =>
        {
            if (!int.TryParse(width.Text, out var w) || !int.TryParse(height.Text, out var h) || !CaptureEditorDocument.ValidSize(w, h)) { error.Text = UiText.Get("Use 8–32.000 px por lado, até 64 megapixels."); return; }
            _editor.ResizeImage(w, h); dialog.DialogResult = true;
        }, primary: true));
        panel.Children.Add(actions); dialog.Content = panel; LabMotion.SetEntrance(panel, "Popup");
        dialog.SourceInitialized += (_, _) => ThemeService.ApplyToWindow(dialog); dialog.ShowDialog();
    }

    private void AddLabel(string text, bool emphasis = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, emphasis ? 18 : 8, 0), FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, emphasis ? "Lab.accent-text" : "Lab.muted"); _panel.Children.Add(label);
    }
    private TextBox DimensionField(double value)
    {
        var field = new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture), Width = 70, Margin = new Thickness(0, 0, 10, 0), ToolTip = UiText.Get("Dimensão em pixels (1–32.000)") };
        field.SetResourceReference(StyleProperty, "Lab.Field"); _panel.Children.Add(field); return field;
    }
    private ComboBox Combo(IEnumerable<string> choices, string selected, Action<string> change, double width)
    {
        var combo = new ComboBox { ItemsSource = choices.Append(selected).Distinct().ToArray(), SelectedItem = selected, Width = width, Margin = new Thickness(0, 0, 12, 0) };
        combo.SetResourceReference(StyleProperty, "Lab.Combo"); combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) change(value); }; _panel.Children.Add(combo);
        return combo;
    }
    private void Toggle(string text, bool state, Action<bool> change)
    {
        var control = new CheckBox { Content = text, IsChecked = state, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        control.SetResourceReference(Control.ForegroundProperty, "Lab.text");
        control.Checked += (_, _) => change(true); control.Unchecked += (_, _) => change(false); _panel.Children.Add(control);
    }
    private Button AddButton(string text, string icon, Action click, bool primary = false) { var button = MakeButton(text, icon, click, primary); _panel.Children.Add(button); return button; }
    private static Button MakeButton(string text, string icon, Action click, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new LabIcon { Kind = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        content.Children.Add(label);
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        button.SetResourceReference(StyleProperty, primary ? "Lab.Pilot.PrimaryButton" : "Lab.Button"); button.Click += (_, _) => click(); return button;
    }
}
