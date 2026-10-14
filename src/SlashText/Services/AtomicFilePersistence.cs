using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SlashText.Services;

internal static class AtomicFilePersistence
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathGates =
        new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<T> WithPathGateAsync<T>(string path, Func<Task<T>> read, CancellationToken token)
    {
        var gate = PathGates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try { return await read(); }
        finally { gate.Release(); }
    }

    public static async Task WriteTextAsync(
        string path,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(
            path,
            async (stream, token) =>
            {
                await using var writer = new StreamWriter(
                    stream,
                    encoding,
                    bufferSize: 4096,
                    leaveOpen: true);
                await writer.WriteAsync(content.AsMemory(), token);
                await writer.FlushAsync(token);
            },
            cancellationToken);
    }

    public static async Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken = default,
        StoredReadProtection.Failure? recovery = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        var fullPath = Path.GetFullPath(path);
        RestoreRecovery.ThrowIfProtected(fullPath);
        if (recovery is null) StoredReadProtection.ThrowIfProtected(fullPath);
        var gate = PathGates.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            RestoreRecovery.ThrowIfProtected(fullPath);
            if (recovery is null) StoredReadProtection.ThrowIfProtected(fullPath);
            else await StoredReadProtection.VerifyRecoveryAsync(fullPath, recovery, cancellationToken);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException(UiText.Get("O arquivo não possui uma pasta válida."));
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}-{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var stream = new FileStream(
                                 temporary,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await write(stream, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                RestoreRecovery.ThrowIfProtected(fullPath);
                if (recovery is null) StoredReadProtection.ThrowIfProtected(fullPath);
                else await StoredReadProtection.VerifyRecoveryAsync(fullPath, recovery, cancellationToken);
                if (File.Exists(fullPath))
                {
                    // Explicit recovery also retains the displaced file, including any external edit racing the final check.
                    var displaced = recovery is null ? null : fullPath + $".recovered-original-{Guid.NewGuid():N}.bak";
                    File.Replace(temporary, fullPath, displaced, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }
}

internal static class CorruptFilePreserver
{
    public static async Task<string?> PreserveAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await StoredReadProtection.ReadAsync(path, cancellationToken);
        return bytes is null ? null : await PreserveBytesAsync(path, bytes, cancellationToken);
    }

    internal static async Task<string> PreserveBytesAsync(string path, byte[] bytes, CancellationToken token = default)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var preservedPath = $"{path}.corrupt-{hash[..16]}.bak";
        RestoreRecovery.ThrowIfProtected(preservedPath);
        if (!File.Exists(preservedPath))
        {
            var temporary = path + $".preserving-{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, token);
                    await stream.FlushAsync(token);
                    stream.Flush(true);
                }
                RestoreRecovery.ThrowIfProtected(preservedPath);
                // Commit without overwriting a prior preservation copy, including a racing reader's copy.
                File.Move(temporary, preservedPath);
            }
            catch (IOException) when (File.Exists(preservedPath)) { }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var copy = await StoredReadProtection.ReadAsync(preservedPath, token);
        if (copy is null || !StoredReadProtection.Same(copy, bytes))
            throw new IOException("The preservation copy could not be verified.");
        return preservedPath;
    }
}
