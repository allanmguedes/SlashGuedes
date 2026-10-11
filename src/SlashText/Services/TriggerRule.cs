using System.Text;

namespace SlashText.Services;

public static class TriggerRule
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 64;

    public static bool IsSupportedPrefix(char value) => value == '/';

    public static bool IsSupportedCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '-' or '_';

    public static bool TryValidate(string? value, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = UiText.Get("Informe um atalho.");
            return false;
        }

        if (value.Length < MinimumLength)
        {
            error = UiText.Get("O atalho precisa começar com / e ter pelo menos um caractere.");
            return false;
        }

        if (value.Length > MaximumLength)
        {
            error = UiText.Format($"O atalho pode ter no máximo {MaximumLength} caracteres.");
            return false;
        }

        if (!IsSupportedPrefix(value[0]) ||
            value.Skip(1).Any(character => !IsSupportedCharacter(character)))
        {
            error = UiText.Get("O atalho deve começar com / e usar somente letras, números, hífen ou sublinhado.");
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    public static bool ConflictsWith(string candidate, IEnumerable<string> existing) =>
        existing.Any(item => string.Equals(
            Normalize(item),
            Normalize(candidate),
            StringComparison.Ordinal));

    public static bool IsPrefixOfAnother(string candidate, IEnumerable<string> existing) =>
        existing.Any(item =>
            item.Length > candidate.Length &&
            Normalize(item).StartsWith(Normalize(candidate), StringComparison.Ordinal));
}
