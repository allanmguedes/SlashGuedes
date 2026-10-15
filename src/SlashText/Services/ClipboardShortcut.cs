using System.Globalization;

namespace SlashText.Services;

/// <summary>
/// Keyboard shortcut grammar for the Clipboard popup. Requires a real
/// Ctrl/Alt/Win modifier; Shift is optional but never sufficient by itself.
/// The last key is a Windows virtual-key code, not a keyboard-layout character,
/// so ABNT2 OEM keys, navigation, and numpad keys can be recorded faithfully.
/// </summary>
public static class ClipboardShortcut
{
    public const string Default = "Win+Shift+V";
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private static readonly Dictionary<uint, string> NamedKeys = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter",
        [0x13] = "Pause", [0x14] = "CapsLock", [0x1B] = "Escape",
        [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown",
        [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
        [0x6A] = "NumMultiply", [0x6B] = "NumAdd",
        [0x6D] = "NumSubtract", [0x6E] = "NumDecimal",
        [0x6F] = "NumDivide",
        [0xBA] = "Oem1", [0xBB] = "OemPlus", [0xBC] = "OemComma",
        [0xBD] = "OemMinus", [0xBE] = "OemPeriod",
        [0xBF] = "Oem2", [0xC0] = "Oem3",
        [0xDB] = "Oem4", [0xDC] = "Oem5",
        [0xDD] = "Oem6", [0xDE] = "Oem7",
        [0xDF] = "Oem8", [0xE2] = "Oem102"
    };
    private static readonly Dictionary<string, uint> AliasKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Del"] = 0x2E, ["Ins"] = 0x2D,
            ["PgUp"] = 0x21, ["PgDn"] = 0x22, ["PgDown"] = 0x22,
            ["Esc"] = 0x1B, ["Return"] = 0x0D,
            ["PrtSc"] = 0x2C, ["Print"] = 0x2C,
            ["Plus"] = 0xBB, ["Minus"] = 0xBD,
            ["Comma"] = 0xBC, ["Period"] = 0xBE,
            ["OemBackslash"] = 0xE2
        };

    public static bool TryParse(string? value, out string normalized,
        out uint modifiers, out uint virtualKey)
    {
        normalized = Default;
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var segments = value.Split('+', StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || segments.Length > 5 ||
            segments.Any(string.IsNullOrWhiteSpace)) return false;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new[] { "Win", "Ctrl", "Alt", "Shift" };
        foreach (var segment in segments[..^1])
        {
            var modifier = segment.ToLowerInvariant() switch
            {
                "win" or "windows" => "Win",
                "ctrl" or "control" => "Ctrl",
                "alt" => "Alt",
                "shift" => "Shift",
                _ => string.Empty
            };
            if (modifier.Length == 0 || !seen.Add(modifier)) return false;
            modifiers |= modifier switch
            {
                "Win" => ModWin,
                "Ctrl" => ModControl,
                "Alt" => ModAlt,
                _ => ModShift
            };
        }

        // Shift-only and single character shortcuts are never registered.
        if ((modifiers & (ModWin | ModControl | ModAlt)) == 0) return false;
        if (!TryGetVirtualKey(segments[^1], out virtualKey)) return false;
        if (!TryDescribeKey(virtualKey, out var name)) return false;

        normalized = string.Join("+", order.Where(seen.Contains).Append(name));
        return true;
    }

    public static bool TryDescribeKey(uint vk, out string name)
    {
        name = string.Empty;
        if (!IsAllowedFinalKey(vk)) return false;

        if (vk is >= 0x41 and <= 0x5A || vk is >= 0x30 and <= 0x39)
            name = ((char)vk).ToString();
        else if (vk is >= 0x70 and <= 0x87)
            name = "F" + (vk - 0x70 + 1);
        else if (vk is >= 0x60 and <= 0x69)
            name = "Num" + (vk - 0x60);
        else if (!NamedKeys.TryGetValue(vk, out name!))
            name = "VK_" + vk.ToString("X2", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryGetVirtualKey(string key, out uint vk)
    {
        vk = 0;
        var value = key.Trim();
        if (value.Length == 1 &&
            (value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            vk = char.ToUpperInvariant(value[0]);
            return true;
        }
        if (value.Length is >= 2 and <= 3 && (value[0] == 'F' || value[0] == 'f') &&
            int.TryParse(value[1..], out var f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }
        if (value.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            value.Length == 4 && char.IsAsciiDigit(value[3]))
        {
            vk = (uint)(0x60 + value[3] - '0');
            return true;
        }
        var match = NamedKeys.FirstOrDefault(pair =>
            pair.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match.Value is not null)
        {
            vk = match.Key;
            return true;
        }
        if (AliasKeys.TryGetValue(value, out vk)) return true;
        return value.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
               value.Length is >= 4 and <= 5 &&
               uint.TryParse(value[3..], NumberStyles.HexNumber,
                   CultureInfo.InvariantCulture, out vk) &&
               IsAllowedFinalKey(vk);
    }

    private static bool IsAllowedFinalKey(uint vk)
    {
        if (vk is < 0x08 or > 0xFE) return false;
        // Reject modifier keys, including their left/right variants, as the final key.
        return vk is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or
            0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);
    }
}
