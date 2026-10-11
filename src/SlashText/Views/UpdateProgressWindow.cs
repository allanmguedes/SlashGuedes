using System.Windows;
using System.Windows.Controls;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

internal sealed class UpdateProgressWindow : Window, IProgress<UpdateProgress>
{
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;

    public UpdateProgressWindow()
    {
        Title = UiText.Get("Atualizando o SlashDesk");
        Width = 500;
        Height = 310;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("CanvasBrush");
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("InkBrush");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        var surface = new Border
        {
            Style = (Style)Application.Current.FindResource("SettingsCard"),
            Margin = new Thickness(18)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = UiText.Get("Preparando atualização segura"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        _status = new TextBlock
        {
            Text = UiText.Get("Conectando ao GitHub..."), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 8)
        };
        panel.Children.Add(_status);
        _progress = new ProgressBar
        {
            Height = 6,
            IsIndeterminate = true,
            Style = (Style)Application.Current.FindResource("LoadingProgress")
        };
        panel.Children.Add(_progress);
        _cancel = new Button
        {
            Content = UiText.Get("Cancelar"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        _cancel.Click += (_, _) => { Cancellation.Cancel(); _cancel.IsEnabled = false; };
        panel.Children.Add(_cancel);
        surface.Child = panel;
        Content = surface;
        AuxiliaryWindowTheme.Apply(this);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && _cancel.IsEnabled) { e.Handled = true; Cancellation.Cancel(); _cancel.IsEnabled = false; } };
        Closing += (_, args) =>
        {
            if (!CanClose)
            {
                args.Cancel = true;
            }
        };
    }

    public CancellationTokenSource Cancellation { get; } = new();
    public bool CanClose { get; private set; }

    public void Report(UpdateProgress value)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Report(value));
            return;
        }
        _status.Text = value.Stage;
        _cancel.IsEnabled = !value.IsApplying && !Cancellation.IsCancellationRequested;
        if (value.TotalBytes is > 0)
        {
            _progress.IsIndeterminate = false;
            _progress.Minimum = 0;
            _progress.Maximum = value.TotalBytes.Value;
            _progress.Value = Math.Min(value.BytesReceived, value.TotalBytes.Value);
        }
        else
        {
            _progress.IsIndeterminate = true;
        }
    }

    public void AllowClose()
    {
        CanClose = true;
        Close();
    }
}
