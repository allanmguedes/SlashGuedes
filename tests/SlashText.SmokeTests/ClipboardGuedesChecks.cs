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

        Console.WriteLine("SlashGuedes clipboard: retenção, favoritos, fixados e teclas OK.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(
            "Teste do SlashGuedes falhou: " + message);
    }
}
