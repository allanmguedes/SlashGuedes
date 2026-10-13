using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SlashText.Models;

namespace SlashText.Services;

public sealed class GifRecordingService
{
    internal const int QueueCapacity = 2;
    internal const int MaximumStoredFrames = 10_000;
    internal const long MaximumTemporaryBytes = 4L * 1024 * 1024 * 1024;
    private readonly Func<Rectangle, bool, Bitmap> _captureFrame;

    public GifRecordingService()
        : this(CaptureService.CaptureBitmap)
    {
    }

    internal GifRecordingService(Func<Rectangle, bool, Bitmap> captureFrame)
    {
        _captureFrame = captureFrame;
    }

    internal GifRecordingSession StartRecording(
        Rectangle bounds,
        RecordingSettings settings)
    {
        RecordingPresetCatalog.Normalize(settings);
        ValidateSettings(settings);
        return new GifRecordingSession(this, bounds, settings);
    }

    // Kept for compatibility with callers/tests that request a bounded capture.
    public async Task<GifRecordingResult> CaptureAsync(
        Rectangle bounds,
        RecordingSettings settings,
        IProgress<RecordingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var session = StartRecording(bounds, settings);
        if (progress is not null)
        {
            session.ProgressChanged += (_, item) => progress.Report(item);
        }
        using var registration = cancellationToken.Register(session.Cancel);
        await Task.Delay(
            TimeSpan.FromSeconds(Math.Max(1, settings.GifDurationSeconds)),
            cancellationToken).ConfigureAwait(false);
        session.Stop();
        return await session.Completion.ConfigureAwait(false);
    }

    internal async Task<GifRecordingResult> CaptureContinuousAsync(
        GifRecordingSession session,
        Rectangle bounds,
        RecordingSettings settings)
    {
        var recordingId = session.RecordingId;
        var interval = TimeSpan.FromSeconds(1d / settings.GifFps);
        var defaultDelay = Math.Max(2, (int)Math.Round(100d / settings.GifFps));
        var temporarySession = TemporarySession.Create("gif");
        var temporaryDirectory = temporarySession.DirectoryPath;
        var framePaths = new List<string>();
        var delays = new List<int>();
        var channel = Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        using var pipelineFailure = new CancellationTokenSource();
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            session.StopToken,
            pipelineFailure.Token);
        long captureTicks = 0;
        long spoolTicks = 0;
        long queueWaitTicks = 0;
        long temporaryBytes = 0;
        var capturedFrames = 0;
        var processedFrames = 0;
        var duplicateFrames = 0;
        var droppedFrames = 0;

        Log("gif.capture-start",
            recordingId,
            ("width", bounds.Width),
            ("height", bounds.Height),
            ("requestedFps", settings.GifFps),
            ("qualityColors", settings.GifQuality),
            ("queueCapacity", QueueCapacity),
            ("maximumStoredFrames", MaximumStoredFrames),
            ("maximumTemporaryBytes", MaximumTemporaryBytes));

        var producer = Task.Run(async () =>
        {
            Exception? failure = null;
            var nextFrameAt = TimeSpan.Zero;
            var index = 0;
            try
            {
                while (true)
                {
                    var token = producerCancellation.Token;
                    token.ThrowIfCancellationRequested();
                    session.WaitUntilResumed(token);
                    var wait = nextFrameAt - session.Elapsed;
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, token).ConfigureAwait(false);
                    }
                    session.WaitUntilResumed(token);

                    var lag = session.Elapsed - nextFrameAt;
                    if (lag >= interval)
                    {
                        var skipped = Math.Max(1, (int)(lag.Ticks / interval.Ticks));
                        droppedFrames += skipped;
                        nextFrameAt += TimeSpan.FromTicks(interval.Ticks * skipped);
                        Log("gif.frames-dropped-overload", recordingId,
                            ("count", skipped),
                            ("total", droppedFrames),
                            ("lagMs", lag.TotalMilliseconds));
                    }

                    var captureStarted = Stopwatch.GetTimestamp();
                    var bitmap = _captureFrame(bounds, settings.IncludeCursor);
                    Interlocked.Add(ref captureTicks, Stopwatch.GetTimestamp() - captureStarted);
                    capturedFrames++;
                    try
                    {
                        var queueStarted = Stopwatch.GetTimestamp();
                        await channel.Writer.WriteAsync(
                            new CapturedFrame(index++, session.Elapsed, bitmap),
                            token).ConfigureAwait(false);
                        Interlocked.Add(ref queueWaitTicks, Stopwatch.GetTimestamp() - queueStarted);
                    }
                    catch
                    {
                        bitmap.Dispose();
                        throw;
                    }
                    nextFrameAt += interval;
                }
            }
            catch (OperationCanceledException) when (session.StopRequested)
            {
                // Explicit stop is the normal completion signal.
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                channel.Writer.TryComplete(failure);
            }
        });

        byte[]? previousPixels = null;
        var previousLength = 0;
        TimeSpan? lastStoredAt = null;
        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var captured in channel.Reader.ReadAllAsync(pipelineFailure.Token)
                                   .ConfigureAwait(false))
                {
                    processedFrames++;
                    using (captured.Bitmap)
                    {
                        if (IsDuplicate(captured.Bitmap, ref previousPixels, ref previousLength))
                        {
                            duplicateFrames++;
                        }
                        else
                        {
                            if (framePaths.Count >= MaximumStoredFrames)
                            {
                                throw TechnicalLimit(
                                    UiText.Format($"O limite técnico de {MaximumStoredFrames:N0} quadros do GIF foi atingido."));
                            }
                            if (lastStoredAt is not null)
                            {
                                delays[^1] = DelayBetween(lastStoredAt.Value, captured.Elapsed);
                            }
                            var framePath = Path.Combine(
                                temporaryDirectory,
                                $"{framePaths.Count:D6}.png");
                            var spoolStarted = Stopwatch.GetTimestamp();
                            captured.Bitmap.Save(framePath, ImageFormat.Png);
                            Interlocked.Add(ref spoolTicks, Stopwatch.GetTimestamp() - spoolStarted);
                            var bytes = new FileInfo(framePath).Length;
                            temporaryBytes += bytes;
                            if (temporaryBytes > MaximumTemporaryBytes)
                            {
                                throw TechnicalLimit(
                                    UiText.Get("O GIF atingiu o limite técnico de 4 GiB de arquivos temporários."));
                            }
                            framePaths.Add(framePath);
                            delays.Add(defaultDelay);
                            lastStoredAt = captured.Elapsed;
                        }
                    }
                    session.Report($"Gravando GIF · {capturedFrames:N0} quadros");
                }
            }
            catch
            {
                pipelineFailure.Cancel();
                throw;
            }
        });

        try
        {
            await Task.WhenAll(producer, consumer).ConfigureAwait(false);
            if (framePaths.Count == 0 || lastStoredAt is null)
            {
                throw new InvalidOperationException(
                    UiText.Get("A gravação foi finalizada antes de produzir um quadro GIF."));
            }
            delays[^1] = DelayBetween(lastStoredAt.Value, session.Elapsed);
            var activeSeconds = Math.Max(session.Elapsed.TotalSeconds, 0.001);
            var metrics = new GifCaptureMetrics(
                capturedFrames,
                framePaths.Count,
                duplicateFrames,
                droppedFrames,
                TicksToMilliseconds(captureTicks),
                TicksToMilliseconds(spoolTicks),
                TicksToMilliseconds(queueWaitTicks),
                temporaryBytes,
                settings.GifFps,
                capturedFrames / activeSeconds,
                processedFrames);
            Log("gif.capture-complete",
                recordingId,
                ("requestedFps", metrics.RequestedFps),
                ("effectiveCapturedFps", metrics.EffectiveCapturedFps),
                ("capturedFrames", metrics.CapturedFrames),
                ("processedFrames", metrics.ProcessedFrames),
                ("storedFrames", metrics.StoredFrames),
                ("duplicateFrames", metrics.DuplicateFrames),
                ("droppedFrames", metrics.DroppedFrames),
                ("captureMs", metrics.CaptureMilliseconds),
                ("spoolMs", metrics.ResizeMilliseconds),
                ("queueWaitMs", metrics.QueueWaitMilliseconds),
                ("temporaryBytes", metrics.TemporaryBytes),
                ("durationMs", session.Elapsed.TotalMilliseconds));
            return new GifRecordingResult(
                framePaths,
                settings.GifFps,
                bounds,
                delays,
                metrics,
                temporarySession);
        }
        catch (Exception exception)
        {
            temporarySession.Dispose();
            AppDiagnosticLog.WriteException("gif.capture-failed", exception);
            throw;
        }
        finally
        {
            if (previousPixels is not null)
            {
                ArrayPool<byte>.Shared.Return(previousPixels);
            }
        }
    }

    public Task<string> SaveAsync(
        GifRecordingResult recording,
        CaptureSettings captureSettings,
        string type,
        int colorCount,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Save(recording, captureSettings, type, colorCount), cancellationToken);

    public string Save(
        GifRecordingResult recording,
        CaptureSettings captureSettings,
        string type,
        int colorCount = 128)
    {
        if (recording.FrameCount == 0)
        {
            throw new InvalidOperationException(UiText.Get("O GIF não contém quadros para salvar."));
        }
        colorCount = RecordingPresetCatalog.NormalizeGifQuality(colorCount);
        var encodeClock = Stopwatch.StartNew();
        var path = ScreenRecordingService.CreateMediaPath(captureSettings, type, ".gif");
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        var encoder = new GifBitmapEncoder();
        for (var index = 0; index < recording.FrameCount; index++)
        {
            using var bitmap = recording.LoadFrame(index);
            var source = Quantize(ToBitmapSource(bitmap), colorCount);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery(
                "/grctlext/Delay",
                (ushort)Math.Clamp(recording.FrameDelaysCentiseconds[index], 2, ushort.MaxValue));
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        }

        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                encoder.Save(stream);
                stream.Flush(flushToDisk: true);
            }
            EnsureLoopExtension(temporaryPath);
            File.Move(temporaryPath, path);
            AppDiagnosticLog.Write(
                "gif.encode-complete",
                ("frames", recording.FrameCount),
                ("qualityColors", colorCount),
                ("encodeMs", encodeClock.Elapsed.TotalMilliseconds),
                ("bytes", new FileInfo(path).Length));
            return path;
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            AppDiagnosticLog.WriteException("gif.encode-failed", exception);
            throw;
        }
    }

    internal static void ValidateSettings(RecordingSettings settings)
    {
        if (!RecordingPresetCatalog.GifFps.Any(item => item.Value == settings.GifFps) ||
            !RecordingPresetCatalog.GifQuality.Any(item => item.Value == settings.GifQuality))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                UiText.Get("Selecione um preset disponível de FPS e qualidade do GIF."));
        }
    }

    internal static BitmapSource Quantize(BitmapSource source, int colorCount)
    {
        colorCount = RecordingPresetCatalog.NormalizeGifQuality(colorCount);
        return new FormatConvertedBitmap(
            source,
            PixelFormats.Indexed8,
            BuildAdaptivePalette(source, colorCount),
            0);
    }

    private static BitmapPalette BuildAdaptivePalette(
        BitmapSource source,
        int colorCount)
    {
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        var stride = source.PixelWidth * 4;
        var bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                null,
                0);
        bgra.CopyPixels(pixels, stride, 0);

        const int maximumSamples = 65_536;
        var pixelCount = source.PixelWidth * source.PixelHeight;
        var sampleStep = Math.Max(
            1,
            (int)Math.Ceiling(Math.Sqrt(pixelCount / (double)maximumSamples)));
        var histogram = new Dictionary<int, PaletteAccumulator>();
        for (var y = 0; y < source.PixelHeight; y += sampleStep)
        {
            var row = y * stride;
            for (var x = 0; x < source.PixelWidth; x += sampleStep)
            {
                var offset = row + x * 4;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var key = ((red >> 3) << 10) |
                          ((green >> 3) << 5) |
                          (blue >> 3);
                if (!histogram.TryGetValue(key, out var accumulator))
                {
                    accumulator = new PaletteAccumulator();
                    histogram.Add(key, accumulator);
                }
                accumulator.Add(red, green, blue);
            }
        }

        var candidates = histogram.Values
            .OrderByDescending(item => item.Count)
            .Take(Math.Max(512, colorCount * 8))
            .Select(item => item.ToCandidate())
            .ToList();
        if (candidates.Count == 0)
        {
            return new BitmapPalette(
                [System.Windows.Media.Color.FromRgb(0, 0, 0)]);
        }

        var selected = new List<PaletteCandidate>(
            Math.Min(colorCount, candidates.Count))
        {
            candidates[0]
        };
        candidates.RemoveAt(0);
        foreach (var candidate in candidates)
        {
            candidate.MinimumDistanceSquared =
                ColorDistanceSquared(candidate, selected[0]);
        }

        while (selected.Count < colorCount && candidates.Count > 0)
        {
            var bestIndex = 0;
            var bestScore = double.MinValue;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var score = Math.Log2(candidate.Count + 1d) *
                            (candidate.MinimumDistanceSquared + 64d);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            var chosen = candidates[bestIndex];
            candidates.RemoveAt(bestIndex);
            selected.Add(chosen);
            foreach (var candidate in candidates)
            {
                candidate.MinimumDistanceSquared = Math.Min(
                    candidate.MinimumDistanceSquared,
                    ColorDistanceSquared(candidate, chosen));
            }
        }

        return new BitmapPalette(
            selected.Select(item =>
                System.Windows.Media.Color.FromRgb(
                    item.Red,
                    item.Green,
                    item.Blue)).ToList());
    }

    private static int ColorDistanceSquared(
        PaletteCandidate first,
        PaletteCandidate second)
    {
        var red = first.Red - second.Red;
        var green = first.Green - second.Green;
        var blue = first.Blue - second.Blue;
        return red * red + green * green + blue * blue;
    }

    private sealed class PaletteAccumulator
    {
        private long _red;
        private long _green;
        private long _blue;

        public int Count { get; private set; }

        public void Add(byte red, byte green, byte blue)
        {
            Count++;
            _red += red;
            _green += green;
            _blue += blue;
        }

        public PaletteCandidate ToCandidate() => new(
            Count,
            (byte)(_red / Count),
            (byte)(_green / Count),
            (byte)(_blue / Count));
    }

    private sealed class PaletteCandidate(
        int count,
        byte red,
        byte green,
        byte blue)
    {
        public int Count { get; } = count;
        public byte Red { get; } = red;
        public byte Green { get; } = green;
        public byte Blue { get; } = blue;
        public int MinimumDistanceSquared { get; set; } = int.MaxValue;
    }

    private static int DelayBetween(TimeSpan start, TimeSpan end) =>
        Math.Max(2, (int)Math.Round(Math.Max(0, (end - start).TotalMilliseconds) / 10d));

    private static GifTechnicalLimitException TechnicalLimit(string message)
    {
        AppDiagnosticLog.Write("gif.technical-limit", ("message", message));
        return new GifTechnicalLimitException(
            message + UiText.Get(" A gravação foi interrompida com uma mensagem explícita para proteger o computador."));
    }

    private static void Log(
        string stage,
        Guid recordingId,
        params (string Key, object? Value)[] fields) =>
        AppDiagnosticLog.Write(
            stage,
            [("recordingId", recordingId.ToString("N")), .. fields]);

    private static bool IsDuplicate(
        Bitmap bitmap,
        ref byte[]? previousPixels,
        ref int previousLength)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(
            rect,
            ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[]? current = null;
        try
        {
            var length = Math.Abs(data.Stride) * data.Height;
            current = ArrayPool<byte>.Shared.Rent(length);
            Marshal.Copy(data.Scan0, current, 0, length);
            var duplicate = previousPixels is not null &&
                previousLength == length &&
                current.AsSpan(0, length).SequenceEqual(previousPixels.AsSpan(0, length));
            if (duplicate)
            {
                ArrayPool<byte>.Shared.Return(current);
                current = null;
                return true;
            }
            if (previousPixels is not null)
            {
                ArrayPool<byte>.Shared.Return(previousPixels);
            }
            previousPixels = current;
            previousLength = length;
            current = null;
            return false;
        }
        finally
        {
            bitmap.UnlockBits(data);
            if (current is not null)
            {
                ArrayPool<byte>.Shared.Return(current);
            }
        }
    }

    private static double TicksToMilliseconds(long ticks) =>
        ticks * 1000d / Stopwatch.Frequency;

    private static void EnsureLoopExtension(string path)
    {
        var gif = File.ReadAllBytes(path);
        if (gif.Length < 13 || gif[0] != 'G' || gif[1] != 'I' || gif[2] != 'F')
        {
            throw new InvalidDataException(UiText.Get("O encoder criou um contêiner GIF inválido."));
        }
        ReadOnlySpan<byte> applicationId = "NETSCAPE2.0"u8;
        if (gif.AsSpan().IndexOf(applicationId) >= 0)
        {
            return;
        }
        var packedFields = gif[10];
        var colorTableLength = (packedFields & 0x80) == 0
            ? 0
            : 3 * (1 << ((packedFields & 0x07) + 1));
        var insertionOffset = 13 + colorTableLength;
        if (insertionOffset > gif.Length)
        {
            throw new InvalidDataException(UiText.Get("A tabela de cores do GIF está incompleta."));
        }
        ReadOnlySpan<byte> loopExtension =
        [
            0x21, 0xFF, 0x0B,
            (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A',
            (byte)'P', (byte)'E', (byte)'2', (byte)'.', (byte)'0',
            0x03, 0x01, 0x00, 0x00, 0x00
        ];
        var result = new byte[gif.Length + loopExtension.Length];
        gif.AsSpan(0, insertionOffset).CopyTo(result);
        loopExtension.CopyTo(result.AsSpan(insertionOffset));
        gif.AsSpan(insertionOffset).CopyTo(result.AsSpan(insertionOffset + loopExtension.Length));
        File.WriteAllBytes(path, result);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    private sealed record CapturedFrame(int Index, TimeSpan Elapsed, Bitmap Bitmap);
}

internal sealed class GifRecordingSession : IRecordingController, IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ManualResetEventSlim _resumeGate = new(initialState: true);
    private readonly MonotonicRecordingClock _clock = new();
    private int _pauseRequested;
    private int _stopRequested;

    public GifRecordingSession(
        GifRecordingService service,
        Rectangle bounds,
        RecordingSettings settings)
    {
        RecordingId = Guid.NewGuid();
        _clock.Start();
        Completion = Task.Run(() => service.CaptureContinuousAsync(this, bounds, settings));
        Report("Gravando GIF");
    }

    public Guid RecordingId { get; }
    public Task<GifRecordingResult> Completion { get; }
    public bool IsPaused => Volatile.Read(ref _pauseRequested) == 1;
    public bool StopRequested => Volatile.Read(ref _stopRequested) == 1;
    public TimeSpan Elapsed => _clock.Elapsed;
    internal CancellationToken StopToken => _stop.Token;

    public event EventHandler<RecordingProgress>? ProgressChanged;

    public void Pause()
    {
        if (StopRequested || Interlocked.CompareExchange(ref _pauseRequested, 1, 0) != 0)
        {
            return;
        }
        _clock.Pause();
        _resumeGate.Reset();
        Report("GIF pausado");
    }

    public void Resume()
    {
        if (StopRequested || Interlocked.CompareExchange(ref _pauseRequested, 0, 1) != 1)
        {
            return;
        }
        _clock.Resume();
        _resumeGate.Set();
        Report("Gravando GIF");
    }

    public void Stop()
    {
        if (Interlocked.CompareExchange(ref _stopRequested, 1, 0) != 0)
        {
            return;
        }
        _clock.Stop();
        _resumeGate.Set();
        Report("Finalizando GIF…");
        _stop.Cancel();
    }

    internal void Cancel() => Stop();

    internal void WaitUntilResumed(CancellationToken token) => _resumeGate.Wait(token);

    internal void Report(string status)
    {
        try
        {
            ProgressChanged?.Invoke(this, new RecordingProgress(Elapsed, IsPaused, status));
        }
        catch (Exception exception)
        {
            AppDiagnosticLog.WriteException("gif.progress-observer-exception", exception);
        }
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
        _resumeGate.Dispose();
    }
}

public sealed class GifTechnicalLimitException : InvalidOperationException
{
    public GifTechnicalLimitException(string message) : base(message) { }
}
