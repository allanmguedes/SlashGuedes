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

    public StoredReadState ReadState { get; private set; } = StoredReadState.Missing;
    public bool IsProtected => StoredReadProtection.Get(_path) is not null;
    public bool CanRecover => StoredReadProtection.Get(_path)?.PreservedPath is not null;

    public Task<List<CaptureRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
        AtomicFilePersistence.WithPathGateAsync(_path, () => LoadCoreAsync(cancellationToken), cancellationToken);

    private async Task<List<CaptureRecord>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes = null;
        var records = new List<CaptureRecord>();
        try
        {
            bytes = await StoredReadProtection.ReadAsync(_path, cancellationToken);
            if (bytes is null) { ReadState = StoredReadState.Missing; return records; }
            using var stream = new MemoryStream(bytes, writable: false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid capture history.");
            var partial = false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    BackupContentValidator.RequireUniqueMembers(element, cancellationToken);
                    BackupContentValidator.RequireTypedMembers(element, typeof(CaptureRecord), cancellationToken);
                    var record = ReadRecord(element) ?? throw new JsonException("Invalid capture record.");
                    if (record.Width < 0 || record.Height < 0 ||
                        !double.IsFinite(record.DurationSeconds) || record.DurationSeconds < 0)
                        throw new JsonException("Invalid capture record.");
                    // Keep every legible row. CaptureService assigns safe session IDs; the original stays protected.
                    if (string.IsNullOrWhiteSpace(record.Id) || !ids.Add(record.Id)) partial = true;
                    records.Add(record);
                }
                catch (Exception error) when (error is JsonException or InvalidDataException) { partial = true; }
            }
            ReadState = partial ? StoredReadState.Partial : StoredReadState.Valid;
            if (partial) await StoredReadProtection.ProtectAsync(_path, ReadState, bytes);
            else StoredReadProtection.Valid(_path);
            return records;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            ReadState = bytes is null ? StoredReadState.Unavailable : StoredReadState.Invalid;
            await StoredReadProtection.ProtectAsync(_path, ReadState, bytes);
            AppDiagnosticLog.Write("history.read.protected", ("state", ReadState.ToString()), ("exceptionType", error.GetType().Name));
            return records;
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

    public Task SaveAsync(IReadOnlyList<CaptureRecord> records, CancellationToken cancellationToken = default) =>
        AtomicFilePersistence.WriteAsync(_path,
            (stream, token) => JsonSerializer.SerializeAsync(stream, records, Options, token), cancellationToken);

    public async Task RecoverAsync(IReadOnlyList<CaptureRecord> records, CancellationToken cancellationToken = default)
    {
        var replacement = ReadRequired(JsonSerializer.SerializeToElement(records, Options));
        if (replacement.Any(r => string.IsNullOrWhiteSpace(r.Id) || r.Width < 0 || r.Height < 0 ||
            !double.IsFinite(r.DurationSeconds) || r.DurationSeconds < 0) ||
            replacement.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != replacement.Count)
            throw new InvalidDataException("Recovery requires a valid history list.");
        var failure = StoredReadProtection.Get(_path) ?? throw new InvalidOperationException("No protected history to recover.");
        await AtomicFilePersistence.WriteAsync(_path,
            (stream, token) => JsonSerializer.SerializeAsync(stream, records, Options, token), cancellationToken, failure);
        await LoadAsync(cancellationToken);
        if (IsProtected) throw new IOException("Recovered history could not be read completely.");
    }
}
