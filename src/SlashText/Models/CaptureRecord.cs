namespace SlashText.Models;

public sealed class CaptureRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public string MediaKind { get; set; } = "image";
    public string FilePath { get; set; } = string.Empty;
    public string? PortableRelativePath { get; set; }
    // Restore always sets this locally, regardless of the value supplied by the archive.
    // Approval is session-only and is never serialized back as trusted.
    public bool RequiresFileApproval { get; set; } = true;
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
}
