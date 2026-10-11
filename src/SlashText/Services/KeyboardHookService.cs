using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using SlashText.Models;
using Point = System.Windows.Point;

namespace SlashText.Services;

public sealed class KeyboardHookService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    // Windows 10 1607+: consulta a tradução sem alterar o estado interno das
    // teclas mortas. Sem esta flag, acentos de layouts ABNT podem ser aplicados
    // uma segunda vez quando o evento chega ao aplicativo de destino.
    private const uint ToUnicodeNoStateChange = 0x04;

    private readonly object _sync = new();
    private readonly LowLevelKeyboardProc _hookCallback;
    private readonly LowLevelMouseProc _mouseCallback;
    private readonly KeyboardBufferState _bufferState = new();
    private readonly KeyboardModifierState _modifierState = new();
    private List<MonitoredSnippet> _snippets = [];
    private IntPtr _hookHandle;
    private IntPtr _mouseHookHandle;
    private IReadOnlyList<Snippet> _currentSuggestions = [];
    private int _selectedSuggestionIndex;
    private bool _disposed;

    public KeyboardHookService()
    {
        _hookCallback = HookCallback;
        _mouseCallback = MouseCallback;
    }

    public event EventHandler<SnippetExpansionRequestedEventArgs>? ExpansionRequested;
    public event EventHandler<SnippetSuggestionsEventArgs>? SuggestionsChanged;

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void UpdateSnippets(IEnumerable<Snippet> snippets)
    {
        lock (_sync)
        {
            _snippets = snippets
                .Where(item => item.Enabled && TriggerRule.TryValidate(item.Trigger, out _))
                .Select(item => new MonitoredSnippet(
                    item,
                    item.Trigger.ToLowerInvariant(),
                    new HashSet<string>(item.ConfirmKeys, StringComparer.OrdinalIgnoreCase)))
                .ToList();
            ClearBuffer(BufferResetReason.Navigation);
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hookHandle = SetWindowsHookEx(
            WhKeyboardLl,
            _hookCallback,
            GetModuleHandle(module?.ModuleName),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                UiText.Format($"O Windows não permitiu iniciar o monitoramento do teclado (erro {Marshal.GetLastWin32Error()})."));
        }

        _mouseHookHandle = SetWindowsHookEx(
            WhMouseLl,
            _mouseCallback,
            GetModuleHandle(module?.ModuleName),
            0);
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        var messageValue = message.ToInt32();
        var isKeyDown = messageValue is WmKeyDown or WmSysKeyDown;
        var isKeyUp = messageValue is WmKeyUp or WmSysKeyUp;
        if (code < 0 || (!isKeyDown && !isKeyUp))
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        var key = Marshal.PtrToStructure<KeyboardData>(data);
        if ((key.Flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        var virtualKey = (int)key.VirtualKeyCode;
        if (KeyboardModifierState.IsModifierKey(virtualKey))
        {
            lock (_sync)
            {
                _modifierState.Update(virtualKey, isKeyDown);
            }
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        if (!isKeyDown || IsOwnWindowInForeground())
        {
            return CallNextHookEx(_hookHandle, code, message, data);
        }

        Snippet? snippetToExpand = null;
        IReadOnlyList<Snippet>? suggestions = null;
        Point suggestionPoint = default;
        var selectedSuggestionIndex = 0;
        var typedCharacterCount = 0;
        var confirmationMethod = SuggestionConfirmation.Automatic;
        var suppressKey = false;
        var targetWindow = GetForegroundWindow();
        var focusWindow = GetFocusedWindow(targetWindow);

        lock (_sync)
        {
            if (_bufferState.TargetChanged(targetWindow, focusWindow))
            {
                ClearBuffer(targetWindow != _bufferState.TargetWindow
                    ? BufferResetReason.WindowChanged
                    : BufferResetReason.FocusChanged);
            }

            var confirmation = ConfirmationName(virtualKey);

            if (_currentSuggestions.Count > 0 && virtualKey is 0x26 or 0x28)
            {
                _selectedSuggestionIndex = virtualKey == 0x26
                    ? (_selectedSuggestionIndex - 1 + _currentSuggestions.Count) % _currentSuggestions.Count
                    : (_selectedSuggestionIndex + 1) % _currentSuggestions.Count;
                suggestions = _currentSuggestions;
                selectedSuggestionIndex = _selectedSuggestionIndex;
                suggestionPoint = CaretLocator.GetScreenPosition();
                suppressKey = true;
            }

            else if (confirmation is not null && HasNavigationModifier())
            {
                ClearBuffer(BufferResetReason.Navigation);
                suggestions = [];
            }
            else if (confirmation is not null)
            {
                var selected = _currentSuggestions.Count > 0
                    ? _currentSuggestions[Math.Clamp(_selectedSuggestionIndex, 0, _currentSuggestions.Count - 1)]
                    : FindExactMatch()?.Snippet;
                if (selected is not null &&
                    selected.ConfirmKeys.Contains(confirmation, StringComparer.OrdinalIgnoreCase))
                {
                    snippetToExpand = selected;
                    typedCharacterCount = _bufferState.Text.Length;
                    confirmationMethod = ParseConfirmation(confirmation);
                    suppressKey = true;
                }

                ClearBuffer(BufferResetReason.Navigation);
                suggestions = [];
            }
            else if (virtualKey == 0x1B)
            {
                ClearBuffer(BufferResetReason.Escape);
                suggestions = [];
            }
            else if (virtualKey == 0x08)
            {
                if (_bufferState.HasValue)
                {
                    _bufferState.Backspace();
                    suggestions = GetSuggestions();
                    SetCurrentSuggestions(suggestions);
                    suggestionPoint = CaretLocator.GetScreenPosition();
                    selectedSuggestionIndex = _selectedSuggestionIndex;
                }
            }
            else if (TryMapTriggerCharacter(virtualKey, key.ScanCode, out var character))
            {
                _bufferState.Append(character, targetWindow, focusWindow);

                if (_bufferState.HasValue)
                {
                    var exact = FindExactMatch();
                    var hasLongerMatch = _snippets.Any(item =>
                        item.NormalizedTrigger.Length > _bufferState.Text.Length &&
                        item.NormalizedTrigger.StartsWith(_bufferState.Text, StringComparison.OrdinalIgnoreCase));

                    suggestions = GetSuggestions();
                    SetCurrentSuggestions(suggestions);
                    suggestionPoint = CaretLocator.GetScreenPosition();
                    selectedSuggestionIndex = _selectedSuggestionIndex;

                    if (exact is not null && !hasLongerMatch)
                    {
                        snippetToExpand = exact.Snippet;
                        typedCharacterCount = _bufferState.Text.Length;
                        ClearBuffer(BufferResetReason.ExpansionStarted);
                        suggestions = [];
                    }
                }
            }
            else if (_bufferState.HasValue)
            {
                ClearBuffer(BufferResetReason.Navigation);
                suggestions = [];
            }
        }

        if (suggestions is not null)
        {
            SuggestionsChanged?.Invoke(
                this,
                new SnippetSuggestionsEventArgs(
                    suggestions,
                    suggestionPoint,
                    selectedSuggestionIndex,
                    targetWindow,
                    _bufferState.Text.Length));
        }

        if (snippetToExpand is not null)
        {
            ExpansionRequested?.Invoke(
                this,
                new SnippetExpansionRequestedEventArgs(
                    snippetToExpand,
                    targetWindow,
                    typedCharacterCount,
                    confirmationMethod));
        }

        return suppressKey ? new IntPtr(1) : CallNextHookEx(_hookHandle, code, message, data);
    }

    private MonitoredSnippet? FindExactMatch() =>
        _snippets.FirstOrDefault(item =>
            item.NormalizedTrigger.Equals(_bufferState.Text, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<Snippet> GetSuggestions() =>
        _snippets
            .Where(item => item.NormalizedTrigger.StartsWith(_bufferState.Text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.NormalizedTrigger.Length)
            .Take(6)
            .Select(item => item.Snippet)
            .ToList();

    private static string? ConfirmationName(int virtualKey) =>
        virtualKey switch
        {
            0x0D => "Enter",
            0x09 => "Tab",
            0x20 => "Space",
            _ => null
        };

    private static SuggestionConfirmation ParseConfirmation(string value) =>
        value switch
        {
            "Enter" => SuggestionConfirmation.Enter,
            "Tab" => SuggestionConfirmation.Tab,
            "Space" => SuggestionConfirmation.Space,
            _ => SuggestionConfirmation.Automatic
        };

    private bool TryMapTriggerCharacter(int virtualKey, uint eventScanCode, out char character)
    {
        if (virtualKey == 0x6F)
        {
            character = '/';
            return true;
        }

        var keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState))
        {
            character = default;
            return false;
        }

        // GetKeyboardState pode estar um evento atrás dentro do hook global.
        // Combina seu snapshot (incluindo Caps Lock e teclas mortas) com os
        // modificadores físicos observados diretamente pelo hook.
        _modifierState.ApplyTo(keyboardState);
        keyboardState[virtualKey] |= 0x80;

        var foreground = GetForegroundWindow();
        var threadId = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var layout = GetKeyboardLayout(threadId);
        var buffer = new StringBuilder(8);
        var scanCode = eventScanCode != 0
            ? eventScanCode
            : MapVirtualKeyEx((uint)virtualKey, 0, layout);
        var count = ToUnicodeEx(
            (uint)virtualKey,
            scanCode,
            keyboardState,
            buffer,
            buffer.Capacity,
            ToUnicodeNoStateChange,
            layout);

        character = default;
        return count > 0 &&
               buffer.Length > 0 &&
               TryNormalizeTranslatedCharacter(buffer[0], out character);
    }

    internal static bool TryNormalizeTranslatedCharacter(char translated, out char character)
    {
        character = char.ToLowerInvariant(translated);
        return TriggerRule.IsSupportedPrefix(character) ||
               TriggerRule.IsSupportedCharacter(character);
    }

    private static bool HasNavigationModifier() =>
        (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x12) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x5C) & 0x8000) != 0;

    private static bool IsOwnWindowInForeground()
    {
        return IsOwnWindow(GetForegroundWindow());
    }

    public void ResetBuffer(BufferResetReason reason)
    {
        lock (_sync)
        {
            ClearBuffer(reason);
        }
        SuggestionsChanged?.Invoke(
            this,
            new SnippetSuggestionsEventArgs([], default, 0, IntPtr.Zero, 0));
    }

    public void ConfirmSuggestion(Snippet snippet, SuggestionConfirmation confirmation)
    {
        SnippetExpansionRequestedEventArgs? expansion = null;
        lock (_sync)
        {
            if (!_bufferState.HasValue || !_currentSuggestions.Contains(snippet))
            {
                return;
            }
            expansion = new SnippetExpansionRequestedEventArgs(
                snippet,
                _bufferState.TargetWindow,
                _bufferState.Text.Length,
                confirmation);
            ClearBuffer(BufferResetReason.ExpansionStarted);
        }
        SuggestionsChanged?.Invoke(this, new SnippetSuggestionsEventArgs([], default, 0, IntPtr.Zero, 0));
        ExpansionRequested?.Invoke(this, expansion);
    }

    private void SetCurrentSuggestions(IReadOnlyList<Snippet> suggestions)
    {
        _currentSuggestions = suggestions;
        _selectedSuggestionIndex = suggestions.Count == 0
            ? 0
            : Math.Clamp(_selectedSuggestionIndex, 0, suggestions.Count - 1);
    }

    private void ClearBuffer(BufferResetReason reason)
    {
        _bufferState.Clear(reason);
        _currentSuggestions = [];
        _selectedSuggestionIndex = 0;
    }

    private IntPtr MouseCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown)
        {
            var point = Marshal.PtrToStructure<MouseData>(data).Point;
            var clickedWindow = WindowFromPoint(point);
            if (!IsOwnWindow(clickedWindow))
            {
                ThreadPool.QueueUserWorkItem(_ => ResetBuffer(BufferResetReason.MouseClick));
            }
        }
        return CallNextHookEx(_mouseHookHandle, code, message, data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hookHandle != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        if (_mouseHookHandle != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed record MonitoredSnippet(
        Snippet Snippet,
        string NormalizedTrigger,
        HashSet<string> ConfirmKeys);

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);
    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseData
    {
        public readonly NativePoint Point;
        public readonly uint MouseDataValue;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    private static bool IsOwnWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }
        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }

    private static IntPtr GetFocusedWindow(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        var threadId = GetWindowThreadProcessId(targetWindow, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) ? info.Focus : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public System.Drawing.Rectangle CaretRectangle;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] keyState);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(
        uint code,
        uint mapType,
        IntPtr keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint virtualKey,
        uint scanCode,
        byte[] keyState,
        [Out] StringBuilder buffer,
        int bufferSize,
        uint flags,
        IntPtr keyboardLayout);
}

internal sealed class KeyboardModifierState
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftShift = 0xA0;
    private const int VkRightShift = 0xA1;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int VkLeftMenu = 0xA4;
    private const int VkRightMenu = 0xA5;

    private readonly HashSet<int> _pressed = [];

    internal static bool IsModifierKey(int virtualKey) =>
        virtualKey is VkShift or VkControl or VkMenu or
            VkLeftShift or VkRightShift or
            VkLeftControl or VkRightControl or
            VkLeftMenu or VkRightMenu;

    internal void Update(int virtualKey, bool isDown)
    {
        if (!IsModifierKey(virtualKey))
        {
            return;
        }

        if (isDown)
        {
            _pressed.Add(virtualKey);
        }
        else
        {
            _pressed.Remove(virtualKey);
        }
    }

    internal void ApplyTo(byte[] keyboardState)
    {
        ArgumentNullException.ThrowIfNull(keyboardState);
        if (keyboardState.Length < 256)
        {
            throw new ArgumentException(UiText.Get("O estado do teclado precisa ter 256 posições."), nameof(keyboardState));
        }

        foreach (var virtualKey in _pressed)
        {
            keyboardState[virtualKey] |= 0x80;
        }

        ApplyAggregate(keyboardState, VkShift, VkLeftShift, VkRightShift);
        ApplyAggregate(keyboardState, VkControl, VkLeftControl, VkRightControl);
        ApplyAggregate(keyboardState, VkMenu, VkLeftMenu, VkRightMenu);
    }

    private void ApplyAggregate(byte[] keyboardState, int aggregate, int left, int right)
    {
        if (_pressed.Contains(aggregate) ||
            _pressed.Contains(left) ||
            _pressed.Contains(right))
        {
            keyboardState[aggregate] |= 0x80;
        }
    }
}

public enum SuggestionConfirmation
{
    Automatic,
    Enter,
    Tab,
    Space,
    Click
}

public sealed class SnippetExpansionRequestedEventArgs(
    Snippet snippet,
    IntPtr targetWindow,
    int typedCharacterCount,
    SuggestionConfirmation confirmation) : EventArgs
{
    public Snippet Snippet { get; } = snippet;
    public IntPtr TargetWindow { get; } = targetWindow;
    public int TypedCharacterCount { get; } = typedCharacterCount;
    public SuggestionConfirmation Confirmation { get; } = confirmation;
}

public sealed class SnippetSuggestionsEventArgs(
    IReadOnlyList<Snippet> snippets,
    Point screenPosition,
    int selectedIndex,
    IntPtr targetWindow,
    int typedCharacterCount) : EventArgs
{
    public IReadOnlyList<Snippet> Snippets { get; } = snippets;
    public Point ScreenPosition { get; } = screenPosition;
    public int SelectedIndex { get; } = selectedIndex;
    public IntPtr TargetWindow { get; } = targetWindow;
    public int TypedCharacterCount { get; } = typedCharacterCount;
}
