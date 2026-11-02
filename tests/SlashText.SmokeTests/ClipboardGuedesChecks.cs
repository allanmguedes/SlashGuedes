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
        // Win+V and Ctrl+V are valid combinations syntactically. If another
        // app or Windows reserves them, RegisterHotKey will reject them safely.
        Require(ClipboardShortcut.TryParse("Win+V", out normal, out modifiers, out key)
            && modifiers == 0x0008 && key == 0x56,
            "permitir Win+tecla, sujeito à reserva do Windows");
        Require(ClipboardShortcut.TryParse("Ctrl+V", out normal, out modifiers, out key)
            && modifiers == 0x0002 && key == 0x56,
            "permitir Ctrl+tecla único modificador");
        Require(ClipboardShortcut.TryParse("Alt+Home", out normal, out modifiers, out key)
            && key == 0x24, "permitir navegação");
        Require(ClipboardShortcut.TryParse("Ctrl+Oem102", out normal, out modifiers, out key)
            && key == 0xE2, "permitir tecla adicional do ABNT2");
        Require(ClipboardShortcut.TryParse("Alt+OemPlus", out normal, out modifiers, out key)
            && key == 0xBB, "permitir tecla + sem ambiguidade");
        Require(ClipboardShortcut.TryParse("Win+Num7", out normal, out modifiers, out key)
            && key == 0x67, "permitir teclado numérico");
        Require(ClipboardShortcut.TryParse("Ctrl+F24", out normal, out modifiers, out key)
            && key == 0x87, "permitir teclas de função até F24");
        Require(ClipboardShortcut.TryParse("Ctrl+VK_E2", out normal, out modifiers, out key)
            && normal == "Ctrl+Oem102" && key == 0xE2,
            "normalizar virtual key OEM");
        Require(ClipboardShortcut.TryParse("Ctrl+Alt+Shift+PrintScreen", out normal,
            out modifiers, out key) && modifiers == 0x0007 && key == 0x2C,
            "permitir três modificadores mais tecla especial");

        Require(!ClipboardShortcut.TryParse("V", out _, out _, out _),
            "rejeitar letra sozinha");
        Require(!ClipboardShortcut.TryParse("Shift+V", out _, out _, out _),
            "Shift sozinho não é modificador suficiente");
        Require(!ClipboardShortcut.TryParse("Ctrl", out _, out _, out _),
            "rejeitar Ctrl sem tecla final");
        Require(!ClipboardShortcut.TryParse("Ctrl+Ctrl+V", out _, out _, out _),
            "rejeitar modificador duplicado");
        Require(!ClipboardShortcut.TryParse("Ctrl+Alt", out _, out _, out _),
            "rejeitar combinação sem tecla não modificadora");
        Require(!ClipboardShortcut.TryParse("Ctrl+Shift+VK_11", out _, out _, out _),
            "rejeitar tecla modificadora como tecla final");

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
        CheckCategoryPersistenceAsync().GetAwaiter().GetResult();

        Require(!ClipboardPasteService.IsExternalTarget(IntPtr.Zero),
            "não enviar colagem quando não existe uma janela de destino");
        Require(ClipboardPasteService.TryPasteAsync(IntPtr.Zero).GetAwaiter().GetResult()
                == ClipboardPasteService.Result.NoExternalTarget,
            "não deve enviar Ctrl+V sem destino válido");
        Require(new AppSettings().ClipboardAutoPasteEnabled,
            "novas instalações ativam colagem com fallback seguro");
        Console.WriteLine("SlashGuedes clipboard: retenção, categorias, pop-up e colagem segura OK.");
    }

    private static async Task CheckCategoryPersistenceAsync()
    {
        var originalEnvironment = AppPaths.Current;
        var sandbox = Path.Combine(Path.GetTempPath(),
            "slashguedes-category-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppPaths.Initialize(new AppDataEnvironment(
                DistributionMode.Portable, sandbox, sandbox, isSlashGuedes: true));
            var file = Path.Combine(AppPaths.DataDirectory, "ClipboardHistory", "history.json");
            await new JsonFileStore<ClipboardHistoryState>(file).SaveAsync(new ClipboardHistoryState
            {
                Items =
                [
                    new() { Id = "texto", Kind = "text", Text = "Texto de teste" },
                    new() { Id = "print", Kind = "image", ImageFile = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png" },
                    new() { Id = "outro", Kind = "text", Text = "Sem categoria" }
                ],
                Categories = ["Trabalho"]
            });
            using (var history = new ClipboardHistoryService())
            {
                await history.LoadAsync(25);
                await history.AddCategoryAsync("Jogos");
                var updated = await history.MoveItemsAsync(
                    ["texto", "print", "inexistente"], "Jogos");
                Require(updated == 2, "movimentação em lote considera apenas itens existentes");
                Require(history.Items.Single(i => i.Id == "outro").Category == "",
                    "itens não selecionados permanecem intactos");
                await history.RenameCategoryAsync("Jogos", "Capturas");
                Require(history.Items.Count(i => i.Category == "Capturas") == 2,
                    "renomear categoria atualiza todas as cópias associadas");
            }
            using (var reloaded = new ClipboardHistoryService())
            {
                await reloaded.LoadAsync(25);
                Require(reloaded.Categories.Contains("Capturas") &&
                    reloaded.Items.Count(i => i.Category == "Capturas") == 2,
                    "categorias e movimentos sobrevivem à reinicialização");
                var rejected = false;
                try { await reloaded.MoveItemsAsync(["texto"], "Inexistente"); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected && reloaded.Items.Single(i => i.Id == "texto").Category == "Capturas",
                    "destino inválido é rejeitado sem alterar o histórico");
                await reloaded.DeleteCategoryAsync("Capturas");
                Require(reloaded.Items.Count == 3 &&
                    reloaded.Items.All(i => i.Category != "Capturas") &&
                    reloaded.Items.Count(i => i.Category == "") == 3,
                    "excluir categoria preserva todos os textos e prints");
            }
        }
        finally
        {
            AppPaths.Initialize(originalEnvironment);
            try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); }
            catch (IOException) { /* Isolated fixture is safe to clean later. */ }
            catch (UnauthorizedAccessException) { }
        }
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
