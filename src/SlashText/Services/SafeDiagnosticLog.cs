using System.IO;

namespace SlashText.Services;

public static class SafeDiagnosticLog
{
    internal static bool EnabledAtLaunch => string.Equals(Environment.GetEnvironmentVariable("SLASHDESK_DIAGNOSTICS"), "1", StringComparison.Ordinal);
    public static bool Enabled =>
        EnabledAtLaunch ||
        File.Exists(Path.Combine(AppPaths.LogsDirectory, "diagnostics.enabled"));

    internal static void SetEnabled(bool enabled)
    {
        using var pin = LocalFileBoundary.PinDirectory(AppPaths.LogsDirectory, create: true);
        var path = Path.Combine(AppPaths.LogsDirectory, "diagnostics.enabled");
        if (enabled) { using var file = LocalFileBoundary.OpenFile(path, create: true, exclusive: true); file.SetLength(0); }
        else if (File.Exists(path)) { using (LocalFileBoundary.OpenFile(path, exclusive: true)) { } File.Delete(path); }
    }

    public static void Write(string eventType, IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (!Enabled) return;
        try { DiagnosticPolicy.Append(true, eventType, fields ?? new Dictionary<string, object?>()); }
        catch { /* Optional diagnostics never interrupt expansion or capture. */ }
    }
}
