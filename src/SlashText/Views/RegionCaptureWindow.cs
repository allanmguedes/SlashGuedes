using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SlashText.Services;
using SlashText.Design;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using Forms = System.Windows.Forms;

namespace SlashText.Views;

public sealed partial class RegionCaptureWindow : Window
{
    private readonly bool _pilotVisuals;
    private ToggleButton? _selectButton;
    private readonly List<(Border Container, Panel Tools)> _pilotGroups = [];
    private int _privacyStrength = 18;
    private bool _selectMode;
    private int _resizeHandle = -1;
    private bool _movingSelection;
    private Point _transformStart;
    private Rect _transformBounds;
    private Point _annotationOrigin;
    private CaptureEditorDocument? _previewDocument;
    private CaptureAnnotation[] _previewAnnotations = [];
    private readonly System.Diagnostics.Stopwatch _previewClock = System.Diagnostics.Stopwatch.StartNew();
    private string _textFont = "Segoe UI";
    private readonly Canvas _canvas = new();
    private readonly Canvas _annotationLayer = new()
    {
        Background = Brushes.Transparent,
        Visibility = Visibility.Collapsed
    };
    private readonly Rectangle _selection = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(39, 200, 218)),
        StrokeThickness = 2,
        StrokeDashArray = new DoubleCollection { 3, 2 },
        Fill = Brushes.Transparent,
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false
    };
    private readonly Rectangle[] _shades =
    [
        Shade(), Shade(), Shade(), Shade()
    ];
    private readonly Border _sizeBadge = new()
    {
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(9, 5, 9, 5),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false
    };
    private readonly Border _toolbar;
    private readonly Window _toolbarWindow;
    private readonly Border[] _handles;
    private readonly DrawingBitmap _desktopBitmap;
    private readonly CaptureAnnotationHistory _annotationHistory = new();
    private readonly Dictionary<CaptureAnnotationKind, ToggleButton> _toolButtons = [];
    private readonly List<Point> _pencilPoints = [];
    private readonly bool _isDark = ThemeService.IsDark;
    private Point _start;
    private Point _annotationStart;
    private Rect _localSelection;
    private bool _dragging;
    private bool _drawing;
    private bool _selectionReady;
    private readonly CaptureToolSelection _toolSelection =
        new(CaptureAnnotationKind.Arrow);
    private CaptureAnnotationKind _tool
    {
        get => _toolSelection.Selected;
        set => _toolSelection.Select(value);
    }
    private int _color = DrawingColor.Red.ToArgb();
    private float _thickness = 4;
    private float _opacity = 1;
    private int? _fillArgb;
    private int? _outlineArgb = DrawingColor.Red.ToArgb();
    private float _annotationSize = 32;
    private bool _textBold = true;
    private string _selectedStamp = "❤️";
    private CaptureStampImage? _selectedStampImage;
    private int _nextNumber = 1;
    private Grid _toolbarLayout = null!;
    private bool _toolbarPositionPending;
    private Window? _contextWindow;
    private FrameworkElement? _contextAnchor;
    private MonitorWorkArea _activeMonitor;
    private Rect _toolbarBoundsPixels;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _eraseButton = null!;
    private Button _reselectButton = null!;
    private Button _cancelButton = null!;
    private Button _overflowButton = null!;
    private Border _captureSeparator = null!;
    private Border _actionSeparator = null!;
    private Border _captureSplitButton = null!;
    private bool _compactToolbar;

    public DrawingBitmap? EditedBitmap { get; private set; }
    public CaptureEditorOutput RequestedOutput { get; private set; } = CaptureEditorOutput.Default;

    public RegionCaptureWindow(bool includeCursor = false)
        : this(CaptureVirtualDesktopBitmap(includeCursor), AppPaths.IsCapturePilot, ownsSource: true) { }

    internal RegionCaptureWindow(DrawingBitmap desktopBitmap, bool pilotVisuals, bool ownsSource = false)
    {
        _pilotVisuals = pilotVisuals;
        _selectMode = pilotVisuals;
        Title = UiText.Get("Selecionar e editar região");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = Brushes.Black;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _desktopBitmap = ownsSource ? desktopBitmap : new DrawingBitmap(desktopBitmap);
        if (!ownsSource) { Width = desktopBitmap.Width; Height = desktopBitmap.Height; }
        _sizeBadge.Background = Brush(
            _isDark ? "#F2121922" : "#F8FFFFFF");
        _sizeBadge.BorderBrush = Brush(
            _isDark ? "#6EFFFFFF" : "#CBD5DF");
        _canvas.Children.Add(new Image
        {
            Source = ToBitmapSource(_desktopBitmap),
            Width = Width,
            Height = Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        });
        foreach (var shade in _shades)
        {
            _canvas.Children.Add(shade);
        }

        var help = new Border
        {
            Background = Brush(_isDark ? "#F2121922" : "#F8FFFFFF"),
            BorderBrush = Brush(_isDark ? "#64FFFFFF" : "#CBD5DF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 9, 14, 9),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = UiText.Get("Selecione uma região e edite sem sair desta tela  ·  Esc cancela"),
                Foreground = Brush(_isDark ? "#F5F8FA" : "#17212B"),
                FontFamily = new FontFamily("Segoe UI Variable Text"),
                FontSize = 13
            }
        };
        Canvas.SetLeft(help, 24);
        Canvas.SetTop(help, 24);
        if (_pilotVisuals)
        {
            help.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
            help.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
            ((TextBlock)help.Child).SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
            foreach (var shade in _shades) shade.Fill = new SolidColorBrush(Color.FromArgb(145, 0, 0, 0));
            _sizeBadge.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
            _sizeBadge.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
        }
        _canvas.Children.Add(help);
        _canvas.Children.Add(_selection);

        _annotationLayer.ClipToBounds = true;
        _annotationLayer.Cursor = Cursors.Cross;
        _annotationLayer.MouseLeftButtonDown += OnAnnotationMouseDown;
        _canvas.Children.Add(_annotationLayer);

        _handles =
        [
            Handle(), Handle(), Handle(), Handle(),
            Handle(), Handle(), Handle(), Handle()
        ];
        var handleCursors = new[] { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
            Cursors.SizeWE, Cursors.SizeNESW, Cursors.SizeNS, Cursors.SizeNWSE };
        for (var index = 0; index < _handles.Length; index++)
        {
            var handle = _handles[index]; var handleIndex = index;
            if (_pilotVisuals)
            {
                handle.Cursor = handleCursors[index]; handle.Focusable = true;
                handle.IsHitTestVisible = true;
                AutomationProperties.SetName(handle, UiText.Format($"Redimensionar seleção {index + 1}"));
                handle.MouseLeftButtonDown += (_, e) => BeginSelectionTransform(e, handleIndex);
                handle.KeyDown += (_, e) => ResizeWithKeyboard(handleIndex, e);
                handle.SetResourceReference(Border.BorderBrushProperty, "Lab.accent");
            }
            _canvas.Children.Add(handle);
        }

        _sizeBadge.Child = new TextBlock
        {
            Foreground = Brush(_isDark ? "#F5F8FA" : "#17212B"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12
        };
        if (_pilotVisuals) ((TextBlock)_sizeBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        _canvas.Children.Add(_sizeBadge);

        _toolbar = BuildToolbar();
        _toolbarWindow = new Window
        {
            Title = UiText.Get("Ferramentas de captura"),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.Height,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Opacity = 0,
            Content = _pilotVisuals ? new Border { Padding = new Thickness(8), Child = _toolbar } : _toolbar
        };
        _toolbarWindow.DpiChanged += (_, _) => RequestToolbarPosition();
        _toolbarWindow.PreviewKeyDown += OnPreviewKeyDown;
        Content = _canvas;

        Loaded += (_, _) =>
        {
            _toolbarWindow.Owner = this;
            UpdateShade(default);
        };
        Closed += (_, _) =>
        {
            _selectionReady = false;
            HideContextWindow(reactivateOverlay: false);
            _toolbarWindow.Close();
            _previewDocument?.Dispose();
            _desktopBitmap.Dispose();
        };
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => { if (_objectDragStart.HasValue) FinishObjectMove(commit: false); };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private Border BuildPilotToolbar()
    {
        var groups = new WrapPanel { Orientation = Orientation.Horizontal };
        _toolbarLayout = new Grid { MinHeight = 44 };
        _toolbarLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _toolbarLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _toolbarLayout.Children.Add(groups);
        Panel Group(string name, bool finish = false)
        {
            var stack = new StackPanel();
            var label = new TextBlock { Text = name, FontSize = 10, Margin = new Thickness(4, 0, 4, 4) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); stack.Children.Add(label);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; stack.Children.Add(row);
            AutomationProperties.SetName(row, name);
            var box = new Border { Child = stack, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 4, 0),
                BorderThickness = new Thickness(0, 0, 1, 0) };
            box.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
            if (finish)
            {
                Grid.SetColumn(box, 1); box.VerticalAlignment = VerticalAlignment.Top;
                box.BorderThickness = new Thickness(0); box.Margin = new Thickness(0);
                _toolbarLayout.Children.Add(box);
            }
            else groups.Children.Add(box);
            _pilotGroups.Add((box, row)); return row;
        }
        var area = Group(UiText.Get("ÁREA"));
        _selectButton = new ToggleButton { Content = new LabIcon { Kind = "MousePointer2", Width = 20, Height = 20 },
            ToolTip = UiText.Get("Mover área, textos e emojis · Ctrl+arraste move a área"), Style = (Style)FindResource("Lab.Overlay.Toggle"), IsChecked = true };
        AutomationProperties.SetName(_selectButton, UiText.Get("Selecionar, mover e redimensionar"));
        _selectButton.Click += (_, _) => SelectRegionTool(); area.Children.Add(_selectButton);
        _reselectButton = IconButton("CaptureIconReselect", UiText.Get("Refazer seleção (R)"), (_, _) => ResetSelection()); area.Children.Add(_reselectButton);
        var annotate = Group(UiText.Get("ANOTAR"));
        annotate.Children.Add(ToolButton("CaptureIconPencil", UiText.Get("Caneta"), CaptureAnnotationKind.Pencil));
        annotate.Children.Add(ToolButton("CaptureIconHighlighter", UiText.Get("Marca-texto"), CaptureAnnotationKind.Highlighter));
        annotate.Children.Add(ToolButton("CaptureIconShapes", UiText.Get("Formas, setas e números"), CaptureAnnotationKind.Rectangle));
        annotate.Children.Add(ToolButton("CaptureIconText", UiText.Get("Texto"), CaptureAnnotationKind.Text));
        annotate.Children.Add(ToolButton("CaptureIconEmoji", UiText.Get("Emotes e meus emojis"), CaptureAnnotationKind.Stamp));
        var privacy = Group(UiText.Get("PRIVACIDADE"));
        privacy.Children.Add(ToolButton("CaptureIconBlur", UiText.Get("Desfocar"), CaptureAnnotationKind.Blur));
        privacy.Children.Add(ToolButton("CaptureIconPixelate", UiText.Get("Pixelizar"), CaptureAnnotationKind.Pixelate));
        var history = Group(UiText.Get("HISTÓRICO"));
        _undoButton = IconButton("CaptureIconUndo", UiText.Get("Desfazer (Ctrl+Z)"), (_, _) => Undo()); history.Children.Add(_undoButton);
        _redoButton = IconButton("CaptureIconRedo", UiText.Get("Refazer (Ctrl+Y)"), (_, _) => Redo()); history.Children.Add(_redoButton);
        _eraseButton = IconButton("CaptureIconEraser", UiText.Get("Limpar marcações"), (_, _) => ClearAllAnnotations()); history.Children.Add(_eraseButton);
        var ocr = Group("OCR"); ocr.Children.Add(OcrButton());
        var finish = Group(UiText.Get("FINALIZAR"), finish: true);
        finish.Children.Add(BuildCaptureSplitButton());
        _overflowButton = IconButton("CaptureIconMore", UiText.Get("Mais ferramentas"), (_, _) => ShowOverflowMenu()); finish.Children.Add(_overflowButton);
        _cancelButton = IconButton("CaptureIconClose", UiText.Get("Cancelar captura (Esc)"), (_, _) => DialogResult = false); finish.Children.Add(_cancelButton);
        _captureSeparator = Separator(); _actionSeparator = Separator();
        var toolbar = new Border { Visibility = Visibility.Collapsed, Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = _toolbarLayout,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .24 } };
        toolbar.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
        toolbar.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
        LabMotion.SetEntrance(toolbar, "Popup"); LabMotion.SetReduced(toolbar, LabMotion.GetReduced(this));
        ApplyToolbarDensity(compact: false);
        return toolbar;
    }

    private static LabIcon PilotIcon(string geometryKey) => new()
    {
        Kind = geometryKey switch
        {
            "CaptureIconArrow" => "ArrowUpRight", "CaptureIconPencil" => "PenLine", "CaptureIconHighlighter" => "Highlighter",
            "CaptureIconShapes" => "Square", "CaptureIconText" => "Type", "CaptureIconNumber" => "Hash",
            "CaptureIconEmoji" => "Smile", "CaptureIconEraser" => "Trash2", "CaptureIconUndo" => "Undo2",
            "CaptureIconRedo" => "Redo2", "CaptureIconReselect" => "ScanLine", "CaptureIconClose" => "X",
            "CaptureIconPalette" => "Palette", "CaptureIconBlur" => "ScanLine", "CaptureIconPixelate" => "Grid2X2", _ => "MoreHorizontal"
        }, Width = 20, Height = 20, IsHitTestVisible = false
    };

    private void SelectRegionTool()
    {
        HideContextWindow(); _selectMode = true; Cursor = Cursors.SizeAll; UpdateToolSelection();
    }

    private void ShowInkContext()
    {
        var content = CaptureInkPicker.CreateContent(() => _color,
            color => { _color = color; _outlineArgb = color; }, () => _thickness, value => _thickness = value);
        ShowContextWindow(content, 700);
    }

    private void ShowShapeInk(bool fill)
    {
        var panel = new StackPanel();
        panel.Children.Add(CaptureInkPicker.CreateColorContent(() => (fill ? _fillArgb : _outlineArgb) ?? _color, color =>
        {
            if (fill) _fillArgb = color; else _outlineArgb = color;
            _color = color;
        }, fill ? UiText.Get("Preenchimento") : UiText.Get("Contorno"), compactPalette: true));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 10) };
        actions.Children.Add(ContextAction(UiText.Get("Sem cor"), () =>
        {
            if (fill) { _fillArgb = null; _outlineArgb ??= _color; }
            else { _outlineArgb = null; _fillArgb ??= _color; }
            ShowToolContext(_tool);
        }));
        actions.Children.Add(ContextAction(UiText.Get("Voltar às formas"), () => ShowToolContext(_tool)));
        panel.Children.Add(actions); ShowContextWindow(panel, 380, _toolButtons[CaptureAnnotationKind.Rectangle]);
    }

    private FrameworkElement BuildPilotStampContext()
    {
        var panel = new StackPanel { Margin = new Thickness(14), Width = 320 };
        var row = new DockPanel();
        var all = ContextAction(UiText.Get("Ver todos / Meus emojis"), () =>
        {
            HideContextWindow();
            var selected = CaptureEmojiPicker.Show(this);
            if (selected is null) return;
            _selectedStamp = selected; _selectedStampImage = CaptureStampCatalog.Current.GetImage(selected); _selectMode = false; _tool = CaptureAnnotationKind.Stamp;
            UpdateToolSelection(); ShowToolContext(CaptureAnnotationKind.Stamp);
        });
        DockPanel.SetDock(all, Dock.Right); row.Children.Add(all);
        row.Children.Add(ContextTitle("Emojis")); panel.Children.Add(row);
        var strip = new UniformGrid { Columns = 6 };
        var quick = NotoEmojiCatalog.QuickItems.AsEnumerable();
        if (!quick.Any(e => e.Value == _selectedStamp) && CaptureStampCatalog.Current.TryGet(_selectedStamp, out var current))
            quick = new[] { current }.Concat(quick);
        var choices = new List<Button>();
        foreach (var item in quick)
        {
            var button = new Button { Content = EmojiImage(item.Value, 26), Width = 38, Height = 40,
                Padding = new Thickness(3), Margin = new Thickness(0, 0, 4, 0), ToolTip = item.Name,
                Tag = item.Value == _selectedStamp ? "Selected" : null, Style = (Style)FindResource("Lab.Pilot.ToolButton") };
            AutomationProperties.SetName(button, item.Name);
            button.Click += (_, _) =>
            {
                _selectedStamp = item.Value; _selectedStampImage = CaptureStampCatalog.Current.GetImage(item.Value); _selectMode = false; _tool = CaptureAnnotationKind.Stamp;
                foreach (var choice in choices) choice.Tag = ReferenceEquals(choice, button) ? "Selected" : null;
                UpdateToolSelection();
            };
            choices.Add(button); strip.Children.Add(button);
        }
        panel.Children.Add(new ScrollViewer { Content = strip, MaxHeight = 268,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(LabeledSlider(UiText.Get("Tamanho"), 24, 128, _annotationSize, value => _annotationSize = (float)value, " px"));
        return panel;
    }

    private void BeginSelectionTransform(MouseButtonEventArgs e, int handle)
    {
        if (!_selectionReady || e.ChangedButton != MouseButton.Left) return;
        HideContextWindow(reactivateOverlay: false);
        _selectMode = true; _drawing = false; _transformStart = e.GetPosition(_canvas);
        _transformBounds = _localSelection; _movingSelection = handle < 0; _resizeHandle = handle;
        _toolbarWindow.Hide(); CaptureMouse(); e.Handled = true; UpdateToolSelection();
    }

    private void ResizeWithKeyboard(int handle, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        var increment = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var delta = new Vector(e.Key == Key.Left ? -increment : e.Key == Key.Right ? increment : 0,
            e.Key == Key.Up ? -increment : e.Key == Key.Down ? increment : 0);
        SetSelection(CaptureSelectionGeometry.Resize(_localSelection, handle, delta, new Rect(0, 0, Width, Height)));
        Rebuild(); RequestToolbarPosition(); e.Handled = true;
    }

    private void SetSelection(Rect selection)
    {
        ClearObjectSelection();
        _localSelection = selection;
        _previewDocument?.Dispose(); _previewDocument = null; _previewAnnotations = [];
        RefreshSelectionVisual(); PositionAnnotationLayer(); PositionHandles();
        // Keep the old preview out of the new crop while dragging; rebuild once on release.
        _annotationLayer.Children.Clear();
    }

    private DrawingBitmap RenderSelection(CaptureAnnotation? pending = null)
    {
        var items = _annotationHistory.Items;
        if (_previewAnnotations.Length > items.Count ||
            !_previewAnnotations.SequenceEqual(items.Take(_previewAnnotations.Length)))
        {
            _previewDocument?.Dispose(); _previewDocument = null; _previewAnnotations = [];
        }
        if (_previewDocument is null)
        {
            using var crop = CropFrozenSelection(); _previewDocument = new CaptureEditorDocument(crop);
        }
        var dimensions = _previewDocument.Dimensions;
        var sx = dimensions.Width / _localSelection.Width; var sy = dimensions.Height / _localSelection.Height;
        CaptureAnnotation Transform(CaptureAnnotation item) => item.Transform(
            _annotationOrigin.X - _localSelection.X, _annotationOrigin.Y - _localSelection.Y, sx, sy);
        foreach (var item in items.Skip(_previewAnnotations.Length)) _previewDocument.AddAnnotation(Transform(item));
        _previewAnnotations = items.ToArray();
        return _previewDocument.Render(pending is null ? null : Transform(pending));
    }

    internal Border ToolbarForEvidence
    {
        get { if (_toolbarWindow.Content is Border frame && ReferenceEquals(frame.Child, _toolbar)) frame.Child = null;
            _toolbarWindow.Content = null; return _toolbar; }
    }
    internal FrameworkElement StampPanelForEvidence() => BuildPilotStampContext();
    internal void SetSelectionForEvidence(Rect selection)
    {
        _selectionReady = true; _annotationOrigin = selection.TopLeft; SetSelection(selection);
        _toolbar.Visibility = Visibility.Visible; UpdateToolSelection();
    }
    internal void SetDensityForEvidence(bool compact) => ApplyToolbarDensity(compact);
    internal StackPanel OverflowForEvidence() => BuildPilotOverflowMenu();
    internal ToggleButton ToolForEvidence(CaptureAnnotationKind tool) => _toolButtons[tool];
    internal void FitToolbarForEvidence(double width)
    {
        _toolbar.Width = _toolbar.MaxWidth = width;
        _toolbarLayout.Width = width - _toolbar.Padding.Left - _toolbar.Padding.Right -
            _toolbar.BorderThickness.Left - _toolbar.BorderThickness.Right;
    }
    internal void AddForEvidence(CaptureAnnotation annotation) => Add(annotation);
    internal FrameworkElement? ContextForEvidence => _contextWindow?.Content as FrameworkElement;
    internal int AnnotationCountForEvidence => _annotationHistory.Items.Count;
    internal DrawingBitmap RenderForEvidence() => RenderSelection();
    internal CaptureAnnotation FinishDragForEvidence(Point start, Point end)
    {
        _annotationStart = start; _pencilPoints.Clear();
        if (_tool == CaptureAnnotationKind.Pencil) _pencilPoints.Add(start);
        FinishAnnotation(new Point(end.X + _annotationOrigin.X, end.Y + _annotationOrigin.Y));
        return _annotationHistory.Items.Last();
    }
    internal void MoveForEvidence(Vector delta) => SetSelection(CaptureSelectionGeometry.Move(_localSelection, delta, new Rect(0, 0, Width, Height)));

    private Border BuildToolbar()
    {
        if (_pilotVisuals) return BuildPilotToolbar();
        var root = new Grid
        {
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _toolbarLayout = root;
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tools.Children.Add(BuildCaptureSplitButton());
        tools.Children.Add(OcrButton());
        _captureSeparator = Separator();
        tools.Children.Add(_captureSeparator);
        tools.Children.Add(ToolButton("CaptureIconArrow", UiText.Get("Seta"), CaptureAnnotationKind.Arrow));
        tools.Children.Add(ToolButton("CaptureIconHighlighter", UiText.Get("Marca-texto"), CaptureAnnotationKind.Highlighter));
        tools.Children.Add(ToolButton("CaptureIconShapes", UiText.Get("Formas"), CaptureAnnotationKind.Rectangle));
        tools.Children.Add(ToolButton("CaptureIconPencil", UiText.Get("Lápis"), CaptureAnnotationKind.Pencil));
        tools.Children.Add(ToolButton("CaptureIconText", UiText.Get("Texto"), CaptureAnnotationKind.Text));
        tools.Children.Add(ToolButton("CaptureIconNumber", UiText.Get("Número"), CaptureAnnotationKind.Number));
        tools.Children.Add(ToolButton("CaptureIconEmoji", "Emoticons", CaptureAnnotationKind.Stamp));
        _eraseButton = IconButton("CaptureIconEraser", UiText.Get("Apagar todas as marcações"), (_, _) => ClearAllAnnotations());
        tools.Children.Add(_eraseButton);
        _actionSeparator = Separator();
        tools.Children.Add(_actionSeparator);
        _undoButton = IconButton("CaptureIconUndo", UiText.Get("Desfazer (Ctrl+Z)"), (_, _) => Undo());
        _redoButton = IconButton("CaptureIconRedo", UiText.Get("Refazer (Ctrl+Y)"), (_, _) => Redo());
        tools.Children.Add(_undoButton);
        tools.Children.Add(_redoButton);
        _reselectButton = IconButton("CaptureIconReselect", UiText.Get("Refazer seleção (R)"), (_, _) => ResetSelection());
        tools.Children.Add(_reselectButton);
        _overflowButton = IconButton("CaptureIconMore", UiText.Get("Mais ferramentas"), (_, _) => ShowOverflowMenu());
        _overflowButton.Visibility = Visibility.Collapsed;
        tools.Children.Add(_overflowButton);
        _cancelButton = IconButton("CaptureIconClose", UiText.Get("Cancelar captura (Esc)"), (_, _) => DialogResult = false);
        tools.Children.Add(_cancelButton);
        root.Children.Add(tools);

        var toolbar = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = ResourceBrush("CaptureToolbarSurfaceBrush"),
            BorderBrush = ResourceBrush("CaptureToolbarBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(6, 4, 6, 4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = .28,
                Color = Colors.Black
            },
            Child = root
        };
        return toolbar;
    }

    private ToggleButton ToolButton(
        string geometryKey,
        string toolTip,
        CaptureAnnotationKind tool)
    {
        var button = new ToggleButton
        {
            Style = (Style)FindResource(_pilotVisuals ? "Lab.Overlay.Toggle" : "CaptureToolbarToggleButton"),
            Content = _pilotVisuals ? PilotIcon(geometryKey) : ToolbarIcon(geometryKey),
            ToolTip = toolTip
        };
        AutomationProperties.SetName(button, toolTip);
        button.Click += (_, _) =>
        {
            var repeated = !_selectMode && _tool == tool;
            HideContextWindow(); _selectMode = false; _tool = tool;
            Cursor = tool == CaptureAnnotationKind.Text ? Cursors.IBeam : Cursors.Cross;
            UpdateToolSelection();
            if (_pilotVisuals || repeated || tool is CaptureAnnotationKind.Rectangle or
                    CaptureAnnotationKind.Text or CaptureAnnotationKind.Number or
                    CaptureAnnotationKind.Stamp)
            {
                ShowToolContext(tool);
            }
        };
        _toolButtons[tool] = button;
        return button;
    }

    private Button IconButton(
        string geometryKey,
        string toolTip,
        RoutedEventHandler click)
    {
        var button = new Button
        {
            Style = (Style)FindResource(_pilotVisuals ? "Lab.Pilot.ToolButton" : "CaptureToolbarIconButton"),
            Content = _pilotVisuals ? PilotIcon(geometryKey) : ToolbarIcon(geometryKey),
            ToolTip = toolTip,
        };
        AutomationProperties.SetName(button, toolTip);
        button.Click += click;
        return button;
    }

    private Border BuildCaptureSplitButton()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var capture = new Button
        {
            Content = UiText.Get("Capturar"),
            Style = (Style)FindResource(_pilotVisuals ? "Lab.Overlay.Capture" : "CaptureToolbarCaptureButton"),
            ToolTip = UiText.Get("Concluir conforme configuração")
        };
        AutomationProperties.SetName(capture, UiText.Get("Capturar conforme configuração"));
        capture.Click += (_, _) => Complete(CaptureEditorOutput.Default);
        var menu = new Button
        {
            Content = new Path
            {
                Data = Geometry.Parse("M4,7 L10,13 L16,7"),
                Stroke = Brush("#061316"),
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.None,
                Width = 20,
                Height = 20
            },
            Style = (Style)FindResource(_pilotVisuals ? "Lab.Overlay.Capture" : "CaptureToolbarCaptureMenuButton"),
            ToolTip = UiText.Get("Opções de captura")
        };
        AutomationProperties.SetName(menu, UiText.Get("Abrir opções de captura"));
        if (_pilotVisuals)
        {
            var label = new TextBlock { Text = UiText.Get("Capturar") };
            label.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground")
                { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            capture.Content = label;
            var chevron = new LabIcon { Kind = "ChevronDown", Width = 14, Height = 14 };
            chevron.SetResourceReference(LabIcon.ForegroundProperty, "Lab.on"); menu.Content = chevron;
            menu.Width = 30; menu.Padding = new Thickness(4);
            capture.Margin = new Thickness(0, 0, 2, 0);
        }
        menu.Click += (_, _) => ShowCaptureMenu();
        panel.Children.Add(capture);
        panel.Children.Add(menu);
        _captureSplitButton = new Border
        {
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = panel
        };
        return _captureSplitButton;
    }

    private Path ToolbarIcon(string geometryKey) => new()
    {
        Data = (Geometry)Application.Current.FindResource(geometryKey),
        Stroke = ResourceBrush("CaptureToolbarTextBrush"),
        StrokeThickness = 1.6,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Stretch = Stretch.Uniform,
        Width = 18,
        Height = 18,
        Margin = new Thickness(1),
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
        IsHitTestVisible = false
    };

    private SolidColorBrush ResourceBrush(string key) => (SolidColorBrush)Application.Current.FindResource(
        _pilotVisuals ? key switch
        {
            "CaptureToolbarSurfaceBrush" => "Lab.panel", "CaptureToolbarElevatedBrush" => "Lab.raised",
            "CaptureToolbarBorderBrush" => "Lab.line-strong", "CaptureToolbarTextBrush" => "Lab.text",
            "CaptureToolbarSecondaryTextBrush" => "Lab.muted", "CaptureToolbarSelectedBrush" => "Lab.tint",
            "CaptureToolbarAccentBrush" => "Lab.accent", _ => key
        } : key);

    private Border Separator() => new()
    {
        Width = 1,
        Height = 24,
        Margin = new Thickness(5, 6, 5, 6),
        Background = ResourceBrush("CaptureToolbarBorderBrush")
    };

    private void ShowCaptureMenu()
    {
        var panel = ContextStack(220);
        panel.Children.Add(ContextAction(UiText.Get("Concluir conforme configuração"), () => Complete(CaptureEditorOutput.Default)));
        panel.Children.Add(ContextAction(UiText.Get("Copiar"), () => Complete(CaptureEditorOutput.Clipboard)));
        panel.Children.Add(ContextAction(UiText.Get("Salvar"), () => Complete(CaptureEditorOutput.File)));
        ShowContextWindow(panel, 220, _captureSplitButton);
    }

    private void ShowOverflowMenu()
    {
        if (_pilotVisuals)
        {
            ShowContextWindow(BuildPilotOverflowMenu(), 260, _overflowButton);
            return;
        }
        var panel = ContextStack(260);
        panel.Children.Add(ContextTitle(UiText.Get("Ferramentas")));
        if (_pilotVisuals) panel.Children.Add(ContextAction(UiText.Get("Selecionar / mover"), SelectRegionTool));
        AddOverflowTool(panel, UiText.Get("Seta"), CaptureAnnotationKind.Arrow);
        AddOverflowTool(panel, UiText.Get("Marca-texto"), CaptureAnnotationKind.Highlighter);
        AddOverflowTool(panel, UiText.Get("Formas"), CaptureAnnotationKind.Rectangle);
        AddOverflowTool(panel, UiText.Get("Lápis"), CaptureAnnotationKind.Pencil);
        AddOverflowTool(panel, UiText.Get("Texto"), CaptureAnnotationKind.Text);
        AddOverflowTool(panel, UiText.Get("Número"), CaptureAnnotationKind.Number);
        AddOverflowTool(panel, "Emoticons", CaptureAnnotationKind.Stamp);
        if (_pilotVisuals)
        {
            AddOverflowTool(panel, UiText.Get("Desfocar"), CaptureAnnotationKind.Blur);
            AddOverflowTool(panel, UiText.Get("Pixelizar"), CaptureAnnotationKind.Pixelate);
        }
        panel.Children.Add(ContextAction(_pilotVisuals ? UiText.Get("Limpar marcações") : UiText.Get("Apagar todas as marcações"), ClearAllAnnotations));
        panel.Children.Add(ContextAction(UiText.Get("Refazer seleção"), ResetSelection));
        ShowContextWindow(panel, 260);
    }

    private StackPanel BuildPilotOverflowMenu()
    {
        var panel = ContextStack(260);
        var tools = new[] {
            (UiText.Get("Caneta"), CaptureAnnotationKind.Pencil), (UiText.Get("Marca-texto"), CaptureAnnotationKind.Highlighter),
            (UiText.Get("Formas, setas e números"), CaptureAnnotationKind.Rectangle), (UiText.Get("Texto"), CaptureAnnotationKind.Text),
            (UiText.Get("Emotes e meus emojis"), CaptureAnnotationKind.Stamp), (UiText.Get("Desfocar"), CaptureAnnotationKind.Blur),
            (UiText.Get("Pixelizar"), CaptureAnnotationKind.Pixelate) };
        var hidden = tools.Where(item => _toolButtons[item.Item2].Visibility != Visibility.Visible).ToArray();
        if (hidden.Length > 0)
        {
            panel.Children.Add(ContextTitle(UiText.Get("Mais ferramentas")));
            foreach (var (label, tool) in hidden) AddOverflowTool(panel, label, tool);
        }
        return panel;
    }

    private void AddOverflowTool(
        Panel panel,
        string label,
        CaptureAnnotationKind tool)
    {
        panel.Children.Add(ContextAction(label, () =>
        {
            if (_pilotVisuals) HideContextWindow(reactivateOverlay: false);
            _selectMode = false; _tool = tool;
            Cursor = tool == CaptureAnnotationKind.Text ? Cursors.IBeam : Cursors.Cross;
            UpdateToolSelection();
            ShowToolContext(tool);
        }));
    }

    private void ShowToolContext(CaptureAnnotationKind tool)
    {
        FrameworkElement content = tool switch
        {
            _ when _pilotVisuals => BuildPilotToolContext(tool),
            CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse or
                CaptureAnnotationKind.Line => BuildShapesContext(),
            CaptureAnnotationKind.Stamp => BuildStampContext(),
            _ => BuildStrokeContext(tool)
        };
        var toolbarTool = _pilotVisuals && tool is CaptureAnnotationKind.Ellipse or CaptureAnnotationKind.Line or CaptureAnnotationKind.Arrow or CaptureAnnotationKind.Number
            ? CaptureAnnotationKind.Rectangle : tool;
        ShowContextWindow(content, 380,
            _toolButtons.TryGetValue(toolbarTool, out var button) ? button : null);
    }

    private FrameworkElement BuildStrokeContext(CaptureAnnotationKind tool)
    {
        var panel = ContextStack(320);
        if (_pilotVisuals && tool is CaptureAnnotationKind.Blur or CaptureAnnotationKind.Pixelate)
        {
            panel.Children.Add(ContextTitle(tool == CaptureAnnotationKind.Blur ? UiText.Get("Desfocar") : UiText.Get("Pixelizar")));
            panel.Children.Add(LabeledSlider(UiText.Get("Intensidade"), 6, 40, _privacyStrength, value => _privacyStrength = (int)value, ""));
            panel.Children.Add(ContextTitle(UiText.Get("Arraste na região. Confira o resultado antes de compartilhar.")));
            return panel;
        }
        panel.Children.Add(ContextTitle(tool switch
        {
            CaptureAnnotationKind.Highlighter => UiText.Get("Marca-texto"),
            CaptureAnnotationKind.Pencil => UiText.Get("Lápis"),
            CaptureAnnotationKind.Text => UiText.Get("Texto"),
            CaptureAnnotationKind.Number => UiText.Get("Número"),
            _ => UiText.Get("Seta")
        }));
        var preview = new Line
        {
            X1 = 10,
            Y1 = 22,
            X2 = 280,
            Y2 = 22,
            Stroke = WpfBrush(_color, _opacity),
            StrokeThickness = tool == CaptureAnnotationKind.Highlighter ? _thickness * 4 : _thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        var previewCanvas = new Canvas { Height = 44, Margin = new Thickness(0, 5, 0, 5) };
        previewCanvas.Children.Add(preview);
        var colors = tool == CaptureAnnotationKind.Highlighter
            ? HighlightColors
            : AnnotationColors;
        if (_pilotVisuals) panel.Children.Add(ContextAction(UiText.Get("Cores, RGB e espessura"), ShowInkContext));
        else panel.Children.Add(ColorPalette(colors, _color, color =>
        {
            _color = color;
            _outlineArgb = color;
            preview.Stroke = WpfBrush(color, _opacity);
        }));
        panel.Children.Add(LabeledSlider(UiText.Get("Opacidade"), 10, 100, _opacity * 100, value =>
        {
            _opacity = (float)(value / 100d);
            preview.Stroke = WpfBrush(_color, _opacity);
        }, "%"));
        if (tool == CaptureAnnotationKind.Text)
        {
            if (_pilotVisuals)
            {
                var font = new ComboBox { ItemsSource = new[] { "Segoe UI", "Arial", "Verdana", "Georgia", "Consolas" }, SelectedItem = _textFont, Margin = new Thickness(0, 5, 0, 5) };
                font.SetResourceReference(StyleProperty, "Lab.Combo");
                AutomationProperties.SetName(font, UiText.Get("Fonte da anotação"));
                font.SelectionChanged += (_, _) => _textFont = (string)font.SelectedItem;
                panel.Children.Add(font);
            }
            panel.Children.Add(LabeledSlider(UiText.Get("Tamanho"), 12, 64, _annotationSize, value =>
                _annotationSize = (float)value, " px"));
            var bold = new CheckBox
            {
                Content = UiText.Get("Negrito"),
                IsChecked = _textBold,
                Foreground = ResourceBrush("CaptureToolbarTextBrush"),
                Margin = new Thickness(0, 7, 0, 0)
            };
            bold.Checked += (_, _) => _textBold = true;
            bold.Unchecked += (_, _) => _textBold = false;
            panel.Children.Add(bold);
        }
        else if (tool == CaptureAnnotationKind.Number)
        {
            panel.Children.Add(LabeledSlider(UiText.Get("Tamanho"), 24, 64, _annotationSize, value =>
                _annotationSize = (float)value, " px"));
            panel.Children.Add(ContextAction(UiText.Get("Reiniciar numeração"), () => _nextNumber = 1));
        }
        else
        {
            panel.Children.Add(LabeledSlider(UiText.Get("Espessura"), 2, 12, _thickness, value =>
            {
                _thickness = (float)value;
                preview.StrokeThickness = tool == CaptureAnnotationKind.Highlighter
                    ? _thickness * 4
                    : _thickness;
            }, " px"));
        }
        panel.Children.Add(previewCanvas);
        return panel;
    }

    private FrameworkElement BuildShapesContext()
    {
        var panel = ContextStack(360);
        panel.Children.Add(ContextTitle(UiText.Get("Formas")));
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (!_pilotVisuals) row.Children.Add(ContextTool("Emoticons", CaptureAnnotationKind.Stamp));
        row.Children.Add(ContextTool(UiText.Get("Retângulo"), CaptureAnnotationKind.Rectangle));
        row.Children.Add(ContextTool(UiText.Get("Elipse"), CaptureAnnotationKind.Ellipse));
        row.Children.Add(ContextTool(UiText.Get("Linha"), CaptureAnnotationKind.Line));
        row.Children.Add(ContextTool(UiText.Get("Seta"), CaptureAnnotationKind.Arrow));
        if (_pilotVisuals) row.Children.Add(ContextTool(UiText.Get("Número"), CaptureAnnotationKind.Number));
        panel.Children.Add(row);
        if (_pilotVisuals)
        {
            panel.Children.Add(ContextAction(UiText.Get("Preenchimento"), () => ShowShapeInk(fill: true)));
            panel.Children.Add(ContextAction(UiText.Get("Contorno"), () => ShowShapeInk(fill: false)));
            panel.Children.Add(LabeledSlider(UiText.Get("Opacidade"), 10, 100, _opacity * 100, value => _opacity = (float)(value / 100), "%"));
            panel.Children.Add(LabeledSlider(UiText.Get("Espessura"), 1, 24, _thickness, value => _thickness = (float)value, " px"));
            return panel;
        }
        panel.Children.Add(ContextTitle(UiText.Get("Preenchimento")));
        panel.Children.Add(ColorPalette(AnnotationColors, _fillArgb, color =>
        {
            _fillArgb = color == 0 ? null : color;
            if (!_fillArgb.HasValue && !_outlineArgb.HasValue) _outlineArgb = _color;
        }, allowNone: true));
        panel.Children.Add(ContextTitle(UiText.Get("Contorno")));
        panel.Children.Add(ColorPalette(AnnotationColors, _outlineArgb, color =>
        {
            _outlineArgb = color == 0 ? null : color;
            if (!_fillArgb.HasValue && !_outlineArgb.HasValue) _fillArgb = _color;
            _color = _outlineArgb ?? _fillArgb ?? _color;
        }, allowNone: true));
        panel.Children.Add(LabeledSlider(UiText.Get("Opacidade"), 10, 100, _opacity * 100,
            value => _opacity = (float)(value / 100d), "%"));
        panel.Children.Add(LabeledSlider(UiText.Get("Espessura"), 2, 12, _thickness,
            value => _thickness = (float)value, " px"));
        return panel;
    }

    private FrameworkElement BuildStampContext()
    {
        if (_pilotVisuals) return BuildPilotStampContext();
        var panel = ContextStack(280);
        panel.Children.Add(ContextTitle(UiText.Get("Emoticons e carimbos")));
        var grid = new UniformGrid { Columns = 6 };
        foreach (var emoji in NotoEmojiCatalog.QuickItems)
        {
            var value = emoji.Value;
            var button = new Button
            {
                Content = EmojiImage(value, 28),
                Width = 42,
                Height = 42,
                Margin = new Thickness(2),
                Padding = new Thickness(6),
                Background = value == _selectedStamp
                    ? ResourceBrush("CaptureToolbarSelectedBrush")
                    : Brushes.Transparent,
                Foreground = ResourceBrush("CaptureToolbarTextBrush"),
                BorderBrush = value == _selectedStamp
                    ? ResourceBrush("CaptureToolbarAccentBrush")
                    : Brushes.Transparent,
                ToolTip = emoji.Name
            };
            AutomationProperties.SetName(button, UiText.Format($"Emoticon {emoji.Name}"));
            button.Click += (_, _) =>
            {
                _selectedStamp = value;
                _tool = CaptureAnnotationKind.Stamp;
                UpdateToolSelection();
            };
            grid.Children.Add(button);
        }
        panel.Children.Add(grid);
        panel.Children.Add(LabeledSlider(UiText.Get("Tamanho"), 24, 64, _annotationSize,
            value => _annotationSize = (float)value, " px"));
        return panel;
    }

    private static Image EmojiImage(string value, double size) => new()
    {
        Source = CaptureStampCatalog.Current.CreateImageSource(value),
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        SnapsToDevicePixels = true,
        IsHitTestVisible = false
    };

    private Button ContextTool(string label, CaptureAnnotationKind tool)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 58,
            Height = 32,
            Margin = new Thickness(2),
            Padding = new Thickness(7, 3, 7, 3),
            Background = _tool == tool
                ? ResourceBrush("CaptureToolbarSelectedBrush")
                : Brushes.Transparent,
            Foreground = ResourceBrush("CaptureToolbarTextBrush"),
            BorderBrush = _tool == tool
                ? ResourceBrush("CaptureToolbarAccentBrush")
                : ResourceBrush("CaptureToolbarBorderBrush")
        };
        AutomationProperties.SetName(button, label);
        if (_pilotVisuals) button.SetResourceReference(StyleProperty, "Lab.Button");
        button.Click += (_, _) =>
        {
            _selectMode = false; _tool = tool;
            UpdateToolSelection();
            if (_pilotVisuals || tool == CaptureAnnotationKind.Stamp) ShowToolContext(tool);
        };
        return button;
    }

    private static StackPanel ContextStack(double width) => new()
    {
        Width = width,
        Margin = new Thickness(14)
    };

    private TextBlock ContextTitle(string text) => new()
    {
        Text = text,
        Foreground = ResourceBrush("CaptureToolbarTextBrush"),
        FontWeight = FontWeights.SemiBold,
        FontSize = 13,
        Margin = new Thickness(0, 4, 0, 7)
    };

    private Button ContextAction(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Height = 34,
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Foreground = ResourceBrush("CaptureToolbarTextBrush"),
            BorderBrush = Brushes.Transparent
        };
        AutomationProperties.SetName(button, label);
        if (_pilotVisuals) { button.SetResourceReference(StyleProperty, "Lab.Button"); button.Height = 38; }
        button.Click += (_, _) => action();
        return button;
    }

    private FrameworkElement ColorPalette(
        IReadOnlyList<int> colors,
        int? selected,
        Action<int> select,
        bool allowNone = false)
    {
        var palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (allowNone)
        {
            var none = new Button
            {
                Content = "∅",
                Width = 30,
                Height = 30,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = ResourceBrush("CaptureToolbarSecondaryTextBrush"),
                BorderBrush = !selected.HasValue
                    ? ResourceBrush("CaptureToolbarAccentBrush")
                    : ResourceBrush("CaptureToolbarBorderBrush"),
                ToolTip = UiText.Get("Sem cor")
            };
            AutomationProperties.SetName(none, UiText.Get("Sem cor"));
            none.Click += (_, _) => select(0);
            palette.Children.Add(none);
        }
        foreach (var value in colors)
        {
            var color = DrawingColor.FromArgb(value);
            var choice = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(3),
                Padding = new Thickness(3),
                Background = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)),
                BorderBrush = selected == value
                    ? ResourceBrush("CaptureToolbarAccentBrush")
                    : ResourceBrush("CaptureToolbarBorderBrush"),
                BorderThickness = new Thickness(selected == value ? 3 : 1),
                ToolTip = UiText.Format($"Selecionar cor {color.Name}")
            };
            AutomationProperties.SetName(choice, UiText.Format($"Cor {color.Name}"));
            choice.Click += (_, _) => select(value);
            palette.Children.Add(choice);
        }
        return palette;
    }

    private FrameworkElement LabeledSlider(
        string label,
        double minimum,
        double maximum,
        double current,
        Action<double> changed,
        string suffix)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ResourceBrush("CaptureToolbarTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = current,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(5, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        var value = new TextBlock
        {
            Text = $"{current:0}{suffix}",
            Foreground = ResourceBrush("CaptureToolbarSecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        slider.ValueChanged += (_, _) =>
        {
            value.Text = $"{slider.Value:0}{suffix}";
            changed(slider.Value);
        };
        if (_pilotVisuals) slider.SetResourceReference(StyleProperty, "Lab.Pilot.InkSlider");
        AutomationProperties.SetName(slider, label);
        return grid;
    }

    private void ShowContextWindow(
        FrameworkElement content,
        double width,
        FrameworkElement? anchor = null)
    {
        HideContextWindow(reactivateOverlay: false);
        _contextAnchor = anchor;
        content.MaxWidth = Math.Max(200, _activeMonitor.WorkAreaPixels.Width / Math.Max(1, _activeMonitor.DpiScaleX) - 48);
        if (content is StackPanel stack)
            foreach (var child in stack.Children.OfType<FrameworkElement>()) child.MaxWidth = Math.Max(160, content.MaxWidth - 28);
        var scaleY = Math.Max(1, _activeMonitor.DpiScaleY);
        var maximumHeightDips = Math.Max(
            180,
            (_activeMonitor.WorkAreaPixels.Height / scaleY) - 24);
        var scroller = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = maximumHeightDips - (_pilotVisuals ? 18 : 0)
        };
        var border = new Border
        {
            Background = ResourceBrush("CaptureToolbarElevatedBrush"),
            BorderBrush = ResourceBrush("CaptureToolbarBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = scroller,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = .3
            }
        };
        if (_pilotVisuals)
        {
            // Keep the shadow off the text/control subtree, as in Capture settings.
            border.Effect = null;
            border.Child = null;
            var layers = new Grid();
            layers.Children.Add(new Border { Background = border.Background, CornerRadius = border.CornerRadius,
                IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .3 } });
            layers.Children.Add(scroller); border.Child = layers;
            RenderOptions.SetClearTypeHint(border, ClearTypeHint.Enabled);
        }
        _contextWindow = new Window
        {
            Title = UiText.Get("Opções da ferramenta"),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            Owner = _toolbarWindow,
            Width = width,
            MaxHeight = maximumHeightDips,
            Opacity = SystemParameters.ClientAreaAnimation ? 0 : 1,
            Content = _pilotVisuals ? new Border { Padding = new Thickness(8), Child = border } : border
        };
        TextOptions.SetTextFormattingMode(_contextWindow, TextFormattingMode.Display);
        _contextWindow.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                HideContextWindow();
                args.Handled = true;
            }
        };
        _contextWindow.Deactivated += (_, _) => HideContextWindow();
        if (_pilotVisuals) { LabMotion.SetEntrance(border, "Popup"); LabMotion.SetReduced(border, LabMotion.GetReduced(this)); }
        _contextWindow.DpiChanged += (_, _) => PositionContextWindow();
        _toolbar.UpdateLayout();
        _contextWindow.Show();
        _contextWindow.UpdateLayout();
        PositionContextWindow();
        var animationDuration = CaptureMotion.Duration(
            SystemParameters.ClientAreaAnimation,
            160);
        if (_pilotVisuals)
        {
            _contextWindow.Opacity = 1; LabMotion.PlayEntrance(border);
            border.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        else if (animationDuration > TimeSpan.Zero)
        {
            _contextWindow.BeginAnimation(OpacityProperty, new DoubleAnimation(
                0,
                1,
                animationDuration));
        }
    }

    private void PositionContextWindow()
    {
        if (_contextWindow is null) return;
        var scaleX = Math.Max(1, _activeMonitor.DpiScaleX);
        var scaleY = Math.Max(1, _activeMonitor.DpiScaleY);
        var size = new Size(
            Math.Ceiling(_contextWindow.ActualWidth * scaleX),
            Math.Ceiling(_contextWindow.ActualHeight * scaleY));
        Rect bounds;
        if (_contextAnchor is not null)
        {
            var topLeft = _contextAnchor.PointToScreen(new Point(0, 0));
            var bottomRight = _contextAnchor.PointToScreen(new Point(
                _contextAnchor.ActualWidth,
                _contextAnchor.ActualHeight));
            var anchorBounds = new Rect(topLeft, bottomRight);
            bounds = AnchoredPopoverPlacementCalculator.Calculate(
                anchorBounds,
                _activeMonitor.WorkAreaPixels,
                size,
                gap: 8,
                dpiScale: Math.Max(scaleX, scaleY)).Bounds;
        }
        else
        {
            bounds = ToolbarPlacementCalculator.Calculate(
                _toolbarBoundsPixels,
                _activeMonitor.WorkAreaPixels,
                size,
                size.Width,
                gap: 8,
                dpiScale: Math.Max(scaleX, scaleY)).Bounds;
        }
        var handle = new WindowInteropHelper(_contextWindow).Handle;
        _ = SetWindowPos(
            handle,
            new nint(-1),
            (int)Math.Round(bounds.Left),
            (int)Math.Round(bounds.Top),
            Math.Max(1, (int)Math.Ceiling(bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height)),
            SwpNoActivate | SwpShowWindow);
    }

    private void HideContextWindow(bool reactivateOverlay = true)
    {
        if (_contextWindow is null) return;
        var window = _contextWindow;
        _contextWindow = null;
        _contextAnchor = null;
        window.Close();
        if (_pilotVisuals) { RequestToolbarPosition(); }
        if (reactivateOverlay && IsVisible) Activate();
    }

    private static SolidColorBrush WpfBrush(int argb, float opacity)
    {
        var color = DrawingColor.FromArgb(argb);
        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)),
            color.R,
            color.G,
            color.B));
    }

    private static readonly int[] AnnotationColors =
    [
        DrawingColor.Black.ToArgb(), DrawingColor.White.ToArgb(), DrawingColor.Gray.ToArgb(),
        DrawingColor.Red.ToArgb(), DrawingColor.OrangeRed.ToArgb(), DrawingColor.Gold.ToArgb(),
        DrawingColor.LimeGreen.ToArgb(), DrawingColor.DeepSkyBlue.ToArgb(), DrawingColor.RoyalBlue.ToArgb(),
        DrawingColor.BlueViolet.ToArgb(), DrawingColor.DeepPink.ToArgb(), DrawingColor.Pink.ToArgb(),
        DrawingColor.LightGray.ToArgb(), DrawingColor.DarkGray.ToArgb(), DrawingColor.LightGreen.ToArgb(),
        DrawingColor.LightSkyBlue.ToArgb(), DrawingColor.Plum.ToArgb(), DrawingColor.Bisque.ToArgb()
    ];

    private static readonly int[] HighlightColors =
    [
        DrawingColor.Gold.ToArgb(), DrawingColor.LimeGreen.ToArgb(), DrawingColor.DeepSkyBlue.ToArgb(),
        DrawingColor.DeepPink.ToArgb(), DrawingColor.Orange.ToArgb(), DrawingColor.BlueViolet.ToArgb()
    ];

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        HideContextWindow(reactivateOverlay: false);
        if (e.Handled ||
            e.ChangedButton != MouseButton.Left ||
            IsToolbarSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var point = e.GetPosition(_canvas);
        if (_selectionReady)
        {
            if (!_localSelection.Contains(point))
            {
                return;
            }
            if (TryBeginObjectMove(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { e.Handled = true; return; }
            if (_pilotVisuals && _selectMode) BeginSelectionTransform(e, -1);
            else BeginAnnotation(point);
            e.Handled = true;
            return;
        }

        _start = point;
        _dragging = true;
        _toolbarWindow.Hide();
        SetHandlesVisibility(Visibility.Collapsed);
        _selection.Visibility = Visibility.Visible;
        _sizeBadge.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelection(_start);
        e.Handled = true;
    }

    private void OnAnnotationMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_selectionReady ||
            e.ChangedButton != MouseButton.Left ||
            IsTextInputSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var local = e.GetPosition(_annotationLayer);
        var canvasPoint = new Point(
            _localSelection.Left + local.X,
            _localSelection.Top + local.Y);
        if (TryBeginObjectMove(canvasPoint, Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { e.Handled = true; return; }
        if (_pilotVisuals && _selectMode) { BeginSelectionTransform(e, -1); return; }
        BeginAnnotation(canvasPoint);
        e.Handled = true;
    }

    private static bool IsTextInputSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(_canvas);
        if (_objectDragStart.HasValue) { UpdateObjectMove(point); return; }
        if (_movingSelection || _resizeHandle >= 0)
        {
            var delta = point - _transformStart;
            var bounds = new Rect(0, 0, Width, Height);
            SetSelection(_movingSelection ? CaptureSelectionGeometry.Move(_transformBounds, delta, bounds)
                : CaptureSelectionGeometry.Resize(_transformBounds, _resizeHandle, delta, bounds));
            return;
        }
        if (_dragging)
        {
            UpdateSelection(point);
        }
        else if (_drawing)
        {
            UpdatePendingAnnotation(point);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_objectDragStart.HasValue && e.ChangedButton == MouseButton.Left)
        { UpdateObjectMove(e.GetPosition(_canvas)); FinishObjectMove(commit: true); e.Handled = true; return; }
        if (_movingSelection || _resizeHandle >= 0)
        {
            _movingSelection = false; _resizeHandle = -1; ReleaseMouseCapture();
            _toolbar.Visibility = Visibility.Visible; RequestToolbarPosition(); Rebuild(); e.Handled = true; return;
        }
        if (_drawing)
        {
            FinishAnnotation(e.GetPosition(_canvas));
            e.Handled = true;
            return;
        }
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        UpdateSelection(e.GetPosition(_canvas));
        if (_localSelection.Width < 4 || _localSelection.Height < 4)
        {
            ResetSelection();
            return;
        }

        _selectionReady = true;
        _annotationOrigin = _localSelection.TopLeft;
        PositionAnnotationLayer();
        SetHandlesVisibility(Visibility.Visible);
        PositionHandles();
        _toolbar.Visibility = Visibility.Visible;
        RequestToolbarPosition();
        UpdateToolSelection();
        Cursor = _selectMode ? Cursors.SizeAll : Cursors.Cross;
        e.Handled = true;
    }

    private void BeginAnnotation(Point canvasPoint)
    {
        ClearObjectSelection();
        var local = ToAnnotationPoint(canvasPoint);
        if (_tool == CaptureAnnotationKind.Text)
        {
            AddInlineTextEditor(local);
            return;
        }
        if (_tool == CaptureAnnotationKind.Number)
        {
            Add(new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Number,
                Start = local,
                End = local,
                Text = (_nextNumber++).ToString(),
                Argb = _color,
                OutlineArgb = _outlineArgb ?? _color,
                Thickness = _thickness,
                Opacity = _opacity,
                Size = _annotationSize
            });
            return;
        }
        if (_tool == CaptureAnnotationKind.Stamp)
        {
            var contained = local;
            Add(new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Stamp,
                Start = contained,
                End = contained,
                Text = _selectedStamp,
                StampImage = _selectedStampImage,
                Size = _annotationSize
            });
            return;
        }

        _annotationStart = local;
        _drawing = true;
        _pencilPoints.Clear();
        _pencilPoints.Add(local);
        CaptureMouse();
    }

    private void UpdatePendingAnnotation(Point canvasPoint)
    {
        var end = ToAnnotationPoint(canvasPoint);
        if (_tool == CaptureAnnotationKind.Pencil)
        {
            _pencilPoints.Add(end);
        }
        if (_pilotVisuals && _previewClock.ElapsedMilliseconds < 30) return;
        _previewClock.Restart();
        Rebuild(new CaptureAnnotation
        {
            Kind = _tool,
            Start = _annotationStart,
            End = end,
            Points = [.. _pencilPoints],
            Argb = _color,
            OutlineArgb = _tool is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse ? _outlineArgb : _outlineArgb ?? _color,
            FillArgb = _fillArgb,
            Thickness = _thickness,
            Opacity = _tool == CaptureAnnotationKind.Highlighter
                ? Math.Min(_opacity, .38f)
                : _opacity,
            Size = _annotationSize,
            PrivacyStrength = _pilotVisuals ? _privacyStrength : 0
        });
    }

    private void FinishAnnotation(Point canvasPoint)
    {
        _drawing = false;
        ReleaseMouseCapture();
        var end = ToAnnotationPoint(canvasPoint);
        if (_tool == CaptureAnnotationKind.Pencil)
        {
            _pencilPoints.Add(end);
        }
        Add(new CaptureAnnotation
        {
            Kind = _tool,
            Start = _annotationStart,
            End = end,
            Points = [.. _pencilPoints],
            Argb = _color,
            OutlineArgb = _tool is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse ? _outlineArgb : _outlineArgb ?? _color,
            FillArgb = _fillArgb,
            Thickness = _thickness,
            Opacity = _tool == CaptureAnnotationKind.Highlighter
                ? Math.Min(_opacity, .38f)
                : _opacity,
            Size = _annotationSize,
            PrivacyStrength = _pilotVisuals ? _privacyStrength : 0
        });
    }

    private void AddInlineTextEditor(Point point)
    {
        var input = new TextBox
        {
            MinWidth = 180,
            MaxWidth = Math.Max(180, _localSelection.Width - point.X),
            Padding = new Thickness(7, 5, 7, 5),
            FontSize = 16,
            Foreground = Brush(_isDark ? "#FFFFFF" : "#17212B"),
            CaretBrush = Brush(_isDark ? "#FFFFFF" : "#17212B"),
            Background = Brush(_isDark ? "#F0121922" : "#F8FFFFFF"),
            BorderBrush = Brush("#2BC9DA"),
            BorderThickness = new Thickness(1)
        };
        Canvas.SetLeft(input, point.X + _annotationOrigin.X - _localSelection.X);
        Canvas.SetTop(input, point.Y + _annotationOrigin.Y - _localSelection.Y);
        _annotationLayer.Children.Add(input);
        input.Focus();
        input.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitInlineText(input, point);
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                _annotationLayer.Children.Remove(input);
                args.Handled = true;
            }
        };
        input.LostKeyboardFocus += (_, _) =>
        {
            if (_annotationLayer.Children.Contains(input))
            {
                CommitInlineText(input, point);
            }
        };
    }

    private void CommitInlineText(TextBox input, Point point)
    {
        _annotationLayer.Children.Remove(input);
        if (!string.IsNullOrWhiteSpace(input.Text))
        {
            Add(new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Text,
                Start = point,
                End = point,
                Text = input.Text.Trim(),
                Argb = _color,
                OutlineArgb = _outlineArgb ?? _color,
                Thickness = _thickness,
                Opacity = _opacity,
                Size = _annotationSize,
                Bold = _textBold, FontFamily = _textFont
            });
        }
    }

    private void Add(CaptureAnnotation annotation)
    {
        _annotationHistory.Add(annotation);
        Rebuild();
        UpdateHistoryButtons();
    }

    private void Undo()
    {
        ClearObjectSelection();
        if (_annotationHistory.Undo()) Rebuild();
        UpdateHistoryButtons();
    }

    private void Redo()
    {
        ClearObjectSelection();
        if (_annotationHistory.Redo()) Rebuild();
        UpdateHistoryButtons();
    }

    private void ClearAllAnnotations()
    {
        ClearObjectSelection();
        if (_annotationHistory.ClearAll()) Rebuild();
        UpdateHistoryButtons();
    }

    private void Rebuild(CaptureAnnotation? pending = null)
    {
        _annotationLayer.Children.Clear();
        if (_pilotVisuals && _selectionReady)
        {
            using var bitmap = RenderSelection(pending);
            _annotationLayer.Children.Add(new Image { Source = CaptureBitmapSource.Create(bitmap),
                Width = _localSelection.Width, Height = _localSelection.Height, Stretch = Stretch.Fill, IsHitTestVisible = false });
            ShowObjectSelection();
            return;
        }
        foreach (var annotation in _annotationHistory.Items)
        {
            AddVisual(annotation);
        }
        if (pending is not null)
        {
            AddVisual(pending);
        }
        ShowObjectSelection();
    }

    private void AddVisual(CaptureAnnotation annotation)
    {
        var color = DrawingColor.FromArgb(annotation.OutlineArgb ?? annotation.Argb);
        var brush = new SolidColorBrush(
            Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Opacity = Math.Clamp(annotation.Opacity, 0, 1);
        var thickness = annotation.Kind == CaptureAnnotationKind.Highlighter
            ? annotation.Thickness * 4
            : annotation.Thickness;
        if (annotation.Kind == CaptureAnnotationKind.Highlighter)
        {
            brush.Opacity = annotation.Opacity >= .99 ? .38 : annotation.Opacity;
        }

        switch (annotation.Kind)
        {
            case CaptureAnnotationKind.Arrow:
            case CaptureAnnotationKind.Highlighter:
            case CaptureAnnotationKind.Line:
                _annotationLayer.Children.Add(new Line
                {
                    X1 = annotation.Start.X,
                    Y1 = annotation.Start.Y,
                    X2 = annotation.End.X,
                    Y2 = annotation.End.Y,
                    Stroke = brush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                });
                if (annotation.Kind == CaptureAnnotationKind.Arrow)
                {
                    _annotationLayer.Children.Add(ArrowHead(annotation, brush));
                }
                break;
            case CaptureAnnotationKind.Rectangle:
            case CaptureAnnotationKind.Ellipse:
                var shape = annotation.Kind == CaptureAnnotationKind.Rectangle
                    ? (Shape)new Rectangle()
                    : new Ellipse();
                shape.Stroke = annotation.OutlineArgb.HasValue || !annotation.FillArgb.HasValue
                    ? brush
                    : Brushes.Transparent;
                if (annotation.FillArgb is int fillArgb)
                {
                    var fillColor = DrawingColor.FromArgb(fillArgb);
                    shape.Fill = new SolidColorBrush(Color.FromArgb(
                        (byte)Math.Round(fillColor.A * Math.Clamp(annotation.Opacity, 0, 1)),
                        fillColor.R,
                        fillColor.G,
                        fillColor.B));
                }
                shape.StrokeThickness = thickness;
                shape.Width = Math.Abs(annotation.End.X - annotation.Start.X);
                shape.Height = Math.Abs(annotation.End.Y - annotation.Start.Y);
                shape.IsHitTestVisible = false;
                Canvas.SetLeft(shape, Math.Min(annotation.Start.X, annotation.End.X));
                Canvas.SetTop(shape, Math.Min(annotation.Start.Y, annotation.End.Y));
                _annotationLayer.Children.Add(shape);
                break;
            case CaptureAnnotationKind.Pencil:
                _annotationLayer.Children.Add(new Polyline
                {
                    Points = new PointCollection(annotation.Points),
                    Stroke = brush,
                    StrokeThickness = thickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                });
                break;
            case CaptureAnnotationKind.Text:
                var text = new TextBlock
                {
                    Text = annotation.Text,
                    Foreground = brush,
                    FontSize = annotation.Size,
                    FontWeight = annotation.Bold ? FontWeights.Bold : FontWeights.Normal,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(text, annotation.Start.X);
                Canvas.SetTop(text, annotation.Start.Y);
                _annotationLayer.Children.Add(text);
                break;
            case CaptureAnnotationKind.Number:
                var badge = new Border
                {
                    Width = annotation.Size,
                    Height = annotation.Size,
                    CornerRadius = new CornerRadius(annotation.Size / 2),
                    Background = brush,
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = annotation.Text,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Canvas.SetLeft(badge, annotation.Start.X - annotation.Size / 2);
                Canvas.SetTop(badge, annotation.Start.Y - annotation.Size / 2);
                _annotationLayer.Children.Add(badge);
                break;
            case CaptureAnnotationKind.Stamp:
                var stamp = EmojiImage(annotation.Text, annotation.Size);
                Canvas.SetLeft(stamp, annotation.Start.X - annotation.Size / 2);
                Canvas.SetTop(stamp, annotation.Start.Y - annotation.Size / 2);
                _annotationLayer.Children.Add(stamp);
                break;
        }
    }

    private static Polygon ArrowHead(CaptureAnnotation annotation, Brush brush)
    {
        var angle = Math.Atan2(
            annotation.End.Y - annotation.Start.Y,
            annotation.End.X - annotation.Start.X);
        const double length = 15;
        var left = new Point(
            annotation.End.X - length * Math.Cos(angle - Math.PI / 6),
            annotation.End.Y - length * Math.Sin(angle - Math.PI / 6));
        var right = new Point(
            annotation.End.X - length * Math.Cos(angle + Math.PI / 6),
            annotation.End.Y - length * Math.Sin(angle + Math.PI / 6));
        return new Polygon
        {
            Points = new PointCollection([annotation.End, left, right]),
            Fill = brush,
            IsHitTestVisible = false
        };
    }

    private void UpdateToolSelection()
    {
        ClearObjectSelection();
        foreach (var (tool, button) in _toolButtons)
        {
            var representsShape = tool == CaptureAnnotationKind.Rectangle &&
                (_tool is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse or CaptureAnnotationKind.Line or CaptureAnnotationKind.Arrow ||
                 _pilotVisuals && _tool == CaptureAnnotationKind.Number);
            button.IsChecked = !_selectMode && (tool == _tool || representsShape);
            if (button.Content is LabIcon labIcon) labIcon.SetResourceReference(LabIcon.ForegroundProperty,
                button.IsChecked == true ? "Lab.accent-text" : "Lab.text");
            if (button.Content is Path icon)
            {
                icon.Stroke = button.IsChecked == true
                    ? ResourceBrush("CaptureToolbarAccentBrush")
                    : ResourceBrush("CaptureToolbarTextBrush");
            }
        }
        if (_selectButton is not null) _selectButton.IsChecked = _selectMode;
        SetHandlesVisibility(!_selectionReady || _pilotVisuals && !_selectMode ? Visibility.Collapsed : Visibility.Visible);
        _annotationLayer.Cursor = _selectMode ? Cursors.SizeAll : _tool == CaptureAnnotationKind.Text ? Cursors.IBeam : Cursors.Cross;
        if (_compactToolbar || _pilotVisuals)
        {
            ApplyToolbarDensity(compact: _compactToolbar);
            RequestToolbarPosition();
        }
        UpdateHistoryButtons();
        if (_pilotVisuals) { RequestToolbarPosition(); }
    }

    private void ApplyToolbarDensity(bool compact)
    {
        _compactToolbar = compact;
        if (_pilotVisuals)
        {
            foreach (var (tool, button) in _toolButtons)
                button.Visibility = !compact || tool is CaptureAnnotationKind.Blur or CaptureAnnotationKind.Pixelate ||
                    (!_selectMode && RepresentsActiveTool(tool)) ? Visibility.Visible : Visibility.Collapsed;
            if (_selectButton is not null) _selectButton.Visibility = Visibility.Visible;
            _eraseButton.Visibility = Visibility.Visible; _reselectButton.Visibility = Visibility.Visible;
            _overflowButton.Visibility = _toolButtons.Values.Any(b => b.Visibility != Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (container, tools) in _pilotGroups)
                container.Visibility = tools.Children.Cast<UIElement>().Any(c => c.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        foreach (var (tool, button) in _toolButtons)
        {
            button.Visibility = !compact || !_selectMode && RepresentsActiveTool(tool)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (_selectButton is not null) _selectButton.Visibility = compact && !_selectMode ? Visibility.Collapsed : Visibility.Visible;
        _eraseButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _reselectButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _captureSeparator.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _actionSeparator.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _overflowButton.Visibility = compact || _pilotVisuals ? Visibility.Visible : Visibility.Collapsed;
        if (_pilotVisuals)
            foreach (var (container, tools) in _pilotGroups)
                container.Visibility = tools.Children.Cast<UIElement>().Any(c => c.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool RepresentsActiveTool(CaptureAnnotationKind toolbarTool) =>
        toolbarTool == _tool ||
        toolbarTool == CaptureAnnotationKind.Rectangle &&
        (_tool is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse or CaptureAnnotationKind.Line ||
            _pilotVisuals && _tool is CaptureAnnotationKind.Arrow or CaptureAnnotationKind.Number);

    private void UpdateHistoryButtons()
    {
        if (_undoButton is null || _redoButton is null || _eraseButton is null) return;
        _undoButton.IsEnabled = _annotationHistory.CanUndo;
        _redoButton.IsEnabled = _annotationHistory.CanRedo;
        _eraseButton.IsEnabled = _annotationHistory.Items.Count > 0;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsTextInputSource(e.OriginalSource as DependencyObject)) return;
        if (_handles.Contains(e.OriginalSource as Border)) return;
        if (_pilotVisuals && IsToolbarSource(e.OriginalSource as DependencyObject) &&
            e.Key is Key.Enter or Key.Space or Key.Left or Key.Right or Key.Up or Key.Down) return;
        if (HandleObjectMoveKey(e)) return;
        if (_pilotVisuals && _selectionReady && _selectMode && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var increment = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var delta = new Vector(e.Key == Key.Left ? -increment : e.Key == Key.Right ? increment : 0,
                e.Key == Key.Up ? -increment : e.Key == Key.Down ? increment : 0);
            SetSelection(CaptureSelectionGeometry.Move(_localSelection, delta, new Rect(0, 0, Width, Height)));
            Rebuild(); RequestToolbarPosition(); e.Handled = true; return;
        }
        if (e.Key == Key.Escape)
        {
            if (_contextWindow is not null)
            {
                HideContextWindow();
                e.Handled = true;
                return;
            }
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter &&
                 _selectionReady &&
                 e.OriginalSource is not TextBox)
        {
            Complete(CaptureEditorOutput.Default);
            e.Handled = true;
        }
        else if (e.Key == Key.R &&
                 _selectionReady &&
                 Keyboard.Modifiers == ModifierKeys.None)
        {
            ResetSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Z &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Redo();
            e.Handled = true;
        }
    }

    private void Complete(CaptureEditorOutput requestedOutput)
    {
        if (!_selectionReady)
        {
            return;
        }

        if (_objectDragStart.HasValue) FinishObjectMove(commit: true);

        _toolbarWindow.Hide();
        HideContextWindow();
        RequestedOutput = requestedOutput;
        using var crop = CropFrozenSelection();
        EditedBitmap?.Dispose();
        EditedBitmap = _pilotVisuals ? RenderSelection() : CaptureAnnotationRenderer.Render(
            crop, _annotationHistory.Items, _localSelection.Width, _localSelection.Height);
        if (_pilotVisuals) { CompletedSession?.Dispose(); CompletedSession = CreateRegionSession(); }
        DialogResult = true;
    }

    private DrawingBitmap CropFrozenSelection()
    {
        var scaleX = _desktopBitmap.Width / Math.Max(1d, Width);
        var scaleY = _desktopBitmap.Height / Math.Max(1d, Height);
        var left = Math.Clamp(
            (int)Math.Round(_localSelection.Left * scaleX),
            0,
            _desktopBitmap.Width - 1);
        var top = Math.Clamp(
            (int)Math.Round(_localSelection.Top * scaleY),
            0,
            _desktopBitmap.Height - 1);
        var width = Math.Clamp(
            (int)Math.Round(_localSelection.Width * scaleX),
            1,
            _desktopBitmap.Width - left);
        var height = Math.Clamp(
            (int)Math.Round(_localSelection.Height * scaleY),
            1,
            _desktopBitmap.Height - top);
        return _desktopBitmap.Clone(
            new System.Drawing.Rectangle(left, top, width, height),
            DrawingPixelFormat.Format32bppArgb);
    }

    private void ResetSelection()
    {
        ClearObjectSelection();
        _dragging = false;
        _drawing = false;
        _selectionReady = false;
        _movingSelection = false; _resizeHandle = -1;
        _previewDocument?.Dispose(); _previewDocument = null; _previewAnnotations = [];
        ReleaseMouseCapture();
        _selection.Visibility = Visibility.Collapsed;
        _annotationLayer.Visibility = Visibility.Collapsed;
        _annotationLayer.Children.Clear();
        _sizeBadge.Visibility = Visibility.Collapsed;
        _toolbarWindow.Hide();
        SetHandlesVisibility(Visibility.Collapsed);
        _annotationHistory.Reset();
        HideContextWindow();
        UpdateHistoryButtons();
        _nextNumber = 1;
        Cursor = Cursors.Cross;
        UpdateShade(default);
    }

    private void UpdateSelection(Point end)
    {
        _localSelection = Normalize(_start, new Point(Math.Clamp(end.X, 0, Width), Math.Clamp(end.Y, 0, Height)));
        RefreshSelectionVisual();
    }

    private void RefreshSelectionVisual()
    {
        Canvas.SetLeft(_selection, _localSelection.Left);
        Canvas.SetTop(_selection, _localSelection.Top);
        _selection.Width = _localSelection.Width;
        _selection.Height = _localSelection.Height;
        UpdateShade(_localSelection);

        if (_sizeBadge.Child is TextBlock size)
        {
            var scaleX = _desktopBitmap.Width / Math.Max(1d, Width);
            var scaleY = _desktopBitmap.Height / Math.Max(1d, Height);
            size.Text =
                $"{Math.Round(_localSelection.Width * scaleX):N0} × " +
                $"{Math.Round(_localSelection.Height * scaleY):N0}";
        }
        Canvas.SetLeft(_sizeBadge, _localSelection.Left);
        Canvas.SetTop(
            _sizeBadge,
            _localSelection.Top > 40
                ? _localSelection.Top - 34
                : _localSelection.Bottom + 8);
    }

    private void PositionAnnotationLayer()
    {
        Canvas.SetLeft(_annotationLayer, _localSelection.Left);
        Canvas.SetTop(_annotationLayer, _localSelection.Top);
        _annotationLayer.Width = _localSelection.Width;
        _annotationLayer.Height = _localSelection.Height;
        _annotationLayer.Visibility = Visibility.Visible;
    }

    private Point ToAnnotationPoint(Point canvasPoint) => new(
        Math.Clamp(canvasPoint.X, _localSelection.Left, _localSelection.Right) - _annotationOrigin.X,
        Math.Clamp(canvasPoint.Y, _localSelection.Top, _localSelection.Bottom) - _annotationOrigin.Y);

    private void UpdateShade(Rect clear)
    {
        var width = Math.Max(0, ActualWidth > 0 ? ActualWidth : Width);
        var height = Math.Max(0, ActualHeight > 0 ? ActualHeight : Height);
        if (clear.IsEmpty || clear.Width <= 0 || clear.Height <= 0)
        {
            Place(_shades[0], 0, 0, width, height);
            for (var index = 1; index < _shades.Length; index++)
            {
                Place(_shades[index], 0, 0, 0, 0);
            }
            return;
        }

        Place(_shades[0], 0, 0, width, clear.Top);
        Place(_shades[1], 0, clear.Bottom, width, height - clear.Bottom);
        Place(_shades[2], 0, clear.Top, clear.Left, clear.Height);
        Place(
            _shades[3],
            clear.Right,
            clear.Top,
            width - clear.Right,
            clear.Height);
    }

    private void PositionHandles()
    {
        var x = new[]
        {
            _localSelection.Left,
            _localSelection.Left + (_localSelection.Width / 2),
            _localSelection.Right
        };
        var y = new[]
        {
            _localSelection.Top,
            _localSelection.Top + (_localSelection.Height / 2),
            _localSelection.Bottom
        };
        var positions = new[]
        {
            new Point(x[0], y[0]), new Point(x[1], y[0]),
            new Point(x[2], y[0]), new Point(x[0], y[1]),
            new Point(x[2], y[1]), new Point(x[0], y[2]),
            new Point(x[1], y[2]), new Point(x[2], y[2])
        };
        for (var index = 0; index < _handles.Length; index++)
        {
            Canvas.SetLeft(_handles[index], positions[index].X - 5);
            Canvas.SetTop(_handles[index], positions[index].Y - 5);
        }
    }

    private void RequestToolbarPosition()
    {
        if (!_selectionReady || _toolbarPositionPending)
        {
            return;
        }
        _toolbarPositionPending = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _toolbarPositionPending = false;
            PositionToolbarAfterLayout();
        }));
    }

    private void PositionToolbarAfterLayout()
    {
        if (!_selectionReady)
        {
            return;
        }

        var selectionPixels = SelectionInPhysicalPixels();
        var monitor = MonitorWorkAreaProvider.FromSelection(selectionPixels);
        _activeMonitor = monitor;
        var marginPixels = Math.Max(1, 12 * monitor.DpiScaleX);
        var maximumWidthPixels = Math.Max(1, monitor.WorkAreaPixels.Width - (marginPixels * 2));
        var framePadding = _pilotVisuals ? 16d : 0d;
        var maximumWidthDips = Math.Max(1, maximumWidthPixels / monitor.DpiScaleX - framePadding);

        // In small work areas, hidden tools move to More options; text extraction stays visible.
        ApplyToolbarDensity(compact: false);
        _toolbar.Width = double.NaN; _toolbarLayout.Width = double.NaN;
        _toolbar.MaxWidth = double.PositiveInfinity;
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        ApplyToolbarDensity(compact: _pilotVisuals ? _toolbar.DesiredSize.Width > maximumWidthDips :
            CaptureToolbarLayoutPolicy.ShouldUseCompactMode(maximumWidthDips));

        _toolbar.Width = double.NaN;
        _toolbar.MaxWidth = double.PositiveInfinity;
        _toolbarLayout.Width = double.NaN;
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var naturalDips = _toolbar.DesiredSize;

        var finalWidthDips = Math.Min(naturalDips.Width, maximumWidthDips);
        _toolbar.Width = finalWidthDips;
        _toolbar.MaxWidth = finalWidthDips;
        _toolbarLayout.Width = Math.Max(1, finalWidthDips -
            _toolbar.Padding.Left - _toolbar.Padding.Right -
            _toolbar.BorderThickness.Left - _toolbar.BorderThickness.Right);
        _toolbarWindow.Width = finalWidthDips + framePadding;
        var entering = !_toolbarWindow.IsVisible;
        _toolbarWindow.Opacity = 1;
        if (!_toolbarWindow.IsVisible)
        {
            _toolbarWindow.Show();
        }
        _toolbarWindow.UpdateLayout();

        var finalDips = new Size(
            Math.Max(1, _toolbar.ActualWidth + framePadding),
            Math.Max(1, _toolbar.ActualHeight + framePadding));
        var finalPixels = new Size(
            Math.Ceiling(finalDips.Width * monitor.DpiScaleX),
            Math.Ceiling(finalDips.Height * monitor.DpiScaleY));
        var placement = ToolbarPlacementCalculator.Calculate(
            selectionPixels,
            monitor.WorkAreaPixels,
            finalPixels,
            (naturalDips.Width + framePadding) * monitor.DpiScaleX,
            dpiScale: Math.Max(monitor.DpiScaleX, monitor.DpiScaleY));
        _toolbarBoundsPixels = placement.Bounds;

        var handle = new WindowInteropHelper(_toolbarWindow).Handle;
        _ = SetWindowPos(
            handle,
            new nint(-1),
            (int)Math.Round(placement.Bounds.Left),
            (int)Math.Round(placement.Bounds.Top),
            Math.Max(1, (int)Math.Ceiling(placement.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(placement.Bounds.Height)),
            SwpNoActivate | SwpShowWindow);
        _toolbarWindow.Opacity = 1;
        if (_pilotVisuals && entering) LabMotion.PlayEntrance(_toolbar);
        if (_contextWindow is not null) PositionContextWindow();

        SafeDiagnosticLog.Write("capture.toolbar-positioned", new Dictionary<string, object?>
        {
            ["selectionPixels"] = RectDescription(selectionPixels),
            ["workAreaPixels"] = RectDescription(monitor.WorkAreaPixels),
            ["dpiScaleX"] = monitor.DpiScaleX,
            ["dpiScaleY"] = monitor.DpiScaleY,
            ["naturalWidthDips"] = naturalDips.Width,
            ["finalWidthDips"] = finalDips.Width,
            ["finalHeightDips"] = finalDips.Height,
            ["finalBoundsPixels"] = RectDescription(placement.Bounds),
            ["placement"] = placement.Side.ToString(),
            ["layoutMode"] = placement.Mode.ToString(),
            ["expectedRows"] = placement.ExpectedRows
        });
    }

    private Rect SelectionInPhysicalPixels()
    {
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var canvasWidth = Math.Max(1, _canvas.ActualWidth);
        var canvasHeight = Math.Max(1, _canvas.ActualHeight);
        var scaleX = virtualScreen.Width / canvasWidth;
        var scaleY = virtualScreen.Height / canvasHeight;
        return new Rect(
            virtualScreen.Left + (_localSelection.Left * scaleX),
            virtualScreen.Top + (_localSelection.Top * scaleY),
            _localSelection.Width * scaleX,
            _localSelection.Height * scaleY);
    }

    private static string RectDescription(Rect rectangle) =>
        $"{rectangle.Left:0},{rectangle.Top:0},{rectangle.Width:0},{rectangle.Height:0}";

    private void SetHandlesVisibility(Visibility visibility)
    {
        foreach (var handle in _handles)
        {
            handle.Visibility = visibility;
        }
    }

    private bool IsToolbarSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, _toolbar))
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static Rectangle Shade() => new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(150, 3, 12, 22)),
        IsHitTestVisible = false
    };

    private static Border Handle() => new()
    {
        Width = 10,
        Height = 10,
        CornerRadius = new CornerRadius(5),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(10, 169, 187)),
        BorderThickness = new Thickness(2),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false
    };

    private static void Place(
        FrameworkElement element,
        double left,
        double top,
        double width,
        double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X),
        Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X),
        Math.Abs(end.Y - start.Y));

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private static DrawingBitmap CaptureVirtualDesktopBitmap() =>
        CaptureVirtualDesktopBitmap(false);

    private static DrawingBitmap CaptureVirtualDesktopBitmap(bool includeCursor)
    {
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        return CaptureService.CaptureBitmap(virtualScreen, includeCursor);
    }

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
            _ = DeleteObject(handle);
        }
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
