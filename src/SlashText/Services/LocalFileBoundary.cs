using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SlashText.Services;

// Pin every ancestor without FILE_SHARE_DELETE. Never traverse junctions or symlinks,
// including a concurrently replaced parent. Restricted to local drive paths.
internal sealed class LocalFileBoundary : IDisposable
{
    private readonly List<SafeFileHandle> _directories = [];
    private LocalFileBoundary() { }

    internal static LocalFileBoundary PinDirectory(string path, bool create = false)
    {
        var full = Path.GetFullPath(path);
        if (full.Length < 3 || !char.IsAsciiLetter(full[0]) || full[1] != ':' ||
            full[2] != '\\' || full.AsSpan(2).Contains(':')) throw new IOException("Invalid local directory.");
        var boundary = new LocalFileBoundary();
        try
        {
            var current = full[..3];
            boundary.Pin(current);
            foreach (var part in full[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.EndsWith('.') || part.EndsWith(' ')) throw new IOException("Ambiguous directory.");
                current = Path.Combine(current, part);
                if (create && !Directory.Exists(current)) Directory.CreateDirectory(current);
                boundary.Pin(current);
            }
            return boundary;
        }
        catch { boundary.Dispose(); throw; }
    }

    private void Pin(string path)
    {
        var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) ||
                (info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0)
                throw new IOException("Unsafe or unavailable directory.");
            _directories.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    // The caller holds a pinned parent. The returned stream owns its native handle.
    internal static FileStream OpenFile(string path, bool create = false, bool exclusive = false, bool writable = true)
    {
        var handle = CreateFileW(path, writable ? 0xC0000000u : 0x80000000u,
            exclusive ? 0u : 3u, IntPtr.Zero, create ? 4u : 3u, 0x00200000, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid && Marshal.GetLastPInvokeError() == 2) throw new FileNotFoundException("Local file unavailable.");
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) ||
                (info.Attributes & (0x400 | 0x10)) != 0 || info.Links != 1)
                throw new IOException("Unsafe or unavailable file.");
            return new FileStream(handle, writable ? FileAccess.ReadWrite : FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    public void Dispose()
    {
        for (var i = _directories.Count - 1; i >= 0; i--) _directories[i].Dispose();
        _directories.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
}
