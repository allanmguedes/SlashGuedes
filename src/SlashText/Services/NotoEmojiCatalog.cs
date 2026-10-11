using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SlashText.Services;

public sealed record NotoEmojiItem(string Value, string Name, string AssetName,
    string Category = "Acesso rápido", string Keywords = "");

public static class NotoEmojiCatalog
{
    private const string ResourcePrefix = "SlashText.Assets.NotoEmoji.";

    public static IReadOnlyList<NotoEmojiItem> QuickItems { get; } =
    [
        new("❤️", "Coração", "emoji_u2764.png"),
        new("⭐", "Estrela", "emoji_u2b50.png"),
        new("❓", "Interrogação", "emoji_u2753.png"),
        new("✔️", "Confirmação", "emoji_u2714.png"),
        new("❌", "Erro", "emoji_u274c.png"),
        new("🔥", "Fogo", "emoji_u1f525.png"),
        new("👍", "Positivo", "emoji_u1f44d.png"),
        new("👎", "Negativo", "emoji_u1f44e.png"),
        new("👏", "Palmas", "emoji_u1f44f.png"),
        new("🙌", "Comemoração", "emoji_u1f64c.png"),
        new("👀", "Olhos", "emoji_u1f440.png"),
        new("💯", "Cem", "emoji_u1f4af.png"),
        new("🙂", "Sorriso leve", "emoji_u1f642.png"),
        new("😟", "Preocupado", "emoji_u1f61f.png"),
        new("😘", "Beijo", "emoji_u1f618.png"),
        new("😍", "Apaixonado", "emoji_u1f60d.png"),
        new("😂", "Rindo com lágrimas", "emoji_u1f602.png"),
        new("😭", "Chorando", "emoji_u1f62d.png"),
        new("😀", "Sorridente", "emoji_u1f600.png"),
        new("😃", "Muito sorridente", "emoji_u1f603.png"),
        new("😄", "Sorriso aberto", "emoji_u1f604.png"),
        new("😁", "Sorriso largo", "emoji_u1f601.png"),
        new("😆", "Riso", "emoji_u1f606.png"),
        new("😅", "Alívio", "emoji_u1f605.png"),
        new("🤣", "Gargalhada", "emoji_u1f923.png"),
        new("😉", "Piscando", "emoji_u1f609.png"),
        new("😮", "Surpreso", "emoji_u1f62e.png"),
        new("🤩", "Olhos de estrela", "emoji_u1f929.png"),
        new("🥳", "Festa", "emoji_u1f973.png"),
        new("🙃", "De cabeça para baixo", "emoji_u1f643.png"),
        new("😊", "Feliz", "emoji_u1f60a.png"),
        new("🥲", "Sorriso com lágrima", "emoji_u1f972.png"),
        new("🥹", "Emocionado", "emoji_u1f979.png"),
        new("😴", "Dormindo", "emoji_u1f634.png"),
        new("😎", "Óculos escuros", "emoji_u1f60e.png"),
        new("🎉", "Confetes", "emoji_u1f389.png")
    ];

    private sealed record Snapshot(string Commit, int Count, NotoEmojiItem[] Items);
    private static Snapshot LoadSnapshot()
    {
        using var stream = typeof(NotoEmojiCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + "catalog.json")
            ?? throw new InvalidOperationException(UiText.Get("Catálogo Noto incorporado ausente."));
        var snapshot = JsonSerializer.Deserialize<Snapshot>(stream)
            ?? throw new InvalidOperationException(UiText.Get("Catálogo Noto inválido."));
        if (snapshot.Items.Length != snapshot.Count || snapshot.Count < 3600)
            throw new InvalidOperationException(UiText.Get("Catálogo Noto incompleto."));
        return snapshot;
    }
    private static readonly Snapshot Catalog = LoadSnapshot();
    public static IReadOnlyList<NotoEmojiItem> Items { get; } = Array.AsReadOnly(Catalog.Items);
    public static string SourceCommit => Catalog.Commit;
    public static IReadOnlyList<string> Categories { get; } = Items.Select(i => i.Category).Distinct().ToArray();
    private sealed record LocalizedSnapshot(Dictionary<string, string[]> Labels);
    private static readonly Lazy<IReadOnlyDictionary<string, Dictionary<string, string[]>>> LocalizedLabels = new(() =>
    {
        var labels = new Dictionary<string, Dictionary<string, string[]>>();
        foreach (var language in new[] { "en", "es" })
        {
            using var stream = typeof(NotoEmojiCatalog).Assembly.GetManifestResourceStream($"SlashText.Localization.emoji-{language}.json")
                ?? throw new InvalidOperationException("Missing embedded emoji language catalog: " + language);
            labels.Add(language, JsonSerializer.Deserialize<LocalizedSnapshot>(stream)?.Labels
                ?? throw new InvalidOperationException("Invalid embedded emoji language catalog: " + language));
        }
        return labels;
    });

    // Custom image names and the canonical catalog metadata remain untouched.
    public static string DisplayName(NotoEmojiItem item) => UiText.Language != "pt-BR" &&
        LocalizedLabels.Value[UiText.Language].TryGetValue(NormalizeValue(item.Value), out var label) ? label[0] : item.Name;

    public static string SearchTerms(NotoEmojiItem item, string language) => language != "pt-BR" &&
        LocalizedLabels.Value[language].TryGetValue(NormalizeValue(item.Value), out var label) ? string.Join(" ", label) : item.Name + " " + item.Keywords;
    private static string NormalizeValue(string value) => value.Replace("\uFE0F", "", StringComparison.Ordinal);
    private static readonly IReadOnlyDictionary<string, NotoEmojiItem> Index =
        Items.ToDictionary(i => NormalizeValue(i.Value), StringComparer.Ordinal);
    private static readonly Dictionary<string, BitmapSource> PreviewCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> PreviewOrder = new();
    private static readonly Lazy<ZipArchive> AssetArchive = new(() => new ZipArchive(
        typeof(NotoEmojiCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + "catalog-assets.zip")
            ?? throw new InvalidOperationException("Pacote Noto incorporado ausente."), ZipArchiveMode.Read));

    public static bool TryGet(string value, out NotoEmojiItem item)
    {
        return Index.TryGetValue(NormalizeValue(value), out item!);
    }

    public static BitmapSource CreateImageSource(string value)
    {
        if (!TryGet(value, out var item))
        {
            throw new ArgumentException(UiText.Get("Emoji não disponível no catálogo local."), nameof(value));
        }
        lock (PreviewCache)
        {
            if (PreviewCache.TryGetValue(item.Value, out var cached)) return cached;
            using var stream = Open(item);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 40;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            if (PreviewCache.Count >= 192) PreviewCache.Remove(PreviewOrder.Dequeue());
            PreviewCache[item.Value] = image; PreviewOrder.Enqueue(item.Value);
            return image;
        }
    }

    public static System.Drawing.Bitmap CreateBitmap(string value)
    {
        if (!TryGet(value, out var item))
        {
            throw new ArgumentException(UiText.Get("Emoji não disponível no catálogo local."), nameof(value));
        }
        using var stream = Open(item);
        using var source = new System.Drawing.Bitmap(stream);
        return new System.Drawing.Bitmap(source);
    }

    public static bool HasAsset(NotoEmojiItem item)
    {
        lock (AssetArchive) return AssetArchive.Value.GetEntry(item.AssetName) is not null;
    }

    private static Stream Open(NotoEmojiItem item)
    {
        // ZipArchive shares a seekable resource stream. Copy only the requested
        // PNG under the lock; callers then decode their own independent stream.
        lock (AssetArchive)
        {
            var entry = AssetArchive.Value.GetEntry(item.AssetName)
                ?? throw new InvalidOperationException($"Recurso Noto Emoji ausente: {item.AssetName}");
            using var source = entry.Open();
            var bytes = new MemoryStream((int)entry.Length);
            source.CopyTo(bytes); bytes.Position = 0; return bytes;
        }
    }
}
