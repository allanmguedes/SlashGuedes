namespace SlashText.Models;

// Stored only on this computer, in a directory separated from the upstream SlashDesk.
public sealed class ClipboardHistoryState
{
    public List<ClipboardHistoryItem> Items { get; set; } = [];
}

public sealed class ClipboardHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CopiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = string.Empty;
    public string ImageFile { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsFavorite { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Summary => Kind == "image"
        ? $"Imagem • {CopiedUtc.ToLocalTime():dd/MM HH:mm}"
        : (Text.Length > 140 ? Text[..140] + "…" : Text).Replace('\r', ' ').Replace('\n', ' ');
}
