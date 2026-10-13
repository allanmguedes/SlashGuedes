using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SlashText.Services;

// Recovery metadata is private state, not diagnostics. No MP4 is ever deleted here.
internal static class RecordingRecovery
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, string> Active = [];
    private static string Root => Path.Combine(AppPaths.DataDirectory, "RecordingRecovery-v1");
    private sealed record Marker(int Schema, Guid Id, int Pid, long StartedUtcTicks, string PartialPath, bool Interrupted);

    internal static void Start(Guid id, string partialPath)
    {
        try
        {
            lock (Gate)
            {
                using var pin = LocalFileBoundary.PinDirectory(Root, create: true);
                InterruptedCount(); // Remove only stale private metadata for proven missing partials.
                if (Directory.EnumerateFileSystemEntries(Root).Take(129).Count() >= 128) return;
                var path = Path.Combine(Root, $"{id:N}.json");
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new Marker(1, id, Environment.ProcessId,
                    Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, partialPath, false));
                if (bytes.Length > 16384) return;
                using var file = LocalFileBoundary.OpenFile(path, create: true, exclusive: true);
                file.Write(bytes);
                file.Flush(true);
                Active[id] = path;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static void End(Guid id, bool completed)
    {
        try
        {
            lock (Gate)
            {
                if (!Active.TryGetValue(id, out var path)) return;
                using var pin = LocalFileBoundary.PinDirectory(Path.GetDirectoryName(path)!);
                using (var file = LocalFileBoundary.OpenFile(path, exclusive: true))
                {
                    if (file.Length > 16384) return;
                    var marker = JsonSerializer.Deserialize<Marker>(file);
                    if (marker is null || marker.Id != id || marker.Pid != Environment.ProcessId ||
                        marker.StartedUtcTicks != Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks) return;
                    if (!completed)
                    {
                        file.Position = 0;
                        JsonSerializer.Serialize(file, marker with { Interrupted = true });
                        file.SetLength(file.Position); file.Flush(true);
                    }
                }
                if (completed) File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        finally { lock (Gate) Active.Remove(id); }
    }

    internal static int InterruptedCount()
    {
        var count = 0;
        try
        {
            if (!Directory.Exists(Root)) return 0;
            using var pin = LocalFileBoundary.PinDirectory(Root);
            foreach (var path in Directory.EnumerateFiles(Root, "*.json").Take(128))
            {
                try
                {
                    if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
                    Marker? marker;
                    using (var file = LocalFileBoundary.OpenFile(path, writable: false))
                    {
                        if (file.Length is <= 0 or > 16384) continue;
                        marker = JsonSerializer.Deserialize<Marker>(file);
                    }
                    if (marker is null || marker.Schema != 1 || marker.Id != id ||
                        (!marker.Interrupted && !TemporarySession.OwnerHasExited(marker.Pid, marker.StartedUtcTicks))) continue;
                    // Pinning also rejects network paths, reparse points and aliases. Inspect
                    // only this recorded partial, never scan or clean the output directory.
                    using var outputPin = LocalFileBoundary.PinDirectory(Path.GetDirectoryName(marker.PartialPath)!);
                    if (!Path.GetFileName(marker.PartialPath).EndsWith(".recording.mp4", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using var partial = LocalFileBoundary.OpenFile(marker.PartialPath, writable: false);
                        if (partial.Length > 0) count++;
                    }
                    catch (FileNotFoundException) when (TemporarySession.OwnerHasExited(marker.Pid, marker.StartedUtcTicks))
                    {
                        using (LocalFileBoundary.OpenFile(path, exclusive: true)) { }
                        File.Delete(path); // The partial is absent; this deletes metadata only.
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return count;
    }
}
