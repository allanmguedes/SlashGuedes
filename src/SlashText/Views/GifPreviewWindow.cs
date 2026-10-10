using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

public sealed class GifPreviewWindow : Window
{
    private readonly GifRecordingResult _recording;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly DispatcherTimer _timer;
    private int _index;

    public GifPreviewWindow(GifRecordingResult recording)
    {
        _recording = recording;
        Title = "Prévia do GIF";
        Width = 900;
        Height = 680;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)Application.Current.FindResource("CanvasBrush");
        Foreground = (Brush)Application.Current.FindResource("InkBrush");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Prévia antes de salvar",
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = $"{recording.FrameCount} quadros · {recording.Fps} FPS · " +
                           $"{recording.Duration.TotalSeconds:0.0}s · " +
                           $"{recording.Width}×{recording.Height}",
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = (Brush)Application.Current.FindResource("MutedBrush")
                }
            }
        });
        var surface = new Border
        {
            Background = (Brush)Application.Current.FindResource("ChromeBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("DividerBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = _preview
        };
        Grid.SetRow(surface, 2);
        root.Children.Add(surface);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Descartar", MinWidth = 100 };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button
        {
            Content = "Salvar GIF",
            MinWidth = 110,
            Margin = new Thickness(10, 0, 0, 0),
            Style = (Style)Application.Current.FindResource("PrimaryButton")
        };

        save.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetRow(actions, 4);
        root.Children.Add(actions);
        Content = root;
        AuxiliaryWindowTheme.Apply(this);

        ShowFrame(0);
        _timer = new DispatcherTimer
        {
            Interval = FrameDelay(0)
        };
        _timer.Tick += PreviewTimer_OnTick;
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= PreviewTimer_OnTick;
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    private void PreviewTimer_OnTick(object? sender, EventArgs e)
    {
        _index = (_index + 1) % _recording.FrameCount;
        ShowFrame(_index);
        _timer.Interval = FrameDelay(_index);
    }

    private void ShowFrame(int index)
    {
        using var bitmap = _recording.LoadFrame(index);
        _preview.Source = GifRecordingService.ToBitmapSource(bitmap);
    }

    private TimeSpan FrameDelay(int index) => TimeSpan.FromMilliseconds(
        Math.Max(2, _recording.FrameDelaysCentiseconds[index]) * 10d);
}
