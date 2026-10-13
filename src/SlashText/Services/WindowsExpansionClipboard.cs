using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using WpfDataObject = System.Windows.DataObject;

namespace SlashText.Services;

// Immediate Win32 publication lets us compare ownership and restore under the
// same OpenClipboard lock. WPF/OLE check-then-SetDataObject retries cannot do this.
internal sealed class WindowsExpansionClipboard : IExpansionClipboard
{
    private readonly HwndSource _window = new(new HwndSourceParameters("SlashDesk.Expansion.Clipboard")
    { ParentWindow = new IntPtr(-3), WindowStyle = 0 });

    public ClipboardSnapshot Capture()
    {
        if (!OpenClipboard(_window.Handle)) throw new ExternalException("Clipboard is busy while capturing");
        var content = new NativeContent();
        uint sequence;
        try
        {
            // Preserve the original native representation. Re-exporting an
            // external editor's formats through WPF can change their storage
            // types (OEM/locale/custom formats) or deserialize arbitrary data.
            // GetClipboardData may ask the owner to render delayed data, but the
            // clipboard remains locked until all independent copies are ready.
            uint format = 0;
            var count = 0;
            while (true)
            {
                Marshal.SetLastPInvokeError(0);
                format = EnumClipboardFormats(format);
                if (format == 0)
                {
                    if (Marshal.GetLastPInvokeError() != 0) throw new ExternalException("Clipboard format enumeration failed");
                    break;
                }
                if (++count > 256) throw CannotPreserve();
                // OLE ownership bookkeeping cannot be restored as ordinary data.
                if (format == (uint)DataFormats.GetDataFormat("DataObject").Id ||
                    format == (uint)DataFormats.GetDataFormat("Ole Private Data").Id) continue;
                if (format is >= 0x200 and <= 0x3ff or 0x80) throw CannotPreserve();
                var handle = GetClipboardData(format);
                var readError = Marshal.GetLastPInvokeError();
                // Notepad/Windows may advertise this optional EDP hint without
                // a renderable value. Omit only that no-error/null placeholder;
                // nonempty identity bytes and access errors remain mandatory.
                if (handle == IntPtr.Zero && readError == 0 && format == EmptyEnterpriseMetadataFormat)
                {
                    continue;
                }
                // WPF advertises this unrenderable derived alias alongside the
                // real CF_BITMAP. Preserve the concrete native bitmap instead.
                if (handle == IntPtr.Zero && format == (uint)DataFormats.GetDataFormat("System.Drawing.Bitmap").Id &&
                    IsClipboardFormatAvailable(2)) continue;
                if (handle == IntPtr.Zero) throw CannotPreserve();
                content.Add(NativeItem.Duplicate(format, handle, StorageFor(format)));
            }
            sequence = GetClipboardSequenceNumber();
        }
        catch { content.Dispose(); throw; }
        finally { CloseClipboard(); }
        if (GetClipboardSequenceNumber() == sequence) return new ClipboardSnapshot(content, sequence);
        content.Dispose();
        throw new ExternalException("Clipboard changed while capturing its native formats");
    }

    private static TYMED StorageFor(uint format) => format switch
    {
        2 or 9 or 0x82 => TYMED.TYMED_GDI,
        3 or 0x83 => TYMED.TYMED_MFPICT,
        14 or 0x8e => TYMED.TYMED_ENHMF,
        _ => TYMED.TYMED_HGLOBAL
    };

    private static uint EmptyEnterpriseMetadataFormat => (uint)DataFormats.GetDataFormat("EnterpriseDataProtectionId").Id;

    public IClipboardContent Prepare(WpfDataObject data) => PrepareNative(data);

    internal static NativeContent PrepareNative(WpfDataObject data)
    {
        var content = new NativeContent();
        try
        {
            foreach (var name in data.GetFormats(autoConvert: false).OrderBy(name =>
                name is ClipboardPrivacy.ExcludeMonitoring or ClipboardPrivacy.IncludeHistory or ClipboardPrivacy.UploadCloud ? 0 : 1))
            {
                var id = (uint)DataFormats.GetDataFormat(name).Id;
                var descriptor = new FORMATETC { cfFormat = unchecked((short)id), dwAspect = DVASPECT.DVASPECT_CONTENT,
                    lindex = -1, tymed = StorageFor(id) };
                ((ComDataObject)data).GetData(ref descriptor, out var medium);
                try { content.Add(NativeItem.Duplicate(id, medium.unionmember, medium.tymed)); }
                finally { ReleaseStgMedium(ref medium); }
            }
            return content;
        }
        catch { content.Dispose(); throw; }
    }

    public ClipboardWriteResult TryWrite(IClipboardContent data, uint expectedSequence, bool requireOwned)
    {
        // Allocate all copies before EmptyClipboard; allocation failures cannot
        // erase the user's content. Windows takes each handle only on success.
        using var copies = ((NativeContent)data).Duplicate();
        if (!OpenClipboard(_window.Handle)) return new(ClipboardWriteStatus.Busy, expectedSequence);
        var changed = false;
        try
        {
            if (GetClipboardSequenceNumber() != expectedSequence ||
                (requireOwned && GetClipboardOwner() != _window.Handle))
                return new(ClipboardWriteStatus.OwnershipLost, expectedSequence);
            if (!EmptyClipboard()) return new(ClipboardWriteStatus.Failed, expectedSequence);
            changed = true;
            foreach (var item in copies.Items)
            {
                if (SetClipboardData(item.Format, item.Handle) == IntPtr.Zero)
                    return new(ClipboardWriteStatus.Failed, GetClipboardSequenceNumber(), Changed: true);
                item.TransferToWindows();
            }
            return new(ClipboardWriteStatus.Success, GetClipboardSequenceNumber(), Changed: true);
        }
        finally
        {
            CloseClipboard();
            if (changed) GC.KeepAlive(_window);
        }
    }

    public void Dispose() => _window.Dispose();
    private static InvalidOperationException CannotPreserve() => new(UiText.Get("Não foi possível preservar a área de transferência. Copie outro conteúdo e tente expandir novamente."));

    internal sealed class NativeContent : IClipboardContent
    {
        internal List<NativeItem> Items { get; } = [];
        private ulong _bytes;
        internal void Add(NativeItem item)
        {
            var bytes = item.Bytes;
            if (_bytes > 128UL * 1024 * 1024 - bytes) { item.Dispose(); throw CannotPreserve(); }
            _bytes += bytes;
            Items.Add(item);
        }
        internal NativeContent Duplicate()
        {
            var copy = new NativeContent();
            try { foreach (var item in Items) copy.Add(NativeItem.Duplicate(item.Format, item.Handle, item.Kind)); return copy; }
            catch { copy.Dispose(); throw; }
        }
        public void Dispose() { foreach (var item in Items) item.Dispose(); Items.Clear(); }
    }

    internal sealed class NativeItem(uint format, IntPtr handle, TYMED kind, ulong bytes) : IDisposable
    {
        internal uint Format { get; } = format;
        internal IntPtr Handle { get; private set; } = handle;
        internal TYMED Kind { get; } = kind;
        internal ulong Bytes { get; } = bytes;
        internal static NativeItem Duplicate(uint format, IntPtr handle, TYMED kind)
        {
            var bytes = EstimateBytes(format, handle, kind);
            // A zero-byte GlobalAlloc handle is discarded, and Windows rejects
            // it at SetClipboardData. Refuse it before deleting the trigger.
            if (handle == IntPtr.Zero || bytes == 0 || bytes > 64UL * 1024 * 1024) throw CannotPreserve();
            var cloneFormat = format switch { 0x82 => 2u, 0x83 => 3u, _ => format };
            var duplicate = kind == TYMED.TYMED_ENHMF ? CopyEnhMetaFile(handle, null) : OleDuplicateData(handle, unchecked((ushort)cloneFormat), 2);
            if (duplicate == IntPtr.Zero) throw CannotPreserve();
            return new NativeItem(format, duplicate, kind, bytes);
        }
        internal void TransferToWindows() => Handle = IntPtr.Zero;
        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            var medium = new STGMEDIUM { tymed = Kind, unionmember = Handle };
            Handle = IntPtr.Zero;
            ReleaseStgMedium(ref medium);
        }
    }

    private static ulong EstimateBytes(uint format, IntPtr handle, TYMED kind)
    {
        if (kind == TYMED.TYMED_HGLOBAL) return GlobalSize(handle).ToUInt64();
        if (kind == TYMED.TYMED_GDI && format is 2 or 0x82)
        {
            if (GetObject(handle, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0 || bitmap.WidthBytes <= 0) return 0;
            return (ulong)bitmap.WidthBytes * (ulong)Math.Abs((long)bitmap.Height);
        }
        if (kind == TYMED.TYMED_GDI && format == 9) return (ulong)GetPaletteEntries(handle, 0, 0, IntPtr.Zero) * 4;
        if (kind == TYMED.TYMED_ENHMF) return GetEnhMetaFileBits(handle, 0, IntPtr.Zero);
        if (kind == TYMED.TYMED_MFPICT)
        {
            if (GlobalSize(handle).ToUInt64() < (ulong)Marshal.SizeOf<NativeMetafile>()) return 0;
            var pointer = GlobalLock(handle); if (pointer == IntPtr.Zero) return 0;
            try { return GetMetaFileBitsEx(Marshal.PtrToStructure<NativeMetafile>(pointer).Handle, 0, IntPtr.Zero); }
            finally { GlobalUnlock(handle); }
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap { internal int Type, Width, Height, WidthBytes; internal ushort Planes, BitsPixel; internal IntPtr Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMetafile { internal int Mapping, X, Y; internal IntPtr Handle; }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("ole32.dll")] private static extern IntPtr OleDuplicateData(IntPtr source, ushort format, uint flags);
    [DllImport("ole32.dll")] internal static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern uint GetPaletteEntries(IntPtr handle, uint start, uint count, IntPtr entries);
    [DllImport("gdi32.dll")] private static extern uint GetEnhMetaFileBits(IntPtr handle, uint size, IntPtr bytes);
    [DllImport("gdi32.dll")] private static extern uint GetMetaFileBitsEx(IntPtr handle, uint size, IntPtr bytes);
    [DllImport("gdi32.dll", EntryPoint = "CopyEnhMetaFileW", CharSet = CharSet.Unicode)] private static extern IntPtr CopyEnhMetaFile(IntPtr source, string? path);
}
