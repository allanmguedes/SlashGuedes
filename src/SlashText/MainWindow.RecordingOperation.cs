using System.Windows;
using System.Windows.Controls;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private RecordingOperation? _recordingOperation;
    private bool _recordingExitPending;
    // Deterministic fixtures pause the real command before preferences/native work.
    internal Func<string, RecordingOperation, Task>? RecordingPreparationForEvidence { get; set; }
    internal Func<string, RecordingOperation, Task>? RecordingPipelineForEvidence { get; set; }

    private bool CanUseRecordingInterface => !_servicesDisposed && !_restoreRecoveryBlocked && !_exitRequested;
    private void CheckRecordingOperation(RecordingOperation operation)
    {
        operation.Token.ThrowIfCancellationRequested();
        if (!CanUseRecordingInterface || !ReferenceEquals(_recordingOperation, operation)) throw new OperationCanceledException(operation.Token);
    }

    private IDisposable? TryReserveCaptureCommand()
    {
        if (!CanUseRecordingInterface || _settingsOperationInProgress || _shortcutOperationInProgress || _activeUpdateCancellation is not null) return null;
        var lease = _captureGate.TryEnter();
        if (lease is null) StatusText.Text = UiText.Get("Já existe uma captura ou gravação em andamento.");
        return lease;
    }

    private async Task RunRecordingOperationAsync(string media, Func<RecordingOperation, Task> action)
    {
        var lease = TryReserveCaptureCommand();
        if (lease is null) { if (CanUseRecordingInterface && _recordingControl is { IsVisible: true } control) control.Activate(); return; }
        var operation = new RecordingOperation(lease);
        _recordingOperation = operation;
        try
        {
            RefreshRecordingAvailability();
            StatusText.Text = UiText.Get("Preparando gravação…");
            if (RecordingPreparationForEvidence is { } preparation) await preparation(media, operation);
            CheckRecordingOperation(operation);
            await action(operation);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !CanUseRecordingInterface)
        {
            if (CanUseRecordingInterface) StatusText.Text = UiText.Get("Preparação da gravação cancelada.");
        }
        catch (Exception error)
        {
            if (CanUseRecordingInterface && !operation.Token.IsCancellationRequested)
                ShowAppDialog(error.Message, media == "MP4" ? UiText.Get("Não foi possível gravar") : UiText.Get("Não foi possível criar o GIF"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try { CleanupRecordingResources(); }
            finally
            {
                if (ReferenceEquals(_recordingOperation, operation)) _recordingOperation = null;
                operation.Dispose();
                if (CanUseRecordingInterface)
                {
                    RefreshRecordingAvailability();
                    if (operation.Token.IsCancellationRequested) StatusText.Text = UiText.Get("Preparação da gravação cancelada.");
                    if (operation.InterfaceHidden && !IsVisible) ShowFromTray();
                }
            }
        }
    }

    private void CancelRecordingPreparation()
    {
        if (_recordingOperation is { Phase: RecordingOperationPhase.Preparing } operation) operation.Cancel();
    }

    private RecordingWindowCancellation WatchPreparedRecordingControl(RecordingControlWindow control, RecordingOperation operation)
    {
        control.Closed += (_, _) =>
        {
            if (ReferenceEquals(_recordingControl, control) && operation.Phase == RecordingOperationPhase.Preparing) operation.Cancel();
        };
        return new RecordingWindowCancellation(control, operation.Token);
    }

    private async Task CloseAfterRecordingPreparationAsync(RecordingOperation operation)
    {
        await operation.Completion;
        if (_servicesDisposed) return;
        _recordingExitPending = false;
        DisposeServices();
        Close();
    }

    private void CleanupRecordingResources()
    {
        var control = _recordingControl; _recordingControl = null;
        var recorder = _recordingService; _recordingService = null;
        var gif = _gifRecordingSession; _gifRecordingSession = null;
        try { control?.Close(); }
        finally { try { recorder?.Dispose(); } finally { gif?.Dispose(); } }
    }

    private void RefreshRecordingAvailability()
    {
        var available = _recordingOperation is null && CanUseRecordingInterface;
        CaptureNewButton.IsEnabled = available;
        CaptureNewButton.ToolTip = available ? null : UiText.Get("Já existe uma captura ou gravação em andamento.");
        ToolTipService.SetShowOnDisabled(CaptureNewButton, true);
        CaptureRuleCard.IsEnabled = available;
        CaptureRecordingCompactCard.IsEnabled = available;
        CaptureImageMediaButton.IsEnabled = CaptureVideoMediaButton.IsEnabled = CaptureGifMediaButton.IsEnabled = available;
        CaptureMonitorModeButton.IsEnabled = CaptureRegionModeButton.IsEnabled = CaptureWindowModeButton.IsEnabled = CaptureGifModeButton.IsEnabled = available;
        CaptureScrollingModeButton.IsEnabled = available && _captureMediaMode != CaptureMediaMode.Video;
        if (_trayIcon?.ContextMenuStrip is { } menu) RefreshQuickAccentTrayState(menu);
    }
}
