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
        Require(new AppSettings().ClipboardMaxEntries == 50 &&
                ClipboardRetentionPolicy.DefaultRecentLimit == 50,
            "novas instalações devem começar com 50 itens recentes");
        var legacy = new AppSettings { ClipboardMaxEntries = 25 };
        Require(ClipboardRetentionPolicy.UpgradeLegacyDefault(legacy) &&
                legacy.ClipboardMaxEntries == 50 &&
                legacy.ClipboardLimit50MigrationApplied,
            "instalações antigas com padrão 25 devem migrar para 50");
        legacy.ClipboardMaxEntries = 25;
        Require(!ClipboardRetentionPolicy.UpgradeLegacyDefault(legacy) &&
                legacy.ClipboardMaxEntries == 25,
            "escolha explícita de 25 após migração deve ser preservada");
        var customLimit = new AppSettings { ClipboardMaxEntries = 100 };
        Require(ClipboardRetentionPolicy.UpgradeLegacyDefault(customLimit) &&
                customLimit.ClipboardMaxEntries == 100,
            "limite personalizado existente deve permanecer inalterado");

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
        CheckCaptureShortcutSettingsCompatibilityAsync().GetAwaiter().GetResult();
        CheckCaptureRegistrationAsync().GetAwaiter().GetResult();

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
            var preferences = new JsonFileStore<AppSettings>(
                Path.Combine(AppPaths.DataDirectory, "settings.json"));
            await preferences.SaveAsync(new AppSettings
            {
                ClipboardMaxEntries = 50,
                ClipboardLimit50MigrationApplied = true,
                ClipboardPopupLeft = 178.5,
                ClipboardPopupTop = 92.25
            });
            var reloadedPreferences = await preferences.LoadAsync();
            Require(reloadedPreferences.ClipboardMaxEntries == 50 &&
                    reloadedPreferences.ClipboardLimit50MigrationApplied &&
                    reloadedPreferences.ClipboardPopupLeft == 178.5 &&
                    reloadedPreferences.ClipboardPopupTop == 92.25,
                "limite e posição do Alt+V persistem após reabrir o aplicativo");
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

    // Regression for preview.117: two new nullable Alt+V position fields
    // caused valid legacy settings to be rejected and capture shortcuts to
    // appear reset. Keep both old (absent) and new (explicit null) formats.
    private static async Task CheckCaptureShortcutSettingsCompatibilityAsync()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "slashguedes-capture-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        var store = new JsonFileStore<AppSettings>(path);
        const string customCapture =
            "\"capture\":{\"activeMonitorShortcut\":\"Ctrl+Alt+F9\"," +
            "\"regionShortcut\":\"Ctrl+Alt+F10\"," +
            "\"windowShortcut\":\"Ctrl+Alt+F11\"," +
            "\"scrollingShortcut\":\"Ctrl+Alt+WheelDown\"}";
        try
        {
            // Settings created before the popup-position upgrade.
            var legacy = "{\"theme\":\"Dark\",\"clipboardMaxEntries\":25," + customCapture + "}";
            await File.WriteAllTextAsync(path, legacy);
            var loaded = await store.LoadAsync();
            Require(store.ReadState == StoredReadState.Valid && !store.IsProtected &&
                    loaded.Capture.ActiveMonitorShortcut == "Ctrl+Alt+F9" &&
                    loaded.Capture.RegionShortcut == "Ctrl+Alt+F10" &&
                    loaded.Capture.WindowShortcut == "Ctrl+Alt+F11" &&
                    loaded.Capture.ScrollingShortcut == "Ctrl+Alt+WheelDown" &&
                    loaded.Theme == "Dark",
                "preferências antigas precisam manter os quatro atalhos de captura");
            Require(await File.ReadAllTextAsync(path) == legacy,
                "a leitura das preferências antigas não pode reescrevê-las");

            // Settings saved by preview.117 before the user drags Alt+V.
            var preview117 = "{\"clipboardPopupLeft\":null,\"clipboardPopupTop\":null," +
                "\"clipboardLimit50MigrationApplied\":true," + customCapture + "}";
            await File.WriteAllTextAsync(path, preview117);
            loaded = await store.LoadAsync();
            Require(store.ReadState == StoredReadState.Valid && !store.IsProtected &&
                    loaded.ClipboardPopupLeft is null && loaded.ClipboardPopupTop is null &&
                    loaded.Capture.ActiveMonitorShortcut == "Ctrl+Alt+F9" &&
                    loaded.Capture.RegionShortcut == "Ctrl+Alt+F10" &&
                    loaded.Capture.WindowShortcut == "Ctrl+Alt+F11",
                "posições opcionais nulas não podem bloquear capturas e atalhos");
            Require(await File.ReadAllTextAsync(path) == preview117,
                "a leitura do arquivo da preview.117 não pode sobrescrever os atalhos");

            // Re-saving an already-valid profile cannot reset custom capture shortcuts.
            ClipboardRetentionPolicy.UpgradeLegacyDefault(loaded);
            await store.SaveAsync(loaded);
            var again = await store.LoadAsync();
            Require(store.ReadState == StoredReadState.Valid && !store.IsProtected &&
                    again.Capture.ActiveMonitorShortcut == "Ctrl+Alt+F9" &&
                    again.Capture.RegionShortcut == "Ctrl+Alt+F10" &&
                    again.Capture.WindowShortcut == "Ctrl+Alt+F11" &&
                    again.Capture.ScrollingShortcut == "Ctrl+Alt+WheelDown",
                "salvar preferências do histórico não deve mudar atalhos de prints");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // A completed screenshot is recorded and survives reloading the same
    // capture history. Work only with isolated test data, never the user's files.
    private static async Task CheckCaptureRegistrationAsync()
    {
        var previous = AppPaths.Current;
        var root = Path.Combine(Path.GetTempPath(),
            "slashguedes-capture-counter-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppPaths.Initialize(new AppDataEnvironment(
                DistributionMode.Portable, root, root, isSlashGuedes: true));
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var output = Path.Combine(AppPaths.DataDirectory, "test-print.png");
            var service = new CaptureService();
            await service.LoadAsync();
            Require(service.History.Count == 0 && !service.IsHistoryProtected,
                "histórico de capturas novo inicia vazio e gravável");
            using (var bitmap = new System.Drawing.Bitmap(8, 8))
                await service.SaveEditedImageAsync(bitmap, output, new CaptureSettings());
            Require(File.Exists(output) && service.History.Count == 1,
                "a captura salva precisa ser contabilizada no histórico");
            var reloaded = new CaptureService();
            await reloaded.LoadAsync();
            Require(reloaded.History.Count == 1 && !reloaded.IsHistoryProtected,
                "contador de capturas permanece correto após reiniciar");
        }
        finally
        {
            AppPaths.Initialize(previous);
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
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
