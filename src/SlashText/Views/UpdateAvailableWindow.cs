using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

internal enum UpdateDecision
{
    Cancel,
    UpdateNow,
    RemindLater,
    IgnoreVersion
}

internal sealed class UpdateAvailableWindow : Window
{
    public UpdateDecision Decision { get; private set; }

    public UpdateAvailableWindow(UpdateCheckResult result)
    {
        Title = AppPaths.IsSlashGuedes ? "Atualização do SlashGuedes" : UiText.Get("Atualização do SlashDesk");
        Width = 590;
        Height = 470;
        MinWidth = 500;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Resource<Brush>("CanvasBrush", Brushes.White);
        Foreground = Resource<Brush>("InkBrush", Brushes.Black);
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = UiText.Format($"{(AppPaths.IsSlashGuedes ? "SlashGuedes" : "SlashDesk")} {result.LatestVersion} está disponível"),
            FontSize = 23,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = Resource<Brush>("InkBrush", Brushes.Black)
        };
        root.Children.Add(heading);

        var summary = new TextBlock
        {
            Text = UiText.Format($"Versão atual: {result.CurrentVersion}   •   Download: {FormatSize(result.DownloadSize)}"),
            Margin = new Thickness(0, 8, 0, 14),
            Foreground = Resource<Brush>("MutedBrush", Brushes.DimGray)
        };
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(result.Notes)
                ? UiText.Get("As notas desta versão não foram informadas.")
                : result.Notes,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12),
            BorderBrush = Resource<Brush>("DividerBrush", Brushes.LightGray),
            Background = Resource<Brush>("SurfaceBrush", Brushes.White),
            Foreground = Resource<Brush>("InkBrush", Brushes.Black)
        };
        Grid.SetRow(notes, 2);
        root.Children.Add(notes);

        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(CreateButton(UiText.Get("Ignorar esta versão"), UpdateDecision.IgnoreVersion));
        buttons.Children.Add(CreateButton(UiText.Get("Lembrar depois"), UpdateDecision.RemindLater));
        var update = CreateButton(UiText.Get("Atualizar agora"), UpdateDecision.UpdateNow);
        if (Application.Current.TryFindResource("PrimaryButton") is Style primary)
        {
            update.Style = primary;
        }
        buttons.Children.Add(update);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;
        AuxiliaryWindowTheme.Apply(this);

    }

    private Button CreateButton(string text, UpdateDecision decision)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 126,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 7, 12, 7)
        };
        button.Click += (_, _) =>
        {
            Decision = decision;
            DialogResult = true;
        };
        return button;
    }

    private static T Resource<T>(string key, T fallback) where T : class =>
        Application.Current.TryFindResource(key) as T ?? fallback;

    private static string FormatSize(long? bytes)
    {
        if (bytes is null or <= 0)
        {
            return UiText.Get("tamanho não informado");
        }
        var mib = bytes.Value / 1024d / 1024d;
        return $"{mib:0.0} MB";
    }
}
