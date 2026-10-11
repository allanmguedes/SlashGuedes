using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SlashText.Views;

public sealed class RegionSelectionWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Rectangle _selection = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(38, 198, 216)),
        StrokeThickness = 2,
        StrokeDashArray = new DoubleCollection { 3, 2 },
        Fill = Brushes.Transparent,
        Visibility = Visibility.Collapsed
    };
    private Point _start;
    private bool _dragging;

    public System.Drawing.Rectangle SelectedBounds { get; private set; }

    public RegionSelectionWindow(string purpose = "Selecione a região")
    {
        Title = purpose;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(96, 3, 12, 22));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var help = new Border
        {
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("DividerBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 9, 14, 9),
            Child = new TextBlock
            {
                Text = UiText.Format($"{purpose}  ·  Esc cancela"),
                Foreground = (Brush)Application.Current.FindResource("InkBrush"),
                FontSize = 13
            }
        };
        Canvas.SetLeft(help, 24);
        Canvas.SetTop(help, 24);
        _canvas.Children.Add(help);
        help.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
        help.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        ((TextBlock)help.Child).SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        _canvas.Children.Add(_selection);
        Content = _canvas;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }
        _start = e.GetPosition(_canvas);
        _dragging = true;
        _selection.Visibility = Visibility.Visible;
        CaptureMouse();
        Update(_start);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            Update(e.GetPosition(_canvas));
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        ReleaseMouseCapture();
        var rect = Normalize(_start, e.GetPosition(_canvas));
        if (rect.Width < 4 || rect.Height < 4)
        {
            _selection.Visibility = Visibility.Collapsed;
            return;
        }

        var scaleX = SystemParameters.VirtualScreenWidth / Math.Max(1, ActualWidth);
        var scaleY = SystemParameters.VirtualScreenHeight / Math.Max(1, ActualHeight);
        SelectedBounds = new System.Drawing.Rectangle(
            (int)Math.Round(SystemParameters.VirtualScreenLeft + rect.Left * scaleX),
            (int)Math.Round(SystemParameters.VirtualScreenTop + rect.Top * scaleY),
            Math.Max(1, (int)Math.Round(rect.Width * scaleX)),
            Math.Max(1, (int)Math.Round(rect.Height * scaleY)));
        DialogResult = true;
    }

    private void Update(Point end)
    {
        var rect = Normalize(_start, end);
        Canvas.SetLeft(_selection, rect.Left);
        Canvas.SetTop(_selection, rect.Top);
        _selection.Width = rect.Width;
        _selection.Height = rect.Height;
    }

    private static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X),
        Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X),
        Math.Abs(end.Y - start.Y));
}
