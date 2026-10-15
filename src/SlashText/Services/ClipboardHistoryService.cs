using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SlashText.Models;

namespace SlashText.Services;

/// <summary>
/// Owns its clipboard history; it does not read the Windows Win+V store.
/// Monitoring is opt-in, and SlashDesk expansion's temporary clipboard is ignored.
/// </summary>
public sealed class ClipboardHistoryService : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 7951;
    private const uint ModWinShiftNoRepeat = 0x0008 | 0x0004 | 0x4000;
    private const uint VkV = 0x56;
    private const long MaxImageUncompressed = 48L * 1024 * 1024;
    private const int MaxImagePng = 8 * 1024 * 1024;

    private readonly string _folder = Path.Combine(AppPaths.DataDirectory, "ClipboardHistory");
    private readonly JsonFileStore<ClipboardHistoryState> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClipboardHistoryState _state = new();
    private HwndSource? _source;
    private IntPtr _handle;
    private uint _lastSequence;
    private uint _selfSequence;
    private bool _listening;
    private bool _hotkeyRegistered;
    private bool _disposed;

    public ClipboardHistoryService()
    {
        _store = new JsonFileStore<ClipboardHistoryState>(
            Path.Combine(_folder, "history.json"));
    }

    public event EventHandler? Changed;
    public event EventHandler? OpenRequested;
    public IReadOnlyList<ClipboardHistoryItem> Items => _state.Items;
    public bool Enabled { get; private set; }
    public bool CanSave => !_store.IsProtected;
    public bool HotkeyRegistered => _hotkeyRegistered;
    public int MaxEntries { get; set; } = 150;

    public async Task LoadAsync()
    {
        _state = await _store.LoadAsync();
        _state.Items ??= [];
        _state.Items = _state.Items
            .Where(i => i.Kind is "text" or "image" &&
                        !string.IsNullOrEmpty(i.Id) &&
                        (i.Kind == "text" || IsSafeImageName(i.ImageFile)))
            .Take(500)
            .ToList();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Attach(Window owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle != IntPtr.Zero) return;
        _handle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(OnWindowMessage);
        _hotkeyRegistered = RegisterHotKey(_handle, HotKeyId, ModWinShiftNoRepeat, VkV);
        _lastSequence = GetClipboardSequenceNumber();
    }

    public bool SetEnabled(bool enabled)
    {
        if (!_listening && enabled)
        {
            if (_handle == IntPtr.Zero || !AddClipboardFormatListener(_handle))
                return false;
            _lastSequence = GetClipboardSequenceNumber();
            _listening = true;
        }
        else if (_listening && !enabled)
        {
            RemoveClipboardFormatListener(_handle);
            _listening = false;
        }
        Enabled = enabled;
        return true;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wparam,
        IntPtr lparam, ref bool handled)
    {
        if (msg == WmHotKey && wparam.ToInt32() == HotKeyId)
        {
            handled = true;
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (msg == WmClipboardUpdate && Enabled)
        {
            // The clipboard owner may still be rendering. Read after the message
            // rather than blocking the native WndProc / keyboard pipeline.
            _ = ReadClipboardAsync();
        }
        return IntPtr.Zero;
    }

    private async Task ReadClipboardAsync()
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            if (_disposed || !Enabled || !CanSave) return;
            var seq = GetClipboardSequenceNumber();
            if (seq == 0 || seq == _lastSequence || seq == _selfSequence) return;
            _lastSequence = seq;
            await Task.Yield();
            if (seq != GetClipboardSequenceNumber()) return;

            var data = Clipboard.GetDataObject();
            if (data is null ||
                data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing", false) ||
                data.GetDataPresent("SlashDesk.Expansion.Marker", false)) return;

            ClipboardHistoryItem? item = null;
            byte[]? imageBytes = null;
            if (data.GetDataPresent(DataFormats.UnicodeText) ||
                data.GetDataPresent(DataFormats.Text))
            {
                var value = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(value) || value.Length > 400_000) return;
                item = new ClipboardHistoryItem { Kind = "text", Text = value };
            }
            else if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image is null ||
                    (long)image.PixelWidth * image.PixelHeight * 4 > MaxImageUncompressed) return;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                if (stream.Length > MaxImagePng) return;
                imageBytes = stream.ToArray();
                item = new ClipboardHistoryItem
                {
                    Kind = "image",
                    ImageFile = Guid.NewGuid().ToString("N") + ".png"
                };
            }
            if (item is null || seq != GetClipboardSequenceNumber()) return;

            var same = _state.Items.FirstOrDefault(old =>
                old.Kind == item.Kind &&
                (item.Kind == "text" && old.Text == item.Text));
            if (same is not null)
            {
                _state.Items.Remove(same);
                same.CopiedUtc = DateTimeOffset.UtcNow;
                _state.Items.Insert(0, same);
            }
            else
            {
                if (imageBytes is not null)
                {
                    var images = Path.Combine(_folder, "images");
                    Directory.CreateDirectory(images);
                    await File.WriteAllBytesAsync(Path.Combine(images, item.ImageFile), imageBytes);
                }
                _state.Items.Insert(0, item);
            }
            await SaveWithRetentionAsync();
        }
        catch (Exception error) when (error is ExternalException or IOException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // A clipboard read failure never blocks the user's copy/paste.
            AppDiagnosticLog.Write("clipboard.read.skipped", ("exceptionType", error.GetType().Name));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ChangeAsync(ClipboardHistoryItem item, bool? favorite = null,
        bool? pinned = null, string? category = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.Items.Contains(item) || !CanSave) return;
            if (favorite.HasValue) item.IsFavorite = favorite.Value;
            if (pinned.HasValue) item.IsPinned = pinned.Value;
            if (category is not null) item.Category = category.Trim()[..Math.Min(50, category.Trim().Length)];
            await SaveWithRetentionAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(ClipboardHistoryItem item)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.Items.Remove(item) || !CanSave) return;
            await _store.SaveAsync(_state);
            DeleteImage(item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!CanSave) return;
            var existing = _state.Items.ToArray();
            _state.Items.Clear();
            await _store.SaveAsync(_state);
            foreach (var item in existing) DeleteImage(item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public bool CopyToClipboard(ClipboardHistoryItem item)
    {
        if (!_state.Items.Contains(item)) return false;
        try
        {
            if (item.Kind == "text") Clipboard.SetText(item.Text);
            else if (item.Kind == "image" && IsSafeImageName(item.ImageFile))
            {
                var path = Path.Combine(_folder, "images", item.ImageFile);
                if (!File.Exists(path)) return false;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                Clipboard.SetImage(bitmap);
            }
            else return false;
            _selfSequence = GetClipboardSequenceNumber();
            return true;
        }
        catch (Exception error) when (error is ExternalException or IOException or
            UnauthorizedAccessException or InvalidOperationException)
        {
            AppDiagnosticLog.Write("clipboard.reuse.failed", ("exceptionType", error.GetType().Name));
            return false;
        }
    }

    public BitmapImage? LoadThumbnail(ClipboardHistoryItem item)
    {
        if (item.Kind != "image" || !IsSafeImageName(item.ImageFile)) return null;
        var path = Path.Combine(_folder, "images", item.ImageFile);
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 360;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task SaveWithRetentionAsync()
    {
        MaxEntries = Math.Clamp(MaxEntries, 10, 500);
        var toDrop = _state.Items
            .Where(item => !item.IsPinned)
            .Skip(Math.Max(0, MaxEntries - _state.Items.Count(i => i.IsPinned)))
            .ToArray();
        foreach (var item in toDrop) _state.Items.Remove(item);
        await _store.SaveAsync(_state);
        foreach (var item in toDrop) DeleteImage(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteImage(ClipboardHistoryItem item)
    {
        if (!IsSafeImageName(item.ImageFile)) return;
        var path = Path.Combine(_folder, "images", item.ImageFile);
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static bool IsSafeImageName(string name) =>
        name.Length == 36 && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        && name[..32].All(Uri.IsHexDigit);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_listening) RemoveClipboardFormatListener(_handle);
        if (_hotkeyRegistered) UnregisterHotKey(_handle, HotKeyId);
        _source?.RemoveHook(OnWindowMessage);
        _gate.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
