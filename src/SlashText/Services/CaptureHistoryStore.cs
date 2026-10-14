using System.IO;
using System.Text.Json;
using SlashText.Models;

namespace SlashText.Services;

internal sealed class CaptureHistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly string _path;

    public CaptureHistoryStore(string path)
    {
        _path = path;
    }

    public async Task<List<CaptureRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }
        try
        {
            await using var stream = File.OpenRead(_path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var records = new List<CaptureRecord>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                try
                {
                    var record = ReadRecord(element);
                    if (record is not null) records.Add(record);
                }
                catch (JsonException exception)
                {
                    AppDiagnosticLog.WriteException("history.item-corrupt", exception);
                }
            }
            return records;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            AppDiagnosticLog.WriteException("history.file-corrupt", exception);
            return [];
        }
    }

    private static CaptureRecord? ReadRecord(JsonElement element)
    {
        var record = element.Deserialize<CaptureRecord>(Options);
        if (record is null) return null;
        record.FilePath ??= string.Empty; record.Type ??= string.Empty;
        record.MediaKind = string.IsNullOrWhiteSpace(record.MediaKind)
            ? CaptureHistoryFilePolicy.MediaKind(record.FilePath) : record.MediaKind.ToLowerInvariant();
        return record;
    }

    internal static List<CaptureRecord> ReadRequired(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid capture history.");
        return element.EnumerateArray().Select(item => ReadRecord(item)
            ?? throw new JsonException("Invalid capture record.")).ToList();
    }

    public async Task SaveAsync(
        IReadOnlyList<CaptureRecord> records,
        CancellationToken cancellationToken = default)
    {
        RestoreRecovery.ThrowIfProtected(_path);
        var directory = Path.GetDirectoryName(_path) ?? AppPaths.DataDirectory;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".capture-history-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, records, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            RestoreRecovery.ThrowIfProtected(_path);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
