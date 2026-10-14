namespace SlashText.Services;

internal enum RecordingOperationPhase { Preparing, Recording, Finalizing }

/// <summary>The UI owns the reservation until all preparation/finalization work unwinds.</summary>
internal sealed class RecordingOperation : IDisposable
{
    private readonly IDisposable _reservation;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _sync = new();
    private bool _disposed;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken Token { get; }
    internal Task Completion => _completion.Task;
    internal bool InterfaceHidden { get; set; }
    internal RecordingOperationPhase Phase { get; private set; } = RecordingOperationPhase.Preparing;

    internal RecordingOperation(IDisposable reservation) { _reservation = reservation; Token = _cancellation.Token; }

    internal void MarkRecording() => Advance(RecordingOperationPhase.Preparing, RecordingOperationPhase.Recording);
    internal void MarkFinalizing() => Advance(RecordingOperationPhase.Recording, RecordingOperationPhase.Finalizing);
    private void Advance(RecordingOperationPhase expected, RecordingOperationPhase next)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Token.ThrowIfCancellationRequested();
            if (Phase != expected) throw new InvalidOperationException("Recording operation cannot restart or skip a phase.");
            Phase = next;
        }
    }
    internal void Cancel() { lock (_sync) { if (!_disposed) _cancellation.Cancel(); } }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            try { _cancellation.Dispose(); }
            finally { try { _reservation.Dispose(); } finally { _completion.TrySetResult(); } }
        }
    }
}
