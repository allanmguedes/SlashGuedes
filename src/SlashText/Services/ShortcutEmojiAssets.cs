using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SlashText.Services;

/// <summary>Snippet-owned PNGs survive removal from the capture emoji collection.
/// Native Inline.Tag preserves Markdown through WPF save/undo/redo.</summary>
internal static partial class ShortcutEmojiAssets
{
    private const string Prefix = "SlashDesk.Emoji:";
    internal static string Save(string value)
    {
        var catalog = CaptureStampCatalog.Current;
        if (!catalog.TryGet(value, out var item)) throw new ArgumentException(UiText.Get("Emoji indisponível."));
        using var bitmap = catalog.GetImage(value)?.CreateBitmap() ?? NotoEmojiCatalog.CreateBitmap(value);
        using var png = new MemoryStream(); bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var bytes = png.ToArray();
        var name = "emote-" + Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".png";
        Directory.CreateDirectory(AppPaths.AssetsDirectory);
        var path = Path.Combine(AppPaths.AssetsDirectory, name);
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        var alt = (CaptureStampCatalog.IsCustom(value) ? item.Name : value).Replace("[", "").Replace("]", "").Replace('\n', ' ').Replace('\r', ' ');
        return $"![Emoji: {alt}](assets/{name})";
    }
    internal static InlineUIContainer? CreateInline(string token, TextPointer? position = null)
    {
        var match = Pattern().Match(token);
        if (!match.Success || match.Length != token.Length) return null;
        var path = Path.Combine(AppPaths.AssetsDirectory, match.Groups["file"].Value);
        if (!File.Exists(path)) return null;
        var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad;
        source.DecodePixelWidth = 64; source.UriSource = new Uri(path); source.EndInit(); source.Freeze();
        var image = new Image { Source = source, Width = 28, Height = 28, Stretch = Stretch.Uniform,
            ToolTip = match.Groups["alt"].Value, Tag = Prefix + token };
        System.Windows.Automation.AutomationProperties.SetName(image, match.Groups["alt"].Value);
        var inline = position is null ? new InlineUIContainer(image) : new InlineUIContainer(image, position);
        inline.Tag = Prefix + token; inline.BaselineAlignment = BaselineAlignment.Center;
        return inline;
    }
    internal static bool TryRead(InlineUIContainer inline, out string token)
    {
        token = inline.Tag is string tag && tag.StartsWith(Prefix, StringComparison.Ordinal) ? tag[Prefix.Length..] : "";
        return Pattern().IsMatch(token);
    }
    internal static void RestoreDocument(FlowDocument document)
    {
        void Inlines(InlineCollection inlines)
        {
            foreach (var inline in inlines)
                if (inline is InlineUIContainer image) Restore(image);
                else if (inline is Span span) Inlines(span.Inlines);
        }
        void Blocks(BlockCollection blocks)
        {
            foreach (var block in blocks)
                if (block is Paragraph paragraph) Inlines(paragraph.Inlines);
                else if (block is System.Windows.Documents.List list)
                    foreach (var item in list.ListItems) Blocks(item.Blocks);
                else if (block is Table table)
                    foreach (var group in table.RowGroups) foreach (var row in group.Rows) foreach (var cell in row.Cells) Blocks(cell.Blocks);
        }
        Blocks(document.Blocks);
    }
    internal static void Restore(InlineUIContainer inline)
    {
        if (!TryRead(inline, out var token) || inline.Child is Image { Source: not null }) return;
        if (CreateInline(token) is not { } restored) return;
        var image = (Image)restored.Child; restored.Child = null;
        // Restore presentation inside the native UI, without adding text undo units.
        if (inline.Child is Grid placeholder) { if (placeholder.Children.Count == 0) placeholder.Children.Add(image); }
        else if (inline.Child is Image existing)
        { existing.Source = image.Source; existing.Width = image.Width; existing.Height = image.Height; existing.ToolTip = image.ToolTip; }
    }
    [GeneratedRegex(@"!\[(?<alt>Emoji: [^\]]*)\]\(assets/(?<file>emote-[0-9a-f]{64}\.png)\)")]
    internal static partial Regex Pattern();
}
