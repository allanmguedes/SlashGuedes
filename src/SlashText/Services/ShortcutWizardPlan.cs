using System.Globalization;
using System.Text;
using SlashText.Models;

namespace SlashText.Services;

/// <summary>
/// A safe, non-executable wizard plan that is persisted using the existing
/// SnippetMarkdownRepository and TemplateEngine. This is deliberately NOT
/// an import format; .sgshortcut validation will be implemented separately.
/// </summary>
public static class ShortcutWizardPlan
{
    public const int MaxFields = 20;
    public const int MaxBodyLength = 30_000;

    public static string FieldIdentifier(string label)
    {
        var source = (label ?? "").Trim().Normalize(NormalizationForm.FormD);
        var text = new StringBuilder();
        var separator = false;
        foreach (var c in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (separator && text.Length > 0) text.Append('_');
                text.Append(char.ToLowerInvariant(c));
                separator = false;
            }
            else separator = true;
        }
        var result = text.ToString().Trim('_');
        if (result.Length > 40) result = result[..40].TrimEnd('_');
        return result;
    }

    public static bool TryCreate(
        string name, string trigger, string category, string body,
        IEnumerable<string> existingTriggers, out Snippet? snippet, out string error)
    {
        snippet = null;
        name = (name ?? "").Trim();
        trigger = (trigger ?? "").Trim();
        body ??= "";
        if (name.Length is < 1 or > 120)
        {
            error = "Informe um nome de até 120 caracteres.";
            return false;
        }
        if (!TriggerRule.TryValidate(trigger, out error)) return false;
        if (TriggerRule.ConflictsWith(trigger, existingTriggers))
        {
            error = "Esse comando já existe. Escolha outro.";
            return false;
        }
        if (body.Length is < 1 or > MaxBodyLength)
        {
            error = "Informe um conteúdo de até 30.000 caracteres.";
            return false;
        }
        if (new TemplateEngine().GetFillableFields(body).Count > MaxFields)
        {
            error = "O assistente permite no máximo 20 campos. Use o editor avançado para mais opções.";
            return false;
        }
        snippet = new Snippet
        {
            Name = name,
            Trigger = trigger,
            Category = ShortcutCategories.Resolve(category).Name,
            Content = body,
            Format = SnippetFormat.Plain
        };
        error = string.Empty;
        return true;
    }
}
