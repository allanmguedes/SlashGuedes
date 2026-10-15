using SlashText.Models;
using SlashText.Services;

internal static class ClipboardGuedesChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(0, 42)
            .Select(i => new ClipboardHistoryItem
            {
                Text = "Item " + i,
                CopiedUtc = now.AddSeconds(-i),
                IsFavorite = i is 35 or 39,
                IsPinned = i == 41
            })
            .ToArray();

        var removed = ClipboardRetentionPolicy.SelectExpired(items, 25);
        Require(removed.Count == 14, "25 recentes + 3 protegidos = 28 itens");
        Require(removed.All(i => !i.IsFavorite && !i.IsPinned),
            "favoritos ou fixados não podem ser expurgados");
        Require(!removed.Any(i => i.Text is "Item 35" or "Item 39" or "Item 41"),
            "favoritos antigos permanecem");
        Require(ClipboardRetentionPolicy.NormalizeLimit(1) == 10,
            "limite mínimo");
        Require(ClipboardRetentionPolicy.NormalizeLimit(9999) == 500,
            "limite máximo");
        Require(ClipboardRetentionPolicy.SelectExpired(items, 10).Count == 29,
            "limite alterado expurga somente recentes");
        Require(ClipboardRetentionPolicy.SelectExpired(items, 500).Count == 0,
            "limite alto preserva todos");

        Require(ClipboardShortcut.TryParse(" ctrl + alt + v ", out var normal,
            out var modifiers, out var key) &&
            normal == "Ctrl+Alt+V" && modifiers == 0x0003 && key == 0x56,
            "atalho Ctrl+Alt+V");
        Require(ClipboardShortcut.TryParse("win+shift+v", out normal,
            out modifiers, out key) && normal == "Win+Shift+V",
            "atalho padrão ABNT2");
        Require(ClipboardShortcut.TryParse("Alt+Shift+F12", out normal,
            out modifiers, out key) && key == 0x7B,
            "atalho com função F12");
        Require(!ClipboardShortcut.TryParse("Win+V", out _, out _, out _),
            "não substituir o Win+V original");
        Require(!ClipboardShortcut.TryParse("Ctrl+V", out _, out _, out _),
            "não substituir Ctrl+V");
        Require(!ClipboardShortcut.TryParse("Ctrl+Ctrl+V", out _, out _, out _),
            "não permitir modificador repetido");
        Require(!ClipboardShortcut.TryParse("Ctrl+Alt+PrintScreen", out _, out _, out _),
            "não misturar atalhos de captura");

        // Explicit clear-all-except-favorites is different from automatic
        // retention: pinned-only copies must be deleted by this command.
        var purged = ClipboardCategoryRules.SelectNonFavorites(items, x => x.IsFavorite);
        Require(purged.Count == 40, "limpeza deve manter somente dois favoritos");
        Require(purged.Any(x => x.IsPinned && !x.IsFavorite),
            "item apenas fixado deve entrar na limpeza explícita");
        Require(purged.All(x => !x.IsFavorite),
            "limpeza nunca pode incluir item com estrela");

        var normalized = ClipboardCategoryRules.Migrate(
            ["Trabalho", "links", "TRABALHO"], ["Capturas", "Links", ""]);
        Require(normalized.SequenceEqual(["Trabalho", "links", "Capturas"]),
            "categorias existentes migradas sem duplicação");
        Require(ClipboardCategoryRules.ValidateNew("  Pessoal  ", normalized) == "Pessoal",
            "criar categoria remove espaços nas bordas");
        Require(Throws<InvalidOperationException>(() =>
            ClipboardCategoryRules.ValidateNew("trabalho", normalized)),
            "categoria duplicada rejeitada ignorando caixa");
        Require(Throws<ArgumentException>(() =>
            ClipboardCategoryRules.ValidateNew("  ", normalized)),
            "categoria sem nome rejeitada");
        Require(Throws<ArgumentException>(() =>
            ClipboardCategoryRules.ValidateNew(new string('X', 51), normalized)),
            "categoria com mais de 50 caracteres rejeitada");

        Console.WriteLine("SlashGuedes clipboard: retenção, favoritos, fixados e teclas OK.");
    }

    private static bool Throws<T>(Action callback) where T : Exception
    {
        try { callback(); return false; }
        catch (T) { return true; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(
            "Teste do SlashGuedes falhou: " + message);
    }
}
