using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SlashText.Design;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>Themed settings feedback and explicit choices; dismissing never accepts maintenance.</summary>
public sealed class SettingsDialogWindow : Window
{
    internal Border Surface { get; }
    internal Button DefaultButton { get; private set; } = null!;
    internal List<Button> Actions { get; } = [];
    internal MessageBoxResult Choice { get; private set; } = MessageBoxResult.Cancel;

    public SettingsDialogWindow(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information, string yesLabel = "Continuar", string noLabel = "Cancelar")
    {
        Title = title; Width = 580; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; AllowsTransparency = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var header = new DockPanel { LastChildFill = true };
        var close = Action(UiText.Get("Fechar"), MessageBoxResult.Cancel); close.Content = new LabIcon { Kind = "X", Width = 18, Height = 18 };
        close.Width = 34; close.Height = 34; close.Padding = new Thickness(7); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var glyph = new LabIcon { Kind = image == MessageBoxImage.Question ? "CircleHelp" : "Info", Width = 22, Height = 22, Margin = new Thickness(0, 0, 12, 0) };
        glyph.SetResourceReference(LabIcon.ForegroundProperty, image == MessageBoxImage.Warning ? "Lab.error" : "Lab.accent-text"); DockPanel.SetDock(glyph, Dock.Left); header.Children.Add(glyph);
        var heading = Label(title, 20); heading.FontWeight = FontWeights.SemiBold; header.Children.Add(heading); panel.Children.Add(header);
        var text = Label(message, 14); text.Margin = new Thickness(0, 20, 8, 20);
        panel.Children.Add(new ScrollViewer { Content = text, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var divider = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 18) }; divider.SetResourceReference(Border.BackgroundProperty, "Lab.line"); panel.Children.Add(divider);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        if (buttons == MessageBoxButton.OK) Add(UiText.Get("Entendi"), MessageBoxResult.OK, true);
        else
        {
            Add(UiText.Get("Cancelar"), MessageBoxResult.Cancel, true);
            if (buttons == MessageBoxButton.YesNoCancel) Add(noLabel, MessageBoxResult.No, false);
            Add(yesLabel, MessageBoxResult.Yes, false);
        }
        panel.Children.Add(actions);
        Surface = new Border { Child = panel, Margin = new Thickness(8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        Surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); Surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
        Content = Surface; SetResourceReference(FontFamilyProperty, "Lab.Font"); LabMotion.SetEntrance(Surface, "Popup");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        Loaded += (_, _) => DefaultButton.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };

        void Add(string label, MessageBoxResult result, bool keyboardDefault)
        {
            var button = Action(label, result); button.Margin = new Thickness(8, 4, 0, 0); button.IsDefault = keyboardDefault;
            if (keyboardDefault) DefaultButton = button;
            Actions.Add(button); actions.Children.Add(button);
        }
    }

    internal void EnableBackdrop() => ModalBackdrop.Attach(this, Surface, Close);
    private Button Action(string label, MessageBoxResult result)
    {
        label = UiText.Get(label);
        var button = new Button { Content = label, MinHeight = 36 };
        button.SetResourceReference(StyleProperty, result == MessageBoxResult.Yes ? "Lab.PrimaryButton" : "Lab.Button"); AutomationProperties.SetName(button, label);
        button.Click += (_, _) => { Choice = result; DialogResult = result is MessageBoxResult.Yes or MessageBoxResult.No or MessageBoxResult.OK; };
        return button;
    }
    private static TextBlock Label(string text, double size)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text"); return label;
    }
}
