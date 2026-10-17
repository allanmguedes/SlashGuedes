using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SlashText.Views;

/// <summary>
/// Shared readable-text presentation for ordinary single-line WPF inputs.
/// The real TextBox keeps keyboard, IME, clipboard, selection, caret and
/// bindings. A separate non-interactive TextBlock paints its glyphs, just as
/// the clipboard popup's verified workaround does. Passwords, multiline,
/// read-only, shortcut recorders and custom rich/code editors are excluded.
/// </summary>
internal static class ReadableInputBehavior
{
    private sealed class State
    {
        public required AdornerLayer Layer { get; init; }
        public required TextPresentation Presentation { get; init; }
        public required Brush OriginalForeground { get; init; }
    }

    private static readonly ConditionalWeakTable<TextBox, State> Attached = new();
    private static bool _installed;

    public static readonly DependencyProperty SkipProperty =
        DependencyProperty.RegisterAttached("Skip", typeof(bool),
            typeof(ReadableInputBehavior), new PropertyMetadata(false));
    public static void SetSkip(TextBox box, bool skip) => box.SetValue(SkipProperty, skip);
    public static bool GetSkip(TextBox box) => (bool)box.GetValue(SkipProperty);

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnUnloaded), true);
    }

    private static bool Eligible(TextBox box) =>
        !GetSkip(box) &&
        !box.IsReadOnly &&
        !box.AcceptsReturn &&
        box.TextWrapping == TextWrapping.NoWrap &&
        box is not ClipboardShortcutRecorderBox &&
        box is not ShortcutRecorderBox;

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || !Eligible(box) || Attached.TryGetValue(box, out _))
            return;

        // An adorner layer is provided by standard WPF windows. If a custom
        // window has no layer, never hide the original text.
        var layer = AdornerLayer.GetAdornerLayer(box);
        if (layer is null) return;
        var presentation = new TextPresentation(box);
        var foreground = box.Foreground;
        try
        {
            layer.Add(presentation);
            Attached.Add(box, new State
            {
                Layer = layer,
                Presentation = presentation,
                OriginalForeground = foreground
            });
            box.Foreground = Brushes.Transparent;
            box.SetResourceReference(TextBox.CaretBrushProperty, "Lab.accent-text");
            box.TextChanged += OnTextChanged;
            box.SizeChanged += OnSizeChanged;
            box.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(OnScrolled), true);
        }
        catch
        {
            layer.Remove(presentation);
            box.Foreground = foreground;
            // Controls without a usable adorner keep native rendering.
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || !Attached.TryGetValue(box, out var state)) return;
        box.TextChanged -= OnTextChanged;
        box.SizeChanged -= OnSizeChanged;
        box.RemoveHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnScrolled));
        state.Layer.Remove(state.Presentation);
        box.Foreground = state.OriginalForeground;
        Attached.Remove(box);
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e) =>
        Invalidate(sender as TextBox, updateText: true);
    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        Invalidate(sender as TextBox, updateText: false);
    private static void OnScrolled(object sender, ScrollChangedEventArgs e) =>
        Invalidate(sender as TextBox, updateText: false);

    private static void Invalidate(TextBox? box, bool updateText)
    {
        if (box is null || !Attached.TryGetValue(box, out var state)) return;
        if (updateText) state.Presentation.Sync();
        state.Presentation.InvalidateMeasure();
        state.Presentation.InvalidateArrange();
    }

    internal static bool TryGetVisibleText(TextBox box, out string visible)
    {
        if (Attached.TryGetValue(box, out var state))
        {
            visible = state.Presentation.DisplayText;
            return true;
        }
        visible = string.Empty;
        return false;
    }

    internal static bool HasReadablePresentation(TextBox box)
        => Attached.TryGetValue(box, out var state) &&
           state.Presentation.IsHitTestVisible == false &&
           state.Presentation.ReadableForeground is SolidColorBrush;

    internal static bool TryGetPresentationForeground(TextBox box, out Brush? brush)
    {
        if (Attached.TryGetValue(box, out var state))
        {
            brush = state.Presentation.ReadableForeground;
            return true;
        }
        brush = null;
        return false;
    }

    private sealed class TextPresentation : Adorner
    {
        private readonly TextBox _owner;
        private readonly TextBlock _text;
        private readonly VisualCollection _visuals;

        internal TextPresentation(TextBox owner) : base(owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            ClipToBounds = true;
            _text = new TextBlock
            {
                IsHitTestVisible = false,
                Focusable = false,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                FontSize = owner.FontSize,
                FontFamily = owner.FontFamily,
                FontStyle = owner.FontStyle,
                FontWeight = owner.FontWeight,
                FontStretch = owner.FontStretch
            };
            _text.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
            _text.Text = owner.Text;
            _visuals = new VisualCollection(this) { _text };
        }

        internal string DisplayText => _text.Text;
        internal Brush ReadableForeground => _text.Foreground;
        internal void Sync() => _text.Text = _owner.Text;

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Size(_owner.ActualWidth, _owner.ActualHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var insetX = _owner.Padding.Left + _owner.BorderThickness.Left;
            var insetY = _owner.Padding.Top + _owner.BorderThickness.Top;
            var innerHeight = Math.Max(0, finalSize.Height - _owner.Padding.Top -
                _owner.Padding.Bottom - _owner.BorderThickness.Top - _owner.BorderThickness.Bottom);
            var y = _owner.VerticalContentAlignment == VerticalAlignment.Center
                ? insetY + Math.Max(0, (innerHeight - _text.DesiredSize.Height) / 2)
                : insetY;
            var offset = Math.Max(0, _owner.HorizontalOffset);
            _text.Arrange(new Rect(insetX - offset, y,
                Math.Max(_text.DesiredSize.Width, finalSize.Width), _text.DesiredSize.Height));
            return finalSize;
        }
    }
}
