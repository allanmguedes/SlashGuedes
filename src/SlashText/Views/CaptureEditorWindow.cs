using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SlashText.Design;
using SlashText.Services;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using WpfPoint = System.Windows.Point;

namespace SlashText.Views;

public enum CaptureEditorOutput
{
    Default,
    Clipboard,
    File
}

public sealed class CaptureEditorWindow : Window
{
    private readonly DrawingBitmap _source;
    private readonly Canvas _overlay = new();
    private readonly List<CaptureAnnotation> _annotations = [];
    private readonly Stack<CaptureAnnotation> _redo = new();
    private readonly Dictionary<CaptureAnnotationKind, Button> _toolButtons = [];
    private readonly double _previewWidth;
    private readonly double _previewHeight;
    private CaptureAnnotationKind _tool = CaptureAnnotationKind.Arrow;
    private int _color = DrawingColor.Red.ToArgb();
    private float _thickness = 4;
    private WpfPoint _start;
    private bool _drawing;
    private readonly List<WpfPoint> _pencilPoints = [];
    private int _nextNumber = 1;
    private Rect? _cropRect;
    private int? _resizeWidth;
    private bool _cropSelecting;

    public DrawingBitmap? EditedBitmap { get; private set; }
    public CaptureEditorOutput RequestedOutput { get; private set; }

    public CaptureEditorWindow(
        DrawingBitmap source,
        CaptureAnnotationKind initialTool = CaptureAnnotationKind.Arrow)
    {
        _source = source;
        _tool = initialTool;
        Title = UiText.Get("Editar captura");
        Width = 1220;
        Height = 860;
        MinWidth = 900;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Lab.shell");
        SetResourceReference(ForegroundProperty, "Lab.text");
        SetResourceReference(FontFamilyProperty, "Lab.Font");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);

        var scale = Math.Min(1d, Math.Min(1080d / source.Width, 620d / source.Height));
        _previewWidth = Math.Max(1, source.Width * scale);
        _previewHeight = Math.Max(1, source.Height * scale);

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var title = new TextBlock { Text = UiText.Get("Editor de captura"), FontSize = 24, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        heading.Children.Add(title);
        var description = new TextBlock { Text = UiText.Get("Mais espaço para sua edição · Recorte, redimensione e proteja informações."),
            Margin = new Thickness(0, 6, 0, 0) };
        description.SetResourceReference(StyleProperty, "Lab.Metadata");
        heading.Children.Add(description);
        root.Children.Add(heading);
        var toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 1);
        LabMotion.SetEntrance(toolbar, "Page");
        root.Children.Add(toolbar);
        UpdateToolSelection();

        var surface = new Grid
        {
            Width = _previewWidth,
            Height = _previewHeight,
            Background = Brushes.Black,
            ClipToBounds = true
        };
        surface.Children.Add(new Image
        {
            Source = ToBitmapSource(source),
            Width = _previewWidth,
            Height = _previewHeight,
            Stretch = Stretch.Fill
        });
        _overlay.Width = _previewWidth;
        _overlay.Height = _previewHeight;
        _overlay.Background = Brushes.Transparent;
        _overlay.Cursor = Cursors.Cross;
        _overlay.MouseLeftButtonDown += OverlayOnMouseLeftButtonDown;
        _overlay.MouseMove += OverlayOnMouseMove;
        _overlay.MouseLeftButtonUp += OverlayOnMouseLeftButtonUp;
        surface.Children.Add(_overlay);

        var viewer = new ScrollViewer
        {
            Content = surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18)
        };
        viewer.SetResourceReference(BackgroundProperty, "Lab.canvas");
        var workspace = new Border { Child = viewer, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), ClipToBounds = true };
        workspace.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        Grid.SetRow(workspace, 3);
        root.Children.Add(workspace);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = new TextBlock
        {
            Text = UiText.Get("Arraste para desenhar · Ctrl+Z desfaz · Esc cancela"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        hint.SetResourceReference(StyleProperty, "Lab.Metadata");
        footer.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton(UiText.Get("Cancelar"), (_, _) => DialogResult = false));
        actions.Children.Add(ActionButton(UiText.Get("Copiar"), (_, _) => Complete(CaptureEditorOutput.Clipboard)));
        actions.Children.Add(ActionButton(UiText.Get("Salvar"), (_, _) => Complete(CaptureEditorOutput.File)));
        var finish = ActionButton(UiText.Get("Concluir"), (_, _) => Complete(CaptureEditorOutput.Default));
        finish.SetResourceReference(StyleProperty, "Lab.Pilot.PrimaryButton");
        actions.Children.Add(finish);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 5);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private FrameworkElement BuildToolbar()
    {
        var panel = new WrapPanel();
        panel.Children.Add(ToolButton(UiText.Get("Seta"), CaptureAnnotationKind.Arrow));
        panel.Children.Add(ToolButton(UiText.Get("Marca-texto"), CaptureAnnotationKind.Highlighter));
        panel.Children.Add(ToolButton(UiText.Get("Retângulo"), CaptureAnnotationKind.Rectangle));
        panel.Children.Add(ToolButton(UiText.Get("Elipse"), CaptureAnnotationKind.Ellipse));
        panel.Children.Add(ToolButton(UiText.Get("Lápis"), CaptureAnnotationKind.Pencil));
        panel.Children.Add(ToolButton(UiText.Get("Texto"), CaptureAnnotationKind.Text));
        panel.Children.Add(ToolButton(UiText.Get("Número"), CaptureAnnotationKind.Number));
        panel.Children.Add(ToolButton("Emoji", CaptureAnnotationKind.Stamp));
        panel.Children.Add(ToolButton(UiText.Get("Desfocar"), CaptureAnnotationKind.Blur));
        panel.Children.Add(ToolButton(UiText.Get("Pixelizar"), CaptureAnnotationKind.Pixelate));
        panel.Children.Add(ActionButton(UiText.Get("Recortar"), (_, _) =>
        {
            _cropRect = null;
            _cropSelecting = true;
            _tool = CaptureAnnotationKind.Rectangle;
            _overlay.Cursor = Cursors.Cross;
            UpdateToolSelection();
            _overlay.ToolTip = UiText.Get("Arraste para selecionar o recorte; Concluir aplica a seleção");
        }));
        panel.Children.Add(ActionButton(UiText.Get("Redimensionar"), (_, _) => ConfigureResize()));
        panel.Children.Add(ActionButton(UiText.Get("Desfazer"), (_, _) => Undo()));
        panel.Children.Add(ActionButton(UiText.Get("Refazer"), (_, _) => Redo()));

        panel.Children.Add(new TextBlock
        {
            Text = UiText.Get("  Cor"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 6),
            Foreground = (Brush)Application.Current.FindResource("Lab.muted")
        });
        foreach (var color in new[]
                 {
                     DrawingColor.Red,
                     DrawingColor.Gold,
                     DrawingColor.DeepSkyBlue,
                     DrawingColor.LimeGreen,
                     DrawingColor.White,
                     DrawingColor.Black
                 })
        {
            var choice = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(
                    Color.FromArgb(color.A, color.R, color.G, color.B)),
                BorderBrush = (Brush)Application.Current.FindResource("Lab.line"),
                BorderThickness = new Thickness(2),
                ToolTip = color.Name,
                Tag = color.ToArgb()
            };
            choice.SetResourceReference(StyleProperty, "Lab.Button");
            choice.MinHeight = 28;
            choice.Click += (_, _) => _color = (int)choice.Tag;
            panel.Children.Add(choice);
        }

        var thickness = new ComboBox
        {
            Width = 86,
            Margin = new Thickness(6, 0, 0, 6),
            ToolTip = UiText.Get("Espessura"),
            SelectedIndex = 1
        };
        foreach (var value in new[] { 2f, 4f, 8f, 12f })
        {
            thickness.Items.Add(new ComboBoxItem
            {
                Content = $"{value:0} px",
                Tag = value
            });
        }
        thickness.SelectionChanged += (_, _) =>
        {
            if (thickness.SelectedItem is ComboBoxItem { Tag: float value })
            {
                _thickness = value;
            }
        };
        thickness.SetResourceReference(StyleProperty, "Lab.Combo");
        panel.Children.Add(thickness);
        return new Border
        {
            Style = (Style)Application.Current.FindResource("Lab.Card"),
            Padding = new Thickness(12, 12, 6, 6),
            Child = panel
        };
    }

    private Button ToolButton(string text, CaptureAnnotationKind tool)
    {
        var button = ActionButton(text, (_, _) =>
        {
            _cropSelecting = false;
            _tool = tool;
            _overlay.Cursor = tool == CaptureAnnotationKind.Text
                ? Cursors.IBeam
                : Cursors.Cross;
            UpdateToolSelection();
        });
        button.ToolTip = UiText.Format($"Ferramenta {text}");
        button.SetResourceReference(StyleProperty, "Lab.Pilot.EditorTool");
        _toolButtons[tool] = button;
        return button;
    }

    private void UpdateToolSelection()
    {
        foreach (var (tool, button) in _toolButtons)
        {
            var selected = tool == _tool;
            button.Tag = selected ? "Selected" : null;
        }
    }

    private static Button ActionButton(string text, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 7, 6),
            Padding = new Thickness(12, 5, 12, 5)
        };
        button.SetResourceReference(StyleProperty, "Lab.Button");
        button.Click += handler;
        return button;
    }

    private void OverlayOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = Clamp(e.GetPosition(_overlay));
        if (_tool == CaptureAnnotationKind.Stamp)
        {
            var value = CaptureEmojiPicker.Show(this);
            if (value is not null) Add(new CaptureAnnotation { Kind = CaptureAnnotationKind.Stamp,
                Start = _start, End = _start, Text = value, StampImage = CaptureStampCatalog.Current.GetImage(value), Size = 42 });
            return;
        }
        if (_tool == CaptureAnnotationKind.Text)
        {
            var text = PromptForText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                Add(new CaptureAnnotation
                {
                    Kind = CaptureAnnotationKind.Text,
                    Start = _start,
                    End = _start,
                    Text = text,
                    Argb = _color,
                    Thickness = _thickness
                });
            }
            return;
        }
        if (_tool == CaptureAnnotationKind.Number)
        {
            Add(new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Number,
                Start = _start,
                End = _start,
                Text = (_nextNumber++).ToString(),
                Argb = _color,
                Thickness = _thickness
            });
            return;
        }

        _drawing = true;
        _pencilPoints.Clear();
        _pencilPoints.Add(_start);
        _overlay.CaptureMouse();
    }

    private void OverlayOnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing)
        {
            return;
        }
        var end = Clamp(e.GetPosition(_overlay));
        if (_tool == CaptureAnnotationKind.Pencil)
        {
            _pencilPoints.Add(end);
        }
        Rebuild(new CaptureAnnotation
        {
            Kind = _tool,
            Start = _start,
            End = end,
            Points = [.. _pencilPoints],
            Argb = _color,
            Thickness = _thickness
        });
    }

    private void OverlayOnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing)
        {
            return;
        }
        _drawing = false;
        _overlay.ReleaseMouseCapture();
        var end = Clamp(e.GetPosition(_overlay));
        if (_tool == CaptureAnnotationKind.Pencil)
        {
            _pencilPoints.Add(end);
        }
        if (_cropSelecting)
        {
            _cropRect = new Rect(
                Math.Min(_start.X, end.X),
                Math.Min(_start.Y, end.Y),
                Math.Abs(end.X - _start.X),
                Math.Abs(end.Y - _start.Y));
            _cropSelecting = false;
            Rebuild();
            return;
        }
        Add(new CaptureAnnotation
        {
            Kind = _tool,
            Start = _start,
            End = end,
            Points = [.. _pencilPoints],
            Argb = _color,
            Thickness = _thickness
        });
    }

    private void Add(CaptureAnnotation annotation)
    {
        _annotations.Add(annotation);
        _redo.Clear();
        Rebuild();
    }

    private void Undo()
    {
        if (_annotations.Count == 0)
        {
            return;
        }
        var last = _annotations[^1];
        _annotations.RemoveAt(_annotations.Count - 1);
        _redo.Push(last);
        Rebuild();
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }
        _annotations.Add(_redo.Pop());
        Rebuild();
    }

    private void Rebuild(CaptureAnnotation? pending = null)
    {
        _overlay.Children.Clear();
        foreach (var annotation in _annotations)
        {
            AddVisual(annotation);
        }
        if (pending is not null)
        {
            AddVisual(pending);
        }
        if (_cropRect is { Width: > 1, Height: > 1 } crop)
        {
            var outline = new Rectangle
            {
                Width = crop.Width,
                Height = crop.Height,
                Stroke = (Brush)Application.Current.FindResource("Lab.accent"),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(outline, crop.Left);
            Canvas.SetTop(outline, crop.Top);
            _overlay.Children.Add(outline);
        }
    }

    private void AddVisual(CaptureAnnotation annotation)
    {
        var color = DrawingColor.FromArgb(annotation.Argb);
        var brush = new SolidColorBrush(
            Color.FromArgb(color.A, color.R, color.G, color.B));
        var thickness = annotation.Kind == CaptureAnnotationKind.Highlighter
            ? annotation.Thickness * 4
            : annotation.Thickness;
        if (annotation.Kind == CaptureAnnotationKind.Highlighter)
        {
            brush.Opacity = .38;
        }

        switch (annotation.Kind)
        {
            case CaptureAnnotationKind.Arrow:
            case CaptureAnnotationKind.Highlighter:
                _overlay.Children.Add(new Line
                {
                    X1 = annotation.Start.X,
                    Y1 = annotation.Start.Y,
                    X2 = annotation.End.X,
                    Y2 = annotation.End.Y,
                    Stroke = brush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
                if (annotation.Kind == CaptureAnnotationKind.Arrow)
                {
                    _overlay.Children.Add(ArrowHead(annotation, brush));
                }
                break;
            case CaptureAnnotationKind.Rectangle:
            case CaptureAnnotationKind.Ellipse:
                var shape = annotation.Kind == CaptureAnnotationKind.Rectangle
                    ? (Shape)new Rectangle()
                    : new Ellipse();
                shape.Stroke = brush;
                shape.StrokeThickness = thickness;
                shape.Width = Math.Abs(annotation.End.X - annotation.Start.X);
                shape.Height = Math.Abs(annotation.End.Y - annotation.Start.Y);
                Canvas.SetLeft(shape, Math.Min(annotation.Start.X, annotation.End.X));
                Canvas.SetTop(shape, Math.Min(annotation.Start.Y, annotation.End.Y));
                _overlay.Children.Add(shape);
                break;
            case CaptureAnnotationKind.Pencil:
                _overlay.Children.Add(new Polyline
                {
                    Points = new PointCollection(annotation.Points),
                    Stroke = brush,
                    StrokeThickness = thickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
                break;
            case CaptureAnnotationKind.Text:
                var text = new TextBlock
                {
                    Text = annotation.Text,
                    Foreground = brush,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(text, annotation.Start.X);
                Canvas.SetTop(text, annotation.Start.Y);
                _overlay.Children.Add(text);
                break;
            case CaptureAnnotationKind.Stamp:
                var stamp = new Image { Source = annotation.StampImage?.CreateImageSource() ?? NotoEmojiCatalog.CreateImageSource(annotation.Text),
                    Width = annotation.Size, Height = annotation.Size, Stretch = Stretch.Uniform };
                Canvas.SetLeft(stamp, annotation.Start.X - annotation.Size / 2);
                Canvas.SetTop(stamp, annotation.Start.Y - annotation.Size / 2);
                _overlay.Children.Add(stamp);
                break;
            case CaptureAnnotationKind.Number:
                var badge = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(15),
                    Background = brush,
                    Child = new TextBlock
                    {
                        Text = annotation.Text,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Canvas.SetLeft(badge, annotation.Start.X - 15);
                Canvas.SetTop(badge, annotation.Start.Y - 15);
                _overlay.Children.Add(badge);
                break;
            case CaptureAnnotationKind.Blur:
            case CaptureAnnotationKind.Pixelate:
                var privacy = new Border
                {
                    Width = Math.Abs(annotation.End.X - annotation.Start.X),
                    Height = Math.Abs(annotation.End.Y - annotation.Start.Y),
                    Background = (Brush)Application.Current.FindResource("Lab.tint"),
                    BorderBrush = (Brush)Application.Current.FindResource("Lab.accent"),
                    BorderThickness = new Thickness(2),
                    Opacity = .72,
                    Child = new TextBlock
                    {
                        Text = annotation.Kind == CaptureAnnotationKind.Blur
                            ? UiText.Get("Desfoque")
                            : UiText.Get("Pixelização"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)Application.Current.FindResource("Lab.accent-text"),
                        FontWeight = FontWeights.SemiBold
                    }
                };
                Canvas.SetLeft(privacy, Math.Min(annotation.Start.X, annotation.End.X));
                Canvas.SetTop(privacy, Math.Min(annotation.Start.Y, annotation.End.Y));
                _overlay.Children.Add(privacy);
                break;
        }
    }

    private static Polygon ArrowHead(CaptureAnnotation annotation, Brush brush)
    {
        var angle = Math.Atan2(
            annotation.End.Y - annotation.Start.Y,
            annotation.End.X - annotation.Start.X);
        const double length = 15;
        var left = new WpfPoint(
            annotation.End.X - length * Math.Cos(angle - Math.PI / 6),
            annotation.End.Y - length * Math.Sin(angle - Math.PI / 6));
        var right = new WpfPoint(
            annotation.End.X - length * Math.Cos(angle + Math.PI / 6),
            annotation.End.Y - length * Math.Sin(angle + Math.PI / 6));
        return new Polygon
        {
            Points = new PointCollection([annotation.End, left, right]),
            Fill = brush
        };
    }

    private string? PromptForText()
    {
        var input = new TextBox { MinWidth = 300, Margin = new Thickness(0, 8, 0, 12) };
        input.SetResourceReference(StyleProperty, "Lab.Field");
        var dialog = new Window
        {
            Title = UiText.Get("Inserir texto"),
            Owner = this,
            Width = 390,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)Application.Current.FindResource("Lab.raised"),
            Foreground = (Brush)Application.Current.FindResource("Lab.text")
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = UiText.Get("Texto da marcação") });
        panel.Children.Add(input);
        var ok = ActionButton(UiText.Get("Inserir"), (_, _) => dialog.DialogResult = true);
        ok.SetResourceReference(StyleProperty, "Lab.Pilot.PrimaryButton");
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(ok);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void Complete(CaptureEditorOutput output)
    {
        EditedBitmap?.Dispose();
        using var rendered = CaptureAnnotationRenderer.Render(
            _source,
            _annotations,
            _previewWidth,
            _previewHeight);
        var crop = _cropRect is { Width: > 1, Height: > 1 }
            ? ScaleCrop(_cropRect.Value, rendered.Width, rendered.Height)
            : new System.Drawing.Rectangle(0, 0, rendered.Width, rendered.Height);
        using var cropped = rendered.Clone(
            crop,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        if (_resizeWidth is > 0 && _resizeWidth.Value != cropped.Width)
        {
            var width = _resizeWidth.Value;
            var height = Math.Max(1, (int)Math.Round(cropped.Height * width / (double)cropped.Width));
            EditedBitmap = new DrawingBitmap(width, height);
            using var graphics = System.Drawing.Graphics.FromImage(EditedBitmap);
            graphics.InterpolationMode =
                System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(cropped, 0, 0, width, height);
        }
        else
        {
            EditedBitmap = new DrawingBitmap(cropped);
        }
        RequestedOutput = output;
        DialogResult = true;
    }

    private void ConfigureResize()
    {
        var input = new TextBox
        {
            Text = (_resizeWidth ?? _source.Width).ToString(),
            MinWidth = 180,
            Margin = new Thickness(0, 8, 0, 12)
        };
        var dialog = new Window
        {
            Title = UiText.Get("Redimensionar"),
            Owner = this,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)Application.Current.FindResource("Lab.raised"),
            Foreground = (Brush)Application.Current.FindResource("Lab.text")
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = UiText.Get("Nova largura em pixels") });
        panel.Children.Add(input);
        var apply = ActionButton(UiText.Get("Aplicar"), (_, _) => dialog.DialogResult = true);
        input.SetResourceReference(StyleProperty, "Lab.Field");
        apply.SetResourceReference(StyleProperty, "Lab.Pilot.PrimaryButton");
        apply.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(apply);
        dialog.Content = panel;
        if (dialog.ShowDialog() == true &&
            int.TryParse(input.Text, out var width))
        {
            _resizeWidth = Math.Clamp(width, 64, 7680);
        }
    }

    private System.Drawing.Rectangle ScaleCrop(Rect crop, int width, int height)
    {
        var scaleX = width / _previewWidth;
        var scaleY = height / _previewHeight;
        var result = new System.Drawing.Rectangle(
            Math.Clamp((int)Math.Round(crop.Left * scaleX), 0, width - 1),
            Math.Clamp((int)Math.Round(crop.Top * scaleY), 0, height - 1),
            Math.Max(1, (int)Math.Round(crop.Width * scaleX)),
            Math.Max(1, (int)Math.Round(crop.Height * scaleY)));
        result.Width = Math.Min(result.Width, width - result.Left);
        result.Height = Math.Min(result.Height, height - result.Top);
        return result;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Redo();
            e.Handled = true;
        }
    }

    private WpfPoint Clamp(WpfPoint point) =>
        new(
            Math.Clamp(point.X, 0, _previewWidth),
            Math.Clamp(point.Y, 0, _previewHeight));

    private static BitmapSource ToBitmapSource(DrawingBitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);
}
