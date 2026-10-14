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

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new T();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
                ?? new T();
        }
        catch (JsonException)
        {
            try
            {
                var preserved = await CorruptFilePreserver.PreserveAsync(
                    _path,
                    CancellationToken.None);
                AppDiagnosticLog.Write(
                    "storage.json.corrupt_preserved",
                    ("file", Path.GetFileName(_path)),
                    ("preserved", !string.IsNullOrWhiteSpace(preserved)));
            }
            catch (Exception exception)
            {
                AppDiagnosticLog.Write(
                    "storage.json.corrupt_preserve_failed",
                    ("file", Path.GetFileName(_path)),
                    ("exceptionType", exception.GetType().Name));
            }

            return new T();
        }
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
