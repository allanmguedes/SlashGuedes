using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SlashText.Models;

namespace SlashText.Services;

public enum SnippetImportSource
{
    SlashDesk,
    TextBlaze,
    Espanso
}

public sealed record SnippetImportResult(
    IReadOnlyList<Snippet> Snippets,
    IReadOnlyList<string> Warnings);

public sealed partial class SnippetImportService
{
    public async Task<SnippetImportResult> ImportAsync(
        string filePath,
        SnippetImportSource source,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(UiText.Get("O arquivo selecionado não foi encontrado."), filePath);
        }

        return source switch
        {
            SnippetImportSource.SlashDesk => await ImportSlashDeskAsync(filePath, cancellationToken),
            SnippetImportSource.TextBlaze => await ImportTextBlazeAsync(filePath, cancellationToken),
            SnippetImportSource.Espanso => await ImportEspansoAsync(filePath, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    private static async Task<SnippetImportResult> ImportSlashDeskAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var repository = new SnippetMarkdownRepository(filePath);
        var snippets = await repository.LoadAsync(cancellationToken);
        return new SnippetImportResult(snippets, []);
    }

    private static async Task<SnippetImportResult> ImportTextBlazeAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("folders", out var folders) ||
            folders.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                UiText.Get("O JSON não parece ser uma exportação do Text Blaze: a lista 'folders' não foi encontrada."));
        }

        var snippets = new List<Snippet>();
        var warnings = new List<string>();
        foreach (var folder in folders.EnumerateArray())
        {
            var category = ReadString(folder, "name") ?? "Text Blaze";
            if (!folder.TryGetProperty("snippets", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                var trigger = NormalizeTrigger(
                    ReadString(item, "shortcut"),
                    out var convertedColonPrefix);
                var name = ReadString(item, "name");
                var text = ReadString(item, "text");
                if (string.IsNullOrWhiteSpace(trigger) ||
                    string.IsNullOrWhiteSpace(name) ||
                    text is null)
                {
                    warnings.Add(UiText.Format($"Um item da pasta '{category}' foi ignorado por não ter nome, atalho ou texto."));
                    continue;
                }

                if (!TriggerRule.TryValidate(trigger, out _))
                {
                    warnings.Add(UiText.Format($"O atalho '{trigger}' foi ignorado porque usa caracteres incompatíveis."));
                    continue;
                }

                if (convertedColonPrefix)
                {
                    warnings.Add($"{trigger}: prefixo ':' convertido para '/'.");
                }

                var converted = ConvertTextBlazeVariables(text, out var unsupported);
                if (unsupported > 0)
                {
                    warnings.Add(
                        UiText.Format($"{trigger}: {unsupported} comando(s) do Text Blaze foram mantidos como texto para revisão."));
                }

                snippets.Add(new Snippet
                {
                    Name = name.Trim(),
                    Trigger = trigger,
                    Category = string.IsNullOrWhiteSpace(category) ? "Text Blaze" : category.Trim(),
                    Content = converted,
                    Format = SnippetFormat.Plain
                });
            }
        }

        return new SnippetImportResult(RemoveDuplicateTriggers(snippets, warnings), warnings);
    }

    private static async Task<SnippetImportResult> ImportEspansoAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var yaml = await File.ReadAllTextAsync(filePath, cancellationToken);
        var lines = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var snippets = new List<Snippet>();
        var warnings = new List<string>();
        var category = Path.GetFileNameWithoutExtension(filePath);

        for (var index = 0; index < lines.Length; index++)
        {
            var triggerMatch = EspansoTriggerPattern().Match(lines[index]);
            if (!triggerMatch.Success)
            {
                continue;
            }

            var itemIndent = triggerMatch.Groups["indent"].Value.Length;
            var triggers = triggerMatch.Groups["key"].Value.Equals(
                "triggers",
                StringComparison.OrdinalIgnoreCase)
                ? SplitInlineList(triggerMatch.Groups["value"].Value)
                : [Unquote(triggerMatch.Groups["value"].Value.Trim())];
            var name = string.Empty;
            string? replacement = null;

            for (index++; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!string.IsNullOrWhiteSpace(line) &&
                    LeadingWhitespace(line) <= itemIndent &&
                    EspansoTriggerPattern().IsMatch(line))
                {
                    index--;
                    break;
                }

                var label = EspansoLabelPattern().Match(line);
                if (label.Success)
                {
                    name = Unquote(label.Groups["value"].Value.Trim());
                    continue;
                }

                var triggerList = EspansoTriggersPattern().Match(line);
                if (triggerList.Success)
                {
                    triggers = SplitInlineList(triggerList.Groups["value"].Value);
                    continue;
                }

                var replace = EspansoReplacePattern().Match(line);
                if (!replace.Success)
                {
                    continue;
                }

                var value = replace.Groups["value"].Value.Trim();
                if (value is "|" or "|-" or ">" or ">-")
                {
                    var blockIndent = -1;
                    var builder = new StringBuilder();
                    while (++index < lines.Length)
                    {
                        var blockLine = lines[index];
                        if (string.IsNullOrWhiteSpace(blockLine))
                        {
                            builder.AppendLine();
                            continue;
                        }

                        var indent = LeadingWhitespace(blockLine);
                        if (indent <= itemIndent)
                        {
                            index--;
                            break;
                        }

                        blockIndent = blockIndent < 0 ? indent : Math.Min(blockIndent, indent);
                        builder.AppendLine(blockLine[Math.Min(blockIndent, blockLine.Length)..]);
                    }
                    replacement = builder.ToString().TrimEnd();
                }
                else
                {
                    replacement = Unquote(value).Replace("\\n", "\n", StringComparison.Ordinal);
                }
            }

            if (replacement is null)
            {
                warnings.Add(UiText.Get("Uma entrada do Espanso foi ignorada porque não possui 'replace'."));
                continue;
            }

            foreach (var rawTrigger in triggers)
            {
                var trigger = NormalizeTrigger(rawTrigger, out var convertedColonPrefix);
                if (!TriggerRule.TryValidate(trigger, out _))
                {
                    warnings.Add(UiText.Format($"O atalho '{trigger}' foi ignorado porque usa caracteres incompatíveis."));
                    continue;
                }

                if (convertedColonPrefix)
                {
                    warnings.Add($"{trigger}: prefixo ':' convertido para '/'.");
                }

                snippets.Add(new Snippet
                {
                    Name = string.IsNullOrWhiteSpace(name) ? trigger : name.Trim(),
                    Trigger = trigger,
                    Category = string.IsNullOrWhiteSpace(category) ? "Espanso" : category,
                    Content = replacement,
                    Format = SnippetFormat.Plain
                });
            }
        }

        if (snippets.Count == 0)
        {
            throw new InvalidDataException(
                UiText.Get("Nenhum atalho Espanso compatível foi encontrado. Selecione um arquivo YAML que contenha 'match', 'trigger' e 'replace'."));
        }

        return new SnippetImportResult(RemoveDuplicateTriggers(snippets, warnings), warnings);
    }

    private static IReadOnlyList<Snippet> RemoveDuplicateTriggers(
        IEnumerable<Snippet> source,
        ICollection<string> warnings)
    {
        var unique = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var snippet in source)
        {
            if (!unique.TryAdd(snippet.Trigger, snippet))
            {
                warnings.Add(UiText.Format($"{snippet.Trigger}: atalho duplicado na origem; somente o primeiro foi importado."));
            }
        }

        return unique.Values.ToList();
    }

    private static string ConvertTextBlazeVariables(string text, out int unsupported)
    {
        var unsupportedCount = 0;
        var converted = TextBlazeTabPattern().Replace(text, "{{tab}}");
        converted = TextBlazeTimePattern().Replace(converted, match =>
        {
            var format = ConvertDateFormat(match.Groups["format"].Value.Trim());
            var shift = match.Groups["shift"].Success
                ? ConvertShift(match.Groups["shift"].Value)
                : string.Empty;
            return string.IsNullOrEmpty(shift)
                ? $"{{{{data|{format}}}}}"
                : $"{{{{data:{shift}|{format}}}}}";
        });

        converted = TextBlazeCommandPattern().Replace(converted, match =>
        {
            unsupportedCount++;
            return match.Value;
        });
        unsupported = unsupportedCount;
        return converted;
    }

    private static string ConvertDateFormat(string format) =>
        format.Replace("YYYY", "yyyy", StringComparison.Ordinal)
            .Replace("YY", "yy", StringComparison.Ordinal)
            .Replace("DD", "dd", StringComparison.Ordinal);

    private static string ConvertShift(string shift)
    {
        var normalized = shift.Trim().ToLowerInvariant();
        var match = ShiftPattern().Match(normalized);
        return match.Success
            ? $"{match.Groups["amount"].Value}{match.Groups["unit"].Value}"
            : string.Empty;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizeTrigger(string? trigger, out bool convertedColonPrefix)
    {
        var normalized = (trigger ?? string.Empty).Trim();
        convertedColonPrefix = normalized.StartsWith(':');
        if (normalized.Length == 0)
        {
            return normalized;
        }

        return normalized[0] switch
        {
            '/' => normalized,
            ':' => "/" + normalized[1..],
            _ => "/" + normalized
        };
    }

    private static int LeadingWhitespace(string value) =>
        value.TakeWhile(char.IsWhiteSpace).Count();

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal);
        }

        return value;
    }

    private static List<string> SplitInlineList(string value) =>
        value.Trim().TrimStart('[').TrimEnd(']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unquote)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

    [GeneratedRegex(@"\{key:\s*tab\s*\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TextBlazeTabPattern();

    [GeneratedRegex(@"\{time:\s*(?<format>[^;}]+)(?:;\s*shift=(?<shift>[^}]+))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TextBlazeTimePattern();

    [GeneratedRegex(@"(?<!\{)\{(?!\{)[a-z][^{}\r\n]*\}(?!\})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TextBlazeCommandPattern();

    [GeneratedRegex(@"^(?<amount>[+-]\d+)(?<unit>[dmy])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShiftPattern();

    [GeneratedRegex(@"^(?<indent>\s*)-\s*(?<key>trigger|triggers|match)\s*:\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EspansoTriggerPattern();

    [GeneratedRegex(@"^\s*triggers\s*:\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EspansoTriggersPattern();

    [GeneratedRegex(@"^\s*label\s*:\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EspansoLabelPattern();

    [GeneratedRegex(@"^\s*replace\s*:\s*(?<value>.*?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EspansoReplacePattern();
}
