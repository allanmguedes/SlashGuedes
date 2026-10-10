using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SlashText.Services;

namespace SlashText.Views;

public sealed class QuickAccentWindow : Window
{
    private readonly WrapPanel _panel = new() { Orientation = Orientation.Horizontal };
    private readonly Border _surface;

    public QuickAccentWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        _surface = new Border
        {
            Background = FindBrush("SurfaceBrush", Brushes.White),
            BorderBrush = FindBrush("DividerBrush", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = ThemeService.IsDark ? .28 : .12
            },
            Child = _panel
        };
        Content = _surface;
        _surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
        _surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        SourceInitialized += (_, _) => FloatingWindowPlacement.PreventActivation(this);
    }

    public void UpdateChoices(string choices, int selectedIndex, string position, bool showUnicode)
    {
        _panel.Children.Clear();
        _surface.Background = FindBrush("SurfaceBrush", Brushes.White);
        _surface.BorderBrush = FindBrush(
            "DividerBrush",
            new SolidColorBrush(Color.FromRgb(40, 48, 61)));
        var accent = FindBrush(
            "AccentBrush",
            new SolidColorBrush(Color.FromRgb(8, 126, 139)));
        var selected = FindBrush("AccentSubtleBrush", accent);
        var ink = FindBrush(
            "InkBrush",
            ThemeService.IsDark ? Brushes.White : Brushes.Black);
        for (var index = 0; index < choices.Length; index++)
        {
            var character = choices[index];
            _panel.Children.Add(new Border
            {
                Background = index == selectedIndex
                    ? selected
                    : Brushes.Transparent,
                BorderBrush = index == selectedIndex ? accent : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(11, 7, 11, 7),
                Margin = new Thickness(2),
                Child = new TextBlock
                {
                    Text = showUnicode ? $"{character}\nU+{(int)character:X4}" : character.ToString(),
                    Foreground = index == selectedIndex ? accent : ink,
                    FontSize = showUnicode ? 12 : 20,
                    TextAlignment = TextAlignment.Center
                }
            });
        }

        _surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel");
        _surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        foreach (Border item in _panel.Children)
        {
            var isSelected = _panel.Children.IndexOf(item) == selectedIndex;
            if (isSelected) { item.SetResourceReference(Border.BackgroundProperty, "Lab.tint"); item.SetResourceReference(Border.BorderBrushProperty, "Lab.accent"); }
            ((TextBlock)item.Child).SetResourceReference(TextBlock.ForegroundProperty, isSelected ? "Lab.accent-text" : "Lab.text");
        }
        var screen = FloatingWindowPlacement.ForegroundScreen();
        Show(); FloatingWindowPlacement.Place(this, screen, position);
    }

    private static Brush FindBrush(string key, Brush fallback) =>
        System.Windows.Application.Current.TryFindResource(key) as Brush ?? fallback;
}
