using System.IO;
using SlashText.Models;

namespace SlashText.Services;

internal static class CapturePathResolver
{
    public static string? CreatePortableRelativePath(
        string? filePath,
        AppDataEnvironment environment)
    {
        if (!environment.IsPortable || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }
        var full = Path.GetFullPath(filePath);
        var root = environment.ExecutableDirectory.TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return Path.GetRelativePath(environment.ExecutableDirectory, full);
    }

    public static string Resolve(CaptureRecord record, AppDataEnvironment environment)
    {
        try
        {
            string candidate;
            if (!string.IsNullOrWhiteSpace(record.PortableRelativePath))
            {
                // Never fall back to an absolute path when a portable reference is invalid.
                var relative = record.PortableRelativePath.Replace('/', '\\');
                if (Path.IsPathRooted(relative) || relative.Split('\\').Any(p => p is ".." or ".")) return string.Empty;
                candidate = Path.GetFullPath(Path.Combine(environment.ExecutableDirectory, relative));
                var root = environment.ExecutableDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
                if (!environment.IsPortable) candidate = record.FilePath;
            }
            else candidate = record.FilePath;
            // Reject network/device paths and alternate data streams before any filesystem access.
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length < 3 ||
                !char.IsAsciiLetter(candidate[0]) || candidate[1] != ':' || candidate[2] != '\\' ||
                candidate.AsSpan(2).Contains(':')) return string.Empty;
            if (candidate.Split('\\').Skip(1).Any(p => p.EndsWith(' ') || p.EndsWith('.') || IsDeviceName(p))) return string.Empty;
            return Path.GetFullPath(candidate);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { return string.Empty; }
    }

    private static bool IsDeviceName(string segment)
    {
        var name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && "123456789¹²³".Contains(name[3]);
    }
}
