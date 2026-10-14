using System.Windows;

namespace SlashText.Views;

/// <summary>Cancel a specific owned modal/overlay; queued callbacks never target another operation.</summary>
internal sealed class RecordingWindowCancellation : IDisposable
{
    private readonly Window _window;
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _registration;
    internal RecordingWindowCancellation(Window window, CancellationToken token)
    {
        _window = window; _token = token;
        window.Loaded += OnLoaded;
        _registration = token.Register(() =>
        {
            if (!window.Dispatcher.HasShutdownStarted)
                _ = window.Dispatcher.BeginInvoke(new Action(CloseIfCancelled));
        });
    }
    private void OnLoaded(object sender, RoutedEventArgs e) => CloseIfCancelled();
    private void CloseIfCancelled() { if (_token.IsCancellationRequested && _window.IsVisible) _window.Close(); }
    public void Dispose() { _window.Loaded -= OnLoaded; _registration.Dispose(); }
}
