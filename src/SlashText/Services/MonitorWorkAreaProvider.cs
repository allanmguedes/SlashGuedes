using System.Runtime.InteropServices;
using System.Windows;

namespace SlashText.Services;

public readonly record struct MonitorWorkArea(
    nint Handle,
    Rect WorkAreaPixels,
    double DpiScaleX,
    double DpiScaleY);

public static class MonitorWorkAreaProvider
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public static MonitorWorkArea FromSelection(Rect selectionPixels)
    {
        var rectangle = new NativeRect
        {
            Left = (int)Math.Floor(selectionPixels.Left),
            Top = (int)Math.Floor(selectionPixels.Top),
            Right = (int)Math.Ceiling(selectionPixels.Right),
            Bottom = (int)Math.Ceiling(selectionPixels.Bottom)
        };
        var monitor = MonitorFromRect(ref rectangle, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            throw new InvalidOperationException(UiText.Get("Não foi possível identificar o monitor da seleção."));
        }

        var information = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref information))
        {
            throw new InvalidOperationException(UiText.Get("Não foi possível obter a área útil do monitor."));
        }

        var dpiX = 96u;
        var dpiY = 96u;
        try
        {
            if (GetDpiForMonitor(monitor, EffectiveDpi, out var reportedX, out var reportedY) == 0)
            {
                dpiX = reportedX;
                dpiY = reportedY;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return new MonitorWorkArea(
            monitor,
            new Rect(
                information.Work.Left,
                information.Work.Top,
                information.Work.Right - information.Work.Left,
                information.Work.Bottom - information.Work.Top),
            Math.Max(1, dpiX / 96d),
            Math.Max(1, dpiY / 96d));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(
        [In] ref NativeRect rectangle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo information);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
