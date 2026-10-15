using System.IO;

namespace SlashText.Models;

public sealed class AppSettings
{
    public bool OnboardingCompleted { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ShowSuggestions { get; set; } = true;
    public string Theme { get; set; } = "System";
    // UI copy only; absent/unsupported values keep the existing Portuguese interface.
    public string Language { get; set; } = "pt-BR";
    // Optional visual preference; snippets.md and category names remain unchanged.
    public Dictionary<string, string> ShortcutCategoryIcons { get; set; } = new();
    public bool QuickAccentEnabled { get; set; }
    public string QuickAccentActivationKey { get; set; } = "Space";
    public string QuickAccentToolbarPosition { get; set; } = "BottomCenter";
    public bool QuickAccentShowUnicode { get; set; }
    public bool QuickAccentSortByUsage { get; set; } = true;
    public int QuickAccentInputDelayMs { get; set; } = 200;
    public string QuickAccentExcludedApps { get; set; } = string.Empty;
    public List<string> QuickAccentCharacterSets { get; set; } = ["PortugueseBrazil"];
    public CaptureSettings Capture { get; set; } = new();
    public bool ClipboardMonitoringEnabled { get; set; }
    public int ClipboardMaxEntries { get; set; } = 150;
}

public sealed class CaptureSettings
{
    public string ActiveMonitorShortcut { get; set; } = "Ctrl+Shift+PrintScreen";
    public string RegionShortcut { get; set; } = "Ctrl+Alt+PrintScreen";
    public string WindowShortcut { get; set; } = "Ctrl+Shift+WheelUp";
    public string ScrollingShortcut { get; set; } = "Ctrl+Shift+WheelDown";
    public string OutputDirectoryTemplate { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "SlashDesk", "{year}", "{month}");
    public string FileNameTemplate { get; set; } =
        "{date}_{time}_{type}_{app}";
    public string ImageFormat { get; set; } = "PNG";
    public int JpegQuality { get; set; } = 90;
    public bool CopyToClipboard { get; set; } = true;
    public bool SaveAutomatically { get; set; } = true;
    public bool HideSlashDeskDuringCapture { get; set; }
    public bool ShouldHideSlashDesk(bool windowIsVisible) =>
        HideSlashDeskDuringCapture && windowIsVisible;
    public int DelaySeconds { get; set; }
    public bool IncludeCursor { get; set; }
    public bool OpenEditorForMonitorAndWindow { get; set; }
    public int HistoryRetentionDays { get; set; } = 90;
    public CaptureOcrSettings Ocr { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
}

public sealed class RecordingSettings
{
    public RecordingAudioSettings Audio { get; set; } = new();
    public int VideoFps { get; set; } = 30;
    public string VideoQuality { get; set; } = "Alta";
    public bool IncludeCursor { get; set; } = true;
    public int GifFps { get; set; } = 10;
    // Legacy fields remain readable so existing settings.json files keep loading.
    // Continuous GIF recording no longer uses either value.
    public int GifDurationSeconds { get; set; } = 5;
    public int GifWidth { get; set; } = 960;
    public int GifQuality { get; set; } = 128;
}

public sealed class RecordingAudioSettings
{
    public bool CaptureComputer { get; set; } = true;
    public bool CaptureMicrophone { get; set; }
    public string OutputDeviceId { get; set; } = string.Empty;
    public string InputDeviceId { get; set; } = string.Empty;
    public float OutputVolume { get; set; } = 1;
    public float InputVolume { get; set; } = 1;

    public RecordingAudioSettings Copy() => new()
    {
        CaptureComputer = CaptureComputer,
        CaptureMicrophone = CaptureMicrophone,
        OutputDeviceId = OutputDeviceId ?? string.Empty,
        InputDeviceId = InputDeviceId ?? string.Empty,
        OutputVolume = float.IsFinite(OutputVolume) ? Math.Clamp(OutputVolume, 0, 1) : 1,
        InputVolume = float.IsFinite(InputVolume) ? Math.Clamp(InputVolume, 0, 1) : 1
    };
}
