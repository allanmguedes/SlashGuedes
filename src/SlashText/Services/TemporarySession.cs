using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SlashText.Services;

internal sealed class TemporarySession : IDisposable
{
    private const string LeaseName = "owner-v1.json";
    private const int MaximumFiles = 10002;
    internal static string Root => Path.Combine(AppPaths.DataDirectory, "Temporary-v1");
    private LocalFileBoundary? _boundary;
    private FileStream? _lease;
    private readonly string _root;
    private readonly Guid _id;
    internal string DirectoryPath { get; }
    private TemporarySession(string root, string folder, Guid id, LocalFileBoundary boundary, FileStream lease)
    { _root = root; DirectoryPath = folder; _id = id; _boundary = boundary; _lease = lease; }

    internal static TemporarySession Create(string kind)
    {
        if (kind is not ("ocr" or "gif")) throw new ArgumentException("Unknown session kind.");
        var root = Root;
        using var rootPin = LocalFileBoundary.PinDirectory(root, create: true);
        var id = Guid.NewGuid();
        var folder = Path.Combine(root, $"{kind}-{id:N}");
        var boundary = LocalFileBoundary.PinDirectory(folder, create: true);
        FileStream? lease = null;
        try
        {
            lease = LocalFileBoundary.OpenFile(Path.Combine(folder, LeaseName), create: true);
            JsonSerializer.Serialize(lease, new Owner(1, kind, id, RootIdentity(root), Environment.ProcessId,
                Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, DateTimeOffset.UtcNow));
            lease.Flush(true);
            return new(root, folder, id, boundary, lease);
        }
        catch { lease?.Dispose(); boundary.Dispose(); throw; }
    }

    // OCR child keeps a shared lease too. Parent termination cannot make a live worker orphaned.
    internal static IDisposable AttachWorker(string input, string output)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(input))!;
        if (Path.GetFileName(input) != "region.png" || Path.GetFileName(output) != "result.json" ||
            !string.Equals(folder, Path.GetDirectoryName(Path.GetFullPath(output)), StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(folder), "^ocr-[a-f0-9]{32}$"))
            throw new IOException("Invalid worker session.");
        var boundary = LocalFileBoundary.PinDirectory(folder);
        try { return new WorkerLease(boundary, LocalFileBoundary.OpenFile(Path.Combine(folder, LeaseName), writable: false)); }
        catch { boundary.Dispose(); throw; }
    }

    internal static int CleanupOrphans(DateTimeOffset? now = null)
    {
        var removed = 0;
        try
        {
            var root = Root;
            if (!Directory.Exists(root)) return 0;
            using var pin = LocalFileBoundary.PinDirectory(root);
            var elapsed = Stopwatch.StartNew();
            foreach (var folder in Directory.EnumerateDirectories(root).Take(256))
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(5)) break;
                if (Cleanup(root, folder, now ?? DateTimeOffset.UtcNow, null)) removed++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    internal static bool OwnerHasExited(int pid, long startedUtcTicks)
    {
        if (pid <= 0 || startedUtcTicks <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited || process.StartTime.ToUniversalTime().Ticks != startedUtcTicks;
        }
        catch (ArgumentException) { return true; } // PID no longer exists.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        { return false; } // An inaccessible owner is not proof of termination.
    }

    private static bool Cleanup(string root, string folder, DateTimeOffset now, Guid? ownId)
    {
        try
        {
            using var rootPin = LocalFileBoundary.PinDirectory(root);
            using (var boundary = LocalFileBoundary.PinDirectory(folder))
            {
                var name = Path.GetFileName(folder);
                if (!Regex.IsMatch(name, "^(ocr|gif)-[a-f0-9]{32}$")) return false;
                using var lease = LocalFileBoundary.OpenFile(Path.Combine(folder, LeaseName), exclusive: true);
                if (lease.Length is <= 0 or > 4096) return false;
                var owner = JsonSerializer.Deserialize<Owner>(lease);
                if (owner is null || owner.Schema != 1 || name != $"{owner.Kind}-{owner.Id:N}" ||
                    owner.RootIdentity != RootIdentity(root) || owner.CreatedUtc > now) return false;
                if (ownId is not null)
                {
                    if (owner.Id != ownId || owner.Pid != Environment.ProcessId ||
                        owner.StartedUtcTicks != Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks) return false;
                }
                else if (now - owner.CreatedUtc < TimeSpan.FromMinutes(2) || !OwnerHasExited(owner.Pid, owner.StartedUtcTicks)) return false;
                var files = Directory.EnumerateFileSystemEntries(folder).Take(MaximumFiles + 1).ToArray();
                if (files.Length > MaximumFiles) return false;
                // Validate the entire flat session before deleting anything. Unexpected entries
                // and every reparse point are preserved. No recursive deletion exists here.
                foreach (var file in files)
                {
                    var filename = Path.GetFileName(file);
                    if (filename != LeaseName && !(owner.Kind == "ocr" && filename is "region.png" or "result.json") &&
                        !(owner.Kind == "gif" && Regex.IsMatch(filename, "^[0-9]{6}\\.png$"))) return false;
                    if (filename != LeaseName) { using var check = LocalFileBoundary.OpenFile(file, writable: false); }
                }
                foreach (var file in files.Where(file => Path.GetFileName(file) != LeaseName)) File.Delete(file);
            }
            File.Delete(Path.Combine(folder, LeaseName));
            Directory.Delete(folder, recursive: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return false; }
    }

    private static string RootIdentity(string root) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())));
    internal sealed record Owner(int Schema, string Kind, Guid Id, string RootIdentity, int Pid, long StartedUtcTicks, DateTimeOffset CreatedUtc);
    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;
        lease.Dispose();
        Interlocked.Exchange(ref _boundary, null)?.Dispose();
        Cleanup(_root, DirectoryPath, DateTimeOffset.UtcNow, _id);
    }
    private sealed record WorkerLease(LocalFileBoundary Boundary, FileStream Lease) : IDisposable
    { public void Dispose() { Lease.Dispose(); Boundary.Dispose(); } }
}
