$ErrorActionPreference = 'Stop'

$xamlPath = 'src/SlashText/MainWindow.xaml'
$codePath = 'src/SlashText/MainWindow.xaml.cs'
$editorPath = 'src/SlashText/Views/CaptureEditorWindow.cs'
$regionPath = 'src/SlashText/Views/RegionCaptureWindow.cs'
$variablePath = 'src/SlashText/Views/VariableInputWindow.cs'
$keyboardPath = 'src/SlashText/Services/KeyboardHookService.cs'
$resourcesPath = 'src/SlashText/App.xaml'
$foundationPath = 'src/SlashText/Styles/Foundation.xaml'
$iconsPath = 'src/SlashText/Styles/Icons.xaml'
$componentsPath = 'src/SlashText/Styles/Components.xaml'
$designGuidePath = 'docs/design-system.md'
$projectPath = 'src/SlashText/SlashText.csproj'
$themePath = 'src/SlashText/Services/ThemeService.cs'
$importPath = 'src/SlashText/Services/SnippetImportService.cs'
$backupPath = 'src/SlashText/Services/BackupService.cs'
$recordingPath = 'src/SlashText/Services/ScreenRecordingService.cs'
$recordingBackendPath = 'src/SlashText/Services/ScreenRecorderBackend.cs'
$gifPath = 'src/SlashText/Services/GifRecordingService.cs'
$recordingBarPath = 'src/SlashText/Views/RecordingControlWindow.cs'
$gifPreviewPath = 'src/SlashText/Views/GifPreviewWindow.cs'

. (Join-Path $PSScriptRoot 'localization-source.ps1')
$xaml = Resolve-InterfaceResourcesForInspection (Get-Content $xamlPath -Raw)
$code = Get-Content $codePath -Raw
Get-ChildItem (Join-Path $PSScriptRoot '../src/SlashText') -Filter 'MainWindow.*.cs' | ForEach-Object { $code += "`n" + (Get-Content $_.FullName -Raw) }
$editor = Get-Content $editorPath -Raw
$region = Get-Content $regionPath -Raw
$variable = Get-Content $variablePath -Raw
$keyboard = Get-Content $keyboardPath -Raw
$resources = Get-Content $resourcesPath -Raw
$foundation = Get-Content $foundationPath -Raw
$icons = Get-Content $iconsPath -Raw
$components = Get-Content $componentsPath -Raw
$designGuide = Get-Content $designGuidePath -Raw
$project = Get-Content $projectPath -Raw
$theme = Get-Content $themePath -Raw
$import = Get-Content $importPath -Raw
$backup = Get-Content $backupPath -Raw
$recording = Get-Content $recordingPath -Raw
$recordingBackend = Get-Content $recordingBackendPath -Raw
$recordingCode = $recording + $recordingBackend
$gif = Get-Content $gifPath -Raw
$recordingBar = Get-Content $recordingBarPath -Raw
$gifPreview = Get-Content $gifPreviewPath -Raw

[xml]$null = $xaml
[xml]$null = $resources
[xml]$null = $foundation
[xml]$null = $icons
[xml]$null = $components

$handlers = [regex]::Matches(
    $xaml,
    '(?:Click|Loaded|Closing|Closed|Activated|SizeChanged|StateChanged|TextChanged|SelectionChanged|ValueChanged|Checked|Unchecked|LostFocus|MouseLeftButtonDown)="([A-Za-z_][A-Za-z0-9_]*)"'
) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

foreach ($handler in $handlers) {
    if (-not $code.Contains("$handler(")) {
        throw "Handler XAML ausente: $handler"
    }
}

$recorderCount = ([regex]::Matches($xaml, '<views:ShortcutRecorderBox')).Count
if ($recorderCount -ne 4) {
    throw "Esperados 4 gravadores de atalho; encontrados: $recorderCount"
}
foreach ($captureRecorder in @(
    'CaptureMonitorShortcutBox',
    'CaptureRegionShortcutBox',
    'CaptureWindowShortcutBox',
    'CaptureScrollingShortcutBox'
)) {
    if (-not $xaml.Contains(('x:Name="{0}"' -f $captureRecorder))) {
        throw "Gravador de atalho de captura ausente: $captureRecorder"
    }
}

foreach ($tool in @(
    'Arrow',
    'Highlighter',
    'Rectangle',
    'Ellipse',
    'Pencil',
    'Text',
    'Number'
)) {
    if (-not $editor.Contains("CaptureAnnotationKind.$tool")) {
        throw "Ferramenta ausente no editor: $tool"
    }
}

if (-not $code.Contains('SelectAndEditRegion(') -or
    -not $code.Contains('_settings.Capture.IncludeCursor') -or
    -not $code.Contains('ProcessEditedRegionAsync(')) {
    throw 'O fluxo de região não edita e processa no mesmo overlay.'
}

foreach ($resource in @(
    'PanelBrush',
    'ChromeBrush',
    'AccentSubtleBrush',
    'NavButton',
    'StatusPill',
    'FeatureTile'
)) {
    if (-not $resources.Contains("`"$resource`"")) {
        throw "Recurso do design system ausente: $resource"
    }
}

foreach ($shellElement in @(
    '<RowDefinition Height="34"/>',
    '<RowDefinition Height="66"/>',
    '<RowDefinition Height="52"/>',
    'Style="{StaticResource Lab.Pilot.ShellHeader}"',
    'Style="{StaticResource Lab.Pilot.NavigationBar}"',
    'Crie, organize e edite seus textos prontos.',
    'Style="{StaticResource Lab.Pilot.NavigationButton}"'
)) {
    if (-not $xaml.Contains($shellElement)) {
        throw "Shell visual novo ausente: $shellElement"
    }
}

$namedControls = [regex]::Matches($xaml, 'x:Name="([A-Za-z_][A-Za-z0-9_]*)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique
if ($namedControls.Count -lt 129) {
    throw "Inventário de controles regrediu: $($namedControls.Count), mínimo 129"
}
if ($handlers.Count -lt 56) {
    throw "Inventário de handlers regrediu: $($handlers.Count), mínimo 56"
}

foreach ($shortcutStructure in @(
    'x:Name="SearchBox"',
    'x:Name="CategoriesPanel"',
    'x:Name="MostUsedPanel"',
    'x:Name="SnippetListPanel"',
    'x:Name="SnippetCountText"',
    'x:Name="ShortcutEditorPanel"',
    'x:Name="ShortcutVariablesPanel"'
)) {
    if (-not $xaml.Contains($shortcutStructure)) {
        throw "Estrutura independente de Atalhos ausente: $shortcutStructure"
    }
}

foreach ($shortcutBehavior in @(
    'item.Content.Contains(query',
    '_selectedCategory',
    '_showMostUsed',
    'DisplayAll_OnClick',
    'DisplayMostUsed_OnClick'
)) {
    if (-not $code.Contains($shortcutBehavior)) {
        throw "Comportamento do piloto de Atalhos ausente: $shortcutBehavior"
    }
}

foreach ($splitterRequirement in @(
    'Style="{StaticResource WorkspaceColumnSplitter}"',
    'ResizeBehavior="PreviousAndNext"',
    'Property="ShowsPreview" Value="False"',
    'ShortcutSplitter_OnPreviewKeyDown',
    'ShortcutSplitter_OnMouseDoubleClick',
    'ShortcutSplitter_OnDragStarted',
    'ShortcutSplitter_OnDragCompleted',
    'ShortcutLeftMinimum = 220',
    'ShortcutLeftMaximum = 460',
    'ShortcutRightMinimum = 240',
    'ShortcutRightMaximum = 480',
    'ShortcutSplitterStep = 16',
    'ShortcutEditorColumn.MinWidth = editorMinimum'
)) {
    if (-not ($xaml.Contains($splitterRequirement) -or
              $code.Contains($splitterRequirement) -or
              $resources.Contains($splitterRequirement) -or
              $foundation.Contains($splitterRequirement))) {
        throw "Divisor responsivo de Atalhos ausente: $splitterRequirement"
    }
}

foreach ($dictionary in @(
    'Styles/Foundation.xaml',
    'Styles/Icons.xaml',
    'Styles/Components.xaml',
    'Styles/VisualLab/Components.xaml',
    'Styles/VisualLab/CapturePilot.xaml'
)) {
    if (-not $resources.Contains("Source=`"$dictionary`"")) {
        throw "O App.xaml não carrega o recurso do design system: $dictionary"
    }
}

foreach ($token in @(
    'FontFamily.Body',
    'FontSize.Title',
    'Space.4',
    'Radius.Control',
    'Padding.Card'
)) {
    if (-not $foundation.Contains("x:Key=`"$token`"")) {
        throw "Token do design system ausente: $token"
    }
}

foreach ($component in @(
    'AppShellHeader',
    'AppNavigationBar',
    'AppNavigationButton',
    'AppWorkspace',
    'WorkspaceSidebar',
    'PageHeading',
    'FieldLabel',
    'SubtlePanel',
    'FluentSectionHeading',
    'FluentSupportingText'
)) {
    if (-not $foundation.Contains("x:Key=`"$component`"")) {
        throw "Componente do design system ausente: $component"
    }
}

foreach ($component in @(
    'ToggleSwitch',
    'FluentSettingsRow',
    'FluentActionCard'
)) {
    if (-not $components.Contains("x:Key=`"$component`"")) {
        throw "Componente Fluent ausente: $component"
    }
}

if (([regex]::Matches($xaml, 'x:Name="CheckUpdatesCheckBox"')).Count -ne 1) {
    throw 'A preferência de atualização automática deve aparecer uma única vez.'
}

foreach ($trueBlackToken in @('#000000', '#0B0B0B', '#050505')) {
    if (-not $theme.Contains($trueBlackToken)) {
        throw "Token do tema preto ausente: $trueBlackToken"
    }
}

foreach ($referenceStyle in @(
    'Style="{StaticResource WorkspaceSidebar}"',
    'Style="{StaticResource SidebarSectionTitle}"',
    'Style="{StaticResource FieldLabel}"',
    'Style="{StaticResource SubtlePanel}"'
)) {
    if (-not $xaml.Contains($referenceStyle)) {
        throw "Tela Atalhos não adotou o design system: $referenceStyle"
    }
}

if ([regex]::IsMatch($xaml, '#[0-9A-Fa-f]{6,8}')) {
    throw 'MainWindow.xaml contém cor fixa; use um token semântico.'
}

foreach ($guideSection in @(
    '## 1. Princípios',
    '## 3. Cores semânticas',
    '## 8. Captura e gravação',
    '## 10. Checklist por PR'
)) {
    if (-not $designGuide.Contains($guideSection)) {
        throw "Style guide incompleto: $guideSection"
    }
}

foreach ($versionElement in @(
    '<Version>3.2.0</Version>',
    '<AssemblyVersion>3.2.0.0</AssemblyVersion>',
    '<FileVersion>3.2.0.0</FileVersion>'
)) {
    if (-not $project.Contains($versionElement)) {
        throw "Versão 3.2.0 inconsistente: $versionElement"
    }
}

foreach ($style in @(
    'NavigationIcon',
    'ActionIcon',
    'IconButton',
    'ToggleSwitch',
    'LoadingProgress'
)) {
    if (-not $components.Contains("x:Key=`"$style`"")) {
        throw "Componente reutilizável ausente: $style"
    }
}

foreach ($geometry in @(
    'Icon.Shortcuts',
    'Icon.QuickAccent',
    'Icon.Capture',
    'Icon.Statistics',
    'Icon.Settings',
    'Icon.About'
)) {
    if (-not $icons.Contains("x:Key=`"$geometry`"")) {
        throw "Ícone vetorial de navegação ausente: $geometry"
    }
}

foreach ($updateControl in @(
    'SettingsVersionText',
    'SettingsLastUpdateCheckText',
    'SettingsLastUpdateResultText',
    'SettingsCheckUpdatesButton'
)) {
    if (-not $xaml.Contains("x:Name=`"$updateControl`"")) {
        throw "Controle de atualização ausente: $updateControl"
    }
}

if (-not $xaml.Contains('IsEditable="False"') -or
    -not $code.Contains('RecordingPresetCatalog.GifFps')) {
    throw 'FPS do GIF deve permanecer em lista fechada de presets.'
}

$recordingInfrastructure = (Get-Content 'src/SlashText/Services/RecordingInfrastructure.cs' -Raw) -replace 'UiText.Get\(("(?:\\.|[^"\\])*")\)', '$1'
foreach ($preset in @(
    'new("Recomendado", 10,',
    'new("Equilibrado", 20,',
    'new("Fluido", 30,'
)) {
    if (-not $recordingInfrastructure.Contains($preset)) {
        throw "Preset obrigatório de GIF ausente: $preset"
    }
}
foreach ($forbiddenPreset in @(', 5,', ', 15,', ', 60,')) {
    if ($recordingInfrastructure.Contains($forbiddenPreset)) {
        throw "Preset proibido de GIF presente: $forbiddenPreset"
    }
}

if ($resources.Contains('PopupAnimation="Fade"') -or
    $resources.Contains('PopupAnimation="Slide"')) {
    throw 'Animação de popup não essencial deve permanecer desativada.'
}

if (-not $project.Contains('ScreenRecorderLib') -or
    -not $recordingCode.Contains('Recorder.CreateRecorder') -or
    -not $recordingCode.Contains('H264VideoEncoder') -or
    -not $recordingCode.Contains('.Pause()') -or
    -not $recordingCode.Contains('.Resume()') -or
    -not $recordingCode.Contains('.Stop()')) {
    throw 'Gravação MP4 local incompleta.'
}

if ($recording.Contains('CleanupRecorder') -or
    -not $recording.Contains('EnqueueNative("recording.finalize"') -or
    -not $recording.Contains('Interlocked.CompareExchange(ref _finalizationClaimed, 1, 0)') -or
    -not $recording.Contains('IsHardwareEncodingEnabled = true') -or
    -not $recording.Contains('IsFixedFramerate = false') -or
    -not $recording.Contains('IsLogEnabled = false')) {
    throw 'Ciclo de finalização MP4 inseguro ou dependente de aceleração de hardware.'
}

foreach ($control in @(
    'RecordingTargetBox',
    'RecordingFpsBox',
    'RecordingQualityBox',
    'RecordingCursorCheckBox',
    'StartMp4RecordingButton',
    'GifFpsBox',
    'GifQualityBox',
    'GifFpsDescriptionText',
    'GifQualityDescriptionText',
    'RecordingQualityDescriptionText',
    'StartGifRecordingButton',
    'CaptureDelayBox',
    'CaptureCursorCheckBox',
    'CaptureEditorCheckBox',
    'CaptureHistoryFilterBox',
    'CaptureRetentionBox'
)) {
    if (-not $xaml.Contains("x:Name=`"$control`"")) {
        throw "Controle de captura/gravação ausente: $control"
    }
}

if (-not $gif.Contains('GifBitmapEncoder') -or
    -not $gif.Contains('EnsureLoopExtension(temporaryPath)') -or
    -not $gif.Contains('"NETSCAPE2.0"u8') -or
    -not $gif.Contains('File.Move(temporaryPath, path)') -or
    -not $recording.Contains('ValidateMp4File(recordedPath)') -or
    -not $recording.Contains('recording.mp4') -or
    -not $gifPreview.Contains('Prévia antes de salvar') -or
    -not $recordingBar.Contains('Finalizar')) {
    throw 'Correções críticas de GIF ou MP4 incompletas.'
}

$windowTargetIndex = $code.LastIndexOf('var target = ResolveRecordingTarget(targetKind);')
$hideBeforeWindowIndex = if ($windowTargetIndex -ge 0) {
    $code.LastIndexOf('Hide();', $windowTargetIndex)
} else {
    -1
}
if ($hideBeforeWindowIndex -lt 0 -or
    $windowTargetIndex - $hideBeforeWindowIndex -gt 300 -or
    -not $code.Contains('RecordingTargetKind.Window => _captureService.SelectRecordingWindow(this')) {
    throw 'A seleção de janela MP4 deve ocorrer após ocultar o SlashDesk.'
}

foreach ($historyAction in @(
    'OpenHistoryItem_OnClick',
    'CopyHistoryItem_OnClick',
    'EditHistoryItem_OnClick',
    'DeleteHistoryItem_OnClick',
    'CleanCaptureHistory_OnClick'
)) {
    if (-not $code.Contains("$historyAction(")) {
        throw "Ação de histórico ausente: $historyAction"
    }
}

foreach ($editorTool in @(
    'CaptureAnnotationKind.Blur',
    'CaptureAnnotationKind.Pixelate',
    'ConfigureResize()',
    '_cropRect'
)) {
    if (-not $editor.Contains($editorTool)) {
        throw "Melhoria do editor ausente: $editorTool"
    }
}

foreach ($brush in @(
    'CanvasBrush',
    'SurfaceBrush',
    'PanelBrush',
    'ChromeBrush',
    'InputBrush',
    'InkBrush',
    'MutedBrush',
    'DividerBrush',
    'AccentBrush'
)) {
    if (-not $theme.Contains("Set(`"$brush`"")) {
        throw "Tema não atualiza o recurso: $brush"
    }
}

if (-not $code.Contains('button.Tag = selected ? "Selected" : null')) {
    throw 'A navegação não mantém estado visual selecionado.'
}

if (-not $editor.Contains('UpdateToolSelection()')) {
    throw 'O editor não informa visualmente a ferramenta selecionada.'
}

foreach ($captureElement in @(
    'CaptureVirtualDesktopBitmap()',
    'UpdateShade(',
    'PositionHandles()',
    'RequestToolbarPosition()',
    'Refazer seleção',
    'EditedBitmap',
    'AddInlineTextEditor(',
    'Undo()',
    'Redo()'
)) {
    if (-not $region.Contains($captureElement)) {
        throw "Seleção de região sem o elemento Snipping Tool: $captureElement"
    }
}

foreach ($toolbarElement in @(
    'MonitorWorkAreaProvider.FromSelection',
    'SelectionInPhysicalPixels()',
    '_toolbarLayout.Width',
    'SetWindowPos(',
    'capture.toolbar-positioned'
)) {
    if (-not $region.Contains($toolbarElement)) {
        throw "Barra de marcações sem contenção per-monitor: $toolbarElement"
    }
}

foreach ($tool in @(
    'Arrow',
    'Highlighter',
    'Rectangle',
    'Ellipse',
    'Pencil',
    'Text',
    'Number'
)) {
    if (-not $region.Contains("CaptureAnnotationKind.$tool")) {
        throw "Ferramenta ausente durante a seleção: $tool"
    }
}

if ($xaml.Contains('Á  Acento Rápido') -or
    -not $xaml.Contains('Text="Acento Rápido"')) {
    throw 'Nome da aba Acento Rápido incorreto.'
}

foreach ($label in @(
    'Seta',
    'Marca-texto',
    'Retângulo',
    'Elipse',
    'Lápis',
    'Texto',
    'Número',
    'Capturar'
)) {
    if (-not $region.Contains("`"$label`"")) {
        throw "Barra de captura sem rótulo legível: $label"
    }
}

foreach ($themeElement in @(
    'ThemeService.IsDark',
    '_isDark ? "#F2121922" : "#F8FFFFFF"',
    'CaptureToolbarSurfaceBrush',
    'CaptureToolbarElevatedBrush',
    'CaptureToolbarAccentBrush'
)) {
    if (-not $region.Contains($themeElement)) {
        throw "Overlay de região sem variante clara/escura: $themeElement"
    }
}

if (-not $region.Contains('_annotationLayer.MouseLeftButtonDown += OnAnnotationMouseDown') -or
    -not $region.Contains('BeginAnnotation(canvasPoint)')) {
    throw 'A camada selecionada não recebe a edição inline diretamente.'
}

if ($variable.Contains('window.SourceInitialized +=') -or
    -not $variable.Contains('new WindowInteropHelper(window).Owner = targetWindow;')) {
    throw 'O proprietário do diálogo de variáveis não é definido antes de ShowDialog.'
}

if (-not $keyboard.Contains('ToUnicodeNoStateChange') -or
    -not $keyboard.Contains('ToUnicodeNoStateChange,')) {
    throw 'A tradução de teclado ainda pode alterar o estado de teclas mortas ABNT.'
}

foreach ($control in @(
    'QuickAccentPreviewChoice0',
    'QuickAccentDelayBox',
    'QuickAccentDelayPreset100Button',
    'QuickAccentDelayPreset200Button',
    'QuickAccentDelayPreset500Button',
    'CapturePreviewImage',
    'CaptureTotalText',
    'CaptureRegionTotalText',
    'AverageCharactersText',
    'ImportSourceBox',
    'BackupSummaryText'
)) {
    if (-not $xaml.Contains("x:Name=`"$control`"")) {
        throw "Controle do novo layout ausente: $control"
    }
}

foreach ($source in @(
    'SnippetImportSource.SlashDesk',
    'SnippetImportSource.TextBlaze',
    'SnippetImportSource.Espanso',
    'ConvertTextBlazeVariables',
    'EspansoReplacePattern'
)) {
    if (-not $import.Contains($source)) {
        throw "Importador ausente ou incompleto: $source"
    }
}

foreach ($backupFeature in @(
    'CreateManualSnapshot()',
    'RestoreSnapshot(',
    'CreateSnapshot(',
    'capture-history.json'
)) {
    if (-not $backup.Contains($backupFeature)) {
        throw "Backup ausente ou incompleto: $backupFeature"
    }
}

foreach ($handler in @(
    'ImportSnippets_OnClick',
    'CreateBackup_OnClick',
    'AnalyzeAssets_OnClick',
    'RestoreBackup_OnClick',
    'OpenBackupFolder_OnClick'
)) {
    if (-not $code.Contains("$handler(")) {
        throw "Fluxo de dados ausente: $handler"
    }
}

$recorder = Get-Content 'src/SlashText/Views/ShortcutRecorderBox.cs' -Raw
if (-not $recorder.Contains('PreviewKeyUp += OnPreviewKeyUp') -or
    -not $recorder.Contains('Key.Snapshot')) {
    throw 'O gravador não trata Print Screen na liberação da tecla.'
}

$quickAccent = Get-Content 'src/SlashText/Services/QuickAccentService.cs' -Raw
if (-not $quickAccent.Contains('_activationDown')) {
    throw 'O Acento Rápido não bloqueia o auto-repeat da tecla de ativação.'
}

Write-Host "UI integrity smoke: OK ($($handlers.Count) handlers)"
