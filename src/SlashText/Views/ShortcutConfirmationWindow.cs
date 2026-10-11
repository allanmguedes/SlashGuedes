using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SlashText.Design;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>Explicit destructive action, with cancel as the keyboard default.</summary>
public sealed class ShortcutConfirmationWindow : Window
{
    internal Border Surface { get; }
    internal Button CancelButton { get; }
    internal Button AcceptButton { get; }

    public ShortcutConfirmationWindow(string title, string description, string detail, string acceptLabel, bool destructive = true)
    {
        Title = title; Width = 500; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; AllowsTransparency = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = Text(title, 20, true); header.Children.Add(heading);
        var close = Button(UiText.Get("Fechar"), "X", false); close.Content = Glyph("X", 16); close.Width = 32; close.Height = 32; close.Padding = new Thickness(6);
        close.Click += (_, _) => Cancel(); Grid.SetColumn(close, 1); header.Children.Add(close); panel.Children.Add(header);
        var message = Text(description, 14); message.Margin = new Thickness(0, 18, 0, 12); panel.Children.Add(message);
        var card = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(14), BorderThickness = new Thickness(1), Child = Text(detail, 13) };
        card.SetResourceReference(Border.BackgroundProperty, "Lab.input"); card.SetResourceReference(Border.BorderBrushProperty, "Lab.line"); panel.Children.Add(card);
        var tip = Text(UiText.Get("Cancelar mantém o atalho e as alterações em edição."), 12); tip.Margin = new Thickness(0, 12, 0, 20); tip.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); panel.Children.Add(tip);
        var line = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 18) }; line.SetResourceReference(Border.BackgroundProperty, "Lab.line"); panel.Children.Add(line);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        CancelButton = Button(UiText.Get("Cancelar"), "X", false); CancelButton.IsDefault = true; CancelButton.Margin = new Thickness(0, 0, 8, 0);
        CancelButton.Click += (_, _) => Cancel(); actions.Children.Add(CancelButton);
        AcceptButton = Button(acceptLabel, destructive ? "Trash2" : "Check", false);
        if (destructive) AcceptButton.SetResourceReference(ForegroundProperty, "Lab.error");
        AcceptButton.Click += (_, _) => DialogResult = true; actions.Children.Add(AcceptButton); panel.Children.Add(actions);
        Surface = new Border { Child = panel, Margin = new Thickness(8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        Surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); Surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
        Content = Surface; LabMotion.SetEntrance(Surface, "Popup");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        Loaded += (_, _) => CancelButton.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Cancel(); } };
    }

    internal void EnableBackdrop() => ModalBackdrop.Attach(this, Surface, Cancel);
    private void Cancel() => Close();
    private static TextBlock Text(string text, double size, bool bold = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text"); return label;
    }
    private static LabIcon Glyph(string kind, double size) => new() { Kind = kind, Width = size, Height = size };
    private static Button Button(string label, string kind, bool primary)
    {
        var button = new Button(); button.SetResourceReference(StyleProperty, primary ? "Lab.Pilot.PrimaryButton" : "Lab.Button");
        AutomationProperties.SetName(button, label);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Glyph(kind, 16); icon.Margin = new Thickness(0, 0, 7, 0);
        icon.SetBinding(LabIcon.ForegroundProperty, new Binding("Foreground") { Source = button }); content.Children.Add(icon);
        var text = Text(label, 13); text.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = button }); content.Children.Add(text);
        button.Content = content; return button;
    }
}
