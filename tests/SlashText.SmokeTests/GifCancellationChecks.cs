using System.Drawing;
using SlashText.Models;
using SlashText.Services;

internal static class GifCancellationChecks
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var previous = AppPaths.Current;
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, output, output, isCapturePilot: true));
        var checks = new List<string>();
        void Require(bool value, string label)
        { if (!value) throw new InvalidOperationException("GIF cancellation: " + label); checks.Add(label + ": PASS"); }
        var bounds = new Rectangle(0, 0, 16, 12);
        RecordingSettings Settings(int seconds = 60) => new() { GifFps = 10, GifQuality = 128, GifDurationSeconds = seconds };
        string[] Folders() => Directory.Exists(TemporarySession.Root)
            ? Directory.GetDirectories(TemporarySession.Root).Order().ToArray() : [];
        try
        {
            var captures = 0;
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                var task = new GifRecordingService((_, _) => { captures++; return Frame(); })
                    .CaptureAsync(bounds, Settings(), cancellationToken: cancelled.Token);
                await Cancelled(task, cancelled.Token);
                Require(captures == 0 && Folders().Length == 0, "pre-cancel produces no frames or temporary session");
            }

            // These active sessions are unrelated to the operation under test.
            using var foreignGif = TemporarySession.Create("gif");
            using var foreignOcr = TemporarySession.Create("ocr");
            var protectedFolders = Folders();
            for (var iteration = 0; iteration < 3; iteration++)
            {
                // Cancel during capture, during draining after duration, and with a
                // concurrent capture fault. No real screen or audio is accessed.
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                using var cancel = new CancellationTokenSource();
                var stored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var calls = 0;
                Bitmap? discarded = null;
                var service = new GifRecordingService((_, _) =>
                {
                    if (Interlocked.Increment(ref calls) == 1) return Frame();
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Synthetic capture gate");
                    if (iteration == 2) throw new IOException("Synthetic capture failure during cancellation");
                    return discarded = Frame();
                });
                var progress = new InlineProgress(item =>
                {
                    if (item.Status.StartsWith("Gravando GIF ·", StringComparison.Ordinal)) stored.TrySetResult();
                    if (item.Status == "Finalizando GIF…") stopped.TrySetResult();
                });
                var operation = service.CaptureAsync(bounds, Settings(iteration == 1 ? 1 : 60), progress, cancel.Token);
                try
                {
                    await stored.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Require(entered.Wait(TimeSpan.FromSeconds(5)), $"case {iteration}: second synthetic capture held");
                    var own = Folders().Except(protectedFolders).Single();
                    Require(File.Exists(Path.Combine(own, "000000.png")), $"case {iteration}: stored frame exists before cancellation");
                    if (iteration == 1) await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    cancel.Cancel();
                    Require(!operation.IsCompleted && Directory.Exists(own), $"case {iteration}: cancellation waits for the in-flight pipeline");
                    release.Set();
                    await Cancelled(operation, cancel.Token);
                    Require(operation.IsCanceled && !Directory.Exists(own), $"case {iteration}: cancelled result and lease removed before return");
                    Require(Folders().SequenceEqual(protectedFolders), $"case {iteration}: unrelated active sessions preserved");
                    if (discarded is not null) Require(IsDisposed(discarded), $"case {iteration}: unpublished bitmap released");
                }
                finally
                {
                    release.Set(); cancel.Cancel();
                    try { (await operation.WaitAsync(TimeSpan.FromSeconds(5))).Dispose(); } catch (OperationCanceledException) { }
                }
            }

            var failure = new IOException("Synthetic capture error");
            var faulted = new GifRecordingService((_, _) => throw failure).CaptureAsync(bounds, Settings());
            try { using var result = await faulted.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("Fault not delivered"); }
            catch (IOException exception) { Require(ReferenceEquals(exception, failure), "early pipeline failure delivered without waiting 60 seconds"); }
            Require(Folders().SequenceEqual(protectedFolders), "pipeline failure leaves no owned temporary files");

            var successful = await new GifRecordingService((_, _) => Frame()).CaptureAsync(bounds, Settings(1));
            var deliveredFolder = Folders().Except(protectedFolders).Single();
            try
            {
                using var frame = successful.LoadFrame(0);
                Require(frame.Width == 16 && frame.Height == 12 && successful.FrameCount == 1 && successful.Fps == 10,
                    "successful result remains readable after controller disposal");
                Require(successful.Duration >= TimeSpan.FromMilliseconds(900) && successful.Metrics is { } metrics &&
                    metrics.CapturedFrames >= metrics.ProcessedFrames &&
                    metrics.ProcessedFrames == metrics.StoredFrames + metrics.DuplicateFrames,
                    "normal duration, deduplication and draining preserved");
            }
            finally { successful.Dispose(); successful.Dispose(); }
            Require(!Directory.Exists(deliveredFolder) && Folders().SequenceEqual(protectedFolders), "consumer disposal releases delivered result exactly once");

            using var captureEntered = new ManualResetEventSlim();
            using var captureRelease = new ManualResetEventSlim();
            var controller = new GifRecordingService((_, _) =>
            {
                captureEntered.Set();
                if (!captureRelease.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Synthetic controller gate");
                return Frame();
            }).StartRecording(bounds, Settings());
            try
            {
                Require(captureEntered.Wait(TimeSpan.FromSeconds(5)), "continuous controller has an in-flight synthetic capture");
                controller.Pause(); controller.Dispose(); controller.Dispose();
                var disposing = controller.DisposeAsync().AsTask();
                Require(!disposing.IsCompleted && controller.StopToken.IsCancellationRequested, "sync disposal is non-blocking and retains synchronization until workers finish");
                captureRelease.Set();
                await disposing.WaitAsync(TimeSpan.FromSeconds(5));
                Require(controller.Completion.IsCompleted, "async disposal observes pipeline completion even without a frame");
                try { _ = controller.StopToken; throw new InvalidOperationException("CTS not released"); }
                catch (ObjectDisposedException) { checks.Add("controller synchronization released after completion: PASS"); }
                controller.Stop(); controller.Pause(); controller.Resume(); await controller.DisposeAsync();
                Require(Folders().SequenceEqual(protectedFolders), "duplicate disposal and late controls leave no owned resources");
            }
            finally { captureRelease.Set(); await controller.DisposeAsync(); }

            var spooled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (var controllerWithResult = new GifRecordingService((_, _) => Frame()).StartRecording(bounds, Settings()))
            {
                controllerWithResult.ProgressChanged += (_, item) =>
                { if (item.Status.StartsWith("Gravando GIF ·", StringComparison.Ordinal)) spooled.TrySetResult(); };
                await spooled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                controllerWithResult.Pause();
                await controllerWithResult.DisposeAsync();
                using var result = await controllerWithResult.Completion;
                using var frame = result.LoadFrame(0);
                Require(frame.Width == 16, "continuous UI caller still owns result after paused controller disposal");
            }
            Require(Folders().SequenceEqual(protectedFolders), "continuous result cleanup preserves unrelated sessions");
            File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
            Console.WriteLine($"GIF cancellation: {checks.Count} checks PASS (synthetic frames only)");
        }
        finally { AppPaths.Initialize(previous); }
    }

    private static Bitmap Frame()
    {
        var frame = new Bitmap(16, 12);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.CornflowerBlue);
        return frame;
    }

    private static bool IsDisposed(Bitmap bitmap)
    {
        try { _ = bitmap.Width; return false; }
        catch (ArgumentException) { return true; }
    }

    private static async Task Cancelled(Task<GifRecordingResult> task, CancellationToken token)
    {
        try { using var result = await task.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("Cancellation not delivered"); }
        catch (OperationCanceledException exception) when (exception.CancellationToken == token) { }
    }

    private sealed class InlineProgress(Action<RecordingProgress> report) : IProgress<RecordingProgress>
    { public void Report(RecordingProgress value) => report(value); }
}
