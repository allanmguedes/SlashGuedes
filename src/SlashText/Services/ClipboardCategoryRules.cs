namespace SlashText.Services;

/// <summary>Pure validation shared by category commands and smoke tests.</summary>
public static class ClipboardCategoryRules
{
    public const int MaximumNameLength = 50;
    public const int MaximumCategories = 100;

    public static string ValidateNew(string? name, IEnumerable<string> existing)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumNameLength ||
            normalized.Any(char.IsControl))
            throw new ArgumentException("Use um nome de 1 a 50 caracteres, sem quebras de linha.");
        var names = existing.ToArray();
        if (names.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Já existe uma categoria com esse nome.");
        if (names.Length >= MaximumCategories)
            throw new InvalidOperationException("Limite de 100 categorias atingido.");
        return normalized;
    }

    public static List<string> Migrate(IEnumerable<string>? saved, IEnumerable<string>? assigned)
    {
        var categories = new List<string>();
        foreach (var raw in (saved ?? []).Concat(assigned ?? []))
        {
            var name = (raw ?? string.Empty).Trim();
            if (name.Length is < 1 or > MaximumNameLength ||
                name.Any(char.IsControl) ||
                categories.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (categories.Count == MaximumCategories) break;
            categories.Add(name);
        }
        return categories;
    }

    public static IReadOnlyList<T> SelectNonFavorites<T>(
        IEnumerable<T> source, Func<T, bool> isFavorite) =>
        source.Where(item => !isFavorite(item)).ToArray();
}
