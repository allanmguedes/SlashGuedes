using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SlashText.Services;

/// <summary>Immutable normalized PNG owned by a document, independent of the collection/file lifetime.</summary>
public sealed class CaptureStampImage
{
    private readonly byte[] _png;
    internal CaptureStampImage(byte[] png) => _png = (byte[])png.Clone();
    public Bitmap CreateBitmap()
    {
        using var stream = new MemoryStream(_png, writable: false);
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }
    public BitmapSource CreateImageSource()
    {
        using var stream = new MemoryStream(_png, writable: false);
        var image = new BitmapImage(); image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 64;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
}

/// <summary>Local image stamps. Kept outside snippet assets so image maintenance cannot delete them.</summary>
public sealed class CaptureStampCatalog
{
    public const string CustomCategory = "Meus emojis";
    private sealed record Entry(string Id, string Name);
    private static CaptureStampCatalog? _current;
    private readonly string _directory;
    private readonly Dictionary<string, CaptureStampImage> _cache = [];
    public CaptureStampCatalog(string directory) => _directory = Path.GetFullPath(directory);
    public static CaptureStampCatalog Current
    {
        get
        {
            var directory = Path.Combine(AppPaths.DataDirectory, "capture-emotes");
            if (_current is null || _current._directory != directory) _current = new CaptureStampCatalog(directory);
            return _current;
        }
    }
    private string Manifest => Path.Combine(_directory, "catalog.json");
    internal static void InvalidateCurrent() => _current = null;
    internal static void ValidateDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException(UiText.Get("Pasta de emojis inválida."));
        var catalog = new CaptureStampCatalog(directory);
        if (File.Exists(catalog.Manifest) && File.GetAttributes(catalog.Manifest).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException(UiText.Get("Catálogo de emojis inválido."));
        var entries = catalog.Read();
        if (entries.Count == 0 && !File.Exists(catalog.Manifest) && Directory.EnumerateFiles(directory, "*.png").Any())
            throw new InvalidDataException(UiText.Get("A coleção de emojis está sem catálogo."));
        foreach (var entry in entries)
        {
            var image = Path.Combine(directory, entry.Id + ".png");
            if (!File.Exists(image) || File.GetAttributes(image).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException(UiText.Get("A coleção de emojis contém uma imagem ausente ou inválida."));
            catalog.GetImage("custom:" + entry.Id);
        }
    }
    public static bool IsCustom(string value) => value.StartsWith("custom:", StringComparison.Ordinal);
    private static string Id(string value)
    {
        if (!IsCustom(value) || !Guid.TryParseExact(value[7..], "N", out var guid))
            throw new ArgumentException(UiText.Get("Emote personalizado inválido."), nameof(value));
        return guid.ToString("N");
    }
    private List<Entry> Read()
    {
        if (!File.Exists(Manifest)) return [];
        if (new FileInfo(Manifest).Length > 1024 * 1024) throw new InvalidDataException(UiText.Get("Catálogo de emotes inválido."));
        var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Manifest)) ?? [];
        if (entries.Count > 1000 || entries.Any(e => e is null || !Guid.TryParseExact(e.Id, "N", out _) || string.IsNullOrWhiteSpace(e.Name)))
            throw new InvalidDataException(UiText.Get("Catálogo de emotes inválido."));
        return entries.DistinctBy(e => e.Id).ToList();
    }
    private void Write(List<Entry> entries)
    {
        Directory.CreateDirectory(_directory);
        var temporary = Manifest + $".{Guid.NewGuid():N}.tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(entries)); File.Move(temporary, Manifest, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public IReadOnlyList<NotoEmojiItem> Items => Read().Where(e => File.Exists(Path.Combine(_directory, e.Id + ".png")))
        .Select(e => new NotoEmojiItem("custom:" + e.Id, e.Name, e.Id + ".png", CustomCategory, e.Name)).ToArray();
    public bool TryGet(string value, out NotoEmojiItem item)
    {
        if (!IsCustom(value)) return NotoEmojiCatalog.TryGet(value, out item);
        item = Items.FirstOrDefault(e => e.Value == value)!;
        return item is not null;
    }
    public string Import(string path, string? name = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0 || info.Length > 16 * 1024 * 1024)
            throw new InvalidDataException(UiText.Get("Escolha um PNG ou JPEG de até 16 MB."));
        using var stream = File.OpenRead(path);
        using var original = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        if (original.RawFormat.Guid != ImageFormat.Png.Guid && original.RawFormat.Guid != ImageFormat.Jpeg.Guid)
            throw new InvalidDataException(UiText.Get("Use uma imagem PNG ou JPEG estática."));
        if ((long)original.Width * original.Height > 16_000_000 || original.Width <= 0 || original.Height <= 0)
            throw new InvalidDataException(UiText.Get("Use uma imagem de até 16 megapixels."));
        var scale = Math.Min(1d, 512d / Math.Max(original.Width, original.Height));
        using var normalized = new Bitmap(Math.Max(1, (int)Math.Round(original.Width * scale)),
            Math.Max(1, (int)Math.Round(original.Height * scale)), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var attributes = new ImageAttributes(); attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(original, new Rectangle(0, 0, normalized.Width, normalized.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
        }
        var entries = Read();
        if (entries.Count >= 1000) throw new InvalidDataException(UiText.Get("A coleção aceita até 1.000 imagens. Remova uma antes de adicionar outra."));
        var id = Guid.NewGuid().ToString("N");
        var label = (string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name).Trim();
        if (label.Length > 60) label = label[..60];
        if (label.Length == 0) label = "Meu emoji";
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, id + ".png");
        try { normalized.Save(destination, ImageFormat.Png); entries.Add(new Entry(id, label)); Write(entries); }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
        return "custom:" + id;
    }
    public void Remove(string value)
    {
        var id = Id(value); var entries = Read();
        if (entries.RemoveAll(e => e.Id == id) == 0) return;
        Write(entries); _cache.Remove(value);
        // Existing annotations own the decoded asset. Removing the collection entry cannot invalidate undo/export.
        File.Delete(Path.Combine(_directory, id + ".png"));
    }
    public CaptureStampImage? GetImage(string value)
    {
        if (!IsCustom(value)) return null;
        if (_cache.TryGetValue(value, out var cached)) return cached;
        var id = Id(value);
        if (!Read().Any(e => e.Id == id)) throw new ArgumentException(UiText.Get("Emote não está mais na coleção."), nameof(value));
        var path = Path.Combine(_directory, id + ".png");
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException(UiText.Get("Imagem local inválida."));
        var asset = new CaptureStampImage(File.ReadAllBytes(path));
        using var check = asset.CreateBitmap();
        if (check.Width > 512 || check.Height > 512) throw new InvalidDataException(UiText.Get("Imagem local inválida."));
        if (_cache.Count >= 96) _cache.Clear();
        _cache[value] = asset; return asset;
    }
    public BitmapSource CreateImageSource(string value) => GetImage(value)?.CreateImageSource() ?? NotoEmojiCatalog.CreateImageSource(value);
}
