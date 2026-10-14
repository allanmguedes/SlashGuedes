using SlashText.Models;
using SlashText.Services;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Diagnostics;
using ScreenRecorderLib;

if (args.Length == 2 && args[0] == "--backup-compatibility-smoke")
{
    await BackupCompatibilityChecks.RunAsync(args[1]); return;
}
if (args.Length == 2 && args[0] == "--backup-compatibility-fixtures")
{
    BackupCompatibilityChecks.CreatePilotFixtures(args[1]); return;
}
if (args.Length == 4 && args[0] == "--restore-recovery-worker")
{
    RestoreRecoveryChecks.Worker(args);
    return;
}
if (args.Length == 2 && args[0] == "--restore-recovery-smoke")
{
    Directory.CreateDirectory(args[1]);
    await RestoreRecoveryChecks.RunAsync(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "--retention-worker")
{
    RetentionPrivacyChecks.Worker(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "--retention-smoke")
{
    Directory.CreateDirectory(args[1]);
    await RetentionPrivacyChecks.RunAsync(args[1]);
    return;
}

if (args.Length == 1 && args[0] == "--clipboard-payload-smoke")
{
    var clipboardRoot = Path.Combine(Path.GetTempPath(), "slashdesk-clipboard-payload-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(clipboardRoot);
    ClipboardPrivacyChecks.Run(clipboardRoot);
    return;
}
if (args.Length == 2 && args[0] == "--clipboard-native-worker")
{
    Environment.Exit(ClipboardNativeChecks.RunWorker(args[1]));
    return;
}
if (args.Length == 2 && args[0] == "--clipboard-native-isolated")
{
    Environment.Exit(ClipboardNativeChecks.RunIsolated(args[1]));
    return;
}

var root = Path.Combine(Path.GetTempPath(), $"slashtext-smoke-{Guid.NewGuid():N}");
AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(root, "initial-app"), root, isCapturePilot: true));

var engine = new TemplateEngine();
var reference = new DateTimeOffset(2026, 7, 27, 14, 35, 0, TimeSpan.FromHours(-3));
var rendered = engine.Render(
    "{{data}}|{{data_curta}}|{{data_extensa}}|{{hora}}|{{mes}}|{{mes_nome}}|" +
    "{{mes_curto}}|{{ano}}|{{ano_curto}}|{{dia_semana_curto}}|{{data:-7d}}|{{tab}}",
    now: reference);

Require(rendered.StartsWith("27/07/2026|27/07/26|", StringComparison.Ordinal), "datas abreviada e extensa");
Require(rendered.Contains("|14:35|07|", StringComparison.Ordinal), "variáveis automáticas");
Require(rendered.Contains("|2026|26|", StringComparison.Ordinal), "ano completo e abreviado");
Require(rendered.Contains("|20/07/2026|", StringComparison.Ordinal), "cálculo de data");
Require(rendered.EndsWith(TemplateEngine.TabMarker, StringComparison.Ordinal), "marcador Tab");

var nativeInputType = typeof(QuickAccentService).GetNestedType(
    "Input",
    BindingFlags.NonPublic);
Require(nativeInputType is not null, "estrutura nativa do Acento Rápido");
Require(
    Marshal.SizeOf(nativeInputType!) == (Environment.Is64BitProcess ? 40 : 28),
    "estrutura INPUT compatível com SendInput");
Require(
    QuickAccentService.ShouldUseUppercase(shiftDown: false, capsLockOn: true),
    "Caps Lock mantém o acento em maiúsculo");
Require(
    !QuickAccentService.ShouldUseUppercase(shiftDown: true, capsLockOn: true),
    "Shift inverte Caps Lock");
Require(
    QuickAccentService.ShouldHandleInput(
        enabled: true,
        ownProcessInForeground: true,
        excludedApp: false),
    "Acento Rápido funciona em campos editáveis do próprio SlashDesk");
Require(
    !QuickAccentService.ShouldHandleInput(
        enabled: true,
        ownProcessInForeground: false,
        excludedApp: true) &&
    !QuickAccentService.ShouldHandleInput(
        enabled: false,
        ownProcessInForeground: true,
        excludedApp: false),
    "Acento Rápido respeita estado inativo e aplicativos excluídos");
var startupCoordinator = new StartupModuleCoordinator();
Require(QuickAccentService.ResolveRuntimeState(false, true, true) == QuickAccentRuntimeState.Disabled &&
        QuickAccentService.ResolveRuntimeState(true, false, true) == QuickAccentRuntimeState.Unavailable &&
        QuickAccentService.ResolveRuntimeState(true, true, true) == QuickAccentRuntimeState.Paused &&
        QuickAccentService.ResolveRuntimeState(true, true, false) == QuickAccentRuntimeState.Ready,
    "status distingue preferencia, inicializacao nativa e exclusao do aplicativo");
Require(QuickAccentService.IsProcessExcluded("GAME", "mstsc.exe; game.exe\nterminal") &&
        QuickAccentService.IsProcessExcluded("terminal", " terminal \r\n") &&
        !QuickAccentService.IsProcessExcluded("gamehelper", "game.exe") &&
        !QuickAccentService.IsProcessExcluded("game", null),
    "status e ativacao compartilham exclusoes exatas, sem diferenca de caixa ou falso positivo por prefixo");
var healthyModuleStarted = false;
Require(
    !await startupCoordinator.RunAsync(
        "Histórico de captura",
        () => Task.FromException(
            new IOException("C:\\Users\\Pessoa\\dado-privado.json"))) &&
    await startupCoordinator.RunAsync(
        "Atalhos",
        () =>
        {
            healthyModuleStarted = true;
            return Task.CompletedTask;
        }) &&
    healthyModuleStarted,
    "falha em módulo opcional não impede iniciar módulo saudável");
Require(
    startupCoordinator.Failures.Count == 1 &&
    startupCoordinator.Failures[0].Module == "Histórico de captura" &&
    startupCoordinator.Failures[0].ExceptionType ==
        typeof(IOException).FullName &&
    typeof(StartupModuleFailure).GetProperty("Message") is null,
    "diagnóstico de inicialização não armazena conteúdo ou mensagem privada");
Require(
    typeof(SlashText.MainWindow).GetMethod(
        "EnsureSnippetStorageAvailable",
        BindingFlags.NonPublic | BindingFlags.Instance) is not null,
    "falha dos atalhos ativa barreira contra sobrescrita");

var translationFlags = typeof(KeyboardHookService).GetField(
    "ToUnicodeNoStateChange",
    BindingFlags.NonPublic | BindingFlags.Static);
Require(
    translationFlags?.GetRawConstantValue() is uint flags && flags == 0x04,
    "leitura do teclado não altera o estado de acentos mortos em layouts ABNT");
Require(
    QuickAccentService.RemainingDelayMilliseconds(1_000, 1_000, 0) == 0 &&
    QuickAccentService.RemainingDelayMilliseconds(1_000, 1_000, 100) == 100 &&
    QuickAccentService.RemainingDelayMilliseconds(1_000, 1_080, 100) == 20 &&
    QuickAccentService.RemainingDelayMilliseconds(1_000, 1_250, 200) == 0,
    "atrasos de 0, 100 e 200 ms usam tempo monotônico real");

using (var quickAccentDelay = new QuickAccentDelayController())
{
    var activation = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var activationClock = Stopwatch.StartNew();
    long activatedAtTicks = 0;
    quickAccentDelay.Schedule(60, () =>
    {
        Interlocked.Exchange(ref activatedAtTicks, activationClock.Elapsed.Ticks);
        activation.TrySetResult();
    });
    await activation.Task.WaitAsync(TimeSpan.FromSeconds(2));
    // Measure the callback itself. A delayed Task.Delay observer is not evidence of early activation.
    Require(
        TimeSpan.FromTicks(Interlocked.Read(ref activatedAtTicks)) >= TimeSpan.FromMilliseconds(35),
        "Acento Rápido não abre antes do atraso, medido no callback com a tolerância existente");

    var cancelledCount = 0;
    quickAccentDelay.Schedule(80, () => Interlocked.Increment(ref cancelledCount));
    quickAccentDelay.Cancel();
    await Task.Delay(120);
    Require(cancelledCount == 0, "soltar a combinação cancela a abertura pendente");

    var deduplicatedCount = 0;
    var latestActivation = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    quickAccentDelay.Schedule(
        80,
        () => Interlocked.Increment(ref deduplicatedCount));
    quickAccentDelay.Schedule(
        20,
        () =>
        {
            Interlocked.Increment(ref deduplicatedCount);
            latestActivation.TrySetResult();
        });
    await latestActivation.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(100);
    Require(
        deduplicatedCount == 1,
        "reagendamento não produz abertura dupla");
}

using (var previewService = new QuickAccentService())
{
    var previewEvents = 0;
    previewService.Changed += (_, _) => previewEvents++;
    previewService.CharacterInserted += (_, _) => previewEvents++;
    Require(previewService.GetPreviewChoices('a') == "áàâã" && previewService.GetPreviewChoices('A') == "ÁÀÂÃ",
        "prévia usa escolhas reais e distingue maiúsculas sem consultar o teclado físico");
    previewService.SetUsage(new Dictionary<char, long> { ['ã'] = 10, ['à'] = 5 });
    Require(previewService.GetPreviewChoices('a') == "ãàáâ", "prévia usa a prioridade real de uso e conserva empates");
    previewService.SortByUsage = false;
    Require(previewService.GetPreviewChoices('a') == "áàâã", "desativar prioridade restaura a ordem dos conjuntos");
    previewService.SetCharacterSets(["Currency", "Special"]);
    Require(previewService.GetPreviewChoices('e') == "€" && previewService.GetPreviewChoices('c') == "¢©" && previewService.GetPreviewChoices('a') == "",
        "prévia respeita moedas, símbolos e letras sem opções");
    previewService.SetCharacterSets([]);
    Require(previewService.GetPreviewChoices('a') == "áàâã" && previewEvents == 0 && !previewService.IsRunning,
        "consultar a prévia não instala hook, dispara eventos nem altera a digitação");
}

var portugueseCharacters = QuickAccentService.PreviewCharacters(["PortugueseBrazil"]);
Require(
    portugueseCharacters.Contains('ã') && !portugueseCharacters.Contains('ä'),
    "conjunto somente PT-BR");
Require(
    QuickAccentService.PreviewCharacters(["PortugueseBrazil", "German", "Currency"])
        .Contains('€'),
    "combinação de conjuntos do Acento Rápido");

var fields = engine.GetFillableFields("Olá {{nome}}, chamado {{chamado|INC000}}. {{nome}}");
Require(fields.Count == 2, "campos únicos");
Require(fields[1].DefaultValue == "INC000", "valor padrão");

Require(TriggerRule.TryValidate("/teste", out _), "regra única aceita prefixo barra");
Require(TriggerRule.TryValidate("/ação_2-rapida", out _), "regra única aceita letras Unicode, números, hífen e sublinhado");
Require(!TriggerRule.TryValidate(":ação_2-rapida", out _), "regra única rejeita prefixo dois-pontos");
Require(!TriggerRule.TryValidate("teste", out _), "regra única rejeita gatilho sem prefixo");
Require(!TriggerRule.TryValidate("/inválido!", out _), "regra única rejeita caractere impossível para o monitor");
Require(
    !KeyboardHookService.TryNormalizeTranslatedCharacter('?', out _),
    "interrogação não é interpretada como prefixo de atalho");
Require(
    KeyboardHookService.TryNormalizeTranslatedCharacter('/', out var slashPrefix) &&
    slashPrefix == '/',
    "barra continua sendo aceita como prefixo");
Require(
    !KeyboardHookService.TryNormalizeTranslatedCharacter(':', out _),
    "dois-pontos não é interpretado como prefixo de atalho");
Require(
    KeyboardHookService.TryNormalizeTranslatedCharacter('A', out var normalizedLetter) &&
    normalizedLetter == 'a',
    "letras traduzidas continuam normalizadas");

var modifierState = new KeyboardModifierState();
modifierState.Update(0xA0, isDown: true);
var shiftKeyboardState = new byte[256];
modifierState.ApplyTo(shiftKeyboardState);
Require(
    (shiftKeyboardState[0x10] & 0x80) != 0 &&
    (shiftKeyboardState[0xA0] & 0x80) != 0 &&
    (shiftKeyboardState[0xA1] & 0x80) == 0,
    "Shift esquerdo observado pelo hook é aplicado à tradução");
modifierState.Update(0xA0, isDown: false);
modifierState.Update(0xA2, isDown: true);
modifierState.Update(0xA5, isDown: true);
var altGrKeyboardState = new byte[256];
modifierState.ApplyTo(altGrKeyboardState);
Require(
    (altGrKeyboardState[0x11] & 0x80) != 0 &&
    (altGrKeyboardState[0x12] & 0x80) != 0 &&
    (altGrKeyboardState[0xA2] & 0x80) != 0 &&
    (altGrKeyboardState[0xA5] & 0x80) != 0,
    "AltGr mantém Control e Alt direito no estado traduzido");
modifierState.Update(0xA2, isDown: false);
modifierState.Update(0xA5, isDown: false);
var releasedKeyboardState = new byte[256];
modifierState.ApplyTo(releasedKeyboardState);
Require(
    releasedKeyboardState.All(value => value == 0),
    "soltar modificadores limpa o estado acompanhado pelo hook");

var maximumTrigger = "/" + new string('a', TriggerRule.MaximumLength - 1);
Require(TriggerRule.TryValidate(maximumTrigger, out _), "gatilho no tamanho máximo");
Require(!TriggerRule.TryValidate(maximumTrigger + "a", out _), "gatilho acima do tamanho máximo");
Require(TriggerRule.ConflictsWith("/TESTE", ["/teste"]), "conflito ignora maiúsculas e minúsculas");
Require(TriggerRule.IsPrefixOfAnother("/at", ["/atendimento"]), "gatilhos com prefixos semelhantes");

var bufferState = new KeyboardBufferState();
bufferState.Append('/', new IntPtr(10), new IntPtr(11));
bufferState.Append('a', new IntPtr(10), new IntPtr(11));
bufferState.Append('t', new IntPtr(10), new IntPtr(11));
Require(bufferState.Text == "/at", "buffer mantém sugestão parcial");
Require(bufferState.TargetChanged(new IntPtr(12), new IntPtr(11)), "troca de janela detectada no buffer");
bufferState.Clear(BufferResetReason.MouseClick);
Require(!bufferState.HasValue && bufferState.LastResetReason == BufferResetReason.MouseClick, "clique limpa buffer");
bufferState.Append(':', new IntPtr(20), new IntPtr(21));
Require(!bufferState.HasValue, "dois-pontos não inicia o buffer");
bufferState.Append('/', new IntPtr(20), new IntPtr(21));
bufferState.Append('x', new IntPtr(20), new IntPtr(21));
Require(bufferState.TargetChanged(new IntPtr(20), new IntPtr(22)), "troca de controle focado limpa buffer");

var planOneTab = ExpansionPlan.Create("Primeiro campo" + TemplateEngine.TabMarker + "Segundo campo");
Require(planOneTab.Count == 2 && planOneTab[0].SendTabAfter && !planOneTab[1].SendTabAfter, "sequência com um Tab");
var planManyTabs = ExpansionPlan.Create(
    "Campo 1" + TemplateEngine.TabMarker + "Campo 2" + TemplateEngine.TabMarker + "Campo 3");
Require(planManyTabs.Count == 3 && planManyTabs.Count(step => step.SendTabAfter) == 2, "sequência com vários Tabs");
Require(Enum.IsDefined(SuggestionConfirmation.Enter) &&
        Enum.IsDefined(SuggestionConfirmation.Tab) &&
        Enum.IsDefined(SuggestionConfirmation.Space) &&
        Enum.IsDefined(SuggestionConfirmation.Click), "formas de confirmação da sugestão");

var expansionGate = new SingleFlightGate();
using (var firstExpansion = expansionGate.TryEnter())
{
    Require(firstExpansion is not null && expansionGate.IsActive, "primeira expansão entra no single-flight");
    Require(expansionGate.TryEnter() is null, "segunda expansão simultânea é ignorada");
}
using var afterExpansion = expansionGate.TryEnter();
Require(!expansionGate.IsActive || afterExpansion is not null, "trava de expansão liberada");
var captureGate = new SingleFlightGate();
try
{
    using var captureLease = captureGate.TryEnter();
    Require(captureGate.TryEnter() is null, "apenas uma captura simultânea");
    throw new InvalidOperationException("falha simulada");
}
catch (InvalidOperationException)
{
    // A liberação ocorre pelo finally implícito de using.
}
Require(!captureGate.IsActive, "trava de captura liberada após exceção");

var debounce = new DebounceGate(TimeSpan.FromMilliseconds(300));
Require(debounce.TryAccept(1_000_000), "primeiro evento do debounce");
Require(!debounce.TryAccept(1_000_001), "evento repetido é bloqueado pelo debounce");
Require(
    (GlobalCaptureShortcutService.ApplyNoRepeat(2) & GlobalCaptureShortcutService.ModNoRepeat) != 0,
    "MOD_NOREPEAT aplicado a hotkeys");

var toolbarScenarios = new[]
{
    (Name: "primário 100% taskbar inferior", Work: new System.Windows.Rect(0, 0, 1920, 1040), Dpi: 1d),
    (Name: "primário 125%", Work: new System.Windows.Rect(0, 0, 2560, 1392), Dpi: 1.25d),
    (Name: "primário 150%", Work: new System.Windows.Rect(0, 0, 2560, 1400), Dpi: 1.5d),
    (Name: "secundário à esquerda DPI diferente", Work: new System.Windows.Rect(-1920, 0, 1920, 1040), Dpi: 1.25d),
    (Name: "secundário acima", Work: new System.Windows.Rect(0, -1080, 1920, 1080), Dpi: 1.5d),
    (Name: "taskbar lateral", Work: new System.Windows.Rect(80, 0, 1840, 1080), Dpi: 1d)
};
foreach (var scenario in toolbarScenarios)
{
    var margin = 12 * scenario.Dpi;
    var selections = new[]
    {
        new System.Windows.Rect(scenario.Work.Left, scenario.Work.Top, 80, 60),
        new System.Windows.Rect(scenario.Work.Right - 80, scenario.Work.Top, 80, 60),
        new System.Windows.Rect(scenario.Work.Left, scenario.Work.Bottom - 80, 80, 80),
        new System.Windows.Rect(scenario.Work.Right - 80, scenario.Work.Bottom - 80, 80, 80),
        new System.Windows.Rect(scenario.Work.Left + 10, scenario.Work.Top + 10, 24, 24),
        new System.Windows.Rect(
            scenario.Work.Left + 4,
            scenario.Work.Top + 4,
            scenario.Work.Width - 8,
            scenario.Work.Height - 8)
    };
    foreach (var selection in selections)
    {
        var availableWidth = scenario.Work.Width - (margin * 2);
        var naturalWidth = 900 * scenario.Dpi;
        var finalWidth = Math.Min(naturalWidth, availableWidth);
        var rows = Math.Max(1, (int)Math.Ceiling(naturalWidth / availableWidth));
        var finalHeight = (72 + ((rows - 1) * 48)) * scenario.Dpi;
        var placement = ToolbarPlacementCalculator.Calculate(
            selection,
            scenario.Work,
            new System.Windows.Size(finalWidth, finalHeight),
            naturalWidth,
            dpiScale: scenario.Dpi);
        Require(
            placement.Bounds.Left >= scenario.Work.Left + margin - .01 &&
            placement.Bounds.Top >= scenario.Work.Top + margin - .01 &&
            placement.Bounds.Right <= scenario.Work.Right - margin + .01 &&
            placement.Bounds.Bottom <= scenario.Work.Bottom - margin + .01,
            $"retângulo completo da barra permanece na área útil: {scenario.Name}");
    }
}
var narrowPlacement = ToolbarPlacementCalculator.Calculate(
    new System.Windows.Rect(430, 350, 48, 48),
    new System.Windows.Rect(0, 0, 480, 440),
    new System.Windows.Size(456, 184),
    naturalWidth: 900);
Require(
    narrowPlacement.Bounds.Width == 456 &&
    narrowPlacement.Mode == ToolbarLayoutMode.Compact &&
    narrowPlacement.ExpectedRows == 2 &&
    narrowPlacement.Side is ToolbarPlacementSide.Above or ToolbarPlacementSide.InsideBottom,
    "barra larga no canto inferior direito usa modo compacto e posição vertical segura");

var toolSelection = new CaptureToolSelection(CaptureAnnotationKind.Arrow);
Require(toolSelection.IsSelected(CaptureAnnotationKind.Arrow), "ferramenta inicial selecionada");
toolSelection.Select(CaptureAnnotationKind.Pencil);
Require(
    toolSelection.IsSelected(CaptureAnnotationKind.Pencil) &&
    !toolSelection.IsSelected(CaptureAnnotationKind.Arrow),
    "somente uma ferramenta de desenho permanece selecionada");
Require(
    CaptureToolbarLayoutPolicy.ShouldUseCompactMode(540) &&
    !CaptureToolbarLayoutPolicy.ShouldUseCompactMode(680),
    "barra alterna entre modo compacto e normal sem corte");
Require(
    CaptureMotion.Duration(animationsEnabled: false, 160) == TimeSpan.Zero &&
    CaptureMotion.Duration(animationsEnabled: true, 160) == TimeSpan.FromMilliseconds(160),
    "animações respeitam a preferência do Windows");
Require(
    NotoEmojiCatalog.Items.Count == 3972 && NotoEmojiCatalog.QuickItems.Count == 36 &&
    NotoEmojiCatalog.Items.Select(item => item.Value).Distinct().Count() == NotoEmojiCatalog.Items.Count &&
    NotoEmojiCatalog.Categories.Count == 9,
    "catálogo Noto completo contém 3972 opções únicas, 9 categorias e 36 acessos rápidos");
Require(
    NotoEmojiCatalog.Items.All(NotoEmojiCatalog.HasAsset),
    "todos os emojis Noto possuem PNG incorporado");
Require(NotoEmojiCatalog.TryGet("❤️", out var qualifiedHeart) &&
        NotoEmojiCatalog.TryGet("❤", out var plainHeart) && qualifiedHeart == plainHeart &&
        NotoEmojiCatalog.QuickItems.All(item => NotoEmojiCatalog.TryGet(item.Value, out _)),
    "sequências legadas/qualificadas e todos os acessos rápidos resolvem o mesmo catálogo completo");
Require(SlashText.Views.CaptureEmojiPicker.Search("gato", "Animais e natureza").Count > 0 &&
        SlashText.Views.CaptureEmojiPicker.Search("", "Bandeiras").All(i => i.Category == "Bandeiras") &&
        NotoEmojiCatalog.TryGet("👍🏽", out _) && NotoEmojiCatalog.TryGet("🇧🇷", out _),
    "catálogo pesquisável inclui animais, categorias, variantes de pele e bandeiras");

foreach (var anchor in new[]
         {
             new System.Windows.Rect(0, 0, 36, 36),
             new System.Windows.Rect(1884, 0, 36, 36),
             new System.Windows.Rect(0, 1004, 36, 36),
             new System.Windows.Rect(1884, 1004, 36, 36)
         })
{
    var popover = ToolbarPlacementCalculator.Calculate(
        anchor,
        new System.Windows.Rect(0, 0, 1920, 1040),
        new System.Windows.Size(340, 420),
        naturalWidth: 340,
        gap: 8,
        dpiScale: 1);
    Require(
        popover.Bounds.Left >= 12 && popover.Bounds.Top >= 12 &&
        popover.Bounds.Right <= 1908 && popover.Bounds.Bottom <= 1028,
        "popover contextual permanece dentro da área útil nos quatro cantos");
}

var captureMenuBelow = AnchoredPopoverPlacementCalculator.Calculate(
    new System.Windows.Rect(120, 420, 118, 40),
    new System.Windows.Rect(0, 0, 1920, 1040),
    new System.Windows.Size(220, 144));
Require(
    captureMenuBelow.Side == AnchoredPopoverSide.Below &&
    captureMenuBelow.Bounds.Left == 120 &&
    captureMenuBelow.Bounds.Top == 468,
    "menu Capturar abre abaixo e alinhado à esquerda do botão dividido");

var captureMenuAbove = AnchoredPopoverPlacementCalculator.Calculate(
    new System.Windows.Rect(1640, 940, 118, 40),
    new System.Windows.Rect(0, 0, 1920, 1040),
    new System.Windows.Size(220, 144));
Require(
    captureMenuAbove.Side == AnchoredPopoverSide.Above &&
    captureMenuAbove.Bounds.Left == 1640 &&
    captureMenuAbove.Bounds.Bottom == 932,
    "menu Capturar inverte para cima somente quando falta espaço abaixo");

var captureMenuClamped = AnchoredPopoverPlacementCalculator.Calculate(
    new System.Windows.Rect(1870, 420, 42, 40),
    new System.Windows.Rect(0, 0, 1920, 1040),
    new System.Windows.Size(220, 144));
Require(
    captureMenuClamped.Bounds.Right == 1908,
    "menu Capturar permanece inteiro na área útil junto à borda direita");

var snippetsFile = Path.Combine(root, "snippets.md");
var backups = Path.Combine(root, "backups");
try
{
    // Exercise the real WPF preview and clipboard payload on an STA thread,
    // without changing the runner's system clipboard or opening production UI.
    Directory.CreateDirectory(root);
await CaptureHistoryTrustChecks.RunAsync(root);
await RestoreRecoveryChecks.RunAsync(root);
await BackupCompatibilityChecks.RunAsync(root);
    await ExternalInputBudgetChecks.RunAsync(root);
    await RetentionPrivacyChecks.RunAsync(root);
    ClipboardPrivacyChecks.Run(root);
    await ClipboardNativeChecks.RunChildAsync(root);
    Exception? workbenchFailure = null;
    var workbenchThread = new Thread(() =>
    {
        try
        {
            CaptureEditorPerformanceChecks.Run();
            CaptureCustomStampChecks.Run();
            using var bitmap = new System.Drawing.Bitmap(800, 400);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.CornflowerBlue);
            }
            using var editor = new SlashText.Views.CaptureWorkbenchEditor();
            editor.LoadImage(bitmap);
            void LayoutPreview()
            {
                editor.Measure(new System.Windows.Size(600, 320));
                editor.Arrange(new System.Windows.Rect(0, 0, 600, 320));
                editor.UpdateLayout();
            }
            LayoutPreview();
            var viewport = (System.Windows.Controls.ScrollViewer)editor.Content;
            var preview = (System.Windows.Controls.Viewbox)viewport.Content;
            editor.SetZoom(1);
            LayoutPreview();
            var fittedWidth = preview.Width;
            Require(double.IsFinite(fittedWidth) && fittedWidth > 0,
                "prévia tem uma dimensão ajustada e limitada ao viewport");
            foreach (var zoom in new[] { .75, 1d, 1.25 })
            {
                editor.SetZoom(zoom);
                LayoutPreview();
                Require(Math.Abs(preview.Width - fittedWidth * zoom) < .01,
                    $"zoom {zoom:P0} muda a dimensão visível sem ser compensado pelo Viewbox");
                using var output = editor.Render();
                Require(output.Width == 800 && output.Height == 400 &&
                        output.GetPixel(200, 100).ToArgb() == bitmap.GetPixel(200, 100).ToArgb(),
                    "zoom não redimensiona nem altera os pixels da imagem exportada");
            }
            RequireThrows<ArgumentOutOfRangeException>(() => editor.SetZoom(double.NaN),
                "zoom inválido é rejeitado");
            editor.InsertStamp("👍", new System.Windows.Point(400, 200));
            var previewImage = (System.Windows.Controls.Image)((System.Windows.Controls.Grid)preview.Child).Children[0];
            Require(previewImage.Source is System.Windows.Media.Imaging.BitmapSource,
                "prévia usa a mesma composição raster do documento, incluindo emote Noto");
            using (var stampOutput = editor.Render())
            using (var expectedStamp = CaptureAnnotationRenderer.Render(bitmap,
                [new CaptureAnnotation { Kind = CaptureAnnotationKind.Stamp, Start = new System.Windows.Point(400, 200),
                    Text = "👍", Size = 48 }], 800, 400))
            {
                Require(stampOutput.GetPixel(400, 200).ToArgb() == expectedStamp.GetPixel(400, 200).ToArgb(),
                    "editor simples exporta o mesmo emote do renderizador compartilhado");
                var pixel = new byte[4];
                ((System.Windows.Media.Imaging.BitmapSource)previewImage.Source).CopyPixels(new System.Windows.Int32Rect(400, 200, 1, 1), pixel, 4, 0);
                var expectedPixel = stampOutput.GetPixel(400, 200);
                Require(pixel[0] == expectedPixel.B && pixel[1] == expectedPixel.G && pixel[2] == expectedPixel.R,
                    "emote da prévia e do arquivo tem os mesmos pixels, sem glifo do sistema");
            }
            var session = editor.Document;
            editor.MarkSaved();
            editor.ResizeImage(400, 200);
            Require(editor.HasUnsavedChanges && ReferenceEquals(session, editor.Document), "redimensionar não recria a sessão");
            editor.Undo();
            Require(!editor.HasUnsavedChanges && editor.CanRedo && editor.Document!.Dimensions.Width == 800,
                "salvar preserva undo/redo e checkpoint");
            editor.Redo();
            Require(editor.Document!.Dimensions.Width == 400, "refazer restaura dimensões");
            editor.DiscardChanges();
            Require(!editor.HasUnsavedChanges && editor.Document!.Dimensions.Width == 800, "descartar volta à última versão salva");
            Require(SlashText.Views.CaptureEmojiPicker.Search("coracao").Count > 0 &&
                    SlashText.Views.CaptureEmojiPicker.Search("emote inexistente xyz").Count == 0,
                "busca do seletor Noto ignora acentos e expõe resultado vazio");
            RequireThrows<ArgumentException>(() => NotoEmojiCatalog.CreateBitmap("emoji-inexistente"),
                "emoji desconhecido não vira silenciosamente um coração");
            foreach (var extension in new[] { ".png", ".jpg", ".gif" })
            {
                var path = Path.Combine(root, "clipboard-probe" + extension);
                var format = extension switch
                {
                    ".jpg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                    ".gif" => System.Drawing.Imaging.ImageFormat.Gif,
                    _ => System.Drawing.Imaging.ImageFormat.Png
                };
                bitmap.Save(path, format);
                var payload = CaptureService.CreateClipboardData(path);
                Require(payload.ContainsImage() && payload.GetImage().PixelWidth == 800 &&
                        payload.GetImage().PixelHeight == 400,
                    $"Recentes disponibiliza imagem colável para {extension}");
                Require(payload.ContainsFileDropList() && payload.GetFileDropList()[0] == path,
                    $"Recentes preserva o arquivo original de {extension}, inclusive GIF animado");
                using var unlockedFile = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            var videoPath = Path.Combine(root, "clipboard-probe.mp4");
            File.WriteAllBytes(videoPath, [0]);
            var videoPayload = CaptureService.CreateClipboardData(videoPath);
            Require(videoPayload.ContainsFileDropList() && !videoPayload.ContainsImage(),
                "copiar MP4 continua copiando o arquivo, sem tentar decodificar como imagem");
            RequireThrows<FileNotFoundException>(
                () => CaptureService.CreateClipboardData(Path.Combine(root, "missing.png")),
                "arquivo ausente é informado sem substituir o clipboard");
        }
        catch (Exception exception)
        {
            workbenchFailure = exception;
        }
    });
    workbenchThread.SetApartmentState(ApartmentState.STA);
    workbenchThread.Start();
    workbenchThread.Join();
    if (workbenchFailure is not null)
    {
        throw new InvalidOperationException("Smoke de zoom/clipboard da Captura falhou.", workbenchFailure);
    }

    using (var original = new System.Drawing.Bitmap(240, 160))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(original)) graphics.Clear(System.Drawing.Color.White);
        using var document = new CaptureEditorDocument(original);
        var points = new List<System.Windows.Point> { new(20, 20), new(200, 20) };
        document.AddAnnotation(new CaptureAnnotation { Kind = CaptureAnnotationKind.Pencil, Points = points,
            OutlineArgb = System.Drawing.Color.Red.ToArgb(), Thickness = 8 });
        points.Clear();
        using (var result = document.Render())
            Require(result.GetPixel(100, 20).R > 230 && result.GetPixel(100, 20).G < 50 && original.GetPixel(100, 20).G == 255,
                "comando copia pontos mutáveis e preserva a fonte original");
        document.MarkSaved();
        Require(!document.Crop(new System.Windows.Rect(0, 0, 7, 7)), "recorte pequeno não altera o documento");
        Require(document.Crop(new System.Windows.Rect(new System.Windows.Point(220, 140), new System.Windows.Point(20, 20))),
            "recorte aceita arraste inverso");
        Require(document.Dimensions == new System.Drawing.Size(200, 120), "recorte usa pixels de imagem");
        document.Resize(100, 60);
        document.AddAnnotation(new CaptureAnnotation { Kind = CaptureAnnotationKind.Rectangle,
            Start = new System.Windows.Point(10, 10), End = new System.Windows.Point(35, 35),
            FillArgb = System.Drawing.Color.Blue.ToArgb(), OutlineArgb = null });
        using (var result = document.Render())
            Require(result.Size == new System.Drawing.Size(100, 60) && result.GetPixel(20, 20).B == 255 && result.GetPixel(20, 20).R == 0,
                "anotações posteriores à geometria usam coordenadas do novo resultado");
        document.Undo(); document.Undo(); document.Undo();
        Require(!document.HasUnsavedChanges && document.CanRedo && document.Dimensions == original.Size,
            "undo de anotações e geometria chega ao checkpoint salvo");
        document.AddAnnotation(new CaptureAnnotation { Kind = CaptureAnnotationKind.Text, Start = new(25, 60),
            Text = "Texto", FontFamily = "Georgia", Size = 24, Bold = false, Italic = true });
        Require(document.HasUnsavedChanges && !document.CanRedo, "bifurcação invalida redo e não se confunde com checkpoint de mesmo tamanho");
        document.DiscardChanges();
        Require(document.OperationCount == 1 && !document.HasUnsavedChanges, "descartar restaura checkpoint, não o arquivo original");
        Require(!CaptureEditorDocument.ValidSize(32000, 32000) && !CaptureEditorDocument.ValidSize(7, 8) && CaptureEditorDocument.ValidSize(8000, 8000),
            "redimensionamento tem limite de memória e dimensões");
        document.Resize(120, 80);
        using var resized = document.Render();
        Require(resized.GetPixel(119, 79).A == 255, "resize não introduz bordas transparentes");
    }
    // Editing an older object must retain its identity and creation coordinate
    // space across later geometry, cache invalidation, undo and save checkpoints.
    using (var source = new System.Drawing.Bitmap(240, 160))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(source)) graphics.Clear(System.Drawing.Color.White);
        using var objects = new CaptureEditorDocument(source);
        objects.AddAnnotation(new CaptureAnnotation { Kind = CaptureAnnotationKind.Rectangle, Start = new(40, 40), End = new(80, 80),
            FillArgb = System.Drawing.Color.Red.ToArgb(), OutlineArgb = null });
        var id = objects.EditableAnnotations().Single().Id;
        using (var cached = objects.Render()) Require(cached.GetPixel(50, 50).R == 255, "primeira renderização do objeto");
        objects.Crop(new System.Windows.Rect(20, 20, 200, 120)); objects.Resize(100, 120);
        var transformed = objects.EditableAnnotations().Single();
        Require(transformed.Id == id && transformed.Annotation.Start == new System.Windows.Point(10, 20) &&
            transformed.ScaleX == .5 && transformed.ScaleY == 1, "objeto conserva identidade após recorte e resize não proporcional");
        objects.MarkSaved();
        Require(objects.MoveAnnotation(id, 10, 10), "mover objeto em coordenadas atuais");
        var moved = objects.EditableAnnotations().Single();
        Require(moved.Annotation.Start == new System.Windows.Point(20, 30), "movimento inverte transformação da sessão");
        Require(objects.HitTestAnnotation(new(25, 35))?.Id == id, "seleção encontra objeto transformado");
        Require(objects.UpdateAnnotation(id, moved.Annotation with { FillArgb = System.Drawing.Color.Blue.ToArgb() }), "alterar propriedades de objeto existente");
        using (var result = objects.Render())
            Require(result.GetPixel(25, 35).B > 240 && result.GetPixel(25, 35).R < 20 && result.GetPixel(12, 22).R > 240 && result.GetPixel(12, 22).G > 240,
                "cache reconstrói composição editada sem manter a posição antiga");
        objects.Undo(); Require(objects.EditableAnnotations().Single().Annotation.FillArgb == System.Drawing.Color.Red.ToArgb(), "undo restaura estilo");
        objects.Undo(); Require(!objects.HasUnsavedChanges, "undo do movimento chega ao checkpoint");
        objects.Redo(); objects.Redo(); Require(objects.DeleteAnnotation(id) && objects.EditableAnnotations().Count == 0, "excluir objeto");
        objects.Undo(); Require(objects.EditableAnnotations().Single().Id == id, "undo da exclusão recupera identidade");
        objects.DiscardChanges(); Require(objects.EditableAnnotations().Single().Annotation.Start == new System.Windows.Point(10, 20) && !objects.HasUnsavedChanges, "descartar restaura objetos do checkpoint");
        Require(!objects.DeleteAnnotation(-1) && !objects.MoveAnnotation(id, double.NaN, 0), "operações inválidas não modificam objetos");
        Require(source.GetPixel(50, 50).G == 255, "edição de objetos preserva fonte original");
        objects.AddAnnotation(new CaptureAnnotation { Kind = CaptureAnnotationKind.Text, Start = new(15, 25), Text = "Texto", FontFamily = "Arial", Size = 20 });
        var text = objects.EditableAnnotations().Last();
        Require(objects.UpdateAnnotation(text.Id, text.Annotation with { Text = "Novo texto", Size = 32, Bold = true }) && objects.EditableAnnotations().Last().Annotation.Text == "Novo texto", "texto pode ser alterado após inserção");
        Require(!objects.CanRedo, "alteração nova invalida ramo redo antigo");
    }
    var historyFixture = Enumerable.Range(0, 75).Select(i => new CaptureRecord {
        FilePath = $"regiao-projeto-{i:000}.png", Type = "regiao", CreatedAt = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero).AddMinutes(i)
    }).ToArray();
    Require(CaptureHistoryQuery.Filter(historyFixture, "all", "").Count == 75, "histórico completo não é limitado aos 40 recentes");
    Require(CaptureHistoryQuery.Filter(historyFixture, "regiao", "REGIÃO 09/10/2026").Count == 75, "busca combina tipo e data sem diferença de acentos ou caixa");
    Require(CaptureHistoryQuery.Filter(historyFixture, "all", "projeto-074").Single().FilePath.EndsWith("074.png"), "busca pelo nome do arquivo");
    Require(CaptureHistoryQuery.Filter(historyFixture, "video", "").Count == 0 && CaptureHistoryQuery.Filter(historyFixture, "all", "outubro").Count == 75, "filtro de mídia e mês em português");
    Require(CaptureHistoryQuery.Filter(historyFixture, "all", "")[0] == historyFixture[74], "resultados ordenam mais recentes primeiro");
    var pilotExe = Path.Combine(root, "pilot-isolation");
    var officialData = Path.Combine(root, "pilot-local", "SlashDesk");
    Directory.CreateDirectory(officialData);
    await File.WriteAllTextAsync(Path.Combine(officialData, "settings.json"), "{\"theme\":\"Dark\"}");
    Directory.CreateDirectory(Path.Combine(pilotExe, "SlashDeskData"));
    await File.WriteAllTextAsync(Path.Combine(pilotExe, "SlashDeskData", "snippets.md"), "official-portable-sentinel");
    var pilotEnvironment = new AppDataEnvironment(DistributionMode.Installed, pilotExe, Path.Combine(root, "pilot-local"), isCapturePilot: true);
    var pilotLayout = new DataMigrationService().EnsureLayout(pilotEnvironment);
    Require(pilotEnvironment.IsPortable && pilotEnvironment.DataDirectory == Path.Combine(pilotExe, "SlashDeskPilotData") &&
            !pilotLayout.Migrated && pilotLayout.SourceDirectory is null && pilotLayout.BackupPath is null &&
            !File.Exists(Path.Combine(pilotEnvironment.DataDirectory, "settings.json")) &&
            !File.Exists(Path.Combine(pilotEnvironment.DataDirectory, "snippets.md")),
        "piloto não migra nem importa dados da instalação oficial ou do portátil adjacente");
    Require(await File.ReadAllTextAsync(Path.Combine(pilotExe, "SlashDeskData", "snippets.md")) == "official-portable-sentinel" &&
            await File.ReadAllTextAsync(Path.Combine(officialData, "settings.json")) == "{\"theme\":\"Dark\"}",
        "isolamento preserva arquivos oficiais byte a byte");

    var repository = new SnippetMarkdownRepository(snippetsFile, backups);
    var snippet = new Snippet
    {
        Name = "Teste",
        Trigger = "/teste",
        Category = "Geral",
        Content = "Primeiro"
    };

    await repository.SaveAsync([snippet]);
    snippet.Content = "Segundo";
    await repository.SaveAsync([snippet]);
    snippet.Content = "Terceiro";
    await repository.SaveAsync([snippet]);

    var loaded = await repository.LoadAsync();
    Require(loaded.Count == 1 && loaded[0].Content == "Terceiro", "persistência Markdown");

    var colonSnippet = new Snippet
    {
        Name = "Dois pontos legado",
        Trigger = ":teste",
        Category = "Geral",
        Content = "Preservado sem ativar",
        HasLegacyIncompatibleTrigger = true
    };
    await repository.SaveAsync([snippet, colonSnippet]);
    loaded = await repository.LoadAsync();
    Require(
        loaded.Any(item =>
            item.Trigger == ":teste" &&
            item.HasLegacyIncompatibleTrigger),
        "gatilho antigo com dois-pontos é preservado como legado incompatível");

    var legacySnippet = new Snippet
    {
        Name = "Legado preservado",
        Trigger = "/legado.incompatível",
        Category = "Geral",
        Content = "Preservar sem ativar",
        HasLegacyIncompatibleTrigger = true
    };
    await repository.SaveAsync([snippet, colonSnippet, legacySnippet]);
    loaded = await repository.LoadAsync();
    Require(
        loaded.Any(item => item.Trigger == legacySnippet.Trigger && item.HasLegacyIncompatibleTrigger),
        "gatilho legado incompatível é preservado e sinalizado");

    var textBlazeFile = Path.Combine(root, "textblaze.json");
    await File.WriteAllTextAsync(
        textBlazeFile,
        """
        {
          "version": 7,
          "folders": [{
            "name": "Atendimento",
            "snippets": [{
              "name": "Resposta diária",
              "shortcut": "/diario",
              "type": "html",
              "text": "Data: {time: DD/MM/YYYY; shift=-1D} {key: tab}Pronto"
            }]
          }]
        }
        """);
    var importService = new SnippetImportService();
    var textBlazeImport = await importService.ImportAsync(
        textBlazeFile,
        SnippetImportSource.TextBlaze);
    Require(
        textBlazeImport.Snippets.Count == 1 &&
        textBlazeImport.Snippets[0].Category == "Atendimento",
        "importa pastas e atalhos do Text Blaze");
    Require(
        textBlazeImport.Snippets[0].Content.Contains(
            "{{data:-1d|dd/MM/yyyy}} {{tab}}",
            StringComparison.Ordinal),
        "converte data e Tab do Text Blaze");

    var espansoFile = Path.Combine(root, "base.yml");
    await File.WriteAllTextAsync(
        espansoFile,
        """
        matches:
          - trigger: ":ola"
            label: "Saudação"
            replace: |
              Olá!
              Como posso ajudar?
          - triggers: [":obg", ":thanks"]
            replace: "Obrigado!"
          - trigger: "/ola"
            replace: "Entrada duplicada"
        """);
    var espansoImport = await importService.ImportAsync(
        espansoFile,
        SnippetImportSource.Espanso);
    Require(
        espansoImport.Snippets.Count == 3 &&
        espansoImport.Snippets.Any(item => item.Trigger == "/ola") &&
        espansoImport.Snippets.Any(item => item.Trigger == "/obg") &&
        espansoImport.Snippets.Any(item => item.Trigger == "/thanks"),
        "importa Espanso convertendo todos os prefixos para barra");
    Require(
        espansoImport.Warnings.Count(item =>
            item.Contains("prefixo ':' convertido", StringComparison.Ordinal)) == 3 &&
        espansoImport.Warnings.Any(item =>
            item.Contains("duplicado", StringComparison.OrdinalIgnoreCase)),
        "importação informa conversões e conflito após normalizar prefixos");

    var usageFile = Path.Combine(root, "usage.json");
    var settingsFile = Path.Combine(root, "settings.json");
    await File.WriteAllTextAsync(settingsFile, """{"theme":"System"}""");
    await File.WriteAllTextAsync(
        usageFile,
        System.Text.Json.JsonSerializer.Serialize(new List<UsageRecord>
        {
            new() { SnippetId = snippet.Id, Count = 3 }
        }));
    var usage = new UsageService(usageFile);
    await usage.LoadAsync();
    Require(usage.Records.Count == 1 && usage.Records[0].Count == 3, "migração de estatísticas antigas");
    await usage.RecordQuickAccentAsync('á');
    var reloadedUsage = new UsageService(usageFile);
    await reloadedUsage.LoadAsync();
    Require(reloadedUsage.QuickAccent.Count == 1, "estatística do Acento Rápido");
    Require(
        reloadedUsage.QuickAccent.Characters.GetValueOrDefault("á") == 1,
        "ranking de caracteres acentuados");

    var backupService = new BackupService(
        backups,
        [snippetsFile, settingsFile, usageFile]);
    backupService.CreateDailySnapshot();
    backupService.CreateDailySnapshot();
    var backupFiles = Directory.GetFiles(backups, "SlashDesk-backup-*.zip");
    Require(backupFiles.Length == 1, "um backup consolidado por dia");
    using (var archive = ZipFile.OpenRead(backupFiles[0]))
    {
        Require(
            archive.Entries.Select(item => item.Name).Order()
                .SequenceEqual(["backup-manifest.json", "settings.json", "snippets.md", "usage.json"]),
            "backup contém manifesto, atalhos, preferências e estatísticas");
        var manifestEntry = archive.GetEntry("backup-manifest.json");
        Require(manifestEntry is not null, "backup contém manifesto único");
        using var manifest = System.Text.Json.JsonDocument.Parse(manifestEntry!.Open());
        var manifestFiles = manifest.RootElement.GetProperty("files")
            .EnumerateArray()
            .ToList();
        Require(
            manifest.RootElement.GetProperty("schemaVersion").GetInt32() == 2 &&
            manifestFiles.Count == 3 &&
            manifestFiles.All(item =>
                item.GetProperty("size").GetInt64() >= 0 &&
                item.GetProperty("sha256").GetString()?.Length == 64),
            "manifesto v2 registra tamanho e SHA-256 de cada arquivo");
    }
    BackupService.ValidateSnapshot(backupFiles[0]);
    var manualBackup = backupService.CreateManualSnapshot();
    Require(
        File.Exists(manualBackup) && backupService.ListSnapshots().Count == 2,
        "backup manual e listagem de cópias");

    var assetSource = Path.Combine(root, "backup-assets");
    var nestedAssetDirectory = Path.Combine(assetSource, "imagens", "nested");
    Directory.CreateDirectory(nestedAssetDirectory);
    var nestedAsset = Path.Combine(nestedAssetDirectory, "logo.png");
    await File.WriteAllBytesAsync(nestedAsset, [1, 2, 3, 4, 5, 6]);
    var assetBackups = Path.Combine(root, "asset-backups");
    var assetBackupService = new BackupService(
        assetBackups,
        [snippetsFile],
        assetSource);
    var assetBackup = assetBackupService.CreateManualSnapshot();
    using (var archive = ZipFile.OpenRead(assetBackup))
    {
        var assetEntry = archive.GetEntry("assets/imagens/nested/logo.png");
        var manifestEntry = archive.GetEntry("backup-manifest.json");
        Require(
            assetEntry is not null && manifestEntry is not null,
            "asset aninhado e manifesto são incluídos");
        using var manifest = System.Text.Json.JsonDocument.Parse(manifestEntry!.Open());
        var recordedAsset = manifest.RootElement.GetProperty("files")
            .EnumerateArray()
            .Single(item =>
                item.GetProperty("path").GetString() ==
                "assets/imagens/nested/logo.png");
        var expectedAssetHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    await File.ReadAllBytesAsync(nestedAsset)))
            .ToLowerInvariant();
        Require(
            recordedAsset.GetProperty("size").GetInt64() == 6 &&
            recordedAsset.GetProperty("sha256").GetString() == expectedAssetHash,
            "manifesto registra tamanho e hash dos bytes arquivados do asset");
    }
    BackupService.ValidateSnapshot(assetBackup);
    Require(
        Directory.GetFiles(assetBackups, "*.tmp").Length == 0,
        "backup validado é publicado sem temporários residuais");

    var tamperedBackup = Path.Combine(root, "tampered-backup.zip");
    File.Copy(manualBackup, tamperedBackup);
    using (var archive = ZipFile.Open(tamperedBackup, ZipArchiveMode.Update))
    {
        var entry = archive.GetEntry("settings.json")
            ?? throw new InvalidOperationException("settings ausente no teste");
        entry.Delete();
        var changed = archive.CreateEntry("settings.json");
        using var writer = new StreamWriter(changed.Open());
        writer.Write("{\"theme\":\"adulterado\"}");
    }
    RequireThrows<InvalidDataException>(
        () => BackupService.ValidateSnapshot(tamperedBackup),
        "adulteração de backup é detectada por tamanho ou hash");

    var duplicateBackup = Path.Combine(root, "duplicate-backup.zip");
    File.Copy(manualBackup, duplicateBackup);
    using (var archive = ZipFile.Open(duplicateBackup, ZipArchiveMode.Update))
    {
        var duplicateEntry = archive.CreateEntry("settings.json");
        using var writer = new StreamWriter(duplicateEntry.Open());
        writer.Write("{}");
    }
    RequireThrows<InvalidDataException>(
        () => BackupService.ValidateSnapshot(duplicateBackup),
        "entrada duplicada no ZIP é rejeitada");

    var legacyBackup = Path.Combine(root, "legacy-schema-1.zip");
    using (var archive = ZipFile.Open(legacyBackup, ZipArchiveMode.Create))
    {
        var snippetsEntry = archive.CreateEntry("snippets.md");
        using (var writer = new StreamWriter(snippetsEntry.Open()))
        {
            writer.Write("# backup legado");
        }
        var manifestEntry = archive.CreateEntry("backup-manifest.json");
        using var manifestWriter = new StreamWriter(manifestEntry.Open());
        manifestWriter.Write(
            "{\"schemaVersion\":1,\"files\":[\"snippets.md\"]}");
    }
    BackupService.ValidateSnapshot(legacyBackup);

    var restorePackageData = Path.Combine(root, "restore-package-data");
    var restorePackageAssets = Path.Combine(restorePackageData, "assets");
    Directory.CreateDirectory(restorePackageAssets);
    var packageSnippets = Path.Combine(restorePackageData, "snippets.md");
    var packageSettings = Path.Combine(restorePackageData, "settings.json");
    await File.WriteAllTextAsync(packageSnippets, "atalhos restaurados");
    await File.WriteAllTextAsync(packageSettings, "{\"theme\":\"Light\"}");
    await File.WriteAllTextAsync(
        Path.Combine(restorePackageAssets, "novo.png"),
        "asset restaurado");
    var restorePackages = Path.Combine(root, "restore-packages");
    var restorePackageService = new BackupService(
        restorePackages,
        [packageSnippets, packageSettings],
        restorePackageAssets,
        restorePackageData);
    var restorePackage = restorePackageService.CreateManualSnapshot();

    var activeRestoreData = Path.Combine(root, "active-restore-data");
    var activeRestoreAssets = Path.Combine(activeRestoreData, "assets");
    Directory.CreateDirectory(activeRestoreAssets);
    var activeSnippets = Path.Combine(activeRestoreData, "snippets.md");
    var activeSettings = Path.Combine(activeRestoreData, "settings.json");
    await File.WriteAllTextAsync(activeSnippets, "atalhos atuais");
    await File.WriteAllTextAsync(activeSettings, "{\"theme\":\"Dark\"}");
    await File.WriteAllTextAsync(
        Path.Combine(activeRestoreAssets, "antigo.png"),
        "asset atual");
    var restoreSafetyBackups = Path.Combine(
        activeRestoreData,
        "Backups");
    var restoreService = new BackupService(
        restoreSafetyBackups,
        [activeSnippets, activeSettings],
        activeRestoreAssets,
        activeRestoreData);
    restoreService.RestoreSnapshot(restorePackage);
    Require(
        await File.ReadAllTextAsync(activeSnippets) ==
            "atalhos restaurados" &&
        await File.ReadAllTextAsync(activeSettings) ==
            "{\"theme\":\"Light\"}" &&
        File.Exists(Path.Combine(activeRestoreAssets, "novo.png")) &&
        !File.Exists(Path.Combine(activeRestoreAssets, "antigo.png")) &&
        Directory.GetFiles(
            restoreSafetyBackups,
            "SlashDesk-backup-before-restore-*.zip").Length == 1,
        "restauração aplica arquivos e substitui assets após criar backup de segurança");
    Require(
        Directory.GetDirectories(
            restoreSafetyBackups,
            ".restore-*").Length == 0,
        "restauração concluída remove staging e rollback");

    await File.WriteAllTextAsync(activeSnippets, "atalhos antes da falha");
    await File.WriteAllTextAsync(activeSettings, "{\"theme\":\"Safe\"}");
    Directory.Delete(activeRestoreAssets, recursive: true);
    Directory.CreateDirectory(activeRestoreAssets);
    await File.WriteAllTextAsync(
        Path.Combine(activeRestoreAssets, "seguro.png"),
        "asset antes da falha");
    restoreService.BeforeRestoreApply = (step, _) =>
    {
        if (step == 1)
        {
            throw new IOException("falha simulada durante aplicação");
        }
    };
    RequireThrows<IOException>(
        () => restoreService.RestoreSnapshot(restorePackage),
        "falha intermediária da restauração é propagada");
    restoreService.BeforeRestoreApply = null;
    Require(
        await File.ReadAllTextAsync(activeSnippets) ==
            "atalhos antes da falha" &&
        await File.ReadAllTextAsync(activeSettings) ==
            "{\"theme\":\"Safe\"}" &&
        File.Exists(Path.Combine(activeRestoreAssets, "seguro.png")) &&
        !File.Exists(Path.Combine(activeRestoreAssets, "novo.png")),
        "rollback restaura integralmente arquivos e assets anteriores");

    var traversalBackup = Path.Combine(root, "restore-traversal.zip");
    using (var archive = ZipFile.Open(
               traversalBackup,
               ZipArchiveMode.Create))
    {
        var manifestEntry = archive.CreateEntry("backup-manifest.json");
        using (var writer = new StreamWriter(manifestEntry.Open()))
        {
            writer.Write(
                "{\"schemaVersion\":1,\"files\":[\"snippets.md\"]}");
        }
        var snippetsEntry = archive.CreateEntry("snippets.md");
        using (var writer = new StreamWriter(snippetsEntry.Open()))
        {
            writer.Write("não deve ser aplicado");
        }
        var traversalEntry = archive.CreateEntry("../escape.txt");
        using var traversalWriter = new StreamWriter(traversalEntry.Open());
        traversalWriter.Write("inseguro");
    }
    RequireThrows<InvalidDataException>(
        () => restoreService.RestoreSnapshot(traversalBackup),
        "path traversal é bloqueado antes de alterar dados ativos");
    Require(
        await File.ReadAllTextAsync(activeSnippets) ==
            "atalhos antes da falha" &&
        !File.Exists(Path.Combine(root, "escape.txt")),
        "backup inseguro não modifica dados nem escreve fora do staging");

    var symlinkBackup = Path.Combine(root, "restore-symlink.zip");
    using (var archive = ZipFile.Open(
               symlinkBackup,
               ZipArchiveMode.Create))
    {
        var manifestEntry = archive.CreateEntry("backup-manifest.json");
        using (var writer = new StreamWriter(manifestEntry.Open()))
        {
            writer.Write(
                "{\"schemaVersion\":1,\"files\":[\"snippets.md\"]}");
        }
        var snippetsEntry = archive.CreateEntry("snippets.md");
        using (var writer = new StreamWriter(snippetsEntry.Open()))
        {
            writer.Write("não deve ser aplicado");
        }
        var linkEntry = archive.CreateEntry("assets/link");
        linkEntry.ExternalAttributes = 0xA000 << 16;
        using var linkWriter = new StreamWriter(linkEntry.Open());
        linkWriter.Write("destino");
    }
    RequireThrows<InvalidDataException>(
        () => restoreService.RestoreSnapshot(symlinkBackup),
        "links simbólicos no ZIP são rejeitados");

    restoreService.RestoreSnapshot(legacyBackup);
    Require(
        await File.ReadAllTextAsync(activeSnippets) ==
            "# backup legado",
        "backup schema 1 continua restaurável");

    var emotePath = "assets/emote-" + new string('a', 64) + ".png";
    foreach (var unicode in new[] { "😀", "❤️", "👩🏽‍💻", "🇧🇷", "🫪" })
    {
        var token = $"![Emoji: {unicode}]({emotePath})";
        Require(RichTextMarkdownConverter.ToPlainText("Antes " + token + " Depois") == "Antes " + unicode + " Depois", "Emoji Unicode preservado em texto simples: " + unicode);
        Require(RichTextMarkdownConverter.ToPlainText(CodeBlockMarkdown.Write(new CodeBlockContent("text", token))) == token, "Código mantém token de imagem literal");
    }
    Require(RichTextMarkdownConverter.ToPlainText($"![Emoji: Meu desenho]({emotePath})") == "[Imagem: Emoji: Meu desenho]", "Imagem personalizada conserva descrição em app somente texto");
    Require(RichTextMarkdownConverter.ToPlainText("![Foto](assets/foto.png)") == "[Imagem: Foto]", "Fallback de imagens normais permanece legível");
    var defaultShare = new Snippet { Id = Guid.Parse("3e387d61-2c04-4b36-988b-6ce918868fcc"), Name = "Divulgar SlashDesk", Trigger = "/ola", Content = ShortcutShareMessage.Content, IsFavorite = true };
    var shareRepository = new SnippetMarkdownRepository(Path.Combine(root, "share-default.md"));
    await shareRepository.SaveAsync([defaultShare]);
    var renamedShare = await ShortcutShareMessage.RenameUntouchedDefaultAsync(shareRepository, await shareRepository.LoadAsync());
    Require(renamedShare.Single().Trigger == "/slashdesk" && renamedShare.Single().IsFavorite && (await shareRepository.LoadAsync()).Single().Content == ShortcutShareMessage.Content, "Somente comando do texto inicial intacto é atualizado");
    defaultShare.Content += " personalizado";
    await shareRepository.SaveAsync([defaultShare]); var personalizedBytes = await File.ReadAllBytesAsync(Path.Combine(root, "share-default.md"));
    var personalizedResult = await ShortcutShareMessage.RenameUntouchedDefaultAsync(shareRepository, await shareRepository.LoadAsync());
    var personalizedAfter = await File.ReadAllBytesAsync(Path.Combine(root, "share-default.md"));
    Require(personalizedResult.Single().Trigger == "/ola" && personalizedBytes.SequenceEqual(personalizedAfter), "Texto personalizado não é migrado");
    defaultShare.Content = ShortcutShareMessage.Content;
    await shareRepository.SaveAsync([defaultShare, new Snippet { Name = "Meu", Trigger = "/slashdesk", Content = "meu conteúdo" }]);
    var conflictedBytes = await File.ReadAllBytesAsync(Path.Combine(root, "share-default.md"));
    await ShortcutShareMessage.RenameUntouchedDefaultAsync(shareRepository, await shareRepository.LoadAsync());
    var conflictedAfter = await File.ReadAllBytesAsync(Path.Combine(root, "share-default.md"));
    Require(conflictedBytes.SequenceEqual(conflictedAfter), "Comando /slashdesk existente não é substituído");

    var literalCode = "  a_b * 2 <tag> &amp; {{nome}} {{tab}}\n\t```\n  final  \n";
    var fencedCode = CodeBlockMarkdown.Write(new CodeBlockContent("javascript", literalCode));
    var code = "Antes\n" + fencedCode + "\nDepois";
    var parsedCode = CodeBlockMarkdown.Read(code).Single();
    Require(parsedCode.Content.Code == literalCode && parsedCode.Content.Language == "javascript", "Código preserva espaços, símbolos, variáveis e crases dentro de cerca mais longa");
    Require(RichTextMarkdownConverter.ToPlainText(code) == "Antes\n" + literalCode + "\nDepois", "Fallback do código é literal, sem remover Markdown nem decodificar entidades");
    Require(RichTextMarkdownConverter.ToHtml(code).Contains(System.Net.WebUtility.HtmlEncode(literalCode), StringComparison.Ordinal), "HTML codifica código sem remover espaços nem interpretar tags");
    Require(engine.GetFillableFields("{{fora}}\n" + fencedCode, literalCodeBlocks: true).Select(f => f.Name).SequenceEqual(new[] { "fora" }), "Somente variáveis fora do código são solicitadas");
    Require(engine.Render("{{fora}}\n" + fencedCode, new Dictionary<string, string> { ["fora"] = "Valor", ["nome"] = "não substituir" }, literalCodeBlocks: true) == "Valor\n" + fencedCode, "Expansão não substitui variáveis nem tabulação do código");
    Require(engine.Render("{{nome}}", new Dictionary<string, string> { ["nome"] = "Texto normal" }) == "Texto normal", "Texto simples mantém variáveis existentes");
    foreach (var payload in new[] { "", "\n", "\t espaços  ", "\r\nlinha\r\nfim\r\n", "~~~\n````\n" })
        Require(CodeBlockMarkdown.Read(CodeBlockMarkdown.Write(new CodeBlockContent("text", payload))).Single().Content.Code == CodeBlockMarkdown.Normalize(payload), "Cerca de código roundtrip literal");
    var flagged = new Snippet { Name = "Código favorito", Trigger = "/codigo", Format = SnippetFormat.Markdown, Content = code, IsFavorite = true, IsPinned = true };
    var codeRepository = new SnippetMarkdownRepository(Path.Combine(root, "code-fixture.md"), Path.Combine(root, "code-backups"));
    await codeRepository.SaveAsync([flagged]);
    var reopenedCode = (await codeRepository.LoadAsync()).Single();
    Require(reopenedCode.Content == code && reopenedCode.IsFavorite && reopenedCode.IsPinned, "Código, favorito e fixado persistem juntos em snippets.md");
    Require(
        RichTextMarkdownConverter.ToHtml(code).Contains("<pre", StringComparison.Ordinal),
        "bloco de código HTML");
    Require(
        RichTextMarkdownConverter.ToPlainText(code).Contains("a_b", StringComparison.Ordinal),
        "fallback de código em texto simples");
    var rich = """
               <p align="center"><span style="font-family:Arial;font-size:16px;background-color:#FFF176">Título</span></p>
               - Primeiro
               - Segundo
               | Nome | Valor |
               | --- | --- |
               | Teste | 10 |
               """;
    var richHtml = RichTextMarkdownConverter.ToHtml(rich);
    Require(richHtml.Contains("<ul>", StringComparison.Ordinal), "lista com marcadores em HTML");
    Require(richHtml.Contains("<table", StringComparison.Ordinal), "tabela em HTML");
    Require(
        richHtml.Contains("background-color:#FFF176", StringComparison.Ordinal),
        "marca-texto em HTML");

    Require(
        CaptureService.ResolveDirectoryTemplate(
                @"C:\Capturas\{year}\{month}",
                reference) ==
            @"C:\Capturas\2026\07",
        "pastas de captura por ano e mês");
    Require(
        CaptureService.SanitizeFileName("Outlook: chamado?") == "Outlook- chamado-",
        "nome de captura remove caracteres inválidos");
    var captureDefaults = new CaptureSettings();
    Require(
        captureDefaults.Recording.VideoFps == 30 &&
        captureDefaults.Recording.GifFps == 10 &&
        captureDefaults.Recording.GifQuality == 128 &&
        captureDefaults.HistoryRetentionDays == 90 &&
        captureDefaults.ScrollingShortcut == "Ctrl+Shift+WheelDown" &&
        !captureDefaults.OpenEditorForMonitorAndWindow,
        "padrões seguros de gravação, histórico e captura direta");
    Require(
        !captureDefaults.HideSlashDeskDuringCapture &&
        !captureDefaults.ShouldHideSlashDesk(windowIsVisible: true) &&
        !captureDefaults.ShouldHideSlashDesk(windowIsVisible: false),
        "padrão permite capturar a própria janela do SlashDesk");
    var hiddenSlashDeskSettings = new AppSettings();
    hiddenSlashDeskSettings.Capture.HideSlashDeskDuringCapture = true;
    var hiddenSlashDeskJson =
        System.Text.Json.JsonSerializer.Serialize(hiddenSlashDeskSettings);
    var reloadedHiddenSlashDesk =
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(hiddenSlashDeskJson);
    Require(
        reloadedHiddenSlashDesk is not null &&
        reloadedHiddenSlashDesk.Capture.HideSlashDeskDuringCapture &&
        reloadedHiddenSlashDesk.Capture.ShouldHideSlashDesk(windowIsVisible: true) &&
        !reloadedHiddenSlashDesk.Capture.ShouldHideSlashDesk(windowIsVisible: false),
        "preferência para ocultar o SlashDesk persiste no JSON");
    var editorCaptureSettings = new AppSettings();
    editorCaptureSettings.Capture.OpenEditorForMonitorAndWindow = true;
    var editorCaptureJson =
        System.Text.Json.JsonSerializer.Serialize(editorCaptureSettings);
    var reloadedEditorCapture =
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(editorCaptureJson);
    Require(
        reloadedEditorCapture is not null &&
        reloadedEditorCapture.Capture.OpenEditorForMonitorAndWindow,
        "preferência existente pelo modo editor persiste no JSON");
    using var scrollingDocument = new System.Drawing.Bitmap(96, 280);
    for (var y = 0; y < scrollingDocument.Height; y++)
    {
        for (var x = 0; x < scrollingDocument.Width; x++)
        {
            scrollingDocument.SetPixel(
                x,
                y,
                System.Drawing.Color.FromArgb(
                    (x * 17 + y * 31) % 256,
                    (x * 29 + y * 13) % 256,
                    (x * 7 + y * 43) % 256));
        }
    }
    using var firstScrollingFrame = scrollingDocument.Clone(
        new System.Drawing.Rectangle(0, 0, 96, 120),
        scrollingDocument.PixelFormat);
    using var repeatedScrollingFrame = new System.Drawing.Bitmap(firstScrollingFrame);
    using var secondScrollingFrame = scrollingDocument.Clone(
        new System.Drawing.Rectangle(0, 75, 96, 120),
        scrollingDocument.PixelFormat);
    Require(
        CaptureService.AreFramesVisuallyEquivalent(
            firstScrollingFrame,
            repeatedScrollingFrame),
        "rolagem detecta o fim quando o quadro não muda");
    Require(
        !CaptureService.AreFramesVisuallyEquivalent(
            firstScrollingFrame,
            secondScrollingFrame),
        "rolagem continua quando há novo conteúdo");
    Require(
        CaptureService.EstimateVerticalScrollDelta(
            firstScrollingFrame,
            secondScrollingFrame,
            fallbackDelta: 100) == 75,
        "rolagem mede o deslocamento real e evita duplicação");
    using var browserFrameBefore = new System.Drawing.Bitmap(240, 160);
    using var browserFrameAfter = new System.Drawing.Bitmap(240, 160);
    using (var beforeGraphics = System.Drawing.Graphics.FromImage(browserFrameBefore))
    using (var afterGraphics = System.Drawing.Graphics.FromImage(browserFrameAfter))
    {
        beforeGraphics.Clear(System.Drawing.Color.FromArgb(13, 17, 23));
        afterGraphics.Clear(System.Drawing.Color.FromArgb(13, 17, 23));
        using var chromeBrush = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(32, 36, 42));
        beforeGraphics.FillRectangle(chromeBrush, 0, 0, 240, 28);
        afterGraphics.FillRectangle(chromeBrush, 0, 0, 240, 28);
        using var chromeLine = new System.Drawing.Pen(
            System.Drawing.Color.FromArgb(110, 118, 129));
        beforeGraphics.DrawLine(chromeLine, 8, 14, 232, 14);
        afterGraphics.DrawLine(chromeLine, 8, 14, 232, 14);
    }
    for (var y = 28; y < 160; y++)
    {
        for (var x = 40; x < 200; x++)
        {
            var beforeDocumentY = y - 28;
            var afterDocumentY = beforeDocumentY + 75;
            browserFrameBefore.SetPixel(
                x,
                y,
                System.Drawing.Color.FromArgb(
                    (x * 11 + beforeDocumentY * 19) % 256,
                    (x * 23 + beforeDocumentY * 7) % 256,
                    (x * 5 + beforeDocumentY * 29) % 256));
            browserFrameAfter.SetPixel(
                x,
                y,
                System.Drawing.Color.FromArgb(
                    (x * 11 + afterDocumentY * 19) % 256,
                    (x * 23 + afterDocumentY * 7) % 256,
                    (x * 5 + afterDocumentY * 29) % 256));
        }
    }
    Require(
        !CaptureService.AreFramesVisuallyEquivalent(
            browserFrameBefore,
            browserFrameAfter),
        "rolagem não confunde barras e fundos estáticos com fim da página");
    Require(
        CaptureService.EstimateVerticalScrollDelta(
            browserFrameBefore,
            browserFrameAfter,
            fallbackDelta: 133) == 75,
        "rolagem mede conteúdo mesmo com barra fixa do navegador");

    Require(
        RecordingPresetCatalog.GifFps.Select(item => item.Value).SequenceEqual([10, 20, 30]) &&
        RecordingPresetCatalog.GifQuality.Select(item => item.Value).SequenceEqual([32, 64, 128, 256]) &&
        RecordingPresetCatalog.Mp4Quality.Select(item => item.Value)
            .SequenceEqual(["Baixa", "Média", "Alta", "Muito alta"]),
        "catálogo expõe somente presets seguros e consistentes");
    var migratedRecording = new RecordingSettings
    {
        GifFps = 17,
        GifQuality = 80,
        GifDurationSeconds = 27,
        GifWidth = 1440,
        VideoQuality = "Máxima"
    };
    RecordingPresetCatalog.Normalize(migratedRecording);
    Require(
        migratedRecording.GifFps == 20 &&
        migratedRecording.GifQuality == 64 &&
        migratedRecording.GifDurationSeconds == 27 &&
        migratedRecording.GifWidth == 1440 &&
        migratedRecording.VideoQuality == "Muito alta",
        "configuração antiga migra para preset próximo sem perder campos legados");
    Require(
        RecordingPresetCatalog.NormalizeGifFps(5) == 10 &&
        RecordingPresetCatalog.NormalizeGifFps(15) == 20 &&
        RecordingPresetCatalog.NormalizeGifFps(60) == 30 &&
        RecordingPresetCatalog.NormalizeGifFps(4) == 10 &&
        RecordingPresetCatalog.NormalizeGifFps(17) == 20 &&
        RecordingPresetCatalog.NormalizeGifFps(29) == 30,
        "FPS legado migra para 10, 20 ou 30 conforme a regra definida");
    foreach (var preset in RecordingPresetCatalog.GifFps)
    {
        Require(
            preset.Description.StartsWith($"{preset.Value} FPS.", StringComparison.Ordinal) &&
            !preset.Description.Contains("5 FPS", StringComparison.Ordinal) &&
            !preset.Description.Contains("15 FPS", StringComparison.Ordinal) &&
            !preset.Description.Contains("60 FPS", StringComparison.Ordinal),
            $"descrição do preset GIF {preset.Value} corresponde ao FPS aplicado");
    }
    RequireThrows<ArgumentOutOfRangeException>(
        () => GifRecordingService.ValidateSettings(
            new RecordingSettings { GifFps = 12, GifQuality = 128 }),
        "GIF bloqueia FPS arbitrário");

    var settingsPath = Path.Combine(root, "legacy-settings.json");
    var settingsStore = new JsonFileStore<AppSettings>(settingsPath);
    await File.WriteAllTextAsync(settingsPath,
        """{"capture":{"recording":{"gifFps":17,"gifQuality":80,"gifDurationSeconds":27,"gifWidth":1440,"videoQuality":"Máxima"}}}""");
    var loadedLegacy = await settingsStore.LoadAsync();
    RecordingPresetCatalog.Normalize(loadedLegacy.Capture.Recording);
    await settingsStore.SaveAsync(loadedLegacy);
    var persistedLegacy = await settingsStore.LoadAsync();
    Require(
        persistedLegacy.Capture.Recording.GifFps == 20 &&
        persistedLegacy.Capture.Recording.GifQuality == 64 &&
        persistedLegacy.Capture.Recording.GifDurationSeconds == 27 &&
        persistedLegacy.Capture.Recording.GifWidth == 1440,
        "migração de GIF antigo persiste sem quebrar inicialização");
    Require(
        ScreenRecordingService.CreateMediaPath(
                new CaptureSettings
                {
                    OutputDirectoryTemplate = root,
                    FileNameTemplate = "{type}_{date}_{time}"
                },
                "video",
                ".mp4")
            .EndsWith(".mp4", StringComparison.OrdinalIgnoreCase),
        "nome local para gravação MP4");
    var validMp4 = Path.Combine(root, "valid.mp4");
    await WriteValidMp4Async(validMp4);
    ScreenRecordingService.ValidateMp4File(validMp4);
    var invalidMp4 = Path.Combine(root, "invalid.mp4");
    await File.WriteAllBytesAsync(invalidMp4, [0, 1, 2, 3]);
    RequireThrows<InvalidDataException>(
        () => ScreenRecordingService.ValidateMp4File(invalidMp4),
        "MP4 vazio ou sem contêiner não entra no histórico");
    var recorderOptions = ScreenRecordingService.BuildOptions(
        new RecordingTarget(
            RecordingTargetKind.Window,
            new System.Drawing.Rectangle(0, 0, 1280, 720),
            new IntPtr(1)),
        new RecordingSettings { VideoFps = 30, VideoQuality = "Alta" },
        Path.Combine(root, "screenrecorderlib.log"));
    Require(
        recorderOptions.VideoEncoderOptions.IsHardwareEncodingEnabled &&
        !recorderOptions.VideoEncoderOptions.IsFixedFramerate &&
        !recorderOptions.VideoEncoderOptions.IsThrottlingDisabled &&
        recorderOptions.VideoEncoderOptions.Encoder is H264VideoEncoder h264 &&
        h264.BitrateMode == H264BitrateControlMode.Quality &&
        !recorderOptions.LogOptions.IsLogEnabled &&
        recorderOptions.LogOptions.LogSeverityLevel == LogLevel.Error,
        "MP4 prefere hardware com fallback Media Foundation, sem quadros fixos e sem persistir log nativo");
    var mp4TechnicalValues = new Dictionary<string, (int Bitrate, int Quality)>
    {
        ["Baixa"] = (2_500_000, 55),
        ["Média"] = (5_000_000, 70),
        ["Alta"] = (9_000_000, 85),
        ["Muito alta"] = (16_000_000, 95)
    };
    foreach (var (computer, microphone) in new[] { (false, false), (true, false), (false, true), (true, true) })
    {
        var audioOptions = ScreenRecordingService.BuildOptions(
            new RecordingTarget(RecordingTargetKind.Window, new System.Drawing.Rectangle(0, 0, 640, 480), new IntPtr(1)),
            new RecordingSettings { Audio = new() { CaptureComputer = computer, CaptureMicrophone = microphone,
                InputDeviceId = "input-fixture", OutputDeviceId = "output-fixture", InputVolume = .4f, OutputVolume = .7f } },
            Path.Combine(root, "audio-options.log")).AudioOptions;
        Require(audioOptions.IsAudioEnabled == (computer || microphone) &&
                audioOptions.IsInputDeviceEnabled == microphone && audioOptions.IsOutputDeviceEnabled == computer &&
                audioOptions.AudioInputDevice == "input-fixture" && audioOptions.AudioOutputDevice == "output-fixture" &&
                audioOptions.InputVolume == .4f && audioOptions.OutputVolume == .7f,
            $"MP4 respeita fontes independentes PC={computer} microfone={microphone}, dispositivos e volumes");
    }
    var savedAudio = System.Text.Json.JsonSerializer.Deserialize<RecordingSettings>(
        System.Text.Json.JsonSerializer.Serialize(new RecordingSettings { Audio = new() {
            CaptureComputer = false, CaptureMicrophone = true, InputDeviceId = "mic-saved", InputVolume = .6f } }))!;
    Require(!savedAudio.Audio.CaptureComputer && savedAudio.Audio.CaptureMicrophone &&
            savedAudio.Audio.InputDeviceId == "mic-saved" && savedAudio.Audio.InputVolume == .6f,
        "preferências de áudio sobrevivem ao JSON");
    var oldRecording = System.Text.Json.JsonSerializer.Deserialize<RecordingSettings>("{\"VideoFps\":24}")!;
    Require(oldRecording.VideoFps == 24 && oldRecording.Audio.CaptureComputer && !oldRecording.Audio.CaptureMicrophone,
        "configuração anterior migra com PC ligado e microfone desligado");
    var unavailableAudio = new RecordingAudioDevices(new([], null), new([], null));
    Require(unavailableAudio.Validate(new() { CaptureComputer = false, CaptureMicrophone = false }) is null &&
            unavailableAudio.Validate(new() { CaptureComputer = true }) is not null,
        "sem dispositivos permite vídeo silencioso e impede fingir áudio ativo");
    var disconnectedAudio = new RecordingAudioDeviceList([new("available", "Disponível")], null);
    Require(disconnectedAudio.Validate(true, "removed", "o microfone") is not null &&
            disconnectedAudio.Validate(true, string.Empty, "o microfone") is null &&
            disconnectedAudio.Validate(false, "removed", "o microfone") is null,
        "dispositivo desconectado não troca silenciosamente pelo padrão");
    foreach (var preset in RecordingPresetCatalog.Mp4Quality)
    {
        var options = ScreenRecordingService.BuildOptions(
            new RecordingTarget(
                RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 640, 480),
                new IntPtr(1)),
            new RecordingSettings { VideoFps = 30, VideoQuality = preset.Value },
            Path.Combine(root, $"mp4-{preset.Value}.log"));
        var expected = mp4TechnicalValues[preset.Value];
        Require(
            options.VideoEncoderOptions.Bitrate == expected.Bitrate &&
            options.VideoEncoderOptions.Quality == expected.Quality &&
            preset.Description.Contains(expected.Quality.ToString(), StringComparison.Ordinal),
            $"preset MP4 {preset.Name} aplica os valores descritos");
    }

    using (var paletteBitmap = new System.Drawing.Bitmap(48, 48))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(paletteBitmap))
        using (var background = new System.Drawing.SolidBrush(
                   System.Drawing.Color.FromArgb(15, 26, 35)))
        {
            graphics.FillRectangle(background, 0, 0, 48, 36);
        }
        for (var y = 36; y < paletteBitmap.Height; y++)
        {
            for (var x = 0; x < paletteBitmap.Width; x++)
            {
                paletteBitmap.SetPixel(
                    x,
                    y,
                    System.Drawing.Color.FromArgb(
                        (x * 17 + y * 5) % 256,
                        (x * 7 + y * 19) % 256,
                        (x * 23 + y * 11) % 256));
            }
        }

        var paletteSource = GifRecordingService.ToBitmapSource(paletteBitmap);
        var paletteSizes = new List<int>();
        foreach (var preset in RecordingPresetCatalog.GifQuality)
        {
            var quantized = GifRecordingService.Quantize(paletteSource, preset.Value);
            var colors = quantized.Palette?.Colors ?? [];
            paletteSizes.Add(colors.Count);
            Require(
                quantized.Format == System.Windows.Media.PixelFormats.Indexed8 &&
                colors.Count is > 0 &&
                colors.Count <= preset.Value &&
                colors.Any(color =>
                    Math.Abs(color.R - 15) +
                    Math.Abs(color.G - 26) +
                    Math.Abs(color.B - 35) <= 60) &&
                preset.Description.Contains(preset.Value.ToString(), StringComparison.Ordinal),
                $"preset GIF {preset.Name} usa paleta adaptativa limitada às cores descritas");
        }
        Require(
            paletteSizes.Zip(paletteSizes.Skip(1), (before, after) => after >= before)
                .All(increases => increases),
            "presets GIF maiores não reduzem a variedade da paleta adaptativa");
    }

    Require(
        typeof(SlashText.MainWindow).GetMethod(
            "CreateTrayMenu",
            BindingFlags.NonPublic | BindingFlags.Instance) is not null &&
        typeof(SlashText.MainWindow).Assembly.GetType("SlashText.Views.TrayMenuTheme")?.GetMethod(
            "Apply", BindingFlags.NonPublic | BindingFlags.Static) is not null,
        "bandeja mantém composição nativa e aplicação do tema compartilhado; recursão verificada pelo settings-smoke");

    var fakeFactory = new FakeRecorderBackendFactory();
    using (var lifecycle = new ScreenRecordingService(fakeFactory, TimeSpan.FromSeconds(1)))
    {
        var lifecycleTask = lifecycle.StartAsync(
            new RecordingTarget(
                RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 640, 480),
                new IntPtr(1)),
            new CaptureSettings
            {
                OutputDirectoryTemplate = root,
                FileNameTemplate = "lifecycle"
            },
            new RecordingSettings { VideoFps = 30, Audio = new() { CaptureComputer = true, CaptureMicrophone = true,
                InputVolume = .4f, OutputVolume = .7f } });
        Require(fakeFactory.Backend.RecordCalled.Wait(TimeSpan.FromSeconds(2)), "MP4 inicia backend");
        await WaitUntilAsync(
            () => lifecycle.State == ScreenRecordingState.Recording,
            "estado Recording");
        await Task.Delay(100);
        Require(lifecycle.Elapsed >= TimeSpan.FromMilliseconds(60), "contador MP4 avança");
        Require(await lifecycle.SetAudioMutedAsync(true, false) && lifecycle.IsMicrophoneMuted &&
                fakeFactory.Backend.InputVolume == 0 && fakeFactory.Backend.OutputVolume == .7f,
            "silenciar microfone mantém o áudio do PC");
        fakeFactory.Backend.RejectAudioChanges = true;
        Require(!await lifecycle.SetAudioMutedAsync(false, true) && lifecycle.IsMicrophoneMuted && !lifecycle.IsComputerMuted &&
                lifecycle.State == ScreenRecordingState.Recording,
            "falha ao silenciar mantém estado real e não interrompe vídeo");
        fakeFactory.Backend.RejectAudioChanges = false;
        lifecycle.Pause();
        await WaitUntilAsync(
            () => lifecycle.State == ScreenRecordingState.Paused,
            "estado Paused");
        var pausedElapsed = lifecycle.Elapsed;
        Require(!await lifecycle.SetAudioMutedAsync(false, true) && lifecycle.IsMicrophoneMuted && !lifecycle.IsComputerMuted,
            "pausa preserva áudio e impede uma alteração que o encoder não aplica");
        await Task.Delay(80);
        Require(
            lifecycle.Elapsed - pausedElapsed < TimeSpan.FromMilliseconds(30),
            "contador MP4 não inclui tempo pausado");
        lifecycle.Resume();
        await WaitUntilAsync(
            () => lifecycle.State == ScreenRecordingState.Recording,
            "retorno ao estado Recording");
        Require(await lifecycle.SetAudioMutedAsync(false, true) && !lifecycle.IsMicrophoneMuted && lifecycle.IsComputerMuted &&
                fakeFactory.Backend.InputVolume == .4f && fakeFactory.Backend.OutputVolume == 0,
            "retomar permite alterar áudio e restaura o volume escolhido");
        await Task.Delay(80);
        Require(lifecycle.Elapsed > pausedElapsed, "contador MP4 retoma do acumulado");
        lifecycle.Stop();
        var stoppedElapsed = lifecycle.Elapsed;
        var lifecyclePath = await lifecycleTask.WaitAsync(TimeSpan.FromSeconds(3));
        Require(File.Exists(lifecyclePath), "MP4 finaliza e publica arquivo");
        Require(lifecycle.State == ScreenRecordingState.Completed, "estado Completed");
        Require(!await lifecycle.SetAudioMutedAsync(false, false), "não muda áudio após finalizar");
        Require(fakeFactory.Backend.DisposeCalls == 1, "Recorder descartado uma única vez");
        Require(fakeFactory.Backend.MaximumConcurrentCalls == 1, "chamadas nativas serializadas");
        Require(
            lifecycle.Elapsed - stoppedElapsed < TimeSpan.FromMilliseconds(30),
            "contador MP4 para no pedido de finalização");
    }

    var duplicateFactory = new FakeRecorderBackendFactory { SendDuplicateCallbacks = true };
    using (var lifecycle = new ScreenRecordingService(duplicateFactory, TimeSpan.FromSeconds(1)))
    {
        var task = lifecycle.StartAsync(
            new RecordingTarget(
                RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 320, 240),
                new IntPtr(1)),
            new CaptureSettings
            {
                OutputDirectoryTemplate = root,
                FileNameTemplate = "duplicate-callback"
            },
            new RecordingSettings());
        Require(duplicateFactory.Backend.RecordCalled.Wait(TimeSpan.FromSeconds(2)), "backend para callback duplicado");
        lifecycle.Stop();
        lifecycle.Stop();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(duplicateFactory.Backend.DisposeCalls == 1, "callback duplicado não duplica descarte");
        Require(duplicateFactory.Backend.StopCalls == 1, "dois cliques em finalizar chamam Stop uma vez");
    }

    var failureFactory = new FakeRecorderBackendFactory { FailOnStop = true };
    using (var lifecycle = new ScreenRecordingService(failureFactory, TimeSpan.FromSeconds(1)))
    {
        var task = lifecycle.StartAsync(
            new RecordingTarget(RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 320, 240), new IntPtr(1)),
            new CaptureSettings { OutputDirectoryTemplate = root, FileNameTemplate = "failed-callback" },
            new RecordingSettings());
        Require(failureFactory.Backend.RecordCalled.Wait(TimeSpan.FromSeconds(2)), "backend para callback de falha");
        lifecycle.Stop();
        await RequireThrowsAsync<InvalidOperationException>(() => task, "callback de falha conclui task");
        Require(lifecycle.State == ScreenRecordingState.Failed, "callback de falha restaura estado terminal");
        Require(failureFactory.Backend.DisposeCalls == 1, "falha descarta Recorder uma vez");
    }

    var delayedFactory = new FakeRecorderBackendFactory { CallbackDelayMs = 120 };
    using (var lifecycle = new ScreenRecordingService(delayedFactory, TimeSpan.FromSeconds(1)))
    {
        var task = lifecycle.StartAsync(
            new RecordingTarget(RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 320, 240), new IntPtr(1)),
            new CaptureSettings { OutputDirectoryTemplate = root, FileNameTemplate = "delayed-callback" },
            new RecordingSettings());
        Require(delayedFactory.Backend.RecordCalled.Wait(TimeSpan.FromSeconds(2)), "backend para callback atrasado");
        lifecycle.Stop();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(delayedFactory.Backend.DisposeCalls == 1, "callback atrasado finaliza uma vez");
    }

    var blockingFactory = new FakeRecorderBackendFactory { BlockStop = true };
    using (var lifecycle = new ScreenRecordingService(blockingFactory, TimeSpan.FromSeconds(2)))
    {
        var task = lifecycle.StartAsync(
            new RecordingTarget(RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 320, 240), new IntPtr(1)),
            new CaptureSettings { OutputDirectoryTemplate = root, FileNameTemplate = "closing-finalization" },
            new RecordingSettings());
        Require(blockingFactory.Backend.RecordCalled.Wait(TimeSpan.FromSeconds(2)), "backend para Stop bloqueante");
        var uiClock = Stopwatch.StartNew();
        lifecycle.Stop();
        lifecycle.Dispose();
        Require(uiClock.Elapsed < TimeSpan.FromMilliseconds(100), "Stop e Dispose não bloqueiam a UI");
        Require(blockingFactory.Backend.StopEntered.Wait(TimeSpan.FromSeconds(2)), "Stop nativo entrou");
        blockingFactory.Backend.ReleaseStop();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(blockingFactory.Backend.DisposeCalls == 1, "fechamento durante finalização descarta uma vez");
    }

    var timeoutFactory = new FakeRecorderBackendFactory { CompleteOnStop = false };
    using (var lifecycle = new ScreenRecordingService(timeoutFactory, TimeSpan.FromMilliseconds(80)))
    {
        var task = lifecycle.StartAsync(
            new RecordingTarget(
                RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 320, 240),
                new IntPtr(1)),
            new CaptureSettings
            {
                OutputDirectoryTemplate = root,
                FileNameTemplate = "late-callback"
            },
            new RecordingSettings());
        Require(timeoutFactory.Backend.RecordCalled.Wait(TimeSpan.FromSeconds(2)), "backend para timeout");
        lifecycle.Stop();
        await RequireThrowsAsync<TimeoutException>(() => task, "timeout nativo controlado");
        Require(timeoutFactory.Backend.DisposeCalls == 0, "timeout não descarta Recorder durante callback nativo");
        timeoutFactory.Backend.CompleteLater();
        // Timeout already publishes Failed; wait for the late callback's actual cleanup.
        await WaitUntilAsync(
            () => lifecycle.State == ScreenRecordingState.Failed && timeoutFactory.Backend.DisposeCalls >= 1,
            "callback tardio conclui limpeza");
        Require(timeoutFactory.Backend.DisposeCalls == 1, "callback tardio executa descarte único");
    }

    var callerThread = 0;
    var gifCaptureThreads = new System.Collections.Concurrent.ConcurrentBag<int>();
    var pipeline = new GifRecordingService((_, _) =>
    {
        gifCaptureThreads.Add(Environment.CurrentManagedThreadId);
        var bitmap = new System.Drawing.Bitmap(16, 12);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.CornflowerBlue);
        return bitmap;
    });
    GifRecordingResult? pipelineResult = null;
    Exception? pipelineFailure = null;
    var caller = new Thread(() =>
    {
        callerThread = Environment.CurrentManagedThreadId;
        try
        {
            pipelineResult = pipeline.CaptureAsync(
                new System.Drawing.Rectangle(0, 0, 16, 12),
                new RecordingSettings
                {
                    GifFps = 10,
                    GifDurationSeconds = 1,
                    GifWidth = 240,
                    GifQuality = 128
                }).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            pipelineFailure = exception;
        }
    });
    caller.Start();
    caller.Join();
    if (pipelineFailure is not null)
    {
        throw new InvalidOperationException("Pipeline GIF falhou.", pipelineFailure);
    }

    using (pipelineResult ?? throw new InvalidOperationException("Pipeline GIF sem resultado."))
    {
        Require(
            pipelineResult.Metrics is
            {
                RequestedFps: 10,
                CapturedFrames: >= 8,
                StoredFrames: 1
            } metrics &&
            metrics.ProcessedFrames == metrics.CapturedFrames &&
            metrics.EffectiveCapturedFps > 0 &&
            metrics.DuplicateFrames == metrics.CapturedFrames - 1,
            "pipeline GIF descarta quadro idêntico preservando duração");
        Require(
            pipelineResult.Duration >= TimeSpan.FromMilliseconds(900) &&
            pipelineResult.Duration <= TimeSpan.FromMilliseconds(1100),
            "deduplicação GIF preserva o tempo monotônico da sessão");
        Require(
            gifCaptureThreads.All(thread => thread != callerThread),
            "captura e redimensionamento GIF fora da UI thread");
    }

    foreach (var fpsPreset in RecordingPresetCatalog.GifFps)
    {
        using var session = pipeline.StartRecording(
            new System.Drawing.Rectangle(0, 0, 16, 12),
            new RecordingSettings { GifFps = fpsPreset.Value, GifQuality = 128 });
        IRecordingController counter = session;
        Require(counter.Elapsed < TimeSpan.FromMilliseconds(80),
            $"contador GIF inicia em zero a {fpsPreset.Value} FPS");
        await Task.Delay(120);
        Require(counter.Elapsed >= TimeSpan.FromMilliseconds(70),
            $"contador GIF avança a {fpsPreset.Value} FPS");
        counter.Pause();
        var gifPausedAt = counter.Elapsed;
        await Task.Delay(80);
        Require(counter.Elapsed - gifPausedAt < TimeSpan.FromMilliseconds(30),
            $"contador GIF pausa a {fpsPreset.Value} FPS");
        counter.Resume();
        await Task.Delay(80);
        Require(counter.Elapsed > gifPausedAt,
            $"contador GIF retoma a {fpsPreset.Value} FPS");
        counter.Stop();
        var gifStoppedAt = counter.Elapsed;
        using var presetResult = await session.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Require(presetResult.Fps == fpsPreset.Value,
            $"pipeline mantÃ©m preset de {fpsPreset.Value} FPS");
        Require(counter.Elapsed - gifStoppedAt < TimeSpan.FromMilliseconds(30),
            $"contador GIF para ao finalizar a {fpsPreset.Value} FPS");
        Require(GifRecordingService.QueueCapacity == 2,
            $"fila GIF limitada no preset de {fpsPreset.Value} FPS");
    }

    var storageRoot = Path.Combine(root, "armazenamento portátil çã com espaços");
    var portableHome = Path.Combine(storageRoot, "origem portátil");
    var localDataRoot = Path.Combine(storageRoot, "local-app-data");
    var installedLegacy = Path.Combine(localDataRoot, "SlashDesk");
    Directory.CreateDirectory(portableHome);
    Directory.CreateDirectory(installedLegacy);
    var preservedSnippetId = Guid.NewGuid();
    await File.WriteAllTextAsync(
        Path.Combine(installedLegacy, "snippets.md"),
        $"## /preservado\n<!-- slashtext:{{\"id\":\"{preservedSnippetId}\",\"name\":\"Preservado\",\"category\":\"Migração\",\"format\":\"plain\",\"enabled\":true,\"confirmKeys\":[\"Enter\"]}} -->\n```text\nConteúdo\n```\n");
    await File.WriteAllTextAsync(Path.Combine(installedLegacy, "settings.json"),
        """{"theme":"Dark","checkUpdatesOnStartup":false}""");
    await File.WriteAllTextAsync(Path.Combine(installedLegacy, "usage.json"),
        "{\"snippets\":[{\"snippetId\":\"" + preservedSnippetId +
        "\",\"count\":9,\"charactersSaved\":42}]," +
        "\"quickAccent\":{\"count\":2,\"characters\":{}}}");
    await File.WriteAllTextAsync(Path.Combine(installedLegacy, "capture-history.json"),
        """[{"id":"history-preserved","createdAt":"2026-08-06T12:00:00-03:00","type":"monitor","mediaKind":"image","filePath":"C:\\Capturas\\preservada.png","width":800,"height":600}]""");

    var portableEnvironment = new AppDataEnvironment(
        DistributionMode.Portable,
        portableHome,
        localDataRoot);
    AppPaths.Initialize(portableEnvironment);
    Require(
        AppPaths.DataDirectory == Path.Combine(portableHome, "SlashDeskData") &&
        AppPaths.LogsDirectory == Path.Combine(portableHome, "SlashDeskData", "Logs") &&
        AppPaths.BackupsDirectory == Path.Combine(portableHome, "SlashDeskData", "Backups"),
        "modo portátil centraliza dados ao lado do executável");
    var migrationResult = AppPaths.EnsureDataLayout();
    Require(
        migrationResult.Migrated &&
        Directory.Exists(installedLegacy) &&
        File.Exists(AppPaths.SnippetsFile) &&
        File.ReadAllText(AppPaths.SnippetsFile).Contains(preservedSnippetId.ToString(), StringComparison.Ordinal) &&
        File.ReadAllText(AppPaths.CaptureHistoryFile).Contains("history-preserved", StringComparison.Ordinal) &&
        migrationResult.BackupPath is not null && File.Exists(migrationResult.BackupPath),
        "migração copia, valida, ativa e preserva origem, atalhos e histórico");
    using (var migrationArchive = ZipFile.OpenRead(migrationResult.BackupPath!))
    {
        Require(
            migrationArchive.GetEntry("migration-manifest.json") is not null &&
            migrationArchive.Entries.Any(item => item.FullName.EndsWith("snippets.md", StringComparison.Ordinal)),
            "backup de migração contém manifesto e atalhos");
    }

    var simultaneousHome = Path.Combine(storageRoot, "duas origens");
    var simultaneousData = Path.Combine(simultaneousHome, "SlashDeskData");
    Directory.CreateDirectory(simultaneousData);
    await File.WriteAllTextAsync(Path.Combine(simultaneousData, "settings.json"),
        """{"theme":"Light"}""");
    AppPaths.Initialize(new AppDataEnvironment(
        DistributionMode.Portable,
        simultaneousHome,
        localDataRoot));
    var simultaneous = AppPaths.EnsureDataLayout();
    Require(
        !simultaneous.Migrated &&
        simultaneous.CompetingSourcePreserved &&
        File.ReadAllText(AppPaths.SettingsFile).Contains("Light", StringComparison.Ordinal) &&
        simultaneous.BackupPath is not null && File.Exists(simultaneous.BackupPath),
        "duas origens priorizam portátil sem mescla destrutiva e preservam a outra origem");

    var movedHome = Path.Combine(storageRoot, "portátil movido");
    Directory.Move(portableHome, movedHome);
    AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, movedHome, localDataRoot));
    Require(
        File.ReadAllText(AppPaths.SnippetsFile).Contains(preservedSnippetId.ToString(), StringComparison.Ordinal) &&
        File.ReadAllText(AppPaths.CaptureHistoryFile).Contains("history-preserved", StringComparison.Ordinal),
        "atalhos e histórico acompanham a pasta portátil movida");

    var movedCaptureDirectory = Path.Combine(movedHome, "Capturas");
    Directory.CreateDirectory(movedCaptureDirectory);
    var movedCapture = Path.Combine(movedCaptureDirectory, "imagem preservada.png");
    await File.WriteAllBytesAsync(movedCapture, [1, 2, 3]);
    var relativeRecord = new CaptureRecord
    {
        FilePath = Path.Combine(portableHome, "Capturas", "imagem preservada.png"),
        PortableRelativePath = Path.Combine("Capturas", "imagem preservada.png")
    };
    Require(
        CapturePathResolver.Resolve(relativeRecord, AppPaths.Current) == movedCapture,
        "caminho relativo localiza mídia após mover a pasta portátil");

    await File.WriteAllTextAsync(AppPaths.CaptureHistoryFile,
        "[{\"id\":\"valid-item\",\"createdAt\":\"2026-08-06T12:00:00-03:00\"," +
        "\"type\":\"regiao\",\"mediaKind\":\"image\",\"filePath\":\"C:\\\\ok.png\"," +
        "\"width\":100,\"height\":80}," +
        "{\"id\":\"corrupt-item\",\"createdAt\":[],\"width\":\"inválido\"}]");
    var tolerantHistory = new CaptureService();
    await tolerantHistory.LoadAsync();
    Require(
        tolerantHistory.History.Count == 1 && tolerantHistory.History[0].Id == "valid-item",
        "item de histórico corrompido não impede carregar registros válidos");

    var editedHistory = new CaptureService();
    await editedHistory.LoadAsync();
    var originalPath = Path.Combine(movedCaptureDirectory, "original-do-editor.png");
    var editedPath = Path.Combine(movedCaptureDirectory, "copia-editada.png");
    using (var originalBitmap = new System.Drawing.Bitmap(80, 60))
    using (var editedBitmap = new System.Drawing.Bitmap(40, 30))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(originalBitmap)) graphics.Clear(System.Drawing.Color.Blue);
        using (var graphics = System.Drawing.Graphics.FromImage(editedBitmap)) graphics.Clear(System.Drawing.Color.Red);
        var originalRecord = await editedHistory.SaveEditedImageAsync(originalBitmap, originalPath, new CaptureSettings());
        var editedRecord = await editedHistory.SaveEditedImageAsync(editedBitmap, editedPath, new CaptureSettings(), originalRecord);
        Require(originalRecord.Id != editedRecord.Id && editedHistory.History[0].Id == editedRecord.Id &&
                editedRecord.Width == 40 && editedRecord.Height == 30 && editedRecord.MediaKind == "image",
            "Salvar cópia registra o arquivo editado como recente, com ID e dimensões próprios");
        using (var originalFile = new System.Drawing.Bitmap(originalPath))
        using (var editedFile = new System.Drawing.Bitmap(editedHistory.ResolveFilePath(editedRecord)))
            Require(originalFile.GetPixel(10, 10).ToArgb() == System.Drawing.Color.Blue.ToArgb() &&
                    editedFile.GetPixel(10, 10).ToArgb() == System.Drawing.Color.Red.ToArgb(),
                "Recentes aponta para pixels editados e preserva o original ao salvar uma cópia");
        using (var graphics = System.Drawing.Graphics.FromImage(editedBitmap)) graphics.Clear(System.Drawing.Color.Green);
        var updatedRecord = await editedHistory.SaveEditedImageAsync(editedBitmap, editedPath, new CaptureSettings(), editedRecord);
        Require(updatedRecord.Id == editedRecord.Id && editedHistory.History.Count(r => r.Id == editedRecord.Id) == 1,
            "Concluir atualiza o mesmo registro sem criar duplicatas");
        var reloadedHistory = new CaptureService();
        await reloadedHistory.LoadAsync();
        var reloadedRecord = reloadedHistory.History.First(r => r.Id == editedRecord.Id);
        using var latest = new System.Drawing.Bitmap(reloadedHistory.ResolveFilePath(reloadedRecord));
        Require(latest.GetPixel(10, 10).ToArgb() == System.Drawing.Color.Green.ToArgb() &&
                reloadedRecord.Width == 40 && reloadedRecord.Height == 30,
            "reiniciar mantém a versão editada e as dimensões corretas do histórico");
        RequireThrows<ArgumentException>(
            () => editedHistory.SaveEditedImageAsync(editedBitmap, Path.Combine(movedCaptureDirectory, "nao-substituir.gif"),
                new CaptureSettings()).GetAwaiter().GetResult(),
            "edição estática não substitui GIF animado");
    }

    var invalidPortableRoot = Path.Combine(storageRoot, "sem-permissão");
    await File.WriteAllTextAsync(invalidPortableRoot, "não é um diretório");
    var invalidPortable = new AppDataEnvironment(
        DistributionMode.Portable,
        invalidPortableRoot,
        localDataRoot);
    Require(
        !invalidPortable.TryProbePortableWrite(out var portableWriteError) &&
        !string.IsNullOrWhiteSpace(portableWriteError),
        "portátil detecta diretório sem capacidade de gravação sem alternar origem");

    AppPaths.Initialize(new AppDataEnvironment(
        DistributionMode.Installed,
        Path.Combine(storageRoot, "installed-bin"),
        localDataRoot));
    Require(
        AppPaths.DataDirectory == installedLegacy &&
        !AppPaths.IsPortable,
        "modo instalado usa %LocalAppData%\\SlashDesk");

    using (var frame1 = new System.Drawing.Bitmap(4, 4))
    using (var frame2 = new System.Drawing.Bitmap(4, 4))
    {
        frame1.SetPixel(0, 0, System.Drawing.Color.Red);
        frame2.SetPixel(0, 0, System.Drawing.Color.Blue);
        using var gifRecording = new GifRecordingResult(
            [frame1.Clone() as System.Drawing.Bitmap ?? throw new InvalidOperationException(),
             frame2.Clone() as System.Drawing.Bitmap ?? throw new InvalidOperationException()],
            10,
            new System.Drawing.Rectangle(0, 0, 4, 4));
        var gifPath = new GifRecordingService().Save(
            gifRecording,
            new CaptureSettings
            {
                OutputDirectoryTemplate = root,
                FileNameTemplate = "animated"
            },
            "gif");
        var gifBytes = await File.ReadAllBytesAsync(gifPath);
        Require(
            System.Text.Encoding.ASCII.GetString(gifBytes).Contains(
                "NETSCAPE2.0",
                StringComparison.Ordinal),
            "GIF inclui extensão de repetição NETSCAPE");
        using var gifStream = File.OpenRead(gifPath);
        var decoder = new System.Windows.Media.Imaging.GifBitmapDecoder(
            gifStream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        Require(decoder.Frames.Count == 2, "GIF preserva todos os quadros");
    }
    Require(
        GlobalCaptureShortcutService.IsValid("Ctrl+Shift+PrintScreen"),
        "atalho de captura pelo teclado");
    Require(
        GlobalCaptureShortcutService.IsValid("Ctrl+Shift+WheelUp") &&
        GlobalCaptureShortcutService.IsValid("Ctrl+Shift+WheelDown") &&
        Enum.IsDefined(CaptureShortcutAction.Scrolling),
        "atalhos de janela e captura longa pela roda do mouse");
    Require(
        !GlobalCaptureShortcutService.IsValid("WheelUp"),
        "roda do mouse exige modificador");
    Require(
        GlobalCaptureShortcutService.FormatKeyboardShortcut(
            System.Windows.Input.Key.F10,
            System.Windows.Input.ModifierKeys.None) == "F10",
        "grava tecla de função sem digitação manual");
    Require(
        GlobalCaptureShortcutService.FormatKeyboardShortcut(
            System.Windows.Input.Key.Snapshot,
            System.Windows.Input.ModifierKeys.None) == "PrintScreen" &&
        GlobalCaptureShortcutService.IsValid("Print") &&
        GlobalCaptureShortcutService.IsValid("PrtSc") &&
        GlobalCaptureShortcutService.IsValid("Snapshot"),
        "grava Print Screen no pressionamento ou na liberação");
    Require(
        GlobalCaptureShortcutService.FormatWheelShortcut(
            120,
            System.Windows.Input.ModifierKeys.Control |
            System.Windows.Input.ModifierKeys.Shift) ==
            "Ctrl+Shift+WheelUp" &&
        GlobalCaptureShortcutService.FormatWheelShortcut(
            -120,
            System.Windows.Input.ModifierKeys.Control |
            System.Windows.Input.ModifierKeys.Shift) ==
            "Ctrl+Shift+WheelDown",
        "grava combinações de janela e captura longa pela roda do mouse");
    Require(
        GlobalCaptureShortcutService.FormatMouseShortcut(
            System.Windows.Input.MouseButton.XButton1,
            System.Windows.Input.ModifierKeys.Alt) ==
            "Alt+MouseX1",
        "grava botão lateral do mouse");
    Require(
        GlobalCaptureShortcutService.IsValid("MouseX2") &&
        GlobalCaptureShortcutService.IsValid("Ctrl+MouseMiddle"),
        "botões do mouse são atalhos válidos");
    Require(
        !GlobalCaptureShortcutService.IsValid("MouseLeft") &&
        !GlobalCaptureShortcutService.IsValid("MouseRight"),
        "cliques essenciais do mouse permanecem livres");

    using (var source = new System.Drawing.Bitmap(120, 90))
    {
        using (var graphics = System.Drawing.Graphics.FromImage(source))
        {
            graphics.Clear(System.Drawing.Color.White);
        }

        var annotationScenarios = new[]
        {
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Arrow,
                Start = new System.Windows.Point(10, 10),
                End = new System.Windows.Point(90, 60)
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Highlighter,
                Start = new System.Windows.Point(8, 45),
                End = new System.Windows.Point(100, 45),
                Argb = System.Drawing.Color.Gold.ToArgb()
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Rectangle,
                Start = new System.Windows.Point(15, 15),
                End = new System.Windows.Point(80, 65)
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Ellipse,
                Start = new System.Windows.Point(20, 15),
                End = new System.Windows.Point(85, 70)
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Line,
                Start = new System.Windows.Point(5, 80),
                End = new System.Windows.Point(110, 10),
                OutlineArgb = System.Drawing.Color.Cyan.ToArgb(),
                Opacity = .7f,
                Thickness = 8
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Rectangle,
                Start = new System.Windows.Point(15, 15),
                End = new System.Windows.Point(80, 65),
                FillArgb = System.Drawing.Color.Orange.ToArgb(),
                OutlineArgb = null,
                Opacity = .6f
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Ellipse,
                Start = new System.Windows.Point(20, 15),
                End = new System.Windows.Point(85, 70),
                FillArgb = null,
                OutlineArgb = System.Drawing.Color.Blue.ToArgb(),
                Thickness = 12
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Pencil,
                Points =
                [
                    new System.Windows.Point(5, 5),
                    new System.Windows.Point(40, 30),
                    new System.Windows.Point(75, 12)
                ]
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Text,
                Start = new System.Windows.Point(10, 20),
                Text = "Teste"
            },
            new CaptureAnnotation
            {
                Kind = CaptureAnnotationKind.Number,
                Start = new System.Windows.Point(55, 42),
                Text = "1"
            }
        };

        foreach (var annotation in annotationScenarios)
        {
            using var renderedCapture = CaptureAnnotationRenderer.Render(
                source,
                [annotation],
                120,
                90);
            Require(
                HasChangedPixel(renderedCapture),
                $"renderiza ferramenta {annotation.Kind}");
        }

        var invisible = new CaptureAnnotation
        {
            Kind = CaptureAnnotationKind.Rectangle,
            FillArgb = null,
            OutlineArgb = null
        };
        Require(!invisible.HasVisibleShapeStyle, "bloqueia forma totalmente invisível");

        System.Drawing.Bitmap? renderedStamp = null;
        Exception? stampFailure = null;
        var stampThread = new Thread(() =>
        {
            try
            {
                renderedStamp = CaptureAnnotationRenderer.Render(
                    source,
                    [new CaptureAnnotation
                    {
                        Kind = CaptureAnnotationKind.Stamp,
                        Start = new System.Windows.Point(60, 45),
                        Text = "❤️",
                        Size = 48
                    }],
                    120,
                    90);
            }
            catch (Exception exception)
            {
                stampFailure = exception;
            }
        });
        stampThread.SetApartmentState(ApartmentState.STA);
        stampThread.Start();
        stampThread.Join();
        if (stampFailure is not null)
        {
            throw new InvalidOperationException("Renderização do emoticon falhou.", stampFailure);
        }
        using (renderedStamp)
        {
            Require(renderedStamp is not null && HasChangedPixel(renderedStamp),
                "emoticon colorido é renderizado no bitmap final");
        }
    }

    var annotationHistory = new CaptureAnnotationHistory();
    var stampAnnotation = new CaptureAnnotation
    {
        Kind = CaptureAnnotationKind.Stamp,
        Text = "⭐",
        Size = 32
    };
    annotationHistory.Add(stampAnnotation);
    Require(annotationHistory.Items.Count == 1 && annotationHistory.CanUndo,
        "emoticon entra no histórico de desfazer");
    annotationHistory.Undo();
    Require(annotationHistory.Items.Count == 0 && annotationHistory.CanRedo,
        "desfaz emoticon");
    annotationHistory.Redo();
    Require(annotationHistory.Items.Count == 1, "refaz emoticon");
    annotationHistory.ClearAll();
    Require(annotationHistory.Items.Count == 0, "limpa todas as marcações");
    annotationHistory.Undo();
    Require(annotationHistory.Items.Count == 1, "limpeza de marcações pode ser desfeita");
    Require(annotationHistory.HitTest(new System.Windows.Point(0, 0)) == 0 &&
        annotationHistory.Move(0, 50, 30) && annotationHistory.Items.Single().Start == new System.Windows.Point(50, 30),
        "move emoji existente sem criar outra anotação");
    Require(annotationHistory.HitTest(new System.Windows.Point(0, 0)) == -1 &&
        annotationHistory.HitTest(new System.Windows.Point(50, 30)) == 0,
        "seleção acompanha a nova posição do emoji");
    Require(!annotationHistory.Move(-1, 10, 10) && !annotationHistory.Move(0, double.NaN, 0) &&
        !annotationHistory.Move(0, 0, 0), "movimentos inválidos e clique sem arraste não criam histórico");
    annotationHistory.Undo();
    Require(annotationHistory.Items.Single().Start == new System.Windows.Point(0, 0), "um undo reverte todo o movimento");
    annotationHistory.Redo();
    Require(annotationHistory.Items.Single().Start == new System.Windows.Point(50, 30) &&
        annotationHistory.Items.Single().Text == stampAnnotation.Text && stampAnnotation.Start == new System.Windows.Point(0, 0),
        "redo mantém conteúdo e não modifica o objeto anterior");

    var atomicStorageRoot = Path.Combine(root, "atomic-storage");
    Directory.CreateDirectory(atomicStorageRoot);
    var concurrentJsonPath = Path.Combine(atomicStorageRoot, "concurrent.json");
    var concurrentStore = new JsonFileStore<AppSettings>(concurrentJsonPath);
    await Task.WhenAll(Enumerable.Range(0, 24).Select(index =>
        concurrentStore.SaveAsync(new AppSettings { Theme = $"Theme-{index}" })));
    var concurrentSettings = await concurrentStore.LoadAsync();
    Require(
        concurrentSettings.Theme.StartsWith("Theme-", StringComparison.Ordinal) &&
        Directory.GetFiles(atomicStorageRoot, ".concurrent.json-*.tmp").Length == 0,
        "gravações JSON concorrentes produzem um arquivo completo sem temporários");

    var preservedPath = Path.Combine(atomicStorageRoot, "preserved.txt");
    await File.WriteAllTextAsync(preservedPath, "último conteúdo válido");
    await RequireThrowsAsync<IOException>(
        () => AtomicFilePersistence.WriteAsync(
            preservedPath,
            async (stream, token) =>
            {
                await stream.WriteAsync("conteúdo incompleto"u8.ToArray(), token);
                throw new IOException("falha simulada antes da substituição");
            }),
        "falha antes da substituição é propagada");
    Require(
        await File.ReadAllTextAsync(preservedPath) == "último conteúdo válido",
        "falha de gravação preserva o arquivo válido anterior");

    using (var cancelledWrite = new CancellationTokenSource())
    {
        cancelledWrite.Cancel();
        await RequireThrowsAsync<OperationCanceledException>(
            () => AtomicFilePersistence.WriteTextAsync(
                preservedPath,
                "não deve substituir",
                System.Text.Encoding.UTF8,
                cancelledWrite.Token),
            "gravação cancelada é interrompida");
    }
    Require(
        await File.ReadAllTextAsync(preservedPath) == "último conteúdo válido",
        "cancelamento preserva o arquivo válido anterior");

    var corruptJsonPath = Path.Combine(atomicStorageRoot, "settings.json");
    const string corruptJson = "{\"theme\": \"Dark\", conteúdo inválido";
    await File.WriteAllTextAsync(corruptJsonPath, corruptJson);
    var corruptStore = new JsonFileStore<AppSettings>(corruptJsonPath);
    var fallbackSettings = await corruptStore.LoadAsync();
    var preservedCorruptFiles = Directory.GetFiles(
        atomicStorageRoot,
        "settings.json.corrupt-*.bak");
    Require(
        fallbackSettings.Theme == new AppSettings().Theme &&
        preservedCorruptFiles.Length == 1 &&
        await File.ReadAllTextAsync(preservedCorruptFiles[0]) == corruptJson,
        "JSON inválido é preservado e retorna configuração padrão");
    _ = await corruptStore.LoadAsync();
    Require(
        Directory.GetFiles(atomicStorageRoot, "settings.json.corrupt-*.bak").Length == 1,
        "preservação de JSON corrompido é idempotente");

    var updateTests = Path.Combine(root, "updates");
    Directory.CreateDirectory(updateTests);
    var now = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    var updateHandler = new FakeUpdateHttpHandler(ReleaseJson("3.0.0"));
    var updateService = new UpdateService(
        new HttpClient(updateHandler),
        Path.Combine(updateTests, "state.json"),
        "2.9.1",
        () => now,
        TimeSpan.FromSeconds(1));
    var updateFound = await updateService.CheckAsync(force: true);
    Require(
        updateFound.UpdateAvailable &&
        updateFound.LatestVersion == "3.0.0" &&
        updateFound.Release?.PortableAsset.Name == "SlashDesk-3.0.0-portable-win-x64.zip" &&
        updateFound.DownloadSize == 12_345,
        "release estável aplica versão e artefatos exatos");
    Require(updateHandler.Calls == 1, "consulta única ao GitHub");

    await updateService.IgnoreVersionAsync("3.0.0");
    var ignored = await updateService.CheckAsync(force: true);
    Require(ignored.Status == UpdateCheckStatus.Ignored && !ignored.UpdateAvailable,
        "versão ignorada não é oferecida novamente");
    updateHandler.ResponseJson = ReleaseJson("3.1.0");
    var afterIgnored = await updateService.CheckAsync(force: true);
    Require(afterIgnored.UpdateAvailable && afterIgnored.LatestVersion == "3.1.0",
        "versão superior volta a ser oferecida");

    await updateService.RemindLaterAsync("3.1.0");
    var cachedDeferred = await updateService.CheckAsync();
    Require(cachedDeferred.Status == UpdateCheckStatus.Cached && !cachedDeferred.UpdateAvailable,
        "cache automático respeita lembrar depois");
    now += UpdateService.AutomaticCheckInterval + TimeSpan.FromMinutes(1);
    var deferred = await updateService.CheckAsync();
    Require(deferred.Status == UpdateCheckStatus.Deferred && !deferred.UpdateAvailable,
        "verificação automática adia somente a versão atual");
    var manualAfterDeferral = await updateService.CheckAsync(force: true);
    Require(
        manualAfterDeferral.Status == UpdateCheckStatus.UpdateAvailable &&
        manualAfterDeferral.UpdateAvailable &&
        manualAfterDeferral.LatestVersion == "3.1.0",
        "busca manual volta a oferecer versão adiada");
    now += UpdateService.RemindLaterInterval + TimeSpan.FromMinutes(1);
    var afterDeferral = await updateService.CheckAsync();
    Require(afterDeferral.UpdateAvailable, "lembrar depois volta a oferecer automaticamente");

    var cacheHandler = new FakeUpdateHttpHandler(ReleaseJson("3.0.0"));
    var cacheService = new UpdateService(
        new HttpClient(cacheHandler),
        Path.Combine(updateTests, "cache-state.json"),
        "2.9.1",
        () => now,
        TimeSpan.FromSeconds(1));
    await Task.WhenAll(cacheService.CheckAsync(), cacheService.CheckAsync());
    Require(cacheHandler.Calls == 1, "pedidos simultâneos usam exclusão e cache");

    var stableFilterHandler = new FakeUpdateHttpHandler(ReleaseJson(
        "3.0.0", includeStable: false, includeDraft: true, includePrerelease: true));
    var stableFilterService = new UpdateService(
        new HttpClient(stableFilterHandler),
        Path.Combine(updateTests, "stable-filter.json"),
        "2.9.1",
        () => now,
        TimeSpan.FromSeconds(1));
    var stableFilter = await stableFilterService.CheckAsync(force: true);
    Require(stableFilter.Status == UpdateCheckStatus.NoRelease,
        "draft e prerelease são ignoradas no canal estável");

    var currentService = new UpdateService(
        new HttpClient(new FakeUpdateHttpHandler(ReleaseJson("2.9.1"))),
        Path.Combine(updateTests, "current.json"),
        "2.9.1",
        () => now,
        TimeSpan.FromSeconds(1));
    Require((await currentService.CheckAsync(force: true)).Status == UpdateCheckStatus.UpToDate,
        "nenhuma atualização quando versões coincidem");

    var offlineService = new UpdateService(
        new HttpClient(new FakeUpdateHttpHandler(new HttpRequestException("offline"))),
        Path.Combine(updateTests, "offline.json"),
        "2.9.1",
        () => now,
        TimeSpan.FromSeconds(1));
    Require((await offlineService.CheckAsync(force: true)).Status == UpdateCheckStatus.Offline,
        "ausência de internet não lança erro invasivo");

    var timeoutService = new UpdateService(
        new HttpClient(new FakeUpdateHttpHandler(TimeSpan.FromMilliseconds(200))),
        Path.Combine(updateTests, "timeout.json"),
        "2.9.1",
        () => now,
        TimeSpan.FromMilliseconds(20));
    Require((await timeoutService.CheckAsync(force: true)).Status == UpdateCheckStatus.Offline,
        "timeout de atualização é tratado");

    var notificationGate = new UpdateNotificationGate();
    Require(
        !notificationGate.TryMark(null) &&
        notificationGate.TryMark("3.2.0") &&
        !notificationGate.TryMark("3.2.0") &&
        notificationGate.TryMark("3.3.0"),
        "monitor notifica uma única vez por versão durante a sessão");
    notificationGate.Mark("3.4.0");
    Require(!notificationGate.TryMark("3.4.0"),
        "busca manual impede repetição automática da mesma versão");

    var transactionRoot = Path.Combine(root, "update-transaction");
    Directory.CreateDirectory(transactionRoot);
    var transactionData = Path.Combine(transactionRoot, "SlashDeskData");
    Directory.CreateDirectory(transactionData);
    var preservedUpdateData = new Dictionary<string, string>
    {
        ["snippets.md"] = "atalhos, categorias, hyperlinks e variáveis preservados",
        ["settings.json"] = "{\"theme\":\"Dark\",\"checkUpdatesOnStartup\":true}",
        ["usage.json"] = "{\"totalExpansions\":17}",
        ["capture-history.json"] = "[{\"id\":\"preservado-na-atualizacao\"}]"
    };
    foreach (var item in preservedUpdateData)
    {
        await File.WriteAllTextAsync(Path.Combine(transactionData, item.Key), item.Value);
    }
    var targetExecutable = Path.Combine(transactionRoot, "SlashDesk.exe");
    var stagedExecutable = Path.Combine(transactionRoot, "SlashDesk.new.exe");
    var backupExecutable = Path.Combine(transactionRoot, "SlashDesk.previous.exe");
    var failedExecutable = Path.Combine(transactionRoot, "SlashDesk.failed.exe");
    await File.WriteAllTextAsync(targetExecutable, "versão anterior");
    await File.WriteAllTextAsync(stagedExecutable, "versão nova");
    PortableUpdateFileTransaction.Apply(targetExecutable, stagedExecutable, backupExecutable);
    Require(
        await File.ReadAllTextAsync(targetExecutable) == "versão nova" &&
        await File.ReadAllTextAsync(backupExecutable) == "versão anterior" &&
        preservedUpdateData.All(item =>
            File.ReadAllText(Path.Combine(transactionData, item.Key)) == item.Value),
        "substituição troca somente o executável e preserva atalhos, histórico, configurações e estatísticas");
    PortableUpdateFileTransaction.Rollback(targetExecutable, backupExecutable, failedExecutable);
    Require(
        await File.ReadAllTextAsync(targetExecutable) == "versão anterior" &&
        preservedUpdateData.All(item =>
            File.ReadAllText(Path.Combine(transactionData, item.Key)) == item.Value),
        "rollback restaura executável sem alterar atalhos, histórico, configurações e estatísticas");

    var replacementInvoked = false;
    RequireThrows<IOException>(() => PortableUpdateFileTransaction.Apply(
        targetExecutable,
        failedExecutable,
        backupExecutable,
        (_, _, _) =>
        {
            replacementInvoked = true;
            throw new IOException("falha simulada");
        }), "falha na substituição é propagada");
    Require(replacementInvoked && preservedUpdateData.All(item =>
            File.ReadAllText(Path.Combine(transactionData, item.Key)) == item.Value),
        "falha na substituição preserva dados");

    var portableUpdateHome = Path.Combine(root, "Portátil Atualização");
    Directory.CreateDirectory(portableUpdateHome);
    var portableExecutable = Path.Combine(portableUpdateHome, "SlashDesk.exe");
    File.Copy(Environment.ProcessPath!, portableExecutable);
    AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, portableUpdateHome,
        Path.Combine(root, "legacy-update")));
    AppPaths.EnsureDataLayout();
    var updaterDataSentinel = Path.Combine(AppPaths.DataDirectory, "history-sentinel.txt");
    await File.WriteAllTextAsync(updaterDataSentinel, "histórico intacto");
    var updaterVersion = Version.Parse(
        FileVersionInfo.GetVersionInfo(portableExecutable).FileVersion ?? "1.0.0.0").ToString(3);
    var packageBytes = CreatePortableZip(portableExecutable);
    var packageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes))
        .ToLowerInvariant();
    var packageName = $"SlashDesk-{updaterVersion}-portable-win-x64.zip";
    var checksumBytes = System.Text.Encoding.ASCII.GetBytes($"{packageHash}  {packageName}");
    var packageRelease = CreatePackageRelease(updaterVersion, packageName, packageBytes.Length,
        checksumBytes.Length);
    var packageHandler = new FakePackageHttpHandler(packageBytes, checksumBytes);
    var packageService = new PortableUpdateService(
        new HttpClient(packageHandler), portableExecutable, currentProcessId: 0);
    var preparedPackage = await packageService.PrepareAsync(packageRelease);
    Require(
        File.Exists(preparedPackage.Manifest.StagedExecutable) &&
        File.Exists(preparedPackage.Manifest.HelperExecutable) &&
        await File.ReadAllTextAsync(updaterDataSentinel) == "histórico intacto",
        "pacote válido é preparado dentro de SlashDeskData sem alterar histórico");

    var badChecksum = checksumBytes.ToArray();
    badChecksum[0] = badChecksum[0] == (byte)'0' ? (byte)'1' : (byte)'0';
    var invalidChecksumService = new PortableUpdateService(
        new HttpClient(new FakePackageHttpHandler(packageBytes, badChecksum)),
        portableExecutable,
        currentProcessId: 0);
    await RequireThrowsAsync<InvalidDataException>(
        () => invalidChecksumService.PrepareAsync(packageRelease),
        "checksum inválido impede atualização");

    var incompleteRelease = CreatePackageRelease(
        updaterVersion, packageName, packageBytes.Length + 10, checksumBytes.Length);
    var incompleteService = new PortableUpdateService(
        new HttpClient(new FakePackageHttpHandler(packageBytes, checksumBytes)),
        portableExecutable,
        currentProcessId: 0);
    await RequireThrowsAsync<EndOfStreamException>(
        () => incompleteService.PrepareAsync(incompleteRelease),
        "download incompleto impede atualização");

    var concurrentHandler = new FakePackageHttpHandler(packageBytes, checksumBytes)
    {
        Delay = TimeSpan.FromMilliseconds(250)
    };
    var concurrentPackageService = new PortableUpdateService(
        new HttpClient(concurrentHandler), portableExecutable, currentProcessId: 0);
    using var concurrentCancellation = new CancellationTokenSource();
    var firstPrepare = concurrentPackageService.PrepareAsync(
        packageRelease, cancellationToken: concurrentCancellation.Token);
    await concurrentHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await RequireThrowsAsync<InvalidOperationException>(
        () => concurrentPackageService.PrepareAsync(packageRelease),
        "dois pedidos simultâneos de atualização são bloqueados");
    concurrentCancellation.Cancel();
    await RequireThrowsAsync<OperationCanceledException>(
        () => firstPrepare,
        "fechamento durante download cancela preparação");
    Require(await File.ReadAllTextAsync(updaterDataSentinel) == "histórico intacto",
        "cancelamento do download preserva SlashDeskData");

    var noCallbackFactory = new FakeRecorderBackendFactory { CompleteOnStop = false };
    using (var lifecycle = new ScreenRecordingService(
               noCallbackFactory,
               TimeSpan.FromMilliseconds(60)))
    {
        var task = lifecycle.StartAsync(
            new RecordingTarget(RecordingTargetKind.Window,
                new System.Drawing.Rectangle(0, 0, 320, 240), new IntPtr(1)),
            new CaptureSettings { OutputDirectoryTemplate = root, FileNameTemplate = "no-callback" },
            new RecordingSettings());
        Require(noCallbackFactory.Backend.RecordCalled.Wait(TimeSpan.FromSeconds(2)),
            "backend para timeout sem callback");
        lifecycle.Stop();
        await RequireThrowsAsync<TimeoutException>(() => task, "timeout real sem callback");
        var disposeClock = Stopwatch.StartNew();
        lifecycle.Dispose();
        Require(disposeClock.Elapsed < TimeSpan.FromMilliseconds(100),
            "Dispose após timeout não bloqueia UI");
        Require(noCallbackFactory.Backend.DisposeCalls == 0,
            "timeout sem callback não concorre Dispose com código nativo");
        Require(lifecycle.State == ScreenRecordingState.Failed,
            "timeout sem callback encerra estado Finalizando");
    }
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, true);
    }
}

var assetMaintenanceRoot = Path.Combine(
    Path.GetTempPath(),
    "SlashDeskAssetMaintenanceTests",
    Guid.NewGuid().ToString("N"));
var assetMaintenanceDirectory = Path.Combine(assetMaintenanceRoot, "assets");
Directory.CreateDirectory(assetMaintenanceDirectory);
try
{
    var referencedAsset = Path.Combine(assetMaintenanceDirectory, "used.png");
    var orphanAsset = Path.Combine(assetMaintenanceDirectory, "orphan.png");
    var temporaryAsset = Path.Combine(assetMaintenanceDirectory, "capture.tmp");
    File.WriteAllBytes(referencedAsset, [1, 2, 3]);
    File.WriteAllBytes(orphanAsset, [4, 5, 6, 7]);
    File.WriteAllBytes(temporaryAsset, [8]);

    var assetMaintenance = new AssetMaintenanceService(assetMaintenanceDirectory);
    var assetReport = assetMaintenance.Analyze(
    [
        new Snippet
        {
            Format = SnippetFormat.Markdown,
            Content = "![Uma](assets/used.png)\n![Duplicada](assets/used.png)\n" +
                      "![Externa](https://example.com/image.png)"
        }
    ]);
    Require(assetReport.ReferencedAssetCount == 1, "referências de assets sem duplicação");
    Require(assetReport.UnsafeReferenceCount == 0, "referências externas não são seguidas");
    Require(
        assetReport.Orphans.Count == 1 &&
        assetReport.Orphans[0].RelativePath == "orphan.png",
        "somente asset sem referência é classificado como órfão");
    Require(
        File.Exists(referencedAsset) && File.Exists(orphanAsset),
        "análise e cancelamento não alteram arquivos");

    var backupObservedBeforeDeletion = false;
    var backupMarker = Path.Combine(assetMaintenanceRoot, "before-cleanup.zip");
    var cleanupResult = assetMaintenance.DeleteOrphansAfterBackup(
        assetReport,
        () =>
        {
            backupObservedBeforeDeletion = File.Exists(orphanAsset);
            File.WriteAllText(backupMarker, "backup");
            return backupMarker;
        });
    Require(backupObservedBeforeDeletion, "backup ocorre antes da exclusão");
    Require(cleanupResult.DeletedCount == 1, "asset órfão removido após backup");
    Require(File.Exists(referencedAsset), "asset referenciado preservado");
    Require(!File.Exists(orphanAsset), "asset órfão removido");
    Require(File.Exists(temporaryAsset), "arquivo temporário ignorado");

    var blockedAsset = Path.Combine(assetMaintenanceDirectory, "blocked.png");
    File.WriteAllBytes(blockedAsset, [9]);
    var unsafeReport = assetMaintenance.Analyze(
    [
        new Snippet
        {
            Format = SnippetFormat.Markdown,
            Content = "![Inválida](assets/../outside.png)"
        }
    ]);
    Require(unsafeReport.UnsafeReferenceCount == 1, "caminho local inseguro detectado");
    var unsafeBackupCalled = false;
    RequireThrows<InvalidOperationException>(
        () => assetMaintenance.DeleteOrphansAfterBackup(
            unsafeReport,
            () =>
            {
                unsafeBackupCalled = true;
                return backupMarker;
            }),
        "limpeza bloqueada por referência insegura");
    Require(!unsafeBackupCalled && File.Exists(blockedAsset), "bloqueio não modifica assets");
}
finally
{
    Directory.Delete(assetMaintenanceRoot, recursive: true);
}

Console.WriteLine("SlashText smoke tests: OK");
return;

static bool HasChangedPixel(System.Drawing.Bitmap bitmap)
{
    for (var y = 0; y < bitmap.Height; y += 2)
    {
        for (var x = 0; x < bitmap.Width; x += 2)
        {
            if (bitmap.GetPixel(x, y).ToArgb() !=
                System.Drawing.Color.White.ToArgb())
            {
                return true;
            }
        }
    }
    return false;
}

static void Require(bool condition, string scenario)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Falha no cenário: {scenario}");
    }
}

static void RequireThrows<TException>(Action action, string scenario)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Falha no smoke test: {scenario}");
}

static async Task WaitUntilAsync(Func<bool> condition, string scenario)
{
    var timeout = Stopwatch.StartNew();
    while (!condition())
    {
        if (timeout.Elapsed > TimeSpan.FromSeconds(2))
        {
            throw new InvalidOperationException($"Timeout no cenário: {scenario}");
        }
        await Task.Delay(10);
    }
}

static async Task RequireThrowsAsync<TException>(Func<Task> action, string scenario)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Falha no smoke test: {scenario}");
}

static Task WriteValidMp4Async(string path) => File.WriteAllBytesAsync(
    path,
    [
        0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0,
        0, 0, 0, 12, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 1, 2, 3, 4,
        0, 0, 0, 8, (byte)'m', (byte)'o', (byte)'o', (byte)'v'
    ]);

static string ReleaseJson(
    string version,
    bool includeStable = true,
    bool includeDraft = false,
    bool includePrerelease = false)
{
    var releases = new List<string>();
    if (includeDraft)
    {
        releases.Add(ReleaseEntry(version, draft: true, prerelease: false));
    }
    if (includePrerelease)
    {
        releases.Add(ReleaseEntry(version + "-rc.1", draft: false, prerelease: true));
    }
    if (includeStable)
    {
        releases.Add(ReleaseEntry(version, draft: false, prerelease: false));
    }
    return $"[{string.Join(',', releases)}]";
}

static string ReleaseEntry(string version, bool draft, bool prerelease) => $$"""
    {
      "tag_name": "v{{version}}",
      "name": "SlashDesk {{version}}",
      "body": "Notas {{version}}",
      "html_url": "https://github.com/lucasllira/SlashText/releases/tag/v{{version}}",
      "published_at": "2026-08-06T12:00:00Z",
      "draft": {{draft.ToString().ToLowerInvariant()}},
      "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
      "assets": [
        {
          "name": "SlashDesk-{{version}}-portable-win-x64.zip",
          "browser_download_url": "https://example.invalid/SlashDesk-{{version}}-portable-win-x64.zip",
          "size": 12345
        },
        {
          "name": "SlashDesk-{{version}}-portable-win-x64.zip.sha256",
          "browser_download_url": "https://example.invalid/SlashDesk-{{version}}-portable-win-x64.zip.sha256",
          "size": 128
        }
      ]
    }
    """;

static byte[] CreatePortableZip(string executable)
{
    using var output = new MemoryStream();
    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = archive.CreateEntry("SlashDesk.exe", CompressionLevel.NoCompression);
        using var destination = entry.Open();
        using var source = File.OpenRead(executable);
        source.CopyTo(destination);
    }
    return output.ToArray();
}

static ReleaseInfo CreatePackageRelease(
    string version,
    string packageName,
    long packageSize,
    long checksumSize) => new(
        version,
        $"SlashDesk {version}",
        "Notas",
        $"https://github.com/lucasllira/SlashText/releases/tag/v{version}",
        DateTimeOffset.UtcNow,
        new ReleaseAssetInfo(
            packageName,
            $"https://github.com/lucasllira/SlashText/releases/download/v{version}/{packageName}",
            packageSize),
        new ReleaseAssetInfo(
            packageName + ".sha256",
            $"https://github.com/lucasllira/SlashText/releases/download/v{version}/{packageName}.sha256",
            checksumSize));

sealed class FakePackageHttpHandler(byte[] package, byte[] checksum) : HttpMessageHandler
{
    public TimeSpan Delay { get; init; }
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }
        var body = request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true
            ? checksum
            : package;
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        };
    }
}

sealed class FakeUpdateHttpHandler : HttpMessageHandler
{
    private readonly Exception? _exception;
    private readonly TimeSpan _delay;
    private int _calls;

    public FakeUpdateHttpHandler(string responseJson) => ResponseJson = responseJson;
    public FakeUpdateHttpHandler(Exception exception) => _exception = exception;
    public FakeUpdateHttpHandler(TimeSpan delay)
    {
        _delay = delay;
        ResponseJson = "[]";
    }

    public string ResponseJson { get; set; } = "[]";
    public int Calls => Volatile.Read(ref _calls);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (_exception is not null)
        {
            throw _exception;
        }
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancellationToken);
        }
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(ResponseJson)
        };
    }
}

sealed class FakeRecorderBackendFactory : IScreenRecorderBackendFactory
{
    public FakeRecorderBackend Backend { get; } = new();
    public bool SendDuplicateCallbacks
    {
        get => Backend.SendDuplicateCallbacks;
        init => Backend.SendDuplicateCallbacks = value;
    }
    public bool CompleteOnStop
    {
        get => Backend.CompleteOnStop;
        init => Backend.CompleteOnStop = value;
    }
    public bool FailOnStop
    {
        get => Backend.FailOnStop;
        init => Backend.FailOnStop = value;
    }
    public int CallbackDelayMs
    {
        get => Backend.CallbackDelayMs;
        init => Backend.CallbackDelayMs = value;
    }
    public bool BlockStop
    {
        get => Backend.BlockStop;
        init => Backend.BlockStop = value;
    }

    public IScreenRecorderBackend Create(RecorderOptions options) => Backend;
}

sealed class FakeRecorderBackend : IScreenRecorderBackend
{
    private int _activeCalls;
    private int _maximumConcurrentCalls;
    private int _disposeCalls;
    private int _stopCalls;
    private string _path = string.Empty;
    private readonly ManualResetEventSlim _stopRelease = new(false);

    public event EventHandler<RecordingCompleteEventArgs>? Completed;
    public event EventHandler<RecordingFailedEventArgs>? Failed;
    public event EventHandler<RecordingStatusEventArgs>? StatusChanged;
    public ManualResetEventSlim RecordCalled { get; } = new(false);
    public ManualResetEventSlim StopEntered { get; } = new(false);
    public bool SendDuplicateCallbacks { get; set; }
    public bool CompleteOnStop { get; set; } = true;
    public bool FailOnStop { get; set; }
    public int CallbackDelayMs { get; set; }
    public bool BlockStop { get; set; }
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);
    public int StopCalls => Volatile.Read(ref _stopCalls);
    public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);
    public float InputVolume { get; private set; }
    public float OutputVolume { get; private set; }
    public bool RejectAudioChanges { get; set; }
    public bool SetAudioVolumes(float inputVolume, float outputVolume)
    {
        NativeCall(() => { if (!RejectAudioChanges) { InputVolume = inputVolume; OutputVolume = outputVolume; } });
        return !RejectAudioChanges;
    }

    public void Record(string path)
    {
        NativeCall(() =>
        {
            _path = path;
            RecordCalled.Set();
            StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecorderStatus.Recording));
        });
    }

    public void Pause() => NativeCall(() =>
        StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecorderStatus.Paused)));

    public void Resume() => NativeCall(() =>
        StatusChanged?.Invoke(this, new RecordingStatusEventArgs(RecorderStatus.Recording)));

    public void Stop()
    {
        NativeCall(() =>
        {
            Interlocked.Increment(ref _stopCalls);
            StopEntered.Set();
            if (BlockStop)
            {
                _stopRelease.Wait(TimeSpan.FromSeconds(3));
            }
            if (FailOnStop)
            {
                Failed?.Invoke(this, new RecordingFailedEventArgs("falha simulada", _path));
                return;
            }
            if (CompleteOnStop)
            {
                if (CallbackDelayMs > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(CallbackDelayMs);
                        CompleteLater();
                    });
                }
                else
                {
                    CompleteLater();
                }
            }
        });
    }

    public void ReleaseStop() => _stopRelease.Set();

    public void CompleteLater()
    {
        WriteValidMp4(_path);
        Completed?.Invoke(
            this,
            new RecordingCompleteEventArgs(_path, new List<FrameData>()));
        if (SendDuplicateCallbacks)
        {
            Failed?.Invoke(
                this,
                new RecordingFailedEventArgs("callback tardio", _path));
        }
    }

    public void Dispose()
    {
        NativeCall(() => Interlocked.Increment(ref _disposeCalls));
    }

    private void NativeCall(Action action)
    {
        var concurrent = Interlocked.Increment(ref _activeCalls);
        UpdateMaximum(concurrent);
        try
        {
            Thread.Sleep(15);
            action();
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }

    private void UpdateMaximum(int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maximumConcurrentCalls);
            if (current >= value ||
                Interlocked.CompareExchange(ref _maximumConcurrentCalls, value, current) == current)
            {
                return;
            }
        }
    }

    private static void WriteValidMp4(string path) => File.WriteAllBytes(
        path,
        [
            0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0,
            0, 0, 0, 12, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 1, 2, 3, 4,
            0, 0, 0, 8, (byte)'m', (byte)'o', (byte)'o', (byte)'v'
        ]);
}
