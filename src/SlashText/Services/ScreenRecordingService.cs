using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using ScreenRecorderLib;
using SlashText.Models;

namespace SlashText.Services;

public sealed class ScreenRecordingService : IRecordingController, IDisposable
{
    private const string EncoderPolicy =
        "H264 Media Foundation; hardware if available, software fallback";
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(12);
    private static int _unresolvedNativeStops;
    private readonly object _gate = new();
    private readonly IScreenRecorderBackendFactory _backendFactory;
    private readonly TimeSpan _stopTimeout;
    private readonly MonotonicRecordingClock _clock = new();
    private IScreenRecorderBackend? _backend;
    private Task _nativeQueue = Task.CompletedTask;
    private TaskCompletionSource<string>? _completion;
    private string _path = string.Empty;
    private string _workingPath = string.Empty;
    private ScreenRecordingState _state = ScreenRecordingState.Idle;
    private Guid _recordingId;
    private int _finalizationClaimed;
    private int _stopRequested;
    private int _pauseRequested;
    private int _timeoutReported;
    private int _nativeStopOutstanding;
    private bool _disposed;
    private RecordingAudioSettings _audio = new();
    public bool IsMicrophoneMuted { get; private set; }
    public bool IsComputerMuted { get; private set; }

    public ScreenRecordingService()
        : this(new ScreenRecorderBackendFactory(), DefaultStopTimeout)
    {
    }

    internal ScreenRecordingService(
        IScreenRecorderBackendFactory backendFactory,
        TimeSpan stopTimeout)
    {
        _backendFactory = backendFactory;
        _stopTimeout = stopTimeout;
    }

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _state is ScreenRecordingState.Starting or
                    ScreenRecordingState.Recording or
                    ScreenRecordingState.Paused or
                    ScreenRecordingState.Stopping or
                    ScreenRecordingState.Finalizing;
            }
        }
    }

    public bool IsPaused => Volatile.Read(ref _pauseRequested) == 1;
    public Guid RecordingId => _recordingId;
    public TimeSpan Elapsed => _clock.Elapsed;

    public ScreenRecordingState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event EventHandler<RecordingProgress>? ProgressChanged;
    public event EventHandler<string>? RecordingFailed;

    public Task<string> StartAsync(
        RecordingTarget target,
        CaptureSettings captureSettings,
        RecordingSettings settings)
    {
        TaskCompletionSource<string> completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRecording)
            {
                throw new InvalidOperationException(UiText.Get("Já existe uma gravação em andamento."));
            }
            if (!Environment.Is64BitProcess)
            {
                throw new PlatformNotSupportedException(
                    UiText.Get("A gravação MP4 do SlashDesk requer o processo x64."));
            }
            if (Volatile.Read(ref _unresolvedNativeStops) != 0)
            {
                throw new InvalidOperationException(
                    UiText.Get("Uma finalização MP4 anterior ainda está presa no encoder nativo. ") +
                    UiText.Get("Reinicie o SlashDesk antes de iniciar outra gravação e envie os logs."));
            }

            RecordingPresetCatalog.Normalize(settings);
            _audio = settings.Audio.Copy();
            IsMicrophoneMuted = !_audio.CaptureMicrophone;
            IsComputerMuted = !_audio.CaptureComputer;
            _recordingId = Guid.NewGuid();
            _path = CreateMediaPath(captureSettings, target.Type, ".mp4");
            _workingPath = CreateWorkingPath(_path);
            _completion = completion = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _state = ScreenRecordingState.Starting;
            Volatile.Write(ref _finalizationClaimed, 0);
            Volatile.Write(ref _stopRequested, 0);
            Volatile.Write(ref _pauseRequested, 0);
            Volatile.Write(ref _timeoutReported, 0);
            Volatile.Write(ref _nativeStopOutstanding, 0);
            _clock.Reset();
        }

        Log(
            "recording.start-requested",
            ("target", target.Kind.ToString()),
            ("width", target.Bounds.Width),
            ("height", target.Bounds.Height),
            ("fps", settings.VideoFps),
            ("cursor", settings.IncludeCursor),
            ("computerAudio", _audio.CaptureComputer),
            ("microphoneAudio", _audio.CaptureMicrophone),
            ("encoder", EncoderPolicy));
        AppDiagnosticLog.MarkRecordingActive(
            _recordingId,
            target,
            settings,
            EncoderPolicy);
        EnqueueNative("recording.start", () => StartCore(target, settings));
        PublishProgress("Inicializando MP4");
        return completion.Task;
    }

    public void Pause()
    {
        if (Interlocked.CompareExchange(ref _pauseRequested, 1, 0) != 0 ||
            Volatile.Read(ref _stopRequested) != 0)
        {
            return;
        }
        _clock.Pause();
        EnqueueNative("recording.pause", PauseCore);
    }

    // Keep volume changes in the same native queue as pause/resume/stop.
    // A failed mute must not stop the video or falsely update the toolbar.
    public Task<bool> SetAudioMutedAsync(bool microphoneMuted, bool computerMuted)
    {
        lock (_gate)
        {
            if (_disposed || _state != ScreenRecordingState.Recording ||
                Volatile.Read(ref _stopRequested) != 0 ||
                (!_audio.CaptureMicrophone && !microphoneMuted) ||
                (!_audio.CaptureComputer && !computerMuted))
                return Task.FromResult(false);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _nativeQueue = _nativeQueue.ContinueWith(_ =>
            {
                try
                {
                    var backend = BackendSnapshot();
                    if (backend is null || State != ScreenRecordingState.Recording || Volatile.Read(ref _stopRequested) != 0)
                    {
                        completion.TrySetResult(false);
                        return;
                    }
                    var applied = backend.SetAudioVolumes(
                        microphoneMuted ? 0 : _audio.InputVolume,
                        computerMuted ? 0 : _audio.OutputVolume);
                    if (applied)
                    {
                        IsMicrophoneMuted = microphoneMuted;
                        IsComputerMuted = computerMuted;
                        Log("recording.audio-muted", ("microphone", microphoneMuted), ("computer", computerMuted));
                    }
                    completion.TrySetResult(applied);
                }
                catch (Exception exception)
                {
                    LogException("recording.audio-change-failed", exception);
                    completion.TrySetException(exception);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return completion.Task;
        }
    }

    public void Resume()
    {
        if (Interlocked.CompareExchange(ref _pauseRequested, 0, 1) != 1 ||
            Volatile.Read(ref _stopRequested) != 0)
        {
            return;
        }
        _clock.Resume();
        EnqueueNative("recording.resume", ResumeCore);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRecording)
            {
                return;
            }
        }
        if (Interlocked.CompareExchange(ref _stopRequested, 1, 0) != 0)
        {
            Log("recording.stop-duplicate-ignored");
            return;
        }

        _clock.Stop();
        Interlocked.Increment(ref _unresolvedNativeStops);
        Volatile.Write(ref _nativeStopOutstanding, 1);
        lock (_gate)
        {
            _state = ScreenRecordingState.Stopping;
        }
        Log("recording.stop-requested", ("elapsedMs", Elapsed.TotalMilliseconds));
        _ = MonitorStopTimeoutAsync();
        EnqueueNative("recording.stop", StopCore);
        PublishProgress("Finalizando MP4…");
    }

    private void StartCore(RecordingTarget target, RecordingSettings settings)
    {
        var started = Stopwatch.StartNew();
        Log("recording.backend-create-enter");
        var libraryLogPath = AppDiagnosticLog.CreateLibraryLogPath(_recordingId);
        var backend = _backendFactory.Create(BuildOptions(target, settings, libraryLogPath));
        backend.Completed += OnRecordingComplete;
        backend.Failed += OnRecordingFailed;
        backend.StatusChanged += OnStatusChanged;
        lock (_gate)
        {
            _backend = backend;
        }
        Log(
            "recording.backend-create-return",
            ("libraryLog", Path.GetFileName(libraryLogPath)),
            ("durationMs", started.Elapsed.TotalMilliseconds));
        started.Restart();
        Log("recording.native-record-enter");
        backend.Record(_workingPath);
        Log("recording.native-record-return", ("durationMs", started.Elapsed.TotalMilliseconds));
    }

    private void PauseCore()
    {
        var backend = BackendSnapshot();
        if (backend is null || Volatile.Read(ref _stopRequested) != 0)
        {
            return;
        }
        var started = Stopwatch.StartNew();
        Log("recording.native-pause-enter");
        backend.Pause();
        Log("recording.native-pause-return", ("durationMs", started.Elapsed.TotalMilliseconds));
        lock (_gate)
        {
            _state = ScreenRecordingState.Paused;
        }
        PublishProgress("MP4 pausado");
    }

    private void ResumeCore()
    {
        var backend = BackendSnapshot();
        if (backend is null || Volatile.Read(ref _stopRequested) != 0)
        {
            return;
        }
        var started = Stopwatch.StartNew();
        Log("recording.native-resume-enter");
        backend.Resume();
        Log("recording.native-resume-return", ("durationMs", started.Elapsed.TotalMilliseconds));
        lock (_gate)
        {
            _state = ScreenRecordingState.Recording;
        }
        PublishProgress("Gravando MP4");
    }

    private void StopCore()
    {
        var backend = BackendSnapshot();
        if (backend is null)
        {
            QueueFinalization(FinalizationRequest.Failed(
                UiText.Get("O gravador não foi inicializado.")));
            return;
        }
        var started = Stopwatch.StartNew();
        Log("recording.native-stop-enter");
        backend.Stop();
        Log("recording.native-stop-return", ("durationMs", started.Elapsed.TotalMilliseconds));
    }

    private async Task MonitorStopTimeoutAsync()
    {
        Log("recording.stop-wait-started", ("timeoutMs", _stopTimeout.TotalMilliseconds));
        await Task.Delay(_stopTimeout).ConfigureAwait(false);
        TaskCompletionSource<string>? completion;
        lock (_gate)
        {
            completion = _completion;
        }
        if (completion is null || completion.Task.IsCompleted ||
            Interlocked.CompareExchange(ref _timeoutReported, 1, 0) != 0)
        {
            return;
        }

        var error =
            UiText.Get("O encoder nativo não confirmou a finalização dentro do prazo. ") +
            UiText.Get("O SlashDesk foi liberado, mas reinicie-o antes de outra gravação e envie os logs.");
        lock (_gate)
        {
            _state = ScreenRecordingState.Failed;
        }
        Log(
            "recording.stop-timeout",
            ("timeoutMs", _stopTimeout.TotalMilliseconds),
            ("elapsedMs", Elapsed.TotalMilliseconds),
            ("temporaryFile", Path.GetFileName(_workingPath)));
        completion.TrySetException(new TimeoutException(error));
        RaiseRecordingFailed(error);
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
    {
        Log("recording.callback-complete", ("elapsedMs", Elapsed.TotalMilliseconds));
        QueueFinalization(FinalizationRequest.Completed(
            string.IsNullOrWhiteSpace(e.FilePath) ? _workingPath : e.FilePath));
    }

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        var error = string.IsNullOrWhiteSpace(e.Error)
            ? UiText.Get("O encoder de vídeo do Windows não conseguiu concluir a gravação.")
            : e.Error;
        Log("recording.callback-failed", ("error", error));
        QueueFinalization(FinalizationRequest.Failed(error));
    }

    private void OnStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        Log("recording.callback-status", ("status", e.Status.ToString()));
        lock (_gate)
        {
            if (Volatile.Read(ref _stopRequested) != 0 || IsTerminal(_state))
            {
                return;
            }
            if (_state == ScreenRecordingState.Starting && e.Status == RecorderStatus.Recording)
                _clock.Start();
            _state = e.Status switch
            {
                RecorderStatus.Recording => ScreenRecordingState.Recording,
                RecorderStatus.Paused => ScreenRecordingState.Paused,
                RecorderStatus.Finishing => ScreenRecordingState.Stopping,
                _ => _state
            };
        }
        PublishProgress(e.Status switch
        {
            RecorderStatus.Recording => "Gravando MP4",
            RecorderStatus.Paused => "MP4 pausado",
            RecorderStatus.Finishing => "Finalizando MP4…",
            _ => "Inicializando MP4"
        });
    }

    private void QueueFinalization(FinalizationRequest request)
    {
        if (Interlocked.CompareExchange(ref _finalizationClaimed, 1, 0) != 0)
        {
            Log("recording.finalization-duplicate-ignored");
            return;
        }
        EnqueueNative("recording.finalize", () => FinalizeCore(request));
    }

    private void FinalizeCore(FinalizationRequest request)
    {
        IScreenRecorderBackend? backend;
        TaskCompletionSource<string>? completion;
        string finalPath;
        string workingPath;
        lock (_gate)
        {
            _state = ScreenRecordingState.Finalizing;
            backend = _backend;
            _backend = null;
            completion = _completion;
            finalPath = _path;
            workingPath = _workingPath;
        }
        _clock.Stop();
        DetachBackend(backend);
        try
        {
            var disposeStarted = Stopwatch.StartNew();
            Log("recording.backend-dispose-enter");
            backend?.Dispose();
            Log("recording.backend-dispose-return",
                ("durationMs", disposeStarted.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception)
        {
            LogException("recording.backend-dispose-exception", exception);
        }

        try
        {
            if (request.Error is not null)
            {
                throw new InvalidOperationException(request.Error);
            }
            if (Volatile.Read(ref _timeoutReported) != 0)
            {
                lock (_gate)
                {
                    _state = ScreenRecordingState.Failed;
                }
                Log(
                    "recording.callback-after-timeout-cleaned",
                    ("temporaryFile", Path.GetFileName(workingPath)));
                return;
            }

            var recordedPath = string.IsNullOrWhiteSpace(request.RecordedPath)
                ? workingPath
                : request.RecordedPath;
            var validationStarted = Stopwatch.StartNew();
            Log("recording.validation-enter", ("file", Path.GetFileName(recordedPath)));
            ValidateMp4File(recordedPath);
            Log("recording.validation-return",
                ("durationMs", validationStarted.Elapsed.TotalMilliseconds));
            var moveStarted = Stopwatch.StartNew();
            Log("recording.temporary-move-enter");
            File.Move(recordedPath, finalPath);
            Log("recording.temporary-move-return",
                ("durationMs", moveStarted.Elapsed.TotalMilliseconds));
            lock (_gate)
            {
                _state = ScreenRecordingState.Completed;
            }
            Log(
                "recording.completed",
                ("durationMs", Elapsed.TotalMilliseconds),
                ("bytes", new FileInfo(finalPath).Length));
            completion?.TrySetResult(finalPath);
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _state = ScreenRecordingState.Failed;
            }
            LogException("recording.finalization-failed", exception);
            Log(
                "recording.temporary-preserved",
                ("file", Path.GetFileName(request.RecordedPath ?? workingPath)));
            completion?.TrySetException(exception);
            RaiseRecordingFailed(exception.Message);
        }
        finally
        {
            ReleaseUnresolvedStop();
            AppDiagnosticLog.MarkRecordingEnded();
        }
    }

    private void EnqueueNative(string stage, Action operation)
    {
        lock (_gate)
        {
            _nativeQueue = _nativeQueue.ContinueWith(
                _ =>
                {
                    try
                    {
                        operation();
                    }
                    catch (Exception exception)
                    {
                        LogException(stage + ".exception", exception);
                        QueueFinalization(FinalizationRequest.Failed(exception.Message));
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    internal static RecorderOptions BuildOptions(
        RecordingTarget target,
        RecordingSettings settings,
        string libraryLogPath)
    {
        RecordingPresetCatalog.Normalize(settings);
        RecordingSourceBase source;
        if (target.Kind == RecordingTargetKind.Window && target.WindowHandle != IntPtr.Zero)
        {
            source = new WindowRecordingSource(target.WindowHandle);
        }
        else
        {
            var display = string.IsNullOrWhiteSpace(target.DisplayDeviceName)
                ? new DisplayRecordingSource(DisplayRecordingSource.MainMonitor)
                : new DisplayRecordingSource(target.DisplayDeviceName);
            if (target.Kind == RecordingTargetKind.Region)
            {
                var screen = System.Windows.Forms.Screen.FromRectangle(target.Bounds);
                display.SourceRect = new ScreenRect(
                    Math.Max(0, target.Bounds.Left - screen.Bounds.Left),
                    Math.Max(0, target.Bounds.Top - screen.Bounds.Top),
                    Even(target.Bounds.Width),
                    Even(target.Bounds.Height));
            }
            source = display;
        }
        if (source is DisplayRecordingSource displaySource)
        {
            displaySource.IsCursorCaptureEnabled = settings.IncludeCursor;
        }
        else if (source is WindowRecordingSource windowSource)
        {
            windowSource.IsCursorCaptureEnabled = settings.IncludeCursor;
        }

        var (bitrate, quality) = settings.VideoQuality switch
        {
            "Baixa" => (2_500_000, 55),
            "Média" => (5_000_000, 70),
            "Muito alta" => (16_000_000, 95),
            _ => (9_000_000, 85)
        };
        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = [source] },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = target.Kind == RecordingTargetKind.Region
                    ? new ScreenSize(Even(target.Bounds.Width), Even(target.Bounds.Height))
                    : ScreenSize.Empty
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = settings.Audio.CaptureComputer || settings.Audio.CaptureMicrophone,
                IsOutputDeviceEnabled = settings.Audio.CaptureComputer,
                IsInputDeviceEnabled = settings.Audio.CaptureMicrophone,
                AudioOutputDevice = settings.Audio.OutputDeviceId,
                AudioInputDevice = settings.Audio.InputDeviceId,
                OutputVolume = settings.Audio.OutputVolume,
                InputVolume = settings.Audio.InputVolume
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Bitrate = bitrate,
                Quality = quality,
                Framerate = Math.Clamp(settings.VideoFps, 10, 60),
                IsFixedFramerate = false,
                IsThrottlingDisabled = false,
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.Quality,
                    EncoderProfile = H264Profile.Main
                },
                IsHardwareEncodingEnabled = true,
                IsLowLatencyEnabled = false,
                IsMp4FastStartEnabled = true
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = settings.IncludeCursor,
                IsMouseClicksDetected = false
            },
            LogOptions = new LogOptions
            {
                IsLogEnabled = true,
                LogFilePath = libraryLogPath,
                LogSeverityLevel = LogLevel.Trace
            }
        };
    }

    private static int Even(int value) => Math.Max(2, value - value % 2);

    public static string CreateMediaPath(CaptureSettings settings, string type, string extension)
    {
        var now = DateTimeOffset.Now;
        var directory = CaptureService.ResolveDirectoryTemplate(
            Environment.ExpandEnvironmentVariables(settings.OutputDirectoryTemplate),
            now);
        Directory.CreateDirectory(directory);
        var baseName = settings.FileNameTemplate
            .Replace("{date}", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HH-mm-ss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", type, StringComparison.OrdinalIgnoreCase)
            .Replace("{app}", "desktop", StringComparison.OrdinalIgnoreCase);
        baseName = CaptureService.SanitizeFileName(baseName);
        var path = Path.Combine(directory, baseName + extension);
        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(directory, $"{baseName}_{index}{extension}");
        }
        return path;
    }

    public static void ValidateMp4File(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException(UiText.Get("O encoder não criou o arquivo MP4."));
        }
        using var stream = File.OpenRead(path);
        if (stream.Length < 32)
        {
            throw new InvalidDataException(UiText.Get("O encoder criou um arquivo MP4 vazio ou incompleto."));
        }

        var foundFtyp = false;
        var foundMoov = false;
        var foundMdat = false;
        Span<byte> header = stackalloc byte[8];
        Span<byte> extended = stackalloc byte[8];
        while (stream.Position + header.Length <= stream.Length)
        {
            if (stream.Read(header) != header.Length)
            {
                break;
            }
            var size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            var type = System.Text.Encoding.ASCII.GetString(header[4..8]);
            long boxSize = size;
            if (size == 1)
            {
                if (stream.Read(extended) != extended.Length)
                {
                    break;
                }
                boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(extended);
            }
            else if (size == 0)
            {
                boxSize = stream.Length - (stream.Position - 8);
            }
            var headerSize = size == 1 ? 16 : 8;
            if (boxSize < headerSize || stream.Position + boxSize - headerSize > stream.Length)
            {
                throw new InvalidDataException(UiText.Get("O encoder criou uma estrutura MP4 truncada."));
            }
            foundFtyp |= type == "ftyp";
            foundMoov |= type == "moov";
            foundMdat |= type == "mdat";
            stream.Position += boxSize - headerSize;
        }
        if (!foundFtyp || !foundMoov || !foundMdat)
        {
            throw new InvalidDataException(
                UiText.Get("O encoder não concluiu a estrutura MP4 (ftyp/moov/mdat)."));
        }
    }

    private static string CreateWorkingPath(string finalPath)
    {
        var directory = Path.GetDirectoryName(finalPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(finalPath);
        return Path.Combine(directory, $".{name}.{Guid.NewGuid():N}.recording.mp4");
    }

    private IScreenRecorderBackend? BackendSnapshot()
    {
        lock (_gate)
        {
            return _backend;
        }
    }

    private static bool IsTerminal(ScreenRecordingState state) => state is
        ScreenRecordingState.Completed or ScreenRecordingState.Failed or
        ScreenRecordingState.Disposed;

    private void PublishProgress(string status)
    {
        try
        {
            ProgressChanged?.Invoke(this, new RecordingProgress(Elapsed, IsPaused, status));
        }
        catch (Exception exception)
        {
            LogException("recording.progress-observer-exception", exception);
        }
    }

    private void RaiseRecordingFailed(string message)
    {
        try
        {
            RecordingFailed?.Invoke(this, message);
        }
        catch (Exception exception)
        {
            LogException("recording.failure-observer-exception", exception);
        }
    }

    private void DetachBackend(IScreenRecorderBackend? backend)
    {
        if (backend is null)
        {
            return;
        }
        backend.Completed -= OnRecordingComplete;
        backend.Failed -= OnRecordingFailed;
        backend.StatusChanged -= OnStatusChanged;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_state == ScreenRecordingState.Idle)
            {
                _state = ScreenRecordingState.Disposed;
                return;
            }
        }
        Log("recording.dispose-requested", ("state", State.ToString()));
        Stop();
        // Never wait here. Native Stop may block; callbacks retain this instance
        // and own the single safe cleanup path.
    }

    private void ReleaseUnresolvedStop()
    {
        if (Interlocked.Exchange(ref _nativeStopOutstanding, 0) == 1)
        {
            Interlocked.Decrement(ref _unresolvedNativeStops);
        }
    }

    private void Log(string stage, params (string Key, object? Value)[] fields) =>
        AppDiagnosticLog.Write(
            stage,
            [("recordingId", _recordingId.ToString("N")), .. fields]);

    private void LogException(string stage, Exception exception) =>
        Log(
            stage,
            ("exceptionType", exception.GetType().FullName),
            ("hresult", $"0x{exception.HResult:X8}"),
            ("message", exception.Message));

    private sealed record FinalizationRequest(string? RecordedPath, string? Error)
    {
        public static FinalizationRequest Completed(string path) => new(path, null);
        public static FinalizationRequest Failed(string error) => new(null, error);
    }
}
