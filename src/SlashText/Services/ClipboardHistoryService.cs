using System.IO;
using System.Security.Cryptography;
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
    private const int FirstHotKeyId = 7951;
    private const int SecondHotKeyId = 7952;
    private const uint ModNoRepeat = 0x4000;
    private const long MaxImageUncompressed = 48L * 1024 * 1024;
    private const int MaxImagePng = 8 * 1024 * 1024;
    // Bound thumbnail cache so scrolling and repeated searches avoid disk decode.
    private const int MaxCachedThumbnails = 24;

    private readonly string _folder = Path.Combine(AppPaths.DataDirectory, "ClipboardHistory");
    private readonly JsonFileStore<ClipboardHistoryState> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClipboardHistoryState _state = new();
    private readonly Dictionary<string, BitmapImage> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _thumbnailOrder = new();
    private HwndSource? _source;
    private IntPtr _handle;
    private uint _lastSequence;
    private uint _selfSequence;
    private bool _listening;
    private bool _hotkeyRegistered;
    private int _hotkeyId;
    private bool _disposed;

    public ClipboardHistoryService()
    {
        _store = new JsonFileStore<ClipboardHistoryState>(
            Path.Combine(_folder, "history.json"));
    }

    public event EventHandler? Changed;
    public event EventHandler? OpenRequested;
    public IReadOnlyList<ClipboardHistoryItem> Items => _state.Items;
    public IReadOnlyList<string> Categories => _state.Categories;
    public bool Enabled { get; private set; }
    public bool CanSave => !_store.IsProtected;
    public bool HotkeyRegistered => _hotkeyRegistered;
    public int MaxEntries { get; private set; } = ClipboardRetentionPolicy.DefaultRecentLimit;
    public string Shortcut { get; private set; } = ClipboardShortcut.Default;

    public async Task LoadAsync(int maxRecentEntries)
    {
        MaxEntries = ClipboardRetentionPolicy.NormalizeLimit(maxRecentEntries);
        _state = await _store.LoadAsync();
        _thumbnailCache.Clear();
        _thumbnailOrder.Clear();
        _state.Items ??= [];
        _state.Categories ??= [];
        _state.Categories = ClipboardCategoryRules.Migrate(
            _state.Categories, _state.Items.Select(i => i?.Category ?? string.Empty));
        _state.Items = _state.Items
            .Where(i => i is not null && (i.Kind is "text" or "image") &&
                        !string.IsNullOrEmpty(i.Id) &&
                        (i.Kind == "text" || IsSafeImageName(i.ImageFile)))
            .OrderByDescending(i => i.CopiedUtc)
            .ToList();
        if (ClipboardRetentionPolicy.SelectExpired(_state.Items, MaxEntries).Count > 0)
        {
            if (CanSave) await SaveWithRetentionAsync();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Attach(Window owner, string? savedShortcut, out string? warning)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        warning = null;
        if (_handle != IntPtr.Zero) return;
        _handle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(OnWindowMessage);
        var requested = string.IsNullOrWhiteSpace(savedShortcut)
            ? ClipboardShortcut.Default : savedShortcut.Trim();
        if (!TrySetShortcut(requested, out var error))
        {
            // A competing application may own the old combo, particularly
            // Win+Shift+V. Try safe alternatives; never leave the user with a
            // deceptively displayed shortcut that Windows did not register.
            var candidates = new[]
            {
                "Ctrl+Alt+V", "Ctrl+Shift+H", "Alt+Shift+V", "Ctrl+Alt+H"
            };
            var fallback = candidates.FirstOrDefault(candidate =>
                !candidate.Equals(requested, StringComparison.OrdinalIgnoreCase) &&
                TrySetShortcut(candidate, out _));
            warning = fallback is null
                ? "Nenhuma combinação automática disponível. Abra pelo botão e escolha outro atalho. " + error
                : $"{requested} não está disponível. Atalho alternativo ativado: {fallback}.";
        }
        _lastSequence = GetClipboardSequenceNumber();
    }

    public bool TrySetShortcut(string value, out string error)
    {
        error = string.Empty;
        if (!ClipboardShortcut.TryParse(value, out var normalized, out var modifiers, out var key))
        {
            error = "O atalho precisa ter Ctrl, Alt ou Win e uma tecla. Shift pode ser adicionado, mas não pode ser usado sozinho.";
            return false;
        }
        if (_hotkeyRegistered && Shortcut.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            return true;
        if (_handle == IntPtr.Zero)
        {
            error = "O aplicativo ainda está iniciando.";
            return false;
        }
        var nextId = _hotkeyId == FirstHotKeyId ? SecondHotKeyId : FirstHotKeyId;
        // Keep the previous hotkey until Windows accepts the replacement.
        if (!RegisterHotKey(_handle, nextId, modifiers | ModNoRepeat, key))
        {
            error = "Esse atalho está reservado ou já está sendo usado. Escolha outra combinação.";
            return false;
        }
        if (_hotkeyRegistered) UnregisterHotKey(_handle, _hotkeyId);
        _hotkeyId = nextId;
        _hotkeyRegistered = true;
        Shortcut = normalized;
        return true;
    }

    public async Task SetMaxEntriesAsync(int recentLimit)
    {
        await _gate.WaitAsync();
        try
        {
            MaxEntries = ClipboardRetentionPolicy.NormalizeLimit(recentLimit);
            if (CanSave) await SaveWithRetentionAsync();
            else Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
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
        if (msg == WmHotKey && _hotkeyRegistered && wparam.ToInt32() == _hotkeyId)
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
                    ImageFile = Guid.NewGuid().ToString("N") + ".png",
                    Fingerprint = Convert.ToHexString(SHA256.HashData(imageBytes))
                };
            }
            if (item is null || seq != GetClipboardSequenceNumber()) return;

            var same = _state.Items.FirstOrDefault(old =>
                old.Kind == item.Kind &&
                (item.Kind == "text" && old.Text == item.Text ||
                 item.Kind == "image" && item.Fingerprint.Length > 0 &&
                 old.Fingerprint == item.Fingerprint));
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
            if (category is not null)
            {
                var normalized = category.Trim();
                if (normalized.Length > 0 && !_state.Categories.Contains(
                        normalized, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Essa categoria não existe. Crie a categoria antes de aplicá-la.");
                item.Category = normalized;
            }
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

    public async Task ClearNonFavoritesAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!CanSave) return;
            // A pinned-only item is intentionally removed: only ★ favorite protects
            // against this explicit action. Automatic expiry still protects pins.
            var toDelete = ClipboardCategoryRules.SelectNonFavorites(
                _state.Items, i => i.IsFavorite).ToArray();
            foreach (var item in toDelete) _state.Items.Remove(item);
            await _store.SaveAsync(_state);
            foreach (var item in toDelete) DeleteImage(item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task AddCategoryAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            EnsureCategoryEditable();
            var normalized = ClipboardCategoryRules.ValidateNew(name, _state.Categories);
            _state.Categories.Add(normalized);
            await _store.SaveAsync(_state);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task RenameCategoryAsync(string oldName, string newName)
    {
        await _gate.WaitAsync();
        try
        {
            EnsureCategoryEditable();
            var index = _state.Categories.FindIndex(x =>
                x.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException("Categoria não encontrada.");
            var normalized = ClipboardCategoryRules.ValidateNew(
                newName, _state.Categories.Where((_, i) => i != index));
            var original = _state.Categories[index];
            _state.Categories[index] = normalized;
            foreach (var item in _state.Items.Where(i =>
                string.Equals(i.Category, original, StringComparison.OrdinalIgnoreCase)))
                item.Category = normalized;
            await _store.SaveAsync(_state);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteCategoryAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            EnsureCategoryEditable();
            var removed = _state.Categories.RemoveAll(x =>
                x.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return;
            // Removing a group never removes the copied content.
            foreach (var item in _state.Items.Where(i =>
                string.Equals(i.Category, name, StringComparison.OrdinalIgnoreCase)))
                item.Category = string.Empty;
            await _store.SaveAsync(_state);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    private void EnsureCategoryEditable()
    {
        if (!CanSave) throw new InvalidOperationException(
            "O armazenamento está protegido. Nenhuma categoria foi alterada.");
    }

    public async Task ClearRecentsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!CanSave) return;
            var toDelete = _state.Items.Where(i => !i.IsFavorite && !i.IsPinned).ToArray();
            foreach (var item in toDelete) _state.Items.Remove(item);
            await _store.SaveAsync(_state);
            foreach (var item in toDelete) DeleteImage(item);
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
        if (!File.Exists(path))
        {
            _thumbnailCache.Remove(item.ImageFile);
            return null;
        }
        if (_thumbnailCache.TryGetValue(item.ImageFile, out var cached)) return cached;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 360;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            _thumbnailCache[item.ImageFile] = image;
            _thumbnailOrder.Enqueue(item.ImageFile);
            while (_thumbnailOrder.Count > MaxCachedThumbnails)
                _thumbnailCache.Remove(_thumbnailOrder.Dequeue());
            return image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task SaveWithRetentionAsync()
    {
        var toDrop = ClipboardRetentionPolicy.SelectExpired(_state.Items, MaxEntries);
        foreach (var item in toDrop) _state.Items.Remove(item);
        await _store.SaveAsync(_state);
        foreach (var item in toDrop) DeleteImage(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteImage(ClipboardHistoryItem item)
    {
        if (!IsSafeImageName(item.ImageFile)) return;
        _thumbnailCache.Remove(item.ImageFile);
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
        if (_hotkeyRegistered) UnregisterHotKey(_handle, _hotkeyId);
        _source?.RemoveHook(OnWindowMessage);
        // A clipboard callback already in flight may finish after closing.
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
