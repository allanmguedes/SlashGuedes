using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SlashText.Services;

internal static class DiagnosticPolicy
{
    internal const long MaximumFileBytes = 1024 * 1024;
    internal const long MaximumTotalBytes = 10 * MaximumFileBytes;
    internal const int RetentionDays = 7;
    private static readonly object Gate = new();
    private static readonly HashSet<string> Stages = new(StringComparer.Ordinal)
    {
        // Fixed application events only. Unknown event names cannot carry user text.
        "application.exit",
        "application.portable-smoke",
        "application.start",
        "capture.completed",
        "capture.failed",
        "capture.ignored_busy",
        "capture.started",
        "capture.settings.save_failed",
        "capture.toolbar-positioned",
        "clipboard.popup.failed",
        "clipboard.settings.save_failed",
        "exception.app-domain",
        "exception.unobserved-task",
        "exception.wpf-dispatcher",
        "expansion.clipboard_restore_unavailable",
        "expansion.completed",
        "expansion.failed",
        "expansion.ignored_busy",
        "expansion.plan",
        "expansion.segment",
        "expansion.started",
        "expansion.tab_sent",
        "gif.capture-complete",
        "gif.capture-failed",
        "gif.capture-start",
        "gif.encode-complete",
        "gif.encode-failed",
        "gif.frames-dropped-overload",
        "gif.progress-observer-exception",
        "gif.technical-limit",
        "history.file-corrupt",
        "history.item-corrupt",
        "quick-accent.settings.save_failed",
        "quick-accent.start_failed",
        "usage.save.pending",
        "usage.update.failed",
        "recording.audio-change-failed",
        "recording.audio-devices-failed",
        "recording.audio-muted",
        "recording.backend-create-enter",
        "recording.backend-create-return",
        "recording.backend-dispose-enter",
        "recording.backend-dispose-exception",
        "recording.backend-dispose-return",
        "recording.callback-after-timeout-cleaned",
        "recording.callback-complete",
        "recording.callback-failed",
        "recording.callback-status",
        "recording.completed",
        "recording.dispose-requested",
        "recording.failure-observer-exception",
        "recording.finalization-duplicate-ignored",
        "recording.finalization-failed",
        "recording.finalize",
        "recording.interface-restored",
        "recording.native-pause-enter",
        "recording.native-pause-return",
        "recording.native-record-enter",
        "recording.native-record-return",
        "recording.native-resume-enter",
        "recording.native-resume-return",
        "recording.native-stop-enter",
        "recording.native-stop-return",
        "recording.overlay-closed",
        "recording.overlay-finish-clicked",
        "recording.pause",
        "recording.previous-process-ended-without-finalization",
        "recording.progress-observer-exception",
        "recording.resume",
        "recording.start",
        "recording.start-requested",
        "recording.stop",
        "recording.stop-duplicate-ignored",
        "recording.stop-requested",
        "recording.stop-timeout",
        "recording.stop-wait-started",
        "recording.temporary-move-enter",
        "recording.temporary-move-return",
        "recording.temporary-preserved",
        "recording.validation-enter",
        "recording.validation-return",
        "retention.cleanup",
        "shortcuts.emoji.failed",
        "shortcuts.flag.save_failed",
        "shortcuts.share_default.rename_failed",
        "startup.module.failed",
        "startup.module.ready",
        "startup.shell.failed",
        "storage.json.corrupt_preserve_failed",
        "storage.json.corrupt_preserved",
        "storage.selected",
        "update.check.completed",
        "update.check.offline",
        "update.check.started",
        "update.cleanup.completed",
        "update.cleanup.failed",
        "update.helper.starting",
        "update.monitor.failed",
        "update.prepare.failed",
        "update.prepared",
        "update.version.deferred",
        "update.version.ignored"
    };
    private static readonly HashSet<string> Numbers = new(StringComparer.Ordinal)
    {
        "processId", "threadId", "width", "height", "fps", "bytes", "durationMs", "elapsedMs",
        "timeoutMs", "count", "migrationWarnings", "capturedFrames", "storedFrames", "frames",
        "duplicateFrames", "droppedFrames", "processedFrames", "temporaryBytes", "captureMs",
        "spoolMs", "encodeMs", "queueWaitMs", "requestedFps", "effectiveCapturedFps", "queueCapacity",
        "maximumStoredFrames", "maximumTemporaryBytes", "qualityColors", "exitCode", "downloadBytes",
        "typedCharacterCount", "insertedCharacterCount", "segmentCount", "segmentIndex", "characterCount",
        "tabCount", "textLength", "removedSessions", "foregroundProcessId", "lagMs"
    };
    private static readonly HashSet<string> Booleans = new(StringComparer.Ordinal)
    {
        "cursor", "computerAudio", "microphoneAudio", "microphone", "computer", "migrated",
        "competingSourcePreserved", "is64BitProcess", "targetHandleValid", "invokedByShortcut",
        "windowWasVisible", "foregroundMatches", "hasTabs", "isRichText", "restored", "usesClipboard", "sendTab"
    };
    private static readonly HashSet<string> Tokens = new(StringComparer.Ordinal)
    {
        "Idle", "Starting", "Recording", "Paused", "Stopping", "Finalizing", "Completed", "Failed", "Disposed",
        "Monitor", "Region", "Window", "Portable", "Installed", "x64", "x86",
        "Enter", "Space", "Tab", "None", "True", "False", "H264-software", "H264 Media Foundation; hardware if available, software fallback",
        "target_changed", "operation_failed", "MonitorImage", "RegionImage", "WindowImage", "LongImage",
        "RegionGif", "MonitorGif", "RegionVideo", "MonitorVideo", "WindowVideo", "StartRecording",
        "Start", "Stop", "Pause", "Resume", "Recording", "Finishing", "Software"
    };
    private static readonly HashSet<string> TokenKeys = new(StringComparer.Ordinal)
        { "target", "state", "status", "distributionMode", "processArchitecture", "encoder", "encoderPolicy", "confirmation", "reason", "action", "kind", "mode" };
    private static readonly HashSet<string> ExceptionTypes = new(StringComparer.Ordinal)
        { "Exception", "IOException", "InvalidDataException", "UnauthorizedAccessException", "InvalidOperationException", "ArgumentException", "ArgumentNullException", "TimeoutException", "OperationCanceledException", "TaskCanceledException", "Win32Exception", "COMException", "JsonException", "NotSupportedException", "ObjectDisposedException", "DllNotFoundException", "TypeLoadException", "FileNotFoundException", "DirectoryNotFoundException", "OutOfMemoryException", "ExternalException" };

    internal static void Append(bool functional, string stage, IEnumerable<KeyValuePair<string, object?>> fields)
    {
        if (!Stages.Contains(stage)) return;
        var entry = new Dictionary<string, object?>
        {
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            [functional ? "eventType" : "stage"] = stage,
            ["processId"] = Environment.ProcessId,
            ["threadId"] = Environment.CurrentManagedThreadId
        };
        foreach (var pair in fields.Take(64))
        {
            if (pair.Value is bool flag && Booleans.Contains(pair.Key)) entry[pair.Key] = flag;
            else if (Numbers.Contains(pair.Key) && pair.Value is byte or short or int or long or float or double or decimal)
            {
                var number = Convert.ToDouble(pair.Value, System.Globalization.CultureInfo.InvariantCulture);
                if (double.IsFinite(number) && Math.Abs(number) <= 9e15) entry[pair.Key] = pair.Value;
            }
            else if (pair.Value is Guid id && pair.Key is "recordingId" or "operationId") entry[pair.Key] = id.ToString("N");
            else if (pair.Value is string value)
            {
                if (value.Length > 128) continue;
                if (TokenKeys.Contains(pair.Key) && Tokens.Contains(value)) entry[pair.Key] = value;
                else if (pair.Key is "recordingId" or "operationId" && Guid.TryParse(value, out var guid)) entry[pair.Key] = guid.ToString("N");
                else if (pair.Key == "hresult" && Regex.IsMatch(value, "^0x[0-9A-Fa-f]{8}$")) entry[pair.Key] = value;
                else if (pair.Key is "exceptionType" or "errorType")
                {
                    var name = value.Split('.').Last();
                    entry[pair.Key] = ExceptionTypes.Contains(name) ? name : "Exception";
                }
                else if (pair.Key is "slashDeskVersion" or "windowsVersion" or "framework" or "version" &&
                    value.Length <= 32 && Regex.IsMatch(value, "^[0-9]+(\\.[0-9]+){1,3}$")) entry[pair.Key] = value;
            }
            // Paths, filenames, messages, titles, text, pixels, device names/IDs,
            // URLs, arbitrary strings and complex values are intentionally omitted.
        }
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        lock (Gate)
        {
            using var pin = LocalFileBoundary.PinDirectory(AppPaths.LogsDirectory, create: true);
            // Cross-process maintenance lease: optional logging skips a busy directory.
            using var maintenance = LocalFileBoundary.OpenFile(Path.Combine(AppPaths.LogsDirectory, "retention-v1.lock"), create: true, exclusive: true);
            if (!PrunePinned(DateTimeOffset.UtcNow, MaximumTotalBytes - bytes.Length)) return;
            var path = Path.Combine(AppPaths.LogsDirectory, $"{(functional ? "functional" : "slashdesk")}-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            using (var stream = LocalFileBoundary.OpenFile(path, create: true, exclusive: true))
            {
                if (stream.Length + bytes.Length > MaximumFileBytes)
                {
                    // Keep one bounded previous chunk. A pinned directory prevents ancestor swaps.
                    stream.Dispose();
                    var previous = path + ".1";
                    if (File.Exists(previous)) { using (LocalFileBoundary.OpenFile(previous, exclusive: true)) { } File.Delete(previous); }
                    File.Move(path, previous);
                    using var fresh = LocalFileBoundary.OpenFile(path, create: true, exclusive: true);
                    fresh.Write(bytes);
                }
                else { stream.Position = stream.Length; stream.Write(bytes); }
            }
        }
    }

    internal static void Prune(DateTimeOffset? now = null)
    {
        try
        {
            lock (Gate)
            {
                if (!Directory.Exists(AppPaths.LogsDirectory)) return;
                using var pin = LocalFileBoundary.PinDirectory(AppPaths.LogsDirectory);
                using var maintenance = LocalFileBoundary.OpenFile(Path.Combine(AppPaths.LogsDirectory, "retention-v1.lock"), create: true, exclusive: true);
                PrunePinned(now ?? DateTimeOffset.UtcNow, MaximumTotalBytes);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool PrunePinned(DateTimeOffset now, long budget)
    {
        var files = new List<(string Path, long Bytes, DateTimeOffset Date)>();
        var candidates = Directory.EnumerateFiles(AppPaths.LogsDirectory).Take(513).ToArray();
        // Refuse new writes rather than silently omitting files from the total budget.
        if (candidates.Length > 512) return false;
        foreach (var path in candidates)
        {
            var name = Path.GetFileName(path);
            if (!Regex.IsMatch(name, "^(slashdesk|functional)-[0-9]{8}\\.jsonl(\\.1)?$") &&
                !Regex.IsMatch(name, "^screenrecorderlib-[0-9]{8}-[0-9]{6}-[0-9]{3}(-[a-f0-9]{32})?\\.log$")) continue;
            try
            {
                using var stream = LocalFileBoundary.OpenFile(path, writable: false);
                var date = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                files.Add((path, stream.Length, date));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return false; } // Busy/unreadable files cannot be excluded from the budget.
        }
        var total = files.Sum(file => file.Bytes);
        foreach (var file in files.OrderBy(file => file.Date).ThenBy(file => file.Path, StringComparer.Ordinal))
        {
            if (!Path.GetFileName(file.Path).StartsWith("screenrecorderlib-", StringComparison.Ordinal) &&
                file.Date >= now.AddDays(-RetentionDays) && file.Bytes <= MaximumFileBytes && total <= budget) continue;
            try
            {
                using (LocalFileBoundary.OpenFile(file.Path, exclusive: true)) { }
                File.Delete(file.Path);
                total -= file.Bytes;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return total <= budget;
    }
}
