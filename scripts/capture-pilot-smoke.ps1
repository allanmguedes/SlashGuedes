$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'localization-source.ps1')

$xaml = Resolve-InterfaceResourcesForInspection (Get-Content 'src/SlashText/MainWindow.xaml' -Raw)
$code = Get-Content 'src/SlashText/MainWindow.xaml.cs' -Raw
$app = Get-Content 'src/SlashText/App.xaml' -Raw
$styles = Get-Content 'src/SlashText/Styles/VisualLab/CapturePilot.xaml' -Raw
$ruleDialog = Resolve-InterfaceResourcesForInspection (Get-Content 'src/SlashText/Views/CaptureRuleDialog.xaml' -Raw)
$ruleCode = Get-Content 'src/SlashText/Views/CaptureRuleDialog.xaml.cs' -Raw
$shortcutDialog = Resolve-InterfaceResourcesForInspection (Get-Content 'src/SlashText/Views/CaptureShortcutDialog.xaml' -Raw)
$shortcutCode = Get-Content 'src/SlashText/Views/CaptureShortcutDialog.xaml.cs' -Raw
$inlineEditor = Get-Content 'src/SlashText/Views/CaptureWorkbenchEditor.cs' -Raw
$advancedEditor = Get-Content 'src/SlashText/Views/CaptureEditorWindow.cs' -Raw
$emojiPicker = Get-Content 'src/SlashText/Views/CaptureEmojiPicker.cs' -Raw
$captureService = Get-Content 'src/SlashText/Services/CaptureService.cs' -Raw
$contextPanel = Get-Content 'src/SlashText/Views/CaptureEditorContextPanel.cs' -Raw
$document = Get-Content 'src/SlashText/Services/CaptureEditorDocument.cs' -Raw
$stampCatalog = Get-Content 'src/SlashText/Services/CaptureStampCatalog.cs' -Raw
foreach ($captureSourceName in @('code','ruleCode','shortcutCode','inlineEditor','advancedEditor','emojiPicker','captureService','contextPanel','document','stampCatalog')) {
    Set-Variable -Name $captureSourceName -Value (Resolve-InterfaceCallsForInspection (Get-Variable $captureSourceName -ValueOnly))
}

[xml]$null = $xaml
[xml]$null = $app
[xml]$null = $styles
[xml]$null = $ruleDialog
[xml]$null = $shortcutDialog

$orderedTabs = @(
    'x:Name="ShortcutsTabButton"',
    'x:Name="CaptureTabButton"',
    'x:Name="QuickAccentTabButton"',
    'x:Name="StatisticsTabButton"',
    'x:Name="SettingsTabButton"',
    'x:Name="AboutTabButton"'
)
$lastIndex = -1
foreach ($tab in $orderedTabs) {
    $index = $xaml.IndexOf($tab, [StringComparison]::Ordinal)
    if ($index -le $lastIndex) {
        throw "Ordem de navegação do contrato 3.3.0 divergente em: $tab"
    }
    $lastIndex = $index
}

foreach ($surface in @(
    'Text="Capture a ideia inteira."',
    'x:Name="CaptureNewButton"',
    'x:Name="CaptureImageMediaButton"',
    'x:Name="CaptureVideoMediaButton"',
    'x:Name="CaptureGifMediaButton"',
    'x:Name="CaptureMonitorModeButton"',
    'x:Name="CaptureRegionModeButton"',
    'x:Name="CaptureWindowModeButton"',
    'x:Name="CaptureScrollingModeButton"',
    'x:Name="CaptureGifModeButton"',
    'x:Name="CaptureRecordingCard"',
    'x:Name="CaptureVideoConfigurationPanel"',
    'x:Name="CaptureGifConfigurationPanel"',
    'x:Name="CaptureRuleCard"',
    'x:Name="CaptureHistoryPanel"',
    'x:Name="CaptureWorkbenchZoomBox"',
    'x:Name="CapturePostModeText"',
    'x:Name="CaptureInlineEditor"',
    'x:Name="CaptureHistoryScroller"',
    'Content="Expandir editor"',
    'x:Name="CaptureEditorContext"',
    'Click="OpenCaptureImage_OnClick"',
    'Click="CopyCapturePreview_OnClick"',
    'Click="SaveCapturePreview_OnClick"',
    'Click="CompleteCapturePreview_OnClick"'
)) {
    if (-not $xaml.Contains($surface)) {
        throw "Superfície funcional do piloto ausente: $surface"
    }
}

foreach ($behavior in @(
    'CaptureLauncherMode_OnClick',
    'CaptureMediaMode_OnClick',
    'StartSelectedCapture_OnClick',
    'CaptureActiveMonitor_OnClick(sender, e)',
    'CaptureRegion_OnClick(sender, e)',
    'CaptureWindow_OnClick(sender, e)',
    'CaptureScrolling_OnClick(sender, e)',
    'StartMp4Recording_OnClick(sender, e)',
    'StartGifRecording_OnClick(sender, e)',
    'TryReadCaptureSettings(out var error)',
    'new CaptureRuleDialog(_settings.Capture)',
    'LoadCaptureWorkbenchImage(',
    'BuildCaptureHistoryCard(item)',
    'SelectCaptureWorkbenchTool_OnClick',
    'ScrollCaptureHistoryBy(',
    'CaptureNewButtonText.Text = "Novo"',
    'SetCaptureEditorExpanded(!_captureEditorExpanded)',
    '_captureService.ImageEditor = EditNewCaptureInWorkbenchAsync',
    'CaptureInlineEditor.MarkSaved()'
)) {
    if (-not $code.Contains($behavior)) {
        throw "Comando real do piloto ausente: $behavior"
    }
}

foreach ($mediaIcon in @(
    'Kind="Camera"',
    'Kind="Video"',
    'Kind="Film"'
)) {
    if (-not $xaml.Contains($mediaIcon)) {
        throw "Ícone do seletor de mídia ausente: $mediaIcon"
    }
}

foreach ($shortcutField in @(
    'x:Name="MonitorBox"',
    'x:Name="RegionBox"',
    'x:Name="WindowBox"',
    'x:Name="ScrollingBox"'
)) {
    if (-not $shortcutDialog.Contains($shortcutField)) {
        throw "O diálogo deixou de exibir todos os atalhos: $shortcutField"
    }
}

if (-not $shortcutDialog.Contains('lab:LabMotion.Entrance="Popup"')) {
    throw 'O diálogo de atalhos perdeu a entrada animada do contrato.'
}

foreach ($style in @(
    'Lab.Pilot.ShellHeader',
    'Lab.Pilot.NavigationButton',
    'Lab.Pilot.CommandBar',
    'Lab.Pilot.Segment',
    'Lab.Pilot.ModeCard',
    'Lab.Pilot.ToolButton',
    'Lab.Pilot.ToolGlyph',
    'Lab.Pilot.NewButton',
    'Lab.Pilot.PrimaryButton',
    'Lab.Pilot.EditorTool',
    'Lab.Pilot.Card'
)) {
    if (-not $styles.Contains("x:Key=`"$style`"")) {
        throw "Estilo opt-in do piloto ausente: $style"
    }
}

foreach ($inlineContract in @(
    'CaptureAnnotationRenderer.Render(',
    'CaptureAnnotationKind.Pencil',
    'CaptureAnnotationKind.Highlighter',
    'CaptureAnnotationKind.Arrow',
    'CaptureAnnotationKind.Rectangle',
    'CaptureAnnotationKind.Text',
    'CaptureAnnotationKind.Stamp',
    'public void Undo()',
    'public void Redo()',
    'public void SetZoom(double zoom)',
    '_viewbox.Width = _surface.Width * fit * _zoom',
    '_viewbox.Height = _surface.Height * fit * _zoom'
)) {
    if (-not $inlineEditor.Contains($inlineContract)) {
        throw "Contrato do editor integrado ausente: $inlineContract"
    }
}

if ($code.Contains('CaptureInlineEditor.LayoutTransform =') -or
    -not $code.Contains('CaptureInlineEditor.SetZoom(zoom)')) {
    throw 'O zoom deve agir dentro do viewport, sem redimensionar o editor inteiro.'
}
[xml]$captureMarkup = $xaml
$ns = New-Object System.Xml.XmlNamespaceManager($captureMarkup.NameTable)
$ns.AddNamespace('p', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$newLabel = $captureMarkup.SelectSingleNode('//p:TextBlock[@x:Name="CaptureNewButtonText"]', $ns)
if ($newLabel.Foreground -ne '{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}' -or
    -not $styles.Contains('Value="{DynamicResource Lab.capture-primary}"')) {
    throw 'Novo perdeu a cor dedicada ou o contraste do texto/ícone no tema ativo.'
}
foreach ($palette in @(
    @{ File = 'Light'; Color = '#337c8f' },
    @{ File = 'Black'; Color = '#74d1e5' }
)) {
    [xml]$theme = Get-Content "src/SlashText/Styles/VisualLab/$($palette.File).xaml" -Raw
    $brush = $theme.ResourceDictionary.SolidColorBrush | Where-Object { $_.Key -eq 'Lab.capture-primary' }
    if ($brush.Color -ne $palette.Color) {
        throw "A cor do Novo diverge da referência visual enviada: $($palette.File)"
    }
}

foreach ($label in @(
    @{ Markup = $captureMarkup; Text = 'Concluir' },
    @{ Markup = [xml]$ruleDialog; Text = 'Salvar configurações' },
    @{ Markup = [xml]$shortcutDialog; Text = 'Salvar atalhos' }
)) {
    $labelNs = New-Object System.Xml.XmlNamespaceManager($label.Markup.NameTable)
    $labelNs.AddNamespace('p', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
    $text = $label.Markup.SelectSingleNode("//p:TextBlock[@Text='$($label.Text)']", $labelNs)
    if ($text.Foreground -ne '{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}' -or
        $text.ParentNode.ParentNode.Style -ne '{StaticResource Lab.Pilot.PrimaryButton}') {
        throw "Botão principal sem cor/contraste do piloto: $($label.Text)"
    }
}
if ($inlineEditor.Contains('FontFamily = new FontFamily("Segoe UI Emoji")') -or
    -not $contextPanel.Contains('CaptureStampCatalog.Current.CreateImageSource(item.Value)') -or
    -not $emojiPicker.Contains('Search(search.Text, selectedCategory)') -or
    -not $stampCatalog.Contains('NotoEmojiCatalog.CreateImageSource(value)') -or
    -not $advancedEditor.Contains('CaptureEmojiPicker.Show(this)')) {
    throw 'Os editores precisam compartilhar catálogo e assets Noto na prévia e exportação.'
}
if ([regex]::IsMatch($advancedEditor, 'FindResource\("(?:PrimaryButton|SettingsCard|CanvasBrush|InkBrush|AccentBrush)"\)') -or
    -not $advancedEditor.Contains('Lab.Pilot.EditorTool')) {
    throw 'Editor avançado ainda usa o tema/controles antigos.'
}
if (-not $code.Contains('_captureService.SaveEditedImageAsync(') -or
    -not $captureService.Contains('public async Task<CaptureRecord> SaveEditedImageAsync(')) {
    throw 'Salvar/Concluir não registra a edição real no histórico.'
}

foreach ($contract in @('CaptureEditorDocument', 'public bool HasUnsavedChanges', 'public void MarkSaved()', 'public void DiscardChanges()', 'public bool Crop(', 'public void Resize(')) {
    if (-not $document.Contains($contract)) { throw "Documento único incompleto: $contract" }
}
if ($code.Contains('CaptureInlineEditor.LoadImage(edited)') -or $code.Contains('new CaptureEditorWindow(current)')) {
    throw 'Expandir não pode rasterizar a sessão ou criar um segundo editor.'
}
foreach ($contract in @('Fonts.SystemFontFamilies', 'TextFontFamily', 'TextItalic', 'Manter proporção', 'ShowMoreTools', 'Aplicar recorte', 'Ver todos')) {
    if (-not $contextPanel.Contains($contract)) { throw "Ferramenta contextual ausente: $contract" }
}
if ($xaml.Contains('Click="EditCapturePreview_OnClick"')) {
    throw 'A toolbar rápida ainda abre diretamente o editor legado.'
}

if (-not $xaml.Contains('Orientation="Horizontal"') -or
    -not $xaml.Contains('HorizontalScrollBarVisibility="Hidden"')) {
    throw 'Recentes não está configurado como faixa horizontal.'
}

if (-not $app.Contains('Source="Styles/VisualLab/CapturePilot.xaml"')) {
    throw 'O aplicativo não carrega o estilo do piloto de Captura.'
}

foreach ($modalContract in @(
    @{ Content = $ruleDialog; Token = 'x:Class="SlashText.Views.CaptureRuleDialog"' },
    @{ Content = $ruleDialog; Token = 'Text="Configurações de captura"' },
    @{ Content = $ruleDialog; Token = 'Text="Salvar configurações"' },
    @{ Content = $ruleCode; Token = 'DialogResult = true' },
    @{ Content = $ruleDialog; Token = 'Header="Atalhos"' },
    @{ Content = $ruleDialog; Token = 'Header="Vídeo e GIF"' },
    @{ Content = $ruleDialog; Token = 'Header="OCR"' },
    @{ Content = $ruleDialog; Token = 'x:Name="ScrollingBox"' },
    @{ Content = $ruleCode; Token = 'GlobalCaptureShortcutService.IsValid' }
)) {
    if (-not $modalContract.Content.Contains($modalContract.Token)) {
        throw "Contrato modal ausente: $($modalContract.Token)"
    }
}

if ($xaml.Contains('CaptureRuleCard.BringIntoView()') -or
    $code.Contains('CaptureRuleCard.BringIntoView()')) {
    throw 'Regra de captura ainda navega para o formulário antigo.'
}

foreach ($shortcutText in @(
    'x:Name="CaptureMonitorShortcutText"',
    'x:Name="CaptureRegionShortcutText"',
    'x:Name="CaptureWindowShortcutText"',
    'x:Name="CaptureScrollingShortcutText"'
)) {
    if (-not $xaml.Contains($shortcutText)) {
        throw "Atalho compacto ausente: $shortcutText"
    }
}

if ([regex]::IsMatch($xaml, '#[0-9A-Fa-f]{6,8}') -or
    [regex]::IsMatch($styles, '#[0-9A-Fa-f]{6,8}')) {
    throw 'O piloto deve usar apenas tokens semânticos, sem cores fixas.'
}

'PASS: shell, ordem das abas, superfície de Captura, comandos reais e tokens do piloto 3.3.0.'
