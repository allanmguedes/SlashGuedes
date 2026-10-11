namespace SlashText.Services;

internal sealed record ShortcutCategory(string Name, string Icon)
{
    public string DisplayName => UiText.Get(Name);
}

internal static class ShortcutCategories
{
    internal static IReadOnlyList<ShortcutCategory> All { get; } = Array.AsReadOnly(new[]
    {
        new ShortcutCategory("Geral", "FolderOpen"),
        new ShortcutCategory("Outros", "Folder"),
        new ShortcutCategory("Trabalho", "Briefcase"),
        new ShortcutCategory("Estudos", "BookOpen"),
        new ShortcutCategory("Mensagens", "Mail"),
        new ShortcutCategory("Documentos", "ScrollText"),
        new ShortcutCategory("Código", "CodeXml"),
        new ShortcutCategory("Comandos", "Keyboard")
    });

    internal static ShortcutCategory Resolve(string? category) =>
        string.IsNullOrWhiteSpace(category) ? All[0] :
        All.FirstOrDefault(item => string.Equals(item.Name, category.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[1];

    internal static bool IsKnown(string? category) => string.IsNullOrWhiteSpace(category) ||
        All.Any(item => string.Equals(item.Name, category.Trim(), StringComparison.OrdinalIgnoreCase));
}
