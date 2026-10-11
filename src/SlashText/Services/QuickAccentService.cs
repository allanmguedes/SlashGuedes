using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SlashText.Services;

public sealed class QuickAccentService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const int VkShift = 0x10;
    private const int VkCapital = 0x14;

    private static readonly string[] CharacterSetOrder =
    [
        "PortugueseBrazil",
        "Spanish",
        "French",
        "German",
        "Italian",
        "Nordic",
        "CentralEuropean",
        "Currency",
        "Special"
    ];

    private static readonly Dictionary<string, IReadOnlyDictionary<int, string>> CharacterSets =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["PortugueseBrazil"] = new Dictionary<int, string>
        {
            [0x41] = "áàâã",
            [0x43] = "ç",
            [0x45] = "éê",
            [0x49] = "í",
            [0x4F] = "óôõ",
            [0x55] = "ú"
        },
        ["Spanish"] = new Dictionary<int, string>
        {
            [0x41] = "á",
            [0x45] = "é",
            [0x49] = "í",
            [0x4E] = "ñ",
            [0x4F] = "ó",
            [0x55] = "úü"
        },
        ["French"] = new Dictionary<int, string>
        {
            [0x41] = "àâäæ",
            [0x43] = "ç",
            [0x45] = "éèêë",
            [0x49] = "îï",
            [0x4F] = "ôöœ",
            [0x55] = "ùûü",
            [0x59] = "ÿ"
        },
        ["German"] = new Dictionary<int, string>
        {
            [0x41] = "ä",
            [0x4F] = "ö",
            [0x53] = "ß",
            [0x55] = "ü"
        },
        ["Italian"] = new Dictionary<int, string>
        {
            [0x41] = "à",
            [0x45] = "èé",
            [0x49] = "ìíî",
            [0x4F] = "òó",
            [0x55] = "ùú"
        },
        ["Nordic"] = new Dictionary<int, string>
        {
            [0x41] = "åäæ",
            [0x4F] = "öø",
            [0x55] = "ü"
        },
        ["CentralEuropean"] = new Dictionary<int, string>
        {
            [0x43] = "ćč",
            [0x4E] = "ń",
            [0x53] = "š",
            [0x59] = "ý",
            [0x5A] = "žźż"
        },
        ["Currency"] = new Dictionary<int, string>
        {
            [0x43] = "¢",
            [0x44] = "₫",
            [0x45] = "€",
            [0x4C] = "£",
            [0x52] = "₹",
            [0x59] = "¥"
        },
        ["Special"] = new Dictionary<int, string>
        {
            [0x43] = "©",
            [0x44] = "°",
            [0x4D] = "™",
            [0x52] = "®",
            [0x53] = "§"
        }
    };

    private readonly LowLevelKeyboardProc _callback;
    private readonly Dictionary<char, long> _usage = [];
    private readonly object _stateSync = new();
    private readonly QuickAccentDelayController _delayController = new();
    private string[] _characterSets = ["PortugueseBrazil"];
    private IntPtr _hook;
    private int? _baseKey;
    private long _basePressedAt;
    private string _choices = string.Empty;
    private int _selected;
    private bool _active;
    private bool _activationDown;
    private bool _activationSuppressed;
    private IntPtr _pendingWindow;
    private bool _disposed;

    public QuickAccentService() => _callback = HookCallback;

    private volatile bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) ResetPendingState(); }
    }
    public string ActivationKey { get; set; } = "Space";
    public bool SortByUsage { get; set; } = true;
    public int InputDelayMs { get; set; } = 200;
    public string ExcludedApps { get; set; } = string.Empty;
    public bool IsRunning => _hook != IntPtr.Zero;

    // The preference and the native hook are separate: enabled alone is not readiness.
    public QuickAccentRuntimeState GetRuntimeState() => ResolveRuntimeState(
        Enabled, IsRunning, Enabled && IsRunning && IsExcludedApp());

    internal static QuickAccentRuntimeState ResolveRuntimeState(bool enabled, bool running, bool excluded) =>
        !enabled ? QuickAccentRuntimeState.Disabled :
        !running ? QuickAccentRuntimeState.Unavailable :
        excluded ? QuickAccentRuntimeState.Paused : QuickAccentRuntimeState.Ready;

    internal static bool IsProcessExcluded(string processName, string? excludedApps) =>
        !string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(excludedApps) &&
        excludedApps.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => item.Equals(processName, StringComparison.OrdinalIgnoreCase) ||
                         item.Equals($"{processName}.exe", StringComparison.OrdinalIgnoreCase));

    public event EventHandler<QuickAccentChangedEventArgs>? Changed;
    public event EventHandler<QuickAccentCharacterInsertedEventArgs>? CharacterInserted;

    public void SetCharacterSets(IEnumerable<string>? characterSets)
    {
        var selected = new HashSet<string>(
            characterSets ?? [],
            StringComparer.OrdinalIgnoreCase);
        var resolved = CharacterSetOrder
            .Where(selected.Contains)
            .ToArray();
        lock (_stateSync)
        {
            _characterSets = resolved.Length == 0
                ? ["PortugueseBrazil"]
                : resolved;
        }
    }

    public void SetUsage(IReadOnlyDictionary<char, long> usage)
    {
        lock (_stateSync)
        {
            _usage.Clear();
            foreach (var item in usage)
            {
                _usage[item.Key] = item.Value;
            }
        }
    }

    public static string PreviewCharacters(IEnumerable<string>? characterSets)
    {
        var selected = new HashSet<string>(
            characterSets ?? [],
            StringComparer.OrdinalIgnoreCase);
        return new string(CharacterSetOrder
            .Where(selected.Contains)
            .SelectMany(set => CharacterSets[set].Values)
            .SelectMany(value => value)
            .Distinct()
            .ToArray());
    }

    public static bool ShouldUseUppercase(bool shiftDown, bool capsLockOn) =>
        shiftDown ^ capsLockOn;

    internal static bool ShouldHandleInput(
        bool enabled,
        bool ownProcessInForeground,
        bool excludedApp)
    {
        // O processo próprio é permitido: campos editáveis do SlashDesk também
        // devem receber o Acento Rápido. A expansão de snippets possui seu
        // próprio bloqueio para janelas do aplicativo.
        _ = ownProcessInForeground;
        return enabled && !excludedApp;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                UiText.Format($"O Windows não permitiu iniciar o Acento Rápido (erro {Marshal.GetLastWin32Error()})."));
        }
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hook, code, message, data);
        }

        var keyboard = Marshal.PtrToStructure<KeyboardData>(data);
        if ((keyboard.Flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(_hook, code, message, data);
        }

        if (!ShouldHandleInput(
                Enabled,
                IsOwnProcessInForeground(),
                IsExcludedApp()))
        {
            ResetPendingState();
            return CallNextHookEx(_hook, code, message, data);
        }

        var key = (int)keyboard.VirtualKeyCode;
        var isDown = message.ToInt32() is WmKeyDown or WmSysKeyDown;
        var isUp = message.ToInt32() is WmKeyUp or WmSysKeyUp;
        var suppress = false;
        var replayActivation = false;
        QuickAccentChangedEventArgs? changed = null;
        QuickAccentCharacterInsertedEventArgs? inserted = null;

        lock (_stateSync)
        {
            if (isDown && ChoicesFor(key).Length > 0 && _baseKey is null)
            {
                _baseKey = key;
                _basePressedAt = Environment.TickCount64;
            }

            if (isDown && _baseKey is not null &&
                key == ActivationVirtualKey())
            {
                suppress = true;
                if (!_activationDown)
                {
                    _activationDown = true;
                    if (_active)
                    {
                        _selected = (_selected + 1) % _choices.Length;
                        changed = CurrentChangedEvent(visible: true);
                    }
                    else
                    {
                        var remaining = RemainingDelayMilliseconds(
                            _basePressedAt,
                            Environment.TickCount64,
                            InputDelayMs);
                        if (remaining == 0)
                        {
                            ActivateLocked();
                            changed = CurrentChangedEvent(visible: true);
                        }
                        else
                        {
                            _activationSuppressed = true;
                            _pendingWindow = GetForegroundWindow();
                            _delayController.Schedule(
                                remaining,
                                TryActivatePending);
                        }
                    }
                }
            }
            else if (isUp && key == ActivationVirtualKey())
            {
                _activationDown = false;
                if (_active)
                {
                    suppress = true;
                }
                else if (_activationSuppressed)
                {
                    suppress = true;
                    replayActivation = true;
                    ResetLocked();
                    changed = CurrentChangedEvent(visible: false);
                }
            }
            else if (_active && isDown && key is 0x25 or 0x27)
            {
                _selected = key == 0x25
                    ? (_selected - 1 + _choices.Length) % _choices.Length
                    : (_selected + 1) % _choices.Length;
                changed = CurrentChangedEvent(visible: true);
                suppress = true;
            }
            else if (_active && isDown && key == 0x1B)
            {
                ResetLocked();
                changed = CurrentChangedEvent(visible: false);
                suppress = true;
            }

            if (isUp && _baseKey == key)
            {
                if (_active && _choices.Length > 0)
                {
                    var selectedCharacter = _choices[_selected];
                    if (ReplaceBaseCharacter(selectedCharacter))
                    {
                        _usage[selectedCharacter] =
                            _usage.GetValueOrDefault(selectedCharacter) + 1;
                        inserted =
                            new QuickAccentCharacterInsertedEventArgs(
                                selectedCharacter);
                    }
                }
                else if (_activationSuppressed)
                {
                    replayActivation = true;
                }

                ResetLocked();
                changed = CurrentChangedEvent(visible: false);
            }
        }

        if (replayActivation)
        {
            SendVirtualKeyStroke(ActivationVirtualKey());
        }
        if (changed is not null)
        {
            Changed?.Invoke(this, changed);
        }
        if (inserted is not null)
        {
            CharacterInserted?.Invoke(this, inserted);
        }

        return suppress
            ? new IntPtr(1)
            : CallNextHookEx(_hook, code, message, data);
    }

    internal static int RemainingDelayMilliseconds(
        long pressedAt,
        long now,
        int configuredDelay)
    {
        var delay = Math.Clamp(configuredDelay, 0, 2000);
        var elapsed = Math.Max(0, now - pressedAt);
        return (int)Math.Max(0, delay - elapsed);
    }

    private void TryActivatePending()
    {
        QuickAccentChangedEventArgs? changed = null;
        lock (_stateSync)
        {
            if (_baseKey is null || !_activationDown ||
                !_activationSuppressed || _active)
            {
                return;
            }

            if (!ShouldHandleInput(
                    Enabled,
                    IsOwnProcessInForeground(),
                    IsExcludedApp()) ||
                !IsKeyDown(_baseKey.Value) ||
                !IsKeyDown(ActivationVirtualKey()) ||
                GetForegroundWindow() != _pendingWindow)
            {
                ResetLocked();
                changed = CurrentChangedEvent(visible: false);
            }
            else
            {
                ActivateLocked();
                changed = CurrentChangedEvent(visible: true);
            }
        }

        if (changed is not null)
        {
            Changed?.Invoke(this, changed);
        }
    }

    private void ActivateLocked()
    {
        _delayController.Cancel();
        _activationSuppressed = false;
        _pendingWindow = IntPtr.Zero;
        _choices = MatchCase(
            ChoicesFor(_baseKey!.Value),
            ShouldUseUppercase(
                IsKeyDown(VkShift),
                IsKeyToggled(VkCapital)));
        if (SortByUsage)
        {
            _choices = new string(_choices
                .OrderByDescending(character =>
                    _usage.GetValueOrDefault(character))
                .ToArray());
        }
        _selected = 0;
        _active = _choices.Length > 0;
    }

    private QuickAccentChangedEventArgs CurrentChangedEvent(bool visible) =>
        new(_choices, _selected, visible && _active);

    private void ResetPendingState()
    {
        QuickAccentChangedEventArgs? changed = null;
        lock (_stateSync)
        {
            if (_baseKey is null && !_active && !_activationSuppressed)
            {
                return;
            }
            ResetLocked();
            changed = CurrentChangedEvent(visible: false);
        }
        Changed?.Invoke(this, changed);
    }

    private void ResetLocked()
    {
        _delayController.Cancel();
        _baseKey = null;
        _active = false;
        _activationDown = false;
        _activationSuppressed = false;
        _pendingWindow = IntPtr.Zero;
        _choices = string.Empty;
        _selected = 0;
    }

    private static void SendVirtualKeyStroke(int virtualKey)
    {
        var inputs = new[]
        {
            KeyInput((ushort)virtualKey, 0, false),
            KeyInput((ushort)virtualKey, 0, true)
        };
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private int ActivationVirtualKey() => ActivationKey switch
    {
        "Left" => 0x25,
        "Right" => 0x27,
        _ => 0x20
    };

    /// <summary>Read-only preview of the same sets, case and usage order as activation.
    /// Does not change pending keys, timers, insertion or usage statistics.</summary>
    public string GetPreviewChoices(char baseLetter)
    {
        lock (_stateSync)
        {
            var choices = MatchCase(ChoicesFor(char.ToUpperInvariant(baseLetter)), char.IsUpper(baseLetter));
            return SortByUsage
                ? new string(choices.OrderByDescending(character => _usage.GetValueOrDefault(character)).ToArray())
                : choices;
        }
    }

    private string ChoicesFor(int virtualKey)
    {
        var result = new List<char>();
        foreach (var set in _characterSets)
        {
            if (!CharacterSets.TryGetValue(set, out var characters) ||
                !characters.TryGetValue(virtualKey, out var choices))
            {
                continue;
            }

            foreach (var character in choices)
            {
                if (!result.Contains(character))
                {
                    result.Add(character);
                }
            }
        }

        return new string(result.ToArray());
    }

    private bool IsExcludedApp()
    {
        if (string.IsNullOrWhiteSpace(ExcludedApps))
        {
            return false;
        }

        try
        {
            var window = GetForegroundWindow();
            _ = GetWindowThreadProcessId(window, out var processId);
            using var foregroundProcess = Process.GetProcessById((int)processId);
            var processName = foregroundProcess.ProcessName;
            return IsProcessExcluded(processName, ExcludedApps);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOwnProcessInForeground()
    {
        var window = GetForegroundWindow();
        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }

    private static string MatchCase(string value, bool upper) =>
        upper ? value.ToUpperInvariant() : value;

    private static bool ReplaceBaseCharacter(char character)
    {
        var inputs = new[]
        {
            KeyInput(0x08, 0, false),
            KeyInput(0x08, 0, true),
            KeyInput(0, character, false, true),
            KeyInput(0, character, true, true)
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
    }

    private static Input KeyInput(ushort key, ushort scan, bool keyUp, bool unicode = false) =>
        new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = key,
                    ScanCode = scan,
                    Flags = (keyUp ? KeyEventKeyUp : 0) | (unicode ? KeyEventUnicode : 0)
                }
            }
        };

    private static bool IsKeyDown(int virtualKey) =>
        (GetKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsKeyToggled(int virtualKey) =>
        (GetKeyState(virtualKey) & 0x0001) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _delayController.Dispose();
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _disposed = true;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
        [FieldOffset(0)] public HardwareInputData Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInputData
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

public enum QuickAccentRuntimeState { Disabled, Ready, Paused, Unavailable }

internal sealed class QuickAccentDelayController : IDisposable
{
    private readonly object _sync = new();
    private Timer? _timer;
    private long _generation;
    private bool _disposed;

    public void Schedule(int delayMilliseconds, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        long generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelLocked();
            generation = _generation;
            _timer = new Timer(
                _ =>
                {
                    lock (_sync)
                    {
                        if (_disposed || generation != _generation)
                        {
                            return;
                        }
                        _timer?.Dispose();
                        _timer = null;
                        _generation++;
                    }
                    callback();
                },
                null,
                Math.Max(0, delayMilliseconds),
                Timeout.Infinite);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            CancelLocked();
        }
    }

    private void CancelLocked()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            CancelLocked();
            _disposed = true;
        }
    }
}

public sealed class QuickAccentChangedEventArgs(
    string choices, int selectedIndex, bool visible) : EventArgs
{
    public string Choices { get; } = choices;
    public int SelectedIndex { get; } = selectedIndex;
    public bool Visible { get; } = visible;
}

public sealed class QuickAccentCharacterInsertedEventArgs(char character) : EventArgs
{
    public char Character { get; } = character;
}
