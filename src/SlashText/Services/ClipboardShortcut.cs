namespace SlashText.Services;

// Two modifiers required: never take over common Ctrl+C/V or Win+V.
public static class ClipboardShortcut
{
    public const string Default = "Win+Shift+V";
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static bool TryParse(string? value, out string normalized,
        out uint modifiers, out uint virtualKey)
    {
        normalized = Default;
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var segments = value.Split('+', StringSplitOptions.TrimEntries);
        if (segments.Length < 3 || segments.Length > 5 || segments.Any(string.IsNullOrEmpty))
            return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modifiersOrder = new[] { "Win", "Ctrl", "Alt", "Shift" };
        foreach (var segment in segments[..^1])
        {
            var current = segment.ToLowerInvariant() switch
            {
                "windows" or "win" => "Win",
                "control" or "ctrl" => "Ctrl",
                "alt" => "Alt",
                "shift" => "Shift",
                _ => ""
            };
            if (current.Length == 0 || !seen.Add(current)) return false;
            modifiers |= current switch
            {
                "Win" => ModWin,
                "Ctrl" => ModControl,
                "Alt" => ModAlt,
                _ => ModShift
            };
        }
        if (seen.Count < 2) return false;
        var key = segments[^1].Trim().ToUpperInvariant();
        if (key.Length == 1 && ((key[0] >= 'A' && key[0] <= 'Z') ||
                                (key[0] >= '0' && key[0] <= '9')))
            virtualKey = key[0];
        else if (key.Length is 2 or 3 && key[0] == 'F' &&
                 int.TryParse(key[1..], out var function) &&
                 function is >= 1 and <= 12)
            virtualKey = (uint)(0x70 + function - 1);
        else return false;
        normalized = string.Join("+", modifiersOrder.Where(seen.Contains).Append(key));
        return true;
    }
}
