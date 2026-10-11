using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SlashText.Services;

internal static class AtomicFilePersistence
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathGates =
        new(StringComparer.OrdinalIgnoreCase);

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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        var fullPath = Path.GetFullPath(path);
        var gate = PathGates.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
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
                if (File.Exists(fullPath))
                {
                    File.Replace(temporary, fullPath, null, ignoreMetadataErrors: true);
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
    public static async Task<string?> PreserveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
        var preservedPath = $"{path}.corrupt-{hash[..16]}.bak";
        if (File.Exists(preservedPath))
        {
            return preservedPath;
        }

        try
        {
            await AtomicFilePersistence.WriteAsync(
                preservedPath,
                (stream, token) => stream.WriteAsync(bytes, token).AsTask(),
                cancellationToken);
        }
        catch (IOException) when (File.Exists(preservedPath))
        {
            // Outra leitura concorrente já preservou exatamente o mesmo conteúdo.
        }

        return preservedPath;
    }
}
