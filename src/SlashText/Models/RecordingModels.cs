using System.Drawing;
using System.IO;

namespace SlashText.Models;

public enum RecordingTargetKind
{
    Monitor,
    Region,
    Window
}

public enum ScreenRecordingState
{
    Idle,
    Starting,
    Recording,
    Paused,
    Stopping,
    Finalizing,
    Completed,
    Failed,
    Disposed
}

public sealed record RecordingTarget(
    RecordingTargetKind Kind,
    Rectangle Bounds,
    IntPtr WindowHandle = default,
    string? DisplayDeviceName = null)
{
    public string Type => Kind switch
    {
        RecordingTargetKind.Monitor => "monitor",
        RecordingTargetKind.Region => "regiao",
        _ => "janela"
    };
}

public sealed record RecordingProgress(
    TimeSpan Elapsed,
    bool IsPaused,
    string Status);

public sealed record GifCaptureMetrics(
    int CapturedFrames,
    int StoredFrames,
    int DuplicateFrames,
    int DroppedFrames,
    double CaptureMilliseconds,
    double ResizeMilliseconds,
    double QueueWaitMilliseconds,
    long TemporaryBytes,
    int RequestedFps = 0,
    double EffectiveCapturedFps = 0,
    int ProcessedFrames = 0);

public sealed class GifRecordingResult : IDisposable
{
    private readonly IReadOnlyList<System.Drawing.Bitmap>? _memoryFrames;
    private readonly IReadOnlyList<string>? _framePaths;
    private readonly Services.TemporarySession? _temporarySession;

    public GifRecordingResult(
        IReadOnlyList<System.Drawing.Bitmap> frames,
        int fps,
        Rectangle bounds,
        IReadOnlyList<int>? frameDelaysCentiseconds = null,
        GifCaptureMetrics? metrics = null)
    {
        _memoryFrames = frames;
        Fps = fps;
        Bounds = bounds;
        FrameDelaysCentiseconds = frameDelaysCentiseconds ??
            Enumerable.Repeat(Math.Max(2, (int)Math.Round(100d / Math.Max(1, fps))), frames.Count)
                .ToArray();
        Metrics = metrics;
    }

    internal GifRecordingResult(
        IReadOnlyList<string> framePaths,
        int fps,
        Rectangle bounds,
        IReadOnlyList<int> frameDelaysCentiseconds,
        GifCaptureMetrics metrics,
        Services.TemporarySession temporarySession)
    {
        _framePaths = framePaths;
        _temporarySession = temporarySession;
        Fps = fps;
        Bounds = bounds;
        FrameDelaysCentiseconds = frameDelaysCentiseconds;
        Metrics = metrics;
    }

    public int Fps { get; }
    public Rectangle Bounds { get; }
    public int FrameCount => _memoryFrames?.Count ?? _framePaths?.Count ?? 0;
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;
    public IReadOnlyList<int> FrameDelaysCentiseconds { get; }
    public GifCaptureMetrics? Metrics { get; }
    public TimeSpan Duration => TimeSpan.FromMilliseconds(
        FrameDelaysCentiseconds.Sum() * 10d);

    public System.Drawing.Bitmap LoadFrame(int index)
    {
        if (_memoryFrames is not null)
        {
            return (System.Drawing.Bitmap)_memoryFrames[index].Clone();
        }
        if (_framePaths is null)
        {
            throw new ObjectDisposedException(nameof(GifRecordingResult));
        }
        return new System.Drawing.Bitmap(_framePaths[index]);
    }

    public void Dispose()
    {
        if (_memoryFrames is not null)
        {
            foreach (var frame in _memoryFrames)
            {
                frame.Dispose();
            }
        }
        _temporarySession?.Dispose();
    }
}
