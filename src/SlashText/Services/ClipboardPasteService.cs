using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SlashText.Services;

/// <summary>
/// Conservatively pastes a clipboard item into the external window that was
/// active immediately BEFORE the popup was shown. Never types into whatever
/// window happens to have focus when a delayed callback finishes.
/// </summary>
public static class ClipboardPasteService
{
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const uint KeyboardInputType = 1;
    private const uint KeyUp = 0x0002;

    public enum Result { Sent, NoExternalTarget, TargetUnavailable, ModifiersHeld, InputBlocked }

    public static IntPtr CaptureExternalTarget()
    {
        var hwnd = GetForegroundWindow();
        return IsExternalTarget(hwnd) ? hwnd : IntPtr.Zero;
    }

    public static bool IsExternalTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && pid != (uint)Environment.ProcessId;
    }

    public static async Task<Result> TryPasteAsync(IntPtr capturedTarget,
        CancellationToken cancellationToken = default)
    {
        if (!IsExternalTarget(capturedTarget)) return Result.NoExternalTarget;

        // Give WPF one dispatcher turn to hide the popup, then restore focus.
        await Task.Delay(65, cancellationToken);
        if (!IsExternalTarget(capturedTarget)) return Result.TargetUnavailable;
        if (GetForegroundWindow() != capturedTarget)
        {
            _ = SetForegroundWindow(capturedTarget);
            await Task.Delay(100, cancellationToken);
        }
        if (GetForegroundWindow() != capturedTarget) return Result.TargetUnavailable;

        // The user may still be holding Win+Shift+V or Ctrl+Alt+V. Sending
        // Ctrl+V with Win/Shift/Alt held can trigger an unrelated action.
        for (var retry = 0; retry < 25 && HasPhysicalModifiers(); retry++)
        {
            await Task.Delay(35, cancellationToken);
            if (GetForegroundWindow() != capturedTarget) return Result.TargetUnavailable;
        }
        if (HasPhysicalModifiers()) return Result.ModifiersHeld;
        if (GetForegroundWindow() != capturedTarget ||
            !IsExternalTarget(capturedTarget)) return Result.TargetUnavailable;

        var events = new[]
        {
            MakeKey(VkControl, false), MakeKey(VkV, false),
            MakeKey(VkV, true), MakeKey(VkControl, true)
        };
        var sent = SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
        return sent == events.Length ? Result.Sent : Result.InputBlocked;
    }

    private static bool HasPhysicalModifiers()
    {
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
        return false;
    }

    private static Input MakeKey(ushort vk, bool up) => new()
    {
        Type = KeyboardInputType,
        Data = new InputUnion
        {
            Keyboard = new KeyboardData
            {
                VirtualKey = vk,
                Flags = up ? KeyUp : 0,
                ExtraInfo = GetMessageExtraInfo()
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardData Keyboard;
        [FieldOffset(0)] public MouseData Mouse;
        [FieldOffset(0)] public HardwareData Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareData
    {
        public uint Message;
        public ushort ParameterLow, ParameterHigh;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern UIntPtr GetMessageExtraInfo();
}
