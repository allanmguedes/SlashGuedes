using System.IO;
using System.Reflection;
using SlashText.Models;

namespace SlashText.Services;

public static class AppDiagnosticLog
{
    private static string CurrentPath => Path.Combine(AppPaths.LogsDirectory, $"slashdesk-{DateTime.UtcNow:yyyyMMdd}.jsonl");

    public static string CurrentLogPath
    {
        get => CurrentPath;
    }

    public static void Initialize()
    {
        DiagnosticPolicy.Prune();

        Write(
            "application.start",
            ("slashDeskVersion", ProductVersion()),
            ("windowsVersion", Environment.OSVersion.Version.ToString()),
            ("processArchitecture", Environment.Is64BitProcess ? "x64" : "x86"),
            ("framework", Environment.Version.ToString()));
    }

    public static void Write(string stage, params (string Key, object? Value)[] fields)
    {
        try
        {
            DiagnosticPolicy.Append(false, stage, fields.Select(field => new KeyValuePair<string, object?>(field.Key, field.Value)));
        }
        catch
        {
            // Diagnostics must never bring down the application.
        }
    }

    public static void WriteException(string stage, Exception exception)
    {
        Write(
            stage,
            ("exceptionType", exception.GetType().FullName),
            ("hresult", $"0x{exception.HResult:X8}"));
    }

    public static string CreateLibraryLogPath(Guid? recordingId = null)
    {
        var suffix = recordingId is null ? string.Empty : $"-{recordingId.Value:N}";
        return Path.Combine(
            AppPaths.LogsDirectory,
            $"screenrecorderlib-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}{suffix}.log");
    }

    public static void MarkRecordingActive(Guid recordingId, RecordingTarget target,
        RecordingSettings settings, string encoderPolicy, string partialPath) =>
        RecordingRecovery.Start(recordingId, partialPath);

    public static void MarkRecordingEnded(Guid recordingId, bool completed) => RecordingRecovery.End(recordingId, completed);

    private static string ProductVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

}
