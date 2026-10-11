using System.Windows;
using System.Windows.Controls;
using SlashText.Services;
using TextBox = System.Windows.Controls.TextBox;

namespace SlashText.Views;

public sealed class PromptDialog : Window
{
    private readonly TextBox _input;

    internal PromptDialog(string title, string label, string initialValue)
    {
        Title = title;
        Width = 480;
        MinWidth = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "CanvasBrush");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 7) });
        _input = new TextBox
        {
            Text = initialValue,
            MinHeight = 40,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(_input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        buttons.Children.Add(new Button
        {
            Content = UiText.Get("Cancelar"),
            IsCancel = true,
            Margin = new Thickness(0, 0, 10, 0)
        });

        var confirm = new Button
        {
            Content = UiText.Get("Aplicar"),
            IsDefault = true,
            Style = (Style)Application.Current.FindResource("PrimaryButton")
        };
        confirm.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        Content = panel;
        AuxiliaryWindowTheme.Apply(this);
        Loaded += (_, _) =>
        {
            Activate();
            _input.Focus();
            _input.SelectAll();
        };
    }

    public static string? Show(Window owner, string title, string label, string initialValue = "")
    {
        var dialog = new PromptDialog(title, label, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._input.Text.Trim() : null;
    }
}
