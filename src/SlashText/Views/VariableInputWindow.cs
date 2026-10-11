using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SlashText.Services;
using TextBox = System.Windows.Controls.TextBox;

namespace SlashText.Views;

public sealed class VariableInputWindow : Window
{
    private readonly Dictionary<string, TextBox> _inputs =
        new(StringComparer.CurrentCultureIgnoreCase);

    public VariableInputWindow(IReadOnlyList<TemplateField> fields)
    {
        Title = UiText.Get("Preencher campos");
        Width = 480;
        Height = Math.Clamp(240 + (fields.Count * 70), 320, 650);
        MinHeight = 320;
        MaxHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "CanvasBrush");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("Complete antes de inserir"),
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });

        var fieldsPanel = new StackPanel();
        var fieldsScroll = new ScrollViewer
        {
            Content = fieldsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Grid.SetRow(fieldsScroll, 2);
        root.Children.Add(fieldsScroll);

        foreach (var field in fields)
        {
            var label = new TextBlock
            {
                Text = Humanize(field.Name), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var input = new TextBox
            {
                Text = field.DefaultValue ?? string.Empty,
                Margin = new Thickness(0, 0, 0, 13),
                MinWidth = 360
            };
            System.Windows.Automation.AutomationProperties.SetName(input, Humanize(field.Name));
            _inputs[field.Name] = input;
            fieldsPanel.Children.Add(label);
            fieldsPanel.Children.Add(input);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        var cancel = new Button { Content = UiText.Get("Cancelar"), IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        var insert = new Button
        {
            Content = UiText.Get("Inserir"),
            IsDefault = true,
            Style = (Style)Application.Current.FindResource("PrimaryButton")
        };
        insert.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(insert);

        Content = root;
        AuxiliaryWindowTheme.Apply(this);
        Loaded += (_, _) =>
        {
            Topmost = true;
            Activate();
            _inputs.Values.FirstOrDefault()?.Focus();
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => Topmost = false));
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                DialogResult = true;
                Close();
            }
        };
    }

    public IReadOnlyDictionary<string, string> Values =>
        _inputs.ToDictionary(item => item.Key, item => item.Value.Text);

    public static VariableInputWindow ShowForTarget(
        IReadOnlyList<TemplateField> fields,
        IntPtr targetWindow)
    {
        var window = new VariableInputWindow(fields);
        if (targetWindow != IntPtr.Zero)
        {
            // O proprietário nativo precisa ser definido antes de ShowDialog.
            // Fazer isso em SourceInitialized é tarde demais para uma janela modal
            // e causa "Cannot set Owner property after Dialog is shown".
            new WindowInteropHelper(window).Owner = targetWindow;
        }
        window.ShowDialog();
        return window;
    }

    private static string Humanize(string name)
    {
        var value = name.Replace('_', ' ').Trim();
        return string.IsNullOrEmpty(value)
            ? UiText.Get("Campo")
            : char.ToUpper(value[0]) + value[1..];
    }
}
