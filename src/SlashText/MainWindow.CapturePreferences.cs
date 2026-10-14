using System.IO;
using System.Windows;
using SlashText.Models;
using SlashText.Services;

namespace SlashText;

public partial class MainWindow
{
    // Only isolated UI fixtures use this boundary. They never register global shortcuts,
    // select real windows, capture pixels or open recording devices.
    internal Action<string>? CapturePreparationForEvidence { get; set; }

    private async Task<bool> PrepareCapturePreferencesAsync(string validationTitle, CancellationToken cancellationToken = default)
    {
        if (_settingsOperationInProgress || _servicesDisposed || _restoreRecoveryBlocked) return false;
        var previous = _settings.Capture;
        if (!TryReadCaptureSettings(out var error))
        {
            ShowAppDialog(error, validationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return await TrySaveCapturePreferencesCoreAsync(previous, null, null, cancellationToken);
    }

    private Task<bool> TrySaveCapturePreferencesAsync(CaptureSettings previous,
        RecordingAudioSettings? previousAudio = null, Action? beforeFailureFeedback = null)
        => TrySaveCapturePreferencesCoreAsync(previous, previousAudio, beforeFailureFeedback, CancellationToken.None);

    private async Task<bool> TrySaveCapturePreferencesCoreAsync(CaptureSettings previous,
        RecordingAudioSettings? previousAudio, Action? beforeFailureFeedback, CancellationToken cancellationToken)
    {
        try
        {
            // Protected reads deliberately permit session-only capture settings.
            await SavePreferencesWithCancellationAsync(cancellationToken);
            if (!CanUseRecordingInterface) return false;
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (previousAudio is not null) previous.Recording.Audio = previousAudio;
            _settings.Capture = previous;
            if (CanUseRecordingInterface) LoadCaptureSettings();
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (previousAudio is not null) previous.Recording.Audio = previousAudio;
            _settings.Capture = previous;
            if (!CanUseRecordingInterface) return false;
            cancellationToken.ThrowIfCancellationRequested();
            beforeFailureFeedback?.Invoke();
            LoadCaptureSettings();
            SafeDiagnosticLog.Write("capture.settings.save_failed", new Dictionary<string, object?> { ["exceptionType"] = error.GetType().Name });
            StatusText.Text = UiText.Get("Preferências de captura não salvas. A ação foi interrompida.");
            ShowAppDialog(UiText.Get("Não foi possível salvar as preferências de captura. Os ajustes anteriores foram mantidos e a captura ou gravação não foi iniciada. Verifique o acesso à pasta de dados, ajuste as opções e tente novamente."),
                UiText.Get("Não foi possível salvar as preferências de captura"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void ShowCapturePreferencesSavedStatus()
    {
        StatusText.Text = _settingsStore.IsProtected
            ? UiText.Get("Preferências protegidas: ajustes desta sessão não serão salvos.")
            : CaptureShortcutStatusText.Text.StartsWith("●", StringComparison.Ordinal)
                ? UiText.Get("Configurações de captura salvas")
                : UiText.Get("Configurações salvas. ") + CaptureShortcutStatusText.Text;
    }

    private void ClosePreparedMp4Recording()
    {
        CleanupRecordingResources();
        ShowFromTray();
    }
}
