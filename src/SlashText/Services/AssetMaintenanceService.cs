using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SlashText.Models;

namespace SlashText.Services;

public sealed record OrphanAsset(string RelativePath, string FullPath, long SizeBytes);

public sealed record AssetMaintenanceReport(
    IReadOnlyList<OrphanAsset> Orphans,
    int ReferencedAssetCount,
    int UnsafeReferenceCount)
{
    public long OrphanSizeBytes => Orphans.Sum(item => item.SizeBytes);
    public bool CanDelete => UnsafeReferenceCount == 0;
}

public sealed record AssetCleanupResult(string BackupPath, int DeletedCount);

public sealed partial class AssetMaintenanceService
{
    private readonly string _assetsDirectory;
    private readonly string _assetsRoot;

    public AssetMaintenanceService(string? assetsDirectory = null)
    {
        _assetsDirectory = assetsDirectory ?? AppPaths.AssetsDirectory;
        _assetsRoot = Path.GetFullPath(_assetsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public AssetMaintenanceReport Analyze(IEnumerable<Snippet> snippets)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsafeReferences = 0;

        foreach (var snippet in snippets.Where(item => item.Format == SnippetFormat.Markdown))
        {
            foreach (Match match in MarkdownImagePattern().Matches(snippet.Content))
            {
                var target = match.Groups["target"].Value.Trim().Trim('<', '>');
                if (!target.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveLocalAsset(target, out var fullPath))
                {
                    referenced.Add(fullPath);
                }
                else
                {
                    unsafeReferences++;
                }
            }
        }

        if (!Directory.Exists(_assetsDirectory))
        {
            return new AssetMaintenanceReport([], referenced.Count, unsafeReferences);
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var orphans = Directory.EnumerateFiles(_assetsDirectory, "*", options)
            .Where(path => !IsTemporary(path))
            .Select(Path.GetFullPath)
            .Where(path => IsInsideAssets(path) && !referenced.Contains(path))
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new OrphanAsset(
                    Path.GetRelativePath(_assetsRoot, path)
                        .Replace(Path.DirectorySeparatorChar, '/'),
                    path,
                    info.Length);
            })
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AssetMaintenanceReport(orphans, referenced.Count, unsafeReferences);
    }

    public AssetCleanupResult DeleteOrphansAfterBackup(
        AssetMaintenanceReport report,
        Func<string> createBackup)
    {
        ArgumentNullException.ThrowIfNull(createBackup);
        if (!report.CanDelete)
        {
            throw new InvalidOperationException(
                UiText.Get("A limpeza foi bloqueada porque existem referências locais de imagem inválidas."));
        }

        var backupPath = createBackup();
        return new AssetCleanupResult(backupPath, DeleteOrphans(report));
    }

    private int DeleteOrphans(AssetMaintenanceReport report)
    {
        if (!report.CanDelete)
        {
            throw new InvalidOperationException(
                UiText.Get("A limpeza foi bloqueada porque existem referências locais de imagem inválidas."));
        }

        var deleted = 0;
        foreach (var orphan in report.Orphans)
        {
            var fullPath = Path.GetFullPath(orphan.FullPath);
            if (!IsInsideAssets(fullPath) || !File.Exists(fullPath))
            {
                continue;
            }

            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            File.Delete(fullPath);
            deleted++;
        }

        return deleted;
    }

    private bool TryResolveLocalAsset(string target, out string fullPath)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(target);
            var relative = decoded["assets/".Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            {
                fullPath = string.Empty;
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(_assetsRoot, relative));
            if (!IsInsideAssets(candidate))
            {
                fullPath = string.Empty;
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception) when (
            target.Length > 0)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private bool IsInsideAssets(string path) =>
        path.StartsWith(
            _assetsRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsTemporary(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        return name.StartsWith(".~", StringComparison.Ordinal) ||
               extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".partial", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(
        @"!\[[^\]]*\]\((?<target>[^)\r\n]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImagePattern();
}
