using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SlashText.Models;

namespace SlashText.Services;

public sealed partial class SnippetMarkdownRepository
{
    private const string Header =
        "# Meus atalhos\n\n<!-- Arquivo gerado pelo SlashDesk. A edição manual é opcional. -->\n\n";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly string _backupDirectory;

    public SnippetMarkdownRepository(string? filePath = null, string? backupDirectory = null)
    {
        _filePath = filePath ?? AppPaths.SnippetsFile;
        _backupDirectory = backupDirectory ?? AppPaths.BackupsDirectory;
    }

    public async Task<IReadOnlyList<Snippet>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            await SaveAsync([], cancellationToken);
            return [];
        }

        var markdown = await File.ReadAllTextAsync(_filePath, cancellationToken);
        return ReadRequired(markdown, cancellationToken);
    }

    internal static List<Snippet> ReadRequired(string markdown, CancellationToken cancellationToken = default, bool forRestore = false)
    {
        var snippets = Parse(markdown, cancellationToken, strict: forRestore);
        if (markdown.Contains("<!-- slashtext:", StringComparison.OrdinalIgnoreCase) &&
            snippets.Count == 0)
        {
            throw new InvalidDataException(UiText.Get("O arquivo contém atalhos, mas nenhum pôde ser interpretado."));
        }

        return snippets;
    }

    public async Task SaveAsync(
        IEnumerable<Snippet> snippets,
        CancellationToken cancellationToken = default)
    {
        var ordered = snippets
            .OrderBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Trigger, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            Validate(ordered[index], ordered.Take(index));
        }

        var markdown = Serialize(ordered);
        var directory = Path.GetDirectoryName(_filePath) ?? AppPaths.BaseDirectory;
        Directory.CreateDirectory(directory);

        if (File.Exists(_filePath))
        {
            var current = await File.ReadAllTextAsync(_filePath, cancellationToken);
            if (current == markdown)
            {
                return;
            }

        }

        await AtomicFilePersistence.WriteTextAsync(
            _filePath,
            markdown,
            new UTF8Encoding(false),
            cancellationToken);
    }

    internal static List<Snippet> ParseImport(string markdown, CancellationToken token)
    {
        ExternalInputBudget.CheckText(markdown, token);
        return Parse(markdown, token, external: true);
    }

    private static List<Snippet> Parse(string markdown, CancellationToken token = default, bool external = false, bool strict = false)
    {
        var normalized = markdown.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var snippets = new List<Snippet>();
        var externalTriggers = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var heading = HeadingPattern().Match(lines[index]);
            if (!heading.Success)
            {
                if (strict && lines[index].TrimStart().StartsWith("<!-- slashtext:", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unparsed shortcut metadata.");
                continue;
            }

            var trigger = heading.Groups["trigger"].Value;
            if (external && (snippets.Count >= ExternalInputBudget.SnippetCount || trigger.Length > ExternalInputBudget.MetadataCharacters)) throw ExternalInputBudget.Exceeded();
            var metadataIndex = NextNonEmptyLine(lines, index + 1);
            if (metadataIndex >= lines.Length ||
                !TryReadMetadata(lines[metadataIndex], out var metadataJson))
            {
                throw new InvalidDataException(
                    UiText.Format($"Metadados do atalho '{trigger}' não encontrados após a linha {index + 1}."));
            }

            if (external && metadataJson.Length > ExternalInputBudget.MetadataCharacters) throw ExternalInputBudget.Exceeded();
            var fenceIndex = NextNonEmptyLine(lines, metadataIndex + 1);
            if (fenceIndex >= lines.Length ||
                !TryReadFence(lines[fenceIndex], out var fence, out var format))
            {
                throw new InvalidDataException(
                    UiText.Format($"Bloco de conteúdo do atalho '{trigger}' não encontrado."));
            }

            var closingFenceIndex = fenceIndex + 1;
            long contentCharacters = 0;
            while (closingFenceIndex < lines.Length &&
                   !lines[closingFenceIndex].Trim().Equals(fence, StringComparison.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                contentCharacters += lines[closingFenceIndex].Length + 1L;
                if (external && contentCharacters > ExternalInputBudget.SnippetCharacters + 1L) throw ExternalInputBudget.Exceeded();
                closingFenceIndex++;
            }

            if (closingFenceIndex >= lines.Length)
            {
                throw new InvalidDataException(UiText.Format($"Bloco de conteúdo do atalho '{trigger}' não foi fechado."));
            }

            SnippetMetadata metadata;
            try
            {
                if (strict)
                {
                    using var document = JsonDocument.Parse(metadataJson);
                    BackupContentValidator.RequireUniqueMembers(document.RootElement, token);
                    BackupContentValidator.RequireTypedMembers(document.RootElement, typeof(SnippetMetadata), token);
                }
                metadata = JsonSerializer.Deserialize<SnippetMetadata>(metadataJson, JsonOptions)
                    ?? throw new JsonException("Metadados vazios.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    UiText.Format($"Metadados inválidos no atalho '{trigger}', linha {metadataIndex + 1}."),
                    exception);
            }

            var snippet = new Snippet
            {
                Id = metadata.Id == Guid.Empty ? Guid.NewGuid() : metadata.Id,
                Name = metadata.Name,
                Trigger = trigger,
                Category = string.IsNullOrWhiteSpace(metadata.Category) ? UiText.Get("Geral") : metadata.Category,
                Format = format,
                Enabled = metadata.Enabled,
                IsFavorite = metadata.IsFavorite, IsPinned = metadata.IsPinned,
                ConfirmKeys = metadata.ConfirmKeys?.Count > 0
                    ? metadata.ConfirmKeys
                    : ["Enter", "Tab", "Space"],
                Content = string.Join("\n", lines[(fenceIndex + 1)..closingFenceIndex]).TrimEnd()
            };

            snippet.HasLegacyIncompatibleTrigger = !TriggerRule.TryValidate(snippet.Trigger, out _);
            if (external) ExternalInputBudget.CheckSnippet(snippet, snippets.Count);
            Validate(snippet, external ? [] : snippets);
            if (external && !externalTriggers.Add(TriggerRule.Normalize(snippet.Trigger)))
                throw new InvalidDataException(UiText.Format($"O atalho '{snippet.Trigger}' está duplicado."));
            snippets.Add(snippet);
            index = closingFenceIndex;
        }

        return snippets;
    }

    private static string Serialize(IEnumerable<Snippet> snippets)
    {
        var builder = new StringBuilder(Header);
        foreach (var snippet in snippets)
        {
            var metadata = new SnippetMetadata(
                snippet.Id,
                snippet.Name,
                snippet.Category,
                snippet.Format == SnippetFormat.Markdown ? "markdown" : "plain",
                snippet.Enabled,
                snippet.ConfirmKeys, snippet.IsFavorite, snippet.IsPinned);

            var fence = new string('`', Math.Max(3, LongestBacktickRun(snippet.Content) + 1));
            builder.Append("## ").AppendLine(snippet.Trigger);
            builder.Append("<!-- slashtext:")
                .Append(JsonSerializer.Serialize(metadata, JsonOptions))
                .AppendLine(" -->");
            builder.Append(fence)
                .AppendLine(snippet.Format == SnippetFormat.Markdown ? "markdown" : "text");
            builder.AppendLine(snippet.Content.TrimEnd());
            builder.AppendLine(fence).AppendLine();
        }

        return builder.ToString();
    }

    private static void Validate(Snippet snippet, IEnumerable<Snippet> existing)
    {
        if (!TriggerRule.TryValidate(snippet.Trigger, out var triggerError) &&
            !snippet.HasLegacyIncompatibleTrigger)
        {
            throw new InvalidDataException(triggerError);
        }

        if (string.IsNullOrWhiteSpace(snippet.Name))
        {
            throw new InvalidDataException(UiText.Format($"O atalho '{snippet.Trigger}' precisa de um nome."));
        }

        if (TriggerRule.ConflictsWith(snippet.Trigger, existing.Select(item => item.Trigger)))
        {
            throw new InvalidDataException(UiText.Format($"O atalho '{snippet.Trigger}' está duplicado."));
        }
    }

    private static int NextNonEmptyLine(string[] lines, int start)
    {
        while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        return start;
    }

    private static bool TryReadMetadata(string line, out string json)
    {
        var trimmed = line.Trim();
        const string prefix = "<!-- slashtext:";
        const string suffix = "-->";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith(suffix, StringComparison.Ordinal))
        {
            json = trimmed[prefix.Length..^suffix.Length].Trim();
            return json.StartsWith('{') && json.EndsWith('}');
        }

        json = string.Empty;
        return false;
    }

    private static bool TryReadFence(string line, out string fence, out SnippetFormat format)
    {
        var trimmed = line.Trim();
        var length = 0;
        while (length < trimmed.Length && trimmed[length] == '`')
        {
            length++;
        }

        if (length < 3)
        {
            fence = string.Empty;
            format = SnippetFormat.Plain;
            return false;
        }

        var language = trimmed[length..].Trim();
        if (!string.IsNullOrEmpty(language) &&
            !language.Equals("text", StringComparison.OrdinalIgnoreCase) &&
            !language.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            fence = string.Empty;
            format = SnippetFormat.Plain;
            return false;
        }

        fence = new string('`', length);
        format = language.Equals("markdown", StringComparison.OrdinalIgnoreCase)
            ? SnippetFormat.Markdown
            : SnippetFormat.Plain;
        return true;
    }

    private static int LongestBacktickRun(string value)
    {
        var longest = 0;
        var current = 0;
        foreach (var character in value)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    [GeneratedRegex(@"^[ \t]*##[ \t]+(?<trigger>[/\:]\S+)[ \t]*$")]
    private static partial Regex HeadingPattern();

    private sealed record SnippetMetadata(
        Guid Id,
        string Name,
        string Category,
        string Format,
        bool Enabled,
        List<string>? ConfirmKeys, bool IsFavorite = false, bool IsPinned = false);
}
