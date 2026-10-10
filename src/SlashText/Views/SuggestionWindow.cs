using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SlashText.Models;
using Point = System.Windows.Point;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;

namespace SlashText.Views;

public sealed class SuggestionWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private readonly StackPanel _items = new();
    private readonly Border _surface;

    public event Action<Snippet>? SnippetChosen;

    public SuggestionWindow()
    {
        Width = 330;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 280;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false; Focusable = false;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        _surface = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = Services.ThemeService.IsDark ? 0.28 : 0.12
            },
            Child = new ScrollViewer { Content = _items, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        Content = _surface;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate);
        };
    }

    public void UpdateSuggestions(
        IReadOnlyList<Snippet> snippets,
        Point position,
        int selectedIndex)
    {
        if (snippets.Count == 0)
        {
            Hide();
            return;
        }

        _surface.Background = FindBrush("SurfaceBrush", Brushes.White);
        _surface.BorderBrush = FindBrush("DividerBrush", Brushes.LightGray);
        _items.Children.Clear();
        for (var index = 0; index < snippets.Count; index++)
        {
            var snippet = snippets[index];
            var row = new Grid { Margin = new Thickness(8, 6, 8, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = snippet.Trigger,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = snippet.Trigger,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AccentBrush", new SolidColorBrush(Color.FromRgb(8, 126, 139)))
            });
            var name = new TextBlock
            {
                Text = snippet.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = FindBrush("MutedBrush", new SolidColorBrush(Color.FromRgb(89, 99, 113)))
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var item = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = index == selectedIndex
                    ? FindBrush("SelectionBrush", FindBrush("AccentSoftBrush", Brushes.LightCyan))
                    : Brushes.Transparent,
                BorderBrush = index == selectedIndex
                    ? FindBrush("AccentBrush", Brushes.Cyan)
                    : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Child = row,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = snippet
            };
            item.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                SnippetChosen?.Invoke(snippet);
            };
            _items.Children.Add(item);
            ((TextBlock)row.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "Lab.accent-text");
            name.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
            if (index == selectedIndex) { item.SetResourceReference(Border.BackgroundProperty, "Lab.tint"); item.SetResourceReference(Border.BorderBrushProperty, "Lab.accent"); }
        }

        if (!IsVisible)
        {
            Show();
        }
        UpdateLayout();
        _surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); _surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        var screen = Forms.Screen.FromPoint(new DrawingPoint((int)position.X, (int)position.Y));
        FloatingWindowPlacement.Place(this, screen, "Caret", position);
        if (selectedIndex >= 0 && selectedIndex < _items.Children.Count) ((FrameworkElement)_items.Children[selectedIndex]).BringIntoView();
    }

    private static Brush FindBrush(string key, Brush fallback) =>
        System.Windows.Application.Current.TryFindResource(key) as Brush ?? fallback;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

}
