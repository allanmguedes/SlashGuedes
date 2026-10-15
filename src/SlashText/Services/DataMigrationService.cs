using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace SlashText.Services;

public sealed record DataMigrationResult(
    string DataDirectory,
    string? SourceDirectory,
    bool Migrated,
    bool CompetingSourcePreserved,
    string? BackupPath,
    IReadOnlyList<string> Warnings);

public sealed class DataMigrationService
{
    private static readonly string[] RecognizedFiles =
    [
        "snippets.md",
        "settings.json",
        "usage.json",
        "capture-history.json",
        "update-state.json"
    ];

    public DataMigrationResult EnsureLayout(AppDataEnvironment environment)
    {
        var target = environment.DataDirectory;
        var warnings = new List<string>();
        // Pilot never probes, imports or backs up the official installation.
        if (environment.IsCapturePilot || environment.IsSlashGuedes)
        {
            EnsureDirectories(target);
            return new DataMigrationResult(target, null, false, false, null, warnings);
        }
        var source = SelectMigrationSource(environment, target);
        var targetHasData = HasRecognizedData(target);
        string? backupPath = null;
        var competingSourcePreserved = false;

        if (targetHasData)
        {
            if (source is not null && !SamePath(source, target) && HasRecognizedData(source))
            {
                var decisionMarker = Path.Combine(target, "migration-competing-source.json");
                if (!File.Exists(decisionMarker))
                {
                    Directory.CreateDirectory(Path.Combine(target, "Backups"));
                    backupPath = CreateSourceBackup(source, target, "competing-source");
                    File.WriteAllText(decisionMarker, JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        decidedAtUtc = DateTimeOffset.UtcNow,
                        selectedDirectory = target,
                        preservedSourceDirectory = source,
                        backupPath
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    competingSourcePreserved = true;
                }
            }
            EnsureDirectories(target);
            return new DataMigrationResult(
                target,
                source,
                Migrated: false,
                competingSourcePreserved,
                backupPath,
                warnings);
        }

        if (source is null || !HasRecognizedData(source))
        {
            EnsureDirectories(target);
            return new DataMigrationResult(target, null, false, false, null, warnings);
        }

        var parent = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException(UiText.Get("O diretório de dados não possui pasta pai."));
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(target)}-migration-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(source, staging);
            warnings.AddRange(ValidateCopiedData(staging));
            backupPath = CreateSourceBackup(source, staging, "before-migration");
            WriteMigrationManifest(staging, source, target, warnings);
            if (Directory.Exists(target))
            {
                if (Directory.EnumerateFileSystemEntries(target).Any())
                {
                    throw new IOException(UiText.Get("A origem de dados foi criada por outro processo durante a migração."));
                }
                Directory.Delete(target);
            }
            Directory.Move(staging, target);
            if (backupPath is not null)
            {
                backupPath = Path.Combine(target, Path.GetRelativePath(staging, backupPath));
            }
            EnsureDirectories(target);
            return new DataMigrationResult(target, source, true, false, backupPath, warnings);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public static bool HasRecognizedData(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }
        return RecognizedFiles.Any(file => File.Exists(Path.Combine(directory, file))) ||
               Directory.Exists(Path.Combine(directory, "assets")) ||
               Directory.Exists(Path.Combine(directory, "Backups")) ||
               Directory.Exists(Path.Combine(directory, "backups"));
    }

    private static string? SelectMigrationSource(AppDataEnvironment environment, string target)
    {
        var adjacentSlashDesk = Path.Combine(environment.ExecutableDirectory, "SlashDeskData");
        var adjacentSlashText = Path.Combine(environment.ExecutableDirectory, "SlashTextData");
        var candidates = environment.IsPortable
            ? new[] { adjacentSlashDesk, environment.LegacyInstalledDataDirectory, adjacentSlashText }
            : new[] { target, adjacentSlashDesk, adjacentSlashText };
        return candidates.FirstOrDefault(candidate =>
            !SamePath(candidate, target) && HasRecognizedData(candidate));
    }

    private static void EnsureDirectories(string target)
    {
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(target, "Backups"));
        Directory.CreateDirectory(Path.Combine(target, "Logs"));
        Directory.CreateDirectory(Path.Combine(target, "Updates"));
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("Updates" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
    }

    private static IReadOnlyList<string> ValidateCopiedData(string directory)
    {
        var warnings = new List<string>();
        foreach (var name in RecognizedFiles.Where(item => item.EndsWith(".json", StringComparison.Ordinal)))
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                using var _ = JsonDocument.Parse(File.ReadAllBytes(path));
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                warnings.Add($"{name}: {exception.GetType().Name}");
            }
        }
        var snippets = Path.Combine(directory, "snippets.md");
        if (File.Exists(snippets))
        {
            try
            {
                _ = File.ReadAllText(snippets);
            }
            catch (IOException exception)
            {
                warnings.Add($"snippets.md: {exception.GetType().Name}");
            }
        }
        return warnings;
    }

    private static string CreateSourceBackup(string source, string destinationRoot, string reason)
    {
        var backups = Path.Combine(destinationRoot, "Backups");
        Directory.CreateDirectory(backups);
        var path = Path.Combine(
            backups,
            $"SlashDesk-migration-{reason}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("Updates" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(file).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            archive.CreateEntryFromFile(file, Path.Combine("data", relative), CompressionLevel.Fastest);
        }
        var manifest = archive.CreateEntry("migration-manifest.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(manifest.Open());
        writer.Write(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            createdAtUtc = DateTimeOffset.UtcNow,
            reason,
            sourceDirectory = source
        }));
        return path;
    }

    private static void WriteMigrationManifest(
        string staging,
        string source,
        string destination,
        IReadOnlyList<string> warnings)
    {
        var path = Path.Combine(staging, "migration-state.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            completedAtUtc = DateTimeOffset.UtcNow,
            sourceDirectory = source,
            destinationDirectory = destination,
            warnings
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool SamePath(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
