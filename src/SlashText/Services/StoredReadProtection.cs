using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;

namespace SlashText.Services;

public enum StoredReadState { Missing, Valid, Partial, Invalid, Unavailable }

// A failed reader protects every writer of the same path, including newly constructed stores.
internal static class StoredReadProtection
{
    internal sealed record Failure(StoredReadState State, byte[]? Original, string? PreservedPath);
    private static readonly ConcurrentDictionary<string, Failure> Failures = new(StringComparer.OrdinalIgnoreCase);
    private static string Key(string path) => Path.GetFullPath(path);
    internal static Failure? Get(string path) => Failures.GetValueOrDefault(Key(path));
    internal static void Valid(string path) => Failures.TryRemove(Key(path), out _);
    internal static void ThrowIfProtected(string path)
    {
        if (Get(path) is not null) throw new IOException(UiText.Get("Os dados estão protegidos após uma leitura incompleta. Revise-os em Configurações antes de salvar."));
    }

    internal static async Task<byte[]?> ReadAsync(string path, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length > BackupInputBudget.MetadataBytes) throw new InvalidDataException("Stored metadata exceeds the reading limit.");
            using var bytes = new MemoryStream();
            using var bounded = new BoundedInputStream(stream, BackupInputBudget.MetadataBytes, token, leaveOpen: true);
            await bounded.CopyToAsync(bytes, token);
            return bytes.ToArray();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            // Disappearance of an already protected file does not authorize replacing it with defaults.
            if (Get(path) is not null) throw new IOException("Protected file is no longer available.");
            return null;
        }
    }

    internal static async Task ProtectAsync(string path, StoredReadState state, byte[]? original)
    {
        var failure = new Failure(state, original, null);
        Failures[Key(path)] = failure;
        if (original is null) return;
        try
        {
            var preserved = await CorruptFilePreserver.PreserveBytesAsync(path, original);
            Failures.TryUpdate(Key(path), failure with { PreservedPath = preserved }, failure);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppDiagnosticLog.Write("storage.preservation.unavailable", ("exceptionType", error.GetType().Name));
        }
    }

    internal static async Task VerifyRecoveryAsync(string path, Failure expected, CancellationToken token)
    {
        if (!ReferenceEquals(Get(path), expected) || expected.Original is null || expected.PreservedPath is null)
            throw new IOException(UiText.Get("A recuperação exige uma cópia verificada do original. Tente ler os dados novamente."));
        var current = await ReadAsync(path, token);
        var preserved = await ReadAsync(expected.PreservedPath, token);
        if (current is null || preserved is null || !Same(current, expected.Original) || !Same(preserved, expected.Original))
            throw new IOException(UiText.Get("Os arquivos mudaram desde a leitura. Tente ler os dados novamente antes de recuperar."));
    }

    internal static bool Same(byte[] left, byte[] right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(left), SHA256.HashData(right));
}
