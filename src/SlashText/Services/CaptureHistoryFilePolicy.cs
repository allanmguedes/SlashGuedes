using System.Buffers.Binary;
using System.IO;
using SlashText.Models;

namespace SlashText.Services;

/// <summary>One gate for history consumers. Metadata alone never makes a file media.</summary>
internal static class CaptureHistoryFilePolicy
{
    public static string MediaKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" => "image",
        ".gif" => "gif",
        ".mp4" => "video",
        _ => string.Empty
    };

    public static bool TryValidate(CaptureRecord record, string path, out string fingerprint)
    {
        fingerprint = string.Empty;
        try
        {
            var kind = MediaKind(path);
            if (kind.Length == 0 || !string.Equals(kind, record.MediaKind, StringComparison.OrdinalIgnoreCase)) return false;
            // Do not follow junctions/symlinks, including any ancestor of the selected file.
            var ancestors = new Stack<string>();
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                ancestors.Push(current);
            // Inspect top-down: querying a child first could already traverse a redirected parent.
            foreach (var current in ancestors)
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[32];
            var count = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            var data = header[..count];
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var valid = extension switch
            {
                ".png" => count >= 24 && data[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) &&
                    data.Slice(12,4).SequenceEqual("IHDR"u8),
                ".jpg" or ".jpeg" => count >= 4 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff,
                ".gif" => count >= 13 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)),
                ".mp4" => count >= 16 && data.Slice(4,4).SequenceEqual("ftyp"u8) &&
                    BinaryPrimitives.ReadUInt32BigEndian(data[..4]) >= 16 &&
                    BinaryPrimitives.ReadUInt32BigEndian(data[..4]) <= stream.Length,
                _ => false
            };
            if (!valid) return false;
            fingerprint = path.ToUpperInvariant() + "|" + stream.Length + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return false; }
    }
}
