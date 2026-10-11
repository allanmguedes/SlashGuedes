using System.IO;
using System.Windows;
using System.Windows.Controls;
using SlashText.Models;
using SlashText.Services;
using Forms = System.Windows.Forms;

namespace SlashText.Views;

public partial class CaptureRuleDialog : Window
{
    private readonly RecordingSettings _recording;
    private readonly RecordingAudioPanel _audio;

    public CaptureSettings Result { get; private set; }
    public string ShortcutStatus { set => ShortcutStatusText.Text = value; }

    public CaptureRuleDialog(CaptureSettings settings)
    {
        InitializeComponent();
        _recording = settings.Recording ?? new RecordingSettings();
        _audio = new RecordingAudioPanel(_recording.Audio ?? new());
        RecordingAudioHost.Content = _audio;
        Result = settings;
        Width = Math.Min(700, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(740, SystemParameters.WorkArea.Height - 32);
        MonitorBox.Text = settings.ActiveMonitorShortcut;
        RegionBox.Text = settings.RegionShortcut;
        WindowBox.Text = settings.WindowShortcut;
        ScrollingBox.Text = settings.ScrollingShortcut;
        var ocr = (settings.Ocr ?? new()).Normalize();
        SelectByTag(OcrModelBox, ocr.Model);
        SelectByTag(OcrLanguageBox, ocr.Languages);
        SelectByTag(OcrLayoutBox, ocr.Layout);
        OcrImproveCheckBox.IsChecked = ocr.ImproveDifficultImages;
        SelectByTag(VideoFpsBox, _recording.VideoFps.ToString());
        SelectByTag(VideoQualityBox, RecordingPresetCatalog.NormalizeMp4Quality(_recording.VideoQuality));
        RecordingCursorBox.IsChecked = _recording.IncludeCursor;
        SelectByTag(GifFpsBox, RecordingPresetCatalog.NormalizeGifFps(_recording.GifFps).ToString());
        SelectByTag(GifQualityBox, RecordingPresetCatalog.NormalizeGifQuality(_recording.GifQuality).ToString());
        DirectoryBox.Text = settings.OutputDirectoryTemplate;
        FileNameBox.Text = settings.FileNameTemplate;
        QualityBox.Text = settings.JpegQuality.ToString();
        CopyCheckBox.IsChecked = settings.CopyToClipboard;
        SaveCheckBox.IsChecked = settings.SaveAutomatically;
        CursorCheckBox.IsChecked = settings.IncludeCursor;
        HideCheckBox.IsChecked = settings.HideSlashDeskDuringCapture;
        SelectByTag(AfterCaptureBox, settings.OpenEditorForMonitorAndWindow ? "Editor" : "Direct");
        SelectByTag(FormatBox, settings.ImageFormat);
        SelectByTag(DelayBox, settings.DelaySeconds.ToString());
        SelectByTag(RetentionBox, settings.HistoryRetentionDays.ToString());
        UpdateQualityState();
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        Loaded += (_, _) => ModalBackdrop.Attach(this, Surface, () => DialogResult = false);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }
        };
    }

    private void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        var expanded = Environment.ExpandEnvironmentVariables(DirectoryBox.Text.Trim());
        var resolved = CaptureService.ResolveDirectoryTemplate(expanded, DateTimeOffset.Now);
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = UiText.Get("Selecione a pasta onde as capturas serão salvas."),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(resolved)
                ? resolved
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            DirectoryBox.Text = dialog.SelectedPath;
        }
    }

    private void Format_OnChanged(object sender, SelectionChangedEventArgs e) => UpdateQualityState();

    private void UpdateQualityState()
    {
        if (QualityBox is not null && FormatBox is not null)
        {
            QualityBox.IsEnabled = SelectedTag(FormatBox, "PNG") == "JPEG";
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildResult(out var result, out var error))
        {
            AuxiliaryWindowTheme.ShowMessage(this, error, UiText.Get("Configurações de captura"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = result;
        DialogResult = true;
    }

    internal bool TryBuildResult(out CaptureSettings result, out string error)
    {
        result = Result; error = string.Empty;
        var shortcuts = new[] { MonitorBox.Text.Trim(), RegionBox.Text.Trim(), WindowBox.Text.Trim(), ScrollingBox.Text.Trim() };
        if (shortcuts.Any(item => !GlobalCaptureShortcutService.IsValid(item)))
        {
            error = UiText.Get("Use uma tecla, roda ou botão do mouse válido. A roda exige Ctrl, Alt, Shift ou Win.");
            Sections.SelectedIndex = 1; return false;
        }
        if (shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != shortcuts.Length)
        {
            error = UiText.Get("Cada ação precisa ter um atalho diferente.");
            Sections.SelectedIndex = 1; return false;
        }
        if (string.IsNullOrWhiteSpace(DirectoryBox.Text) || string.IsNullOrWhiteSpace(FileNameBox.Text))
        {
            error = UiText.Get("Informe a pasta e o modelo de nome do arquivo.");
            Sections.SelectedIndex = 0; return false;
        }
        var editor = SelectedTag(AfterCaptureBox, "Direct") == "Editor";
        var copy = CopyCheckBox.IsChecked == true;
        var save = SaveCheckBox.IsChecked == true;
        if (!editor && !copy && !save)
        {
            error = UiText.Get("No modo direto, ative Copiar após capturar, Salvar automaticamente ou ambas.");
            Sections.SelectedIndex = 0; return false;
        }
        var quality = int.TryParse(QualityBox.Text, out var parsed) ? Math.Clamp(parsed, 1, 100) : 90;
        result = new CaptureSettings
        {
            ActiveMonitorShortcut = shortcuts[0],
            RegionShortcut = shortcuts[1],
            WindowShortcut = shortcuts[2],
            ScrollingShortcut = shortcuts[3],
            OutputDirectoryTemplate = DirectoryBox.Text.Trim(),
            FileNameTemplate = FileNameBox.Text.Trim(),
            ImageFormat = SelectedTag(FormatBox, "PNG"),
            JpegQuality = quality,
            CopyToClipboard = copy,
            SaveAutomatically = save,
            HideSlashDeskDuringCapture = HideCheckBox.IsChecked == true,
            IncludeCursor = CursorCheckBox.IsChecked == true,
            OpenEditorForMonitorAndWindow = editor,
            DelaySeconds = SelectedInt(DelayBox, 0),
            HistoryRetentionDays = SelectedInt(RetentionBox, 90),
            Ocr = new CaptureOcrSettings
            {
                Model = SelectedTag(OcrModelBox, "Best"), Languages = SelectedTag(OcrLanguageBox, "por+eng"),
                Layout = SelectedTag(OcrLayoutBox, "Auto"), ImproveDifficultImages = OcrImproveCheckBox.IsChecked == true
            }.Normalize(),
            Recording = new RecordingSettings
            {
                Audio = _audio.ReadSettings(),
                VideoFps = SelectedInt(VideoFpsBox, 30), VideoQuality = SelectedTag(VideoQualityBox, "Alta"),
                IncludeCursor = RecordingCursorBox.IsChecked == true,
                GifFps = SelectedInt(GifFpsBox, 10), GifQuality = SelectedInt(GifQualityBox, 128),
                GifDurationSeconds = _recording.GifDurationSeconds, GifWidth = _recording.GifWidth
            }
        };
        return true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string SelectedTag(ComboBox box, string fallback) =>
        box.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : fallback;

    private static int SelectedInt(ComboBox box, int fallback) =>
        int.TryParse(SelectedTag(box, fallback.ToString()), out var value) ? value : fallback;

    private static void SelectByTag(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && tag.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = 0;
    }
}
