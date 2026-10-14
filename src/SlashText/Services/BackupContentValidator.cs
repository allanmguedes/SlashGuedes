using System.Collections;
using System.IO;
using System.Text;
using System.Text.Json;
using SlashText.Models;

namespace SlashText.Services;

// Read only the staged metadata. Never invoke fallback loaders, save normalized values,
// open history references, or inspect the active profile to fill missing archive entries.
internal static class BackupContentValidator
{
    internal static void Validate(string staging, IEnumerable<string> files, CancellationToken token)
    {
        foreach (var name in files)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var path = Path.Combine(staging, name);
                if (name.Equals("snippets.md", StringComparison.OrdinalIgnoreCase))
                {
                    var text = ReadText(path, token);
                    var snippets = SnippetMarkdownRepository.ReadRequired(text, token, forRestore: true);
                    if (snippets.Select(s => s.Id).Distinct().Count() != snippets.Count) throw new InvalidDataException();
                    continue;
                }
                using var document = ReadJson(path, name.Equals("usage.json", StringComparison.OrdinalIgnoreCase), token);
                RequireUniqueMembers(document.RootElement, token);
                switch (name.ToLowerInvariant())
                {
                    case "settings.json":
                        RequireTypedMembers(document.RootElement, typeof(AppSettings), token);
                        RequireSettingsMembers(JsonFileStore<AppSettings>.ReadRequired(document.RootElement), token);
                        break;
                    case "usage.json":
                        RequireTypedMembers(document.RootElement, document.RootElement.ValueKind == JsonValueKind.Array ? typeof(List<UsageRecord>) : typeof(UsageSnapshot), token);
                        var usage = UsageService.ReadRequired(document.RootElement);
                        if (usage.Snippets is null || usage.QuickAccent is null || usage.QuickAccent.Characters is null ||
                            usage.Snippets.Any(r => r is null || r.Count < 0 || r.CharactersSaved < 0) ||
                            usage.QuickAccent.Count < 0 || usage.QuickAccent.Characters.Values.Any(v => v < 0)) throw new InvalidDataException();
                        break;
                    case "capture-history.json":
                        RequireTypedMembers(document.RootElement, typeof(List<CaptureRecord>), token);
                        var history = CaptureHistoryStore.ReadRequired(document.RootElement);
                        if (history.Any(r => string.IsNullOrWhiteSpace(r.Id) || r.Width < 0 || r.Height < 0 ||
                            !double.IsFinite(r.DurationSeconds) || r.DurationSeconds < 0) ||
                            history.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != history.Count) throw new InvalidDataException();
                        break;
                    default: throw new InvalidDataException();
                }
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException)
            {
                // Do not surface document values or raw parser exceptions in UI/diagnostics.
                throw IncompatibleFile(name);
            }
        }
    }

    internal static InvalidDataException IncompatibleFile(string name) => new(
        UiText.Format($"O arquivo '{name}' do backup contém dados incompatíveis. Os dados atuais não foram alterados."));

    private static string ReadText(string path, CancellationToken token)
    {
        using var file = File.OpenRead(path);
        using var bounded = new BoundedInputStream(file, BackupInputBudget.MetadataBytes, token);
        using var reader = new StreamReader(bounded, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static JsonDocument ReadJson(string path, bool textReader, CancellationToken token)
    {
        // usage.json historically accepts BOM-detected text; the other loaders read UTF-8 streams.
        if (textReader)
            return JsonDocument.Parse(ReadText(path, token));
        using var file = File.OpenRead(path);
        using var bounded = new BoundedInputStream(file, BackupInputBudget.MetadataBytes, token);
        return JsonDocument.Parse(bounded);
    }

    private static void RequireSettingsMembers(object? value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (value is null) throw new InvalidDataException();
        if (value is string) return;
        if (value is float number && !float.IsFinite(number)) throw new InvalidDataException();
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary) RequireSettingsMembers(entry.Value, token);
        }
        else if (value is IEnumerable list)
        {
            foreach (var item in list) RequireSettingsMembers(item, token);
        }
        else if (value.GetType().IsClass)
        {
            // Settings DTOs declare non-null reference members with defaults for absent legacy fields.
            // Explicit null is refused rather than installing a value that crashes later consumers.
            foreach (var property in value.GetType().GetProperties())
                if (property.CanRead && property.GetIndexParameters().Length == 0) RequireSettingsMembers(property.GetValue(value), token);
        }
    }

    internal static void RequireTypedMembers(JsonElement element, Type type, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RequireTypedMembers(item, type.GenericTypeArguments[0], token);
        }
        else if (element.ValueKind == JsonValueKind.Object && type.IsClass && type != typeof(string))
        {
            var properties = type.GetProperties().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!properties.TryGetValue(property.Name, out var contract)) continue;
                if (!seen.Add(contract.Name)) throw new InvalidDataException();
                RequireTypedMembers(property.Value, contract.PropertyType, token);
            }
        }
    }

    internal static void RequireUniqueMembers(JsonElement element, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException();
                RequireUniqueMembers(property.Value, token);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RequireUniqueMembers(item, token);
    }
}
