using System.IO;
using System.Text.Json;

namespace SlashText.Services;

public sealed class JsonFileStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    public JsonFileStore(string path)
    {
        _path = path;
    }

    // Restore validates without invoking the loader's corrupt-file preservation/default fallback.
    internal static T ReadRequired(JsonElement element) => element.Deserialize<T>(Options)
        ?? throw new InvalidDataException("Invalid stored JSON value.");

    public StoredReadState ReadState { get; private set; } = StoredReadState.Missing;
    public bool IsProtected => StoredReadProtection.Get(_path) is not null;
    public bool CanRecover => StoredReadProtection.Get(_path)?.PreservedPath is not null;

    public Task<T> LoadAsync(CancellationToken cancellationToken = default) =>
        AtomicFilePersistence.WithPathGateAsync(_path, () => LoadCoreAsync(cancellationToken), cancellationToken);

    private async Task<T> LoadCoreAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes = null;
        try
        {
            bytes = await StoredReadProtection.ReadAsync(_path, cancellationToken);
            if (bytes is null) { ReadState = StoredReadState.Missing; return new T(); }
            using var stream = new MemoryStream(bytes, writable: false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            BackupContentValidator.RequireUniqueMembers(document.RootElement, cancellationToken);
            BackupContentValidator.RequireTypedMembers(document.RootElement, typeof(T), cancellationToken);
            var value = ReadRequired(document.RootElement);
            if (value is SlashText.Models.AppSettings) BackupContentValidator.RequireSettingsMembers(value, cancellationToken);
            ReadState = StoredReadState.Valid;
            StoredReadProtection.Valid(_path);
            return value;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ReadState = bytes is null ? StoredReadState.Unavailable : StoredReadState.Invalid;
            await StoredReadProtection.ProtectAsync(_path, ReadState, bytes);
            AppDiagnosticLog.Write("storage.read.protected", ("state", ReadState.ToString()), ("exceptionType", error.GetType().Name));
            return new T();
        }
    }

    public async Task RecoverAsync(T value, CancellationToken cancellationToken = default)
    {
        _ = ReadRequired(JsonSerializer.SerializeToElement(value, Options));
        if (value is SlashText.Models.AppSettings) BackupContentValidator.RequireSettingsMembers(value, cancellationToken);
        var failure = StoredReadProtection.Get(_path) ?? throw new InvalidOperationException("No protected read to recover.");
        await AtomicFilePersistence.WriteAsync(_path,
            (stream, token) => JsonSerializer.SerializeAsync(stream, value, Options, token), cancellationToken, failure);
        // Only a successful, fully validated read releases protection.
        await LoadAsync(cancellationToken);
        if (IsProtected) throw new IOException("Recovered data could not be read completely.");
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        await AtomicFilePersistence.WriteAsync(
            _path,
            (stream, token) =>
                JsonSerializer.SerializeAsync(stream, value, Options, token),
            cancellationToken);
    }
}
