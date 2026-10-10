using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace SlashText.Views;

internal static class FloatingWindowPlacement
{
    internal static Forms.Screen ForegroundScreen() => Forms.Screen.FromHandle(GetForegroundWindow());

    internal static Rect Calculate(Rect area, Size size, Point desired) => new(
        Math.Clamp(desired.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(desired.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)),
        Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));

    internal static void PreventActivation(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000);
    }

    internal static void Place(Window window, Forms.Screen screen, string position, Point? physicalPoint = null)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var working = screen.WorkingArea;
        // Move first: PMv2 updates the HWND's DPI before converting the measured WPF size.
        SetWindowPos(handle, IntPtr.Zero, working.Left + 8, working.Top + 8, 0, 0, 0x0010 | 0x0004 | 0x0001);
        var scale = Math.Max(96u, GetDpiForWindow(handle)) / 96d;
        window.MaxWidth = Math.Max(1, working.Width / scale - 16);
        window.MaxHeight = Math.Max(1, working.Height / scale - 16);
        window.UpdateLayout();
        var size = new Size(Math.Ceiling(window.ActualWidth * scale), Math.Ceiling(window.ActualHeight * scale));
        var area = new Rect(working.Left + 8, working.Top + 8, Math.Max(1, working.Width - 16), Math.Max(1, working.Height - 16));
        var desired = physicalPoint ?? new Point(area.Left + (area.Width - size.Width) / 2,
            position == "TopCenter" ? area.Top + 16 : position == "Center" ? area.Top + (area.Height - size.Height) / 2 : area.Bottom - size.Height - 24);
        var bounds = Calculate(area, size, desired);
        SetWindowPos(handle, IntPtr.Zero, (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, 0x0010 | 0x0004);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")] internal static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
