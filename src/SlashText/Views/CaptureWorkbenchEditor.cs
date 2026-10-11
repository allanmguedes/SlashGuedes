using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SlashText.Services;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace SlashText.Views;

/// <summary>One in-page image session in normal and expanded layouts.</summary>
public sealed class CaptureWorkbenchEditor : UserControl, IDisposable
{
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent };
    private readonly Grid _surface = new() { ClipToBounds = true };
    private readonly CaptureOcrPulse _ocrPulse = new();
    private readonly Viewbox _viewbox = new() { Stretch = Stretch.Uniform };
    private readonly ScrollViewer _viewport = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        CanContentScroll = false
    };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(24) };
    private readonly List<Point> _pencilPoints = [];
    private CaptureEditorDocument? _document;
    private DrawingBitmap? _previewBitmap;
    private BitmapSource? _previewSource;
    private CaptureAnnotation? _pending;
    private CaptureAnnotationKind? _tool;
    private Point _start;
    private Point? _panStart;
    private double _panLeft, _panTop, _zoom = 1d;
    private bool _drawing, _cropTool;
    private Rect? _crop;
    private int _color = DrawingColor.FromArgb(232, 78, 96).ToArgb();
    private float _thickness = 4;
    private long? _selectedAnnotationId;
    private Point? _objectDragStart;
    private Vector _objectDragDelta;
    public CaptureEditableAnnotation? SelectedAnnotation => _document?.EditableAnnotations().FirstOrDefault(a => a.Id == _selectedAnnotationId);

    public event EventHandler? StateChanged;
    public event EventHandler? SaveCopyRequested;
    public event EventHandler? ExitRequested;
    public bool HasImage => _document is not null;
    public bool CanUndo => _document?.CanUndo == true;
    public bool CanRedo => _document?.CanRedo == true;
    public bool HasUnsavedChanges => _document?.HasUnsavedChanges == true;
    public bool HasPendingCrop => _crop.HasValue;
    public bool IsCropTool => _cropTool;
    public CaptureAnnotationKind? SelectedTool => _tool;
    public CaptureEditorDocument? Document => _document;
    public double Zoom => _zoom;
    public int InkArgb => _color;
    public float InkThickness => _thickness;
    public string AnnotationText { get; set; } = "Anotação";
    public string TextFontFamily { get; set; } = "Segoe UI";
    public float TextSize { get; set; } = 24;
    public bool TextBold { get; set; }
    public bool TextItalic { get; set; }
    public string TextAlignment { get; set; } = "Left";
    private string _selectedStamp = "👍";
    private CaptureStampImage? _selectedStampImage;
    public string SelectedStamp
    {
        get => _selectedStamp;
        set { var asset = CaptureStampCatalog.Current.GetImage(value); _selectedStamp = value; _selectedStampImage = asset; }
    }
    public float StampSize { get; set; } = 48;
    public int PrivacyStrength { get; set; } = 16;
    public int? ShapeFill { get; set; }
    public bool ShapeOutline { get; set; } = true;
    public float AnnotationOpacity { get; set; } = 1;

    public CaptureWorkbenchEditor()
    {
        Focusable = true;
        _surface.Children.Add(_image);
        _surface.Children.Add(_overlay);
        _surface.Children.Add(_ocrPulse);
        _overlay.MouseLeftButtonDown += PointerDown;
        _overlay.MouseMove += PointerMove;
        _overlay.MouseLeftButtonUp += PointerUp;
        _overlay.LostMouseCapture += (_, _) => { if (_drawing || _panStart.HasValue || _objectDragStart.HasValue) CancelGesture(); };
        _viewbox.Child = _surface;
        _viewbox.HorizontalAlignment = HorizontalAlignment.Center;
        _viewbox.VerticalAlignment = VerticalAlignment.Center;
        _viewport.Content = _viewbox;
        _viewport.SizeChanged += (_, _) => UpdateZoom();
        Content = _viewport;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += (_, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !HasImage) return;
            SetZoom(Math.Clamp(_zoom + (e.Delta > 0 ? .25 : -.25), .25, 4));
            e.Handled = true;
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (_pending is not null && _previewBitmap is not null)
            {
                using var rendered = CaptureAnnotationRenderer.Render(
                    _previewBitmap, [_pending], _previewBitmap.Width, _previewBitmap.Height);
                _image.Source = ToBitmapSource(rendered);
            }
        };
    }

    public void SetZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom < .25 || zoom > 4)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        _zoom = zoom;
        UpdateZoom();
        NotifyStateChanged();
    }

    private void UpdateZoom()
    {
        if (!HasImage || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return;
        var width = Math.Max(1d, _viewport.ActualWidth - SystemParameters.VerticalScrollBarWidth);
        var height = Math.Max(1d, _viewport.ActualHeight - SystemParameters.HorizontalScrollBarHeight);
        var fit = Math.Min(width / _surface.Width, height / _surface.Height);
        _viewbox.Width = _surface.Width * fit * _zoom;
        _viewbox.Height = _surface.Height * fit * _zoom;
        if (HasPendingCrop) ShowCrop(); else ShowSelection();
    }

    public void LoadImage(string path)
    {
        using var source = new DrawingBitmap(path);
        // Never flatten animation into an editable still image.
        if (source.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Gif.Guid)
            throw new NotSupportedException(UiText.Get("GIF é uma gravação; sua edição ficará para outra etapa."));
        LoadImage(source);
    }

    public void LoadImage(DrawingBitmap bitmap)
    {
        var next = new CaptureEditorDocument(bitmap);
        _document?.Dispose();
        _document = next;
        _selectedAnnotationId = null;
        _tool = null;
        _cropTool = false;
        _crop = null;
        CancelGesture();
        RefreshPreview();
        _viewport.ScrollToHome();
        NotifyStateChanged();
    }

    public void LoadSession(CaptureRegionSession session)
    {
        LoadImage(session.Source);
        foreach (var annotation in session.Annotations) _document!.AddAnnotation(annotation);
        _document!.MarkSaved(); RefreshPreview(); NotifyStateChanged();
    }

    public void Clear()
    {
        _selectedAnnotationId = null;
        CancelGesture();
        _document?.Dispose(); _document = null;
        _previewBitmap?.Dispose(); _previewBitmap = null;
        _image.Source = null; _previewSource = null;
        _overlay.Children.Clear();
        _tool = null; _cropTool = false; _crop = null;
        NotifyStateChanged();
    }

    public void SelectTool(CaptureAnnotationKind? tool)
    {
        _selectedAnnotationId = null;
        CancelGesture(); _crop = null; _cropTool = false; _tool = tool;
        _overlay.Cursor = tool switch { null => Cursors.Hand, CaptureAnnotationKind.Text => Cursors.IBeam, _ => Cursors.Cross };
        _overlay.Children.Clear(); NotifyStateChanged();
    }
    public void SelectCrop()
    {
        SelectTool(null); _cropTool = true; _overlay.Cursor = Cursors.Cross; NotifyStateChanged();
    }
    public void SetColor(int argb) { _color = argb; if (ShapeFill.HasValue) ShapeFill = argb; NotifyStateChanged(); }
    public void SetThickness(float thickness)
    {
        var next = Math.Clamp(thickness, 1, 24);
        if (Math.Abs(_thickness - next) < .001f) return;
        _thickness = next; NotifyStateChanged();
    }
    public void Undo() { CancelCrop(); _document?.Undo(); RefreshSelection(); RefreshPreview(); NotifyStateChanged(); }
    public void Redo() { CancelCrop(); _document?.Redo(); RefreshSelection(); RefreshPreview(); NotifyStateChanged(); }
    public void MarkSaved() { _document?.MarkSaved(); NotifyStateChanged(); }
    public void DiscardChanges() { CancelCrop(); _document?.DiscardChanges(); RefreshSelection(); RefreshPreview(); NotifyStateChanged(); }
    public DrawingBitmap Render()
    {
        if (_document is null) throw new InvalidOperationException(UiText.Get("Nenhuma imagem carregada."));
        if (HasPendingCrop) throw new InvalidOperationException(UiText.Get("Aplique ou cancele o recorte antes de copiar/salvar."));
        return _document.Render();
    }
    public void SetOcrReading(bool reading)
    {
        if (reading) { CancelGesture(); _ocrPulse.Start(); }
        else _ocrPulse.Stop();
    }
    public bool ApplyCrop()
    {
        if (_document is null || !_crop.HasValue || !_document.Crop(_crop.Value)) return false;
        CancelCrop(); _cropTool = false; RefreshSelection(); RefreshPreview(); NotifyStateChanged(); return true;
    }
    public void CancelCrop()
    {
        CancelGesture(); _crop = null; _overlay.Children.Clear(); NotifyStateChanged();
    }
    public void ResizeImage(int width, int height)
    {
        if (_document is null) return;
        CancelCrop(); _document.Resize(width, height); RefreshSelection(); RefreshPreview(); NotifyStateChanged();
    }

    public bool SelectAnnotation(long id)
    {
        if (_document?.EditableAnnotations().FirstOrDefault(a => a.Id == id) is not { Bounds.IsEmpty: false }) return false;
        SelectTool(null); _selectedAnnotationId = id; RefreshSelection(); ShowSelection(); NotifyStateChanged(); return true;
    }
    private void RefreshSelection()
    {
        if (SelectedAnnotation is not { } view) { _selectedAnnotationId = null; return; }
        var a = view.Annotation; _color = a.OutlineArgb ?? a.FillArgb ?? a.Argb; _thickness = a.Thickness;
        AnnotationText = a.Text; TextFontFamily = a.FontFamily; TextSize = a.Size; TextBold = a.Bold; TextItalic = a.Italic;
        TextAlignment = a.Alignment; StampSize = a.Size; _selectedStamp = a.Text; _selectedStampImage = a.StampImage;
        PrivacyStrength = a.PrivacyStrength; ShapeFill = a.FillArgb; ShapeOutline = a.OutlineArgb.HasValue; AnnotationOpacity = a.Opacity;
    }
    public bool ApplySelectedProperties(double? width = null, double? height = null)
    {
        if (SelectedAnnotation is not { } view || _document is null) return false;
        var a = view.Annotation;
        var value = a with { Argb = _color, OutlineArgb = ShapeOutline ? _color : null, FillArgb = ShapeFill,
            Thickness = _thickness, Opacity = AnnotationOpacity, Bold = TextBold, Italic = TextItalic,
            FontFamily = TextFontFamily, Alignment = TextAlignment, PrivacyStrength = PrivacyStrength,
            Text = a.Kind == CaptureAnnotationKind.Stamp ? _selectedStamp : a.Kind is CaptureAnnotationKind.Text or CaptureAnnotationKind.Number ? AnnotationText : a.Text,
            StampImage = a.Kind == CaptureAnnotationKind.Stamp ? _selectedStampImage : a.StampImage,
            Size = a.Kind == CaptureAnnotationKind.Stamp ? StampSize : TextSize };
        if (width.HasValue && height.HasValue && a.Kind is CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse or CaptureAnnotationKind.Blur or CaptureAnnotationKind.Pixelate)
        {
            if (!double.IsFinite(width.Value) || !double.IsFinite(height.Value) || width.Value < 1 || height.Value < 1 || width.Value > 32000 || height.Value > 32000) return false;
            value = value with { End = new Point(a.Start.X + (a.End.X < a.Start.X ? -width.Value : width.Value),
                a.Start.Y + (a.End.Y < a.Start.Y ? -height.Value : height.Value)) };
        }
        if (!_document.UpdateAnnotation(view.Id, value)) return false;
        RefreshSelection(); RefreshPreview(); NotifyStateChanged(); return true;
    }
    public bool DeleteSelectedAnnotation()
    {
        if (_selectedAnnotationId is not long id || _document?.DeleteAnnotation(id) != true) return false;
        _selectedAnnotationId = null; RefreshPreview(); NotifyStateChanged(); return true;
    }
    private void ShowSelection()
    {
        _overlay.Children.Clear(); if (SelectedAnnotation is not { Bounds.IsEmpty: false } view) return;
        var bounds = view.Bounds; bounds.Offset(_objectDragDelta.X, _objectDragDelta.Y);
        var outline = new Rectangle { Width = bounds.Width, Height = bounds.Height, StrokeThickness = 2 / Math.Max(.05, _viewbox.Width / _surface.Width),
            StrokeDashArray = new DoubleCollection([4, 3]), IsHitTestVisible = false };
        outline.SetResourceReference(Shape.StrokeProperty, "Lab.accent");
        Canvas.SetLeft(outline, bounds.X); Canvas.SetTop(outline, bounds.Y); _overlay.Children.Add(outline);
    }

    public void InsertAnnotation(CaptureAnnotation annotation)
    {
        if (_document is null) throw new InvalidOperationException(UiText.Get("Nenhuma imagem carregada."));
        _document.AddAnnotation(annotation); RefreshPreview(); NotifyStateChanged();
    }
    public void InsertStamp(string value, Point center, float size = 48)
    {
        var asset = value == _selectedStamp && _selectedStampImage is not null ? _selectedStampImage : CaptureStampCatalog.Current.GetImage(value);
        if (asset is null && !NotoEmojiCatalog.TryGet(value, out _)) throw new ArgumentException(UiText.Get("Emoji não disponível."), nameof(value));
        InsertAnnotation(new CaptureAnnotation { Kind = CaptureAnnotationKind.Stamp, Start = Clamp(center), End = Clamp(center), Text = value, Size = size, StampImage = asset });
    }

    private void PointerDown(object sender, MouseButtonEventArgs e)
    {
        if (_document is null || e.ChangedButton != MouseButton.Left) return;
        Focus(); e.Handled = true;
        _start = Clamp(e.GetPosition(_overlay));
        if (_cropTool) { _drawing = true; _crop = new Rect(_start, _start); _overlay.CaptureMouse(); return; }
        if (TrySelectAnnotationAt(_start, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            _objectDragStart = _start; _objectDragDelta = new Vector();
            ShowSelection(); _overlay.CaptureMouse(); return;
        }
        if (_tool is null)
        {
            _selectedAnnotationId = null; _overlay.Children.Clear(); NotifyStateChanged();
            _panStart = e.GetPosition(_viewport); _panLeft = _viewport.HorizontalOffset; _panTop = _viewport.VerticalOffset;
            _overlay.CaptureMouse(); return;
        }
        if (_tool == CaptureAnnotationKind.Text)
        {
            if (!string.IsNullOrWhiteSpace(AnnotationText)) InsertAnnotation(CreateAnnotation(_start));
            return;
        }
        if (_tool == CaptureAnnotationKind.Stamp) { InsertStamp(SelectedStamp, _start, StampSize); return; }
        if (_tool == CaptureAnnotationKind.Number) { InsertAnnotation(CreateAnnotation(_start)); return; }
        _drawing = true; _pencilPoints.Clear(); _pencilPoints.Add(_start); _overlay.CaptureMouse();
    }

    internal bool TrySelectAnnotationAt(Point point, bool navigate = false)
    {
        if (_document is null || _cropTool || navigate) return false;
        var hit = _document.HitTestAnnotation(point, 4 / Math.Max(.05, _viewbox.Width / _surface.Width));
        if (hit is null || _tool is not null &&
            (_tool is not (CaptureAnnotationKind.Text or CaptureAnnotationKind.Stamp) || _tool != hit.Annotation.Kind)) return false;
        return SelectAnnotation(hit.Id);
    }

    private void PointerMove(object sender, MouseEventArgs e)
    {
        if (_objectDragStart is Point objectStart)
        { _objectDragDelta = Clamp(e.GetPosition(_overlay)) - objectStart; ShowSelection(); return; }
        if (_panStart is Point origin)
        {
            var point = e.GetPosition(_viewport);
            _viewport.ScrollToHorizontalOffset(_panLeft - point.X + origin.X);
            _viewport.ScrollToVerticalOffset(_panTop - point.Y + origin.Y);
            return;
        }
        if (!_drawing) return;
        var end = Clamp(e.GetPosition(_overlay));
        if (_cropTool)
        {
            _crop = new Rect(_start, end); ShowCrop(); NotifyStateChanged(); return;
        }
        if (_tool == CaptureAnnotationKind.Pencil) _pencilPoints.Add(end);
        _pending = CreateAnnotation(end);
        if (!_previewTimer.IsEnabled) _previewTimer.Start();
    }

    private void PointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_objectDragStart.HasValue && _selectedAnnotationId is long id)
        {
            var delta = _objectDragDelta; _objectDragStart = null; _objectDragDelta = new Vector(); _overlay.ReleaseMouseCapture();
            _document?.MoveAnnotation(id, delta.X, delta.Y); RefreshPreview(); NotifyStateChanged(); return;
        }
        if (_panStart.HasValue) { _panStart = null; _overlay.ReleaseMouseCapture(); return; }
        if (!_drawing) return;
        var end = Clamp(e.GetPosition(_overlay));
        _drawing = false; _overlay.ReleaseMouseCapture(); _previewTimer.Stop(); _pending = null;
        if (_cropTool) { _crop = new Rect(_start, end); ShowCrop(); NotifyStateChanged(); return; }
        if (_tool == CaptureAnnotationKind.Pencil) _pencilPoints.Add(end);
        if (_tool == CaptureAnnotationKind.Pencil || (end - _start).Length > 2)
            InsertAnnotation(CreateAnnotation(end));
        else RefreshPreview();
    }

    private CaptureAnnotation CreateAnnotation(Point end) => new()
    {
        Kind = _tool ?? CaptureAnnotationKind.Arrow, Start = _start, End = end, Points = [.. _pencilPoints],
        Argb = _color, OutlineArgb = ShapeOutline || _tool is not (CaptureAnnotationKind.Rectangle or CaptureAnnotationKind.Ellipse) ? _color : null,
        FillArgb = ShapeFill, Thickness = _thickness, Opacity = _tool == CaptureAnnotationKind.Highlighter ? .35f * AnnotationOpacity : AnnotationOpacity,
        Size = _tool == CaptureAnnotationKind.Number ? 32 : TextSize,
        Bold = TextBold, Italic = TextItalic, FontFamily = TextFontFamily, Alignment = TextAlignment,
        PrivacyStrength = PrivacyStrength, Text = _tool == CaptureAnnotationKind.Number ? (_document?.NextNumber ?? 1).ToString() : AnnotationText
    };

    private void CancelGesture()
    {
        _previewTimer.Stop(); _pending = null; _drawing = false; _panStart = null;
        _objectDragStart = null; _objectDragDelta = new Vector();
        _overlay.ReleaseMouseCapture();
        if (_previewSource is not null) _image.Source = _previewSource;
        if (!_cropTool) ShowSelection();
    }

    private void RefreshPreview()
    {
        if (_document is null) return;
        var next = _document.Render();
        _previewBitmap?.Dispose(); _previewBitmap = next;
        _surface.Width = _image.Width = _overlay.Width = next.Width;
        _surface.Height = _image.Height = _overlay.Height = next.Height;
        _image.Source = _previewSource = ToBitmapSource(next);
        _overlay.Children.Clear(); UpdateZoom(); ShowSelection();
    }

    private void ShowCrop()
    {
        _overlay.Children.Clear();
        if (_crop is not Rect crop) return;
        var outline = new Rectangle { Width = crop.Width, Height = crop.Height, Fill = Brushes.Transparent, StrokeThickness = 2 / Math.Max(.05, _viewbox.Width / _surface.Width), StrokeDashArray = new DoubleCollection([4, 3]), IsHitTestVisible = false };
        outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Lab.accent");
        Canvas.SetLeft(outline, crop.Left); Canvas.SetTop(outline, crop.Top); _overlay.Children.Add(outline);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible || !IsEnabled) return;
        if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else Undo(); e.Handled = true; }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Redo(); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SaveCopyRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if (e.Key == Key.Delete && _selectedAnnotationId.HasValue) { DeleteSelectedAnnotation(); e.Handled = true; }
        else if (_selectedAnnotationId is long id && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var distance = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            _document?.MoveAnnotation(id, e.Key == Key.Left ? -distance : e.Key == Key.Right ? distance : 0,
                e.Key == Key.Up ? -distance : e.Key == Key.Down ? distance : 0);
            RefreshPreview(); NotifyStateChanged(); e.Handled = true;
        }
        else if (e.Key == Key.Escape) { if (HasPendingCrop) CancelCrop(); else if (_selectedAnnotationId.HasValue) SelectTool(null); else ExitRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, Math.Max(1, _surface.Width)), Math.Clamp(point.Y, 0, Math.Max(1, _surface.Height)));
    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
    public void Dispose() { _previewTimer.Stop(); _document?.Dispose(); _document = null; _previewBitmap?.Dispose(); _previewBitmap = null; _previewSource = null; }

    private static BitmapSource ToBitmapSource(DrawingBitmap bitmap) => CaptureBitmapSource.Create(bitmap);
}
