using SlashText.Services;

internal static class RecordingOperationChecks
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name + ": PASS"); }
        var gate = new SingleFlightGate();
        var released = 0;
        var first = new RecordingOperation(new CountedLease(gate.TryEnter()!, () => released++));
        Require(first.Phase == RecordingOperationPhase.Preparing && gate.IsActive && !first.Token.IsCancellationRequested, "reservation owns preparation from construction");
        Require(gate.TryEnter() is null, "preparation excludes competitors");
        first.Cancel(); first.Cancel();
        Require(first.Token.IsCancellationRequested && gate.IsActive && !first.Completion.IsCompleted, "cancel retains reservation while work unwinds");
        try { first.MarkRecording(); throw new InvalidOperationException("cancelled preparation started"); }
        catch (OperationCanceledException) { checks.Add("cancelled preparation cannot advance: PASS"); }
        first.Dispose(); first.Dispose(); first.Cancel();
        await first.Completion;
        Require(released == 1 && !gate.IsActive, "duplicate cancel/dispose releases once");
        using (var second = new RecordingOperation(new CountedLease(gate.TryEnter()!, () => released++)))
        {
            second.MarkRecording(); Require(second.Phase == RecordingOperationPhase.Recording && gate.TryEnter() is null, "recording retains exclusion");
            second.MarkFinalizing(); Require(second.Phase == RecordingOperationPhase.Finalizing && gate.TryEnter() is null, "finalization retains exclusion");
            first.Dispose(); Require(gate.IsActive && released == 1, "late old disposal cannot release a new operation");
        }
        Require(released == 2 && !gate.IsActive, "successful finalization releases once");
        var attempts = 0; var winners = 0;
        var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            var lease = gate.TryEnter(); Interlocked.Increment(ref attempts);
            if (lease is null) return;
            using var operation = new RecordingOperation(lease);
            Interlocked.Increment(ref winners); await pause.Task;
        })).ToArray();
        while (Volatile.Read(ref attempts) != 32) await Task.Delay(1);
        Require(winners == 1 && gate.IsActive, "concurrent requests admit exactly one owner");
        pause.SetResult(); await Task.WhenAll(jobs);
        Require(!gate.IsActive, "parallel requests leave no reservation");
        try
        {
            using var failed = new RecordingOperation(new CountedLease(gate.TryEnter()!, () => released++));
            throw new IOException("Synthetic pipeline fault");
        }
        catch (IOException) { }
        Require(released == 3 && !gate.IsActive, "fault unwinds the reservation");
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
        Console.WriteLine($"Recording reservation: {checks.Count} checks PASS");
    }
    private sealed class CountedLease(IDisposable inner, Action released) : IDisposable
    { public void Dispose() { inner.Dispose(); released(); } }
}
