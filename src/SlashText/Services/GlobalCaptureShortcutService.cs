using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SlashText.Services;

public enum CaptureShortcutAction
{
    ActiveMonitor,
    Region,
    Window,
    Scrolling
}

public sealed class CaptureShortcutEventArgs(CaptureShortcutAction action) : EventArgs
{
    public CaptureShortcutAction Action { get; } = action;
}

public sealed class GlobalCaptureShortcutService : IDisposable
{
    private const int HotKeyMessage = 0x0312;
    private const int MouseLowLevel = 14;
    private const int MouseWheel = 0x020A;
    private const int MouseMiddleDown = 0x0207;
    private const int MouseXDown = 0x020B;
    private const int WheelUp = 1;
    private const int WheelDown = -1;
    private const int MouseMiddle = 10;
    private const int MouseX1 = 11;
    private const int MouseX2 = 12;
    public const uint ModNoRepeat = 0x4000;
    private readonly Dictionary<int, CaptureShortcutAction> _actions = [];
    private readonly HookProcedure _mouseProcedure;
    private HwndSource? _source;
    private IntPtr _window;
    private IntPtr _mouseHook;
    private CaptureSettingsSnapshot[] _mouseShortcuts = [];
    private readonly DebounceGate _mouseDebounce = new(TimeSpan.FromMilliseconds(350));

    public event EventHandler<CaptureShortcutEventArgs>? Triggered;

    public GlobalCaptureShortcutService()
    {
        _mouseProcedure = MouseHook;
    }

    public IReadOnlyList<string> Configure(
        Window owner,
        string activeMonitor,
        string region,
        string window,
        string scrolling)
    {
        DisposeRegistrations();
        _window = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_window);
        _source?.AddHook(WindowHook);

        var errors = new List<string>();
        var values = new[]
        {
            (CaptureShortcutAction.ActiveMonitor, activeMonitor),
            (CaptureShortcutAction.Region, region),
            (CaptureShortcutAction.Window, window),
            (CaptureShortcutAction.Scrolling, scrolling)
        };
        var mouse = new List<CaptureSettingsSnapshot>();
        var id = 4100;
        foreach (var (action, text) in values)
        {
            if (!TryParse(text, out var parsed))
            {
                errors.Add(UiText.Format($"Atalho inválido: {text}"));
                continue;
            }

            if (parsed.MouseSignal != 0)
            {
                mouse.Add(new CaptureSettingsSnapshot(action, parsed));
                continue;
            }
            if (!RegisterHotKey(_window, id, ApplyNoRepeat(parsed.Modifiers), parsed.Key))
            {
                errors.Add(UiText.Format($"Atalho em uso: {text}"));
                continue;
            }
            _actions[id++] = action;
        }

        _mouseShortcuts = mouse.ToArray();
        if (_mouseShortcuts.Length > 0)
        {
            _mouseHook = SetWindowsHookEx(
                MouseLowLevel,
                _mouseProcedure,
                IntPtr.Zero,
                0);
            if (_mouseHook == IntPtr.Zero)
            {
                errors.Add(UiText.Get("Não foi possível ativar atalhos com o mouse."));
            }
        }
        return errors;
    }

    public static bool IsValid(string value) => TryParse(value, out _);

    public static uint ApplyNoRepeat(uint modifiers) => modifiers | ModNoRepeat;

    public static string? FormatKeyboardShortcut(
        Key key,
        ModifierKeys modifiers)
    {
        if (key == Key.None)
        {
            return null;
        }

        var keyName = key == Key.Snapshot
            ? "PrintScreen"
            : key.ToString();
        var keyValue = (int)key;
        if (keyValue >= (int)Key.D0 && keyValue <= (int)Key.D9)
        {
            keyName = (keyValue - (int)Key.D0).ToString();
        }
        else if (keyValue >= (int)Key.NumPad0 &&
                 keyValue <= (int)Key.NumPad9)
        {
            keyName = $"Num{keyValue - (int)Key.NumPad0}";
        }
        return JoinShortcut(modifiers, keyName);
    }

    public static string? FormatWheelShortcut(
        int delta,
        ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None || delta == 0)
        {
            return null;
        }
        return JoinShortcut(
            modifiers,
            delta > 0 ? "WheelUp" : "WheelDown");
    }

    public static string? FormatMouseShortcut(
        MouseButton button,
        ModifierKeys modifiers)
    {
        var name = button switch
        {
            MouseButton.Middle => "MouseMiddle",
            MouseButton.XButton1 => "MouseX1",
            MouseButton.XButton2 => "MouseX2",
            _ => null
        };
        return name is null ? null : JoinShortcut(modifiers, name);
    }

    public void Dispose()
    {
        DisposeRegistrations();
        GC.SuppressFinalize(this);
    }

    private static string JoinShortcut(
        ModifierKeys modifiers,
        string action)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }
        parts.Add(action);
        return string.Join("+", parts);
    }

    private IntPtr WindowHook(
        IntPtr handle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == HotKeyMessage &&
            Keyboard.FocusedElement is not SlashText.Views.ShortcutRecorderBox &&
            _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Triggered?.Invoke(this, new CaptureShortcutEventArgs(action));
        }
        return IntPtr.Zero;
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 ||
            _mouseShortcuts.Length == 0 ||
            Keyboard.FocusedElement is SlashText.Views.ShortcutRecorderBox)
        {
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var data = Marshal.PtrToStructure<MouseHookData>(lParam);
        var signal = message switch
        {
            MouseWheel => (short)((data.MouseData >> 16) & 0xffff) > 0
                ? WheelUp
                : WheelDown,
            MouseMiddleDown => MouseMiddle,
            MouseXDown when ((data.MouseData >> 16) & 0xffff) == 1 => MouseX1,
            MouseXDown when ((data.MouseData >> 16) & 0xffff) == 2 => MouseX2,
            _ => 0
        };
        if (signal == 0)
        {
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        foreach (var item in _mouseShortcuts)
        {
            if (signal == item.Shortcut.MouseSignal &&
                ModifiersDown(item.Shortcut.Modifiers))
            {
                if (!_mouseDebounce.TryAccept(Stopwatch.GetTimestamp()))
                {
                    return (IntPtr)1;
                }
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => Triggered?.Invoke(
                        this,
                        new CaptureShortcutEventArgs(item.Action))));
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void DisposeRegistrations()
    {
        foreach (var id in _actions.Keys)
        {
            if (_window != IntPtr.Zero)
            {
                UnregisterHotKey(_window, id);
            }
        }
        _actions.Clear();
        if (_source is not null)
        {
            _source.RemoveHook(WindowHook);
            _source = null;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        _mouseShortcuts = [];
    }

    private static bool ModifiersDown(uint modifiers) =>
        ((modifiers & 1) == 0 ||
          Keyboard.IsKeyDown(Key.LeftAlt) ||
          Keyboard.IsKeyDown(Key.RightAlt)) &&
        ((modifiers & 2) == 0 ||
          Keyboard.IsKeyDown(Key.LeftCtrl) ||
          Keyboard.IsKeyDown(Key.RightCtrl)) &&
        ((modifiers & 4) == 0 ||
          Keyboard.IsKeyDown(Key.LeftShift) ||
          Keyboard.IsKeyDown(Key.RightShift)) &&
        ((modifiers & 8) == 0 ||
          Keyboard.IsKeyDown(Key.LWin) ||
          Keyboard.IsKeyDown(Key.RWin));

    private static bool TryParse(
        string text,
        out ParsedShortcut result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint modifiers = 0;
        uint key = 0;
        var mouseSignal = 0;
        foreach (var rawPart in text.Split(
                     '+',
                     StringSplitOptions.TrimEntries |
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.ToLowerInvariant();
            switch (part)
            {
                case "alt":
                    modifiers |= 1;
                    break;
                case "ctrl":
                case "control":
                    modifiers |= 2;
                    break;
                case "shift":
                    modifiers |= 4;
                    break;
                case "win":
                case "windows":
                    modifiers |= 8;
                    break;
                case "wheelup":
                case "scrollup":
                    if (mouseSignal != 0 || key != 0)
                    {
                        return false;
                    }
                    mouseSignal = WheelUp;
                    break;
                case "wheeldown":
                case "scrolldown":
                    if (mouseSignal != 0 || key != 0)
                    {
                        return false;
                    }
                    mouseSignal = WheelDown;
                    break;
                case "mousemiddle":
                case "middlemouse":
                    if (mouseSignal != 0 || key != 0)
                    {
                        return false;
                    }
                    mouseSignal = MouseMiddle;
                    break;
                case "mousex1":
                case "xbutton1":
                    if (mouseSignal != 0 || key != 0)
                    {
                        return false;
                    }
                    mouseSignal = MouseX1;
                    break;
                case "mousex2":
                case "xbutton2":
                    if (mouseSignal != 0 || key != 0)
                    {
                        return false;
                    }
                    mouseSignal = MouseX2;
                    break;
                default:
                    if (key != 0 || mouseSignal != 0)
                    {
                        return false;
                    }
                    var keyName = part switch
                    {
                        "printscreen" or "print" or "prtsc" or "snapshot" =>
                            nameof(Key.Snapshot),
                        "escape" => nameof(Key.Escape),
                        "pageup" => nameof(Key.PageUp),
                        "pagedown" => nameof(Key.PageDown),
                        _ => rawPart
                    };
                    if (part.Length == 1 && char.IsDigit(part[0]))
                    {
                        keyName = $"D{part}";
                    }
                    else if (part.StartsWith("num", StringComparison.Ordinal) &&
                             part.Length == 4 &&
                             char.IsDigit(part[3]))
                    {
                        keyName = $"NumPad{part[3]}";
                    }
                    if (!Enum.TryParse<Key>(
                            keyName,
                            true,
                            out var parsedKey))
                    {
                        return false;
                    }
                    key = (uint)KeyInterop.VirtualKeyFromKey(parsedKey);
                    break;
            }
        }

        if ((mouseSignal is WheelUp or WheelDown) && modifiers == 0)
        {
            return false;
        }
        if (mouseSignal == 0 && key == 0)
        {
            return false;
        }
        result = new ParsedShortcut(
            modifiers,
            key,
            mouseSignal);
        return true;
    }

    private readonly record struct ParsedShortcut(
        uint Modifiers,
        uint Key,
        int MouseSignal);

    private readonly record struct CaptureSettingsSnapshot(
        CaptureShortcutAction Action,
        ParsedShortcut Shortcut);

    private delegate IntPtr HookProcedure(
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(
        IntPtr window,
        int id,
        uint modifiers,
        uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProcedure procedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);
}
