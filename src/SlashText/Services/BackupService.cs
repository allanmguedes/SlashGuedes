using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace SlashText.Services;

public sealed class BackupService
{
    private const int RetentionDays = 7;
    private const int CurrentSchemaVersion = 2;
    private const int MaximumRestoreEntries = 10_000;
    private const long MaximumRestoreEntryBytes = 256L * 1024 * 1024;
    private const long MaximumRestoreTotalBytes = 2L * 1024 * 1024 * 1024;
    private const string ManifestName = "backup-manifest.json";
    private static readonly HashSet<string> RestorableFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "snippets.md",
            "settings.json",
            "usage.json",
            "capture-history.json"
        };
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _backupDirectory;
    private readonly IReadOnlyList<string> _sources;
    private readonly bool _includeAssets;
    private readonly string _assetsDirectory;
    private readonly string _dataDirectory;
    private readonly bool _includeCaptureEmotes;
    private readonly string _captureEmotesDirectory;

    internal Action<int, string>? BeforeRestoreApply { get; set; }

    public BackupService(
        string? backupDirectory = null,
        IReadOnlyList<string>? sources = null,
        string? assetsDirectory = null,
        string? dataDirectory = null,
        string? captureEmotesDirectory = null)
    {
        _backupDirectory = backupDirectory ?? AppPaths.BackupsDirectory;
        _includeAssets = sources is null || assetsDirectory is not null;
        _assetsDirectory = assetsDirectory ?? AppPaths.AssetsDirectory;
        _dataDirectory = dataDirectory ?? AppPaths.DataDirectory;
        _includeCaptureEmotes = sources is null || captureEmotesDirectory is not null;
        _captureEmotesDirectory = captureEmotesDirectory ?? Path.Combine(_dataDirectory, "capture-emotes");
        _sources = sources ??
        [
            AppPaths.SnippetsFile,
            AppPaths.SettingsFile,
            AppPaths.UsageFile,
            AppPaths.CaptureHistoryFile
        ];
    }

    public void CreateDailySnapshot()
    {
        CreateSnapshot(
            $"SlashDesk-backup-{DateTime.Now:yyyyMMdd}.zip",
            skipWhenExisting: true);
        Prune();
    }

    public string CreateManualSnapshot()
    {
        var name = $"SlashDesk-backup-manual-{DateTime.Now:yyyyMMdd-HHmmss-fff}.zip";
        var path = CreateSnapshot(name, skipWhenExisting: false);
        Prune();
        return path;
    }

    public IReadOnlyList<FileInfo> ListSnapshots() =>
        Directory.Exists(_backupDirectory)
            ? new DirectoryInfo(_backupDirectory)
                .GetFiles("SlashDesk-backup-*.zip")
                .OrderByDescending(item => item.LastWriteTimeUtc)
                .ToList()
            : [];

    public void RestoreSnapshot(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                "O backup selecionado não foi encontrado.",
                archivePath);
        }

        ValidateSnapshot(archivePath);
        Directory.CreateDirectory(_backupDirectory);
        var transactionId = Guid.NewGuid().ToString("N");
        var stagingDirectory = Path.Combine(
            _backupDirectory,
            $".restore-staging-{transactionId}");
        var rollbackDirectory = Path.Combine(
            _backupDirectory,
            $".restore-rollback-{transactionId}");

        try
        {
            var plan = ExtractToStaging(archivePath, stagingDirectory);
            if (CollectSources().Count > 0)
            {
                CreateSnapshot(
                    $"SlashDesk-backup-before-restore-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.zip",
                    skipWhenExisting: false);
            }

            ApplyStagedRestore(plan, stagingDirectory, rollbackDirectory);
            Prune();
        }
        finally
        {
            DeleteDirectoryIfExists(stagingDirectory);
            DeleteDirectoryIfExists(rollbackDirectory);
        }
    }

    private RestorePlan ExtractToStaging(
        string archivePath,
        string stagingDirectory)
    {
        Directory.CreateDirectory(stagingDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries
            .Where(entry =>
                !string.IsNullOrEmpty(entry.Name) &&
                !NormalizeArchivePath(entry.FullName)
                    .Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0 || entries.Count > MaximumRestoreEntries)
        {
            throw new InvalidDataException(
                "O backup não contém uma quantidade válida de arquivos.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var restoredFiles = new List<string>();
        var includesAssets = ReadIncludesAssets(archive);
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            var relative = NormalizeArchivePath(entry.FullName);
            EnsureAllowedArchivePath(relative);
            if (!paths.Add(relative))
            {
                throw new InvalidDataException(
                    $"O backup contém a entrada duplicada '{relative}'.");
            }
            if (IsLinkEntry(entry) ||
                entry.Length < 0 ||
                entry.Length > MaximumRestoreEntryBytes ||
                totalBytes > MaximumRestoreTotalBytes - entry.Length)
            {
                throw new InvalidDataException(
                    $"A entrada '{relative}' não é segura para restauração.");
            }
            totalBytes += entry.Length;

            var destination = ResolveWithin(stagingDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.WriteThrough);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > entry.Length ||
                    written > MaximumRestoreEntryBytes)
                {
                    throw new InvalidDataException(
                        $"A entrada '{relative}' excedeu o tamanho declarado.");
                }
                output.Write(buffer, 0, read);
            }
            output.Flush(flushToDisk: true);
            if (written != entry.Length)
            {
                throw new InvalidDataException(
                    $"A entrada '{relative}' foi extraída de forma incompleta.");
            }

            if (RestorableFiles.Contains(relative))
            {
                restoredFiles.Add(relative);
            }
        }

        var includesEmotes = ReadIncludesCaptureEmotes(archive);
        if (includesEmotes) CaptureStampCatalog.ValidateDirectory(Path.Combine(stagingDirectory, "capture-emotes"));
        return new RestorePlan(restoredFiles, includesAssets, includesEmotes);
    }

    private void ApplyStagedRestore(
        RestorePlan plan,
        string stagingDirectory,
        string rollbackDirectory)
    {
        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(rollbackDirectory);
        var appliedFiles = new List<AppliedFile>();
        AppliedDirectory? appliedAssets = null;
        AppliedDirectory? appliedEmotes = null;
        var step = 0;
        try
        {
            foreach (var relative in plan.Files)
            {
                BeforeRestoreApply?.Invoke(step++, relative);
                var staged = ResolveWithin(stagingDirectory, relative);
                var target = ResolveWithin(_dataDirectory, relative);
                var rollback = ResolveWithin(rollbackDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.CreateDirectory(Path.GetDirectoryName(rollback)!);
                var existed = File.Exists(target);
                if (existed)
                {
                    File.Replace(
                        staged,
                        target,
                        rollback,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(staged, target);
                }
                appliedFiles.Add(new AppliedFile(target, rollback, existed));
            }

            if (plan.IncludesAssets)
            {
                const string assetsLabel = "assets/";
                BeforeRestoreApply?.Invoke(step++, assetsLabel);
                var stagedAssets = Path.Combine(stagingDirectory, "assets");
                Directory.CreateDirectory(stagedAssets);
                var targetAssets = Path.GetFullPath(_assetsDirectory);
                EnsureDirectoryWithinData(targetAssets);
                var rollbackAssets = Path.Combine(rollbackDirectory, "assets");
                var existed = Directory.Exists(targetAssets);
                if (existed)
                {
                    Directory.Move(targetAssets, rollbackAssets);
                }
                try
                {
                    Directory.Move(stagedAssets, targetAssets);
                    appliedAssets = new AppliedDirectory(
                        targetAssets,
                        rollbackAssets,
                        existed);
                }
                catch
                {
                    if (existed &&
                        !Directory.Exists(targetAssets) &&
                        Directory.Exists(rollbackAssets))
                    {
                        Directory.Move(rollbackAssets, targetAssets);
                    }
                    throw;
                }
            }
            if (plan.IncludesCaptureEmotes)
            {
                BeforeRestoreApply?.Invoke(step, "capture-emotes/");
                var staged = Path.Combine(stagingDirectory, "capture-emotes");
                Directory.CreateDirectory(staged);
                var target = Path.GetFullPath(_captureEmotesDirectory);
                EnsureDirectoryWithinData(target);
                var rollback = Path.Combine(rollbackDirectory, "capture-emotes");
                var existed = Directory.Exists(target);
                if (existed) Directory.Move(target, rollback);
                try
                {
                    Directory.Move(staged, target);
                    appliedEmotes = new AppliedDirectory(target, rollback, existed);
                }
                catch
                {
                    if (existed && !Directory.Exists(target) && Directory.Exists(rollback)) Directory.Move(rollback, target);
                    throw;
                }
            }
        }
        catch
        {
            if (appliedEmotes is not null)
            {
                DeleteDirectoryIfExists(appliedEmotes.Target);
                if (appliedEmotes.Existed && Directory.Exists(appliedEmotes.Rollback))
                    Directory.Move(appliedEmotes.Rollback, appliedEmotes.Target);
            }
            if (appliedAssets is not null)
            {
                DeleteDirectoryIfExists(appliedAssets.Target);
                if (appliedAssets.Existed &&
                    Directory.Exists(appliedAssets.Rollback))
                {
                    Directory.Move(
                        appliedAssets.Rollback,
                        appliedAssets.Target);
                }
            }

            foreach (var applied in appliedFiles.AsEnumerable().Reverse())
            {
                if (File.Exists(applied.Target))
                {
                    File.Delete(applied.Target);
                }
                if (applied.Existed && File.Exists(applied.Rollback))
                {
                    File.Move(
                        applied.Rollback,
                        applied.Target,
                        overwrite: true);
                }
            }
            throw;
        }
    }

    private static bool ReadIncludesAssets(ZipArchive archive)
    {
        var manifest = archive.Entries.Single(entry =>
            NormalizeArchivePath(entry.FullName)
                .Equals(ManifestName, StringComparison.OrdinalIgnoreCase));
        using var document = JsonDocument.Parse(manifest.Open());
        if (document.RootElement.TryGetProperty(
                "includesAssets",
                out var includesElement) &&
            includesElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return includesElement.GetBoolean();
        }

        return archive.Entries.Any(entry =>
            NormalizeArchivePath(entry.FullName)
                .StartsWith("assets/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ReadIncludesCaptureEmotes(ZipArchive archive)
    {
        var manifest = archive.Entries.Single(entry => NormalizeArchivePath(entry.FullName).Equals(ManifestName, StringComparison.OrdinalIgnoreCase));
        using var document = JsonDocument.Parse(manifest.Open());
        if (document.RootElement.TryGetProperty("includesCaptureEmotes", out var flag))
        {
            if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Indicador da coleção de emojis inválido.");
            var includes = flag.GetBoolean();
            if (!includes && archive.Entries.Any(entry => NormalizeArchivePath(entry.FullName).StartsWith("capture-emotes/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A coleção de emojis não corresponde ao manifesto.");
            return includes;
        }
        // Backups written before this optional collection flag leave personal emojis untouched.
        return archive.Entries.Any(entry => NormalizeArchivePath(entry.FullName).StartsWith("capture-emotes/", StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureDirectoryWithinData(string directory)
    {
        var dataRoot = Path.GetFullPath(_dataDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var normalized = directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(
                dataRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A pasta de assets está fora de SlashDeskData.");
        }
    }

    private static string ResolveWithin(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            NormalizeArchivePath(relative)
                .Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"O backup contém o caminho inseguro '{relative}'.");
        }
        return destination;
    }

    private static bool IsLinkEntry(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        var windowsAttributes =
            (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixType == 0xA000 ||
               windowsAttributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private string CreateSnapshot(string fileName, bool skipWhenExisting)
    {
        var sources = CollectSources();
        if (sources.Count == 0)
        {
            throw new InvalidOperationException("Ainda não há dados do SlashDesk para incluir no backup.");
        }

        var duplicate = sources
            .GroupBy(item => item.ArchivePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Mais de um arquivo produziria a entrada '{duplicate.Key}' no backup.");
        }

        Directory.CreateDirectory(_backupDirectory);
        var destination = Path.Combine(_backupDirectory, fileName);
        if (skipWhenExisting && File.Exists(destination))
        {
            ValidateSnapshot(destination);
            return destination;
        }

        var temporary = Path.Combine(
            _backupDirectory,
            $".{fileName}-{Guid.NewGuid():N}.tmp");
        try
        {
            CreateArchive(temporary, sources, _includeAssets, _includeCaptureEmotes);
            ValidateSnapshot(temporary);
            File.Move(temporary, destination, overwrite: false);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private List<BackupSource> CollectSources()
    {
        var sources = _sources
            .Where(File.Exists)
            .Select(path => new BackupSource(path, Path.GetFileName(path)))
            .ToList();

        if (_includeCaptureEmotes)
        {
            CaptureStampCatalog.ValidateDirectory(_captureEmotesDirectory);
            if (Directory.Exists(_captureEmotesDirectory))
                foreach (var file in Directory.EnumerateFiles(_captureEmotesDirectory, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
                {
                    var name = Path.GetFileName(file);
                    if (name == "catalog.json" || (Path.GetExtension(name) == ".png" && Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _)))
                        sources.Add(new BackupSource(file, "capture-emotes/" + name));
                }
        }
        if (!_includeAssets || !Directory.Exists(_assetsDirectory))
        {
            return sources;
        }

        var assetsRoot = Path.GetFullPath(_assetsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPrefix = assetsRoot + Path.DirectorySeparatorChar;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var asset in Directory.EnumerateFiles(
                     assetsRoot,
                     "*",
                     options))
        {
            var fullAsset = Path.GetFullPath(asset);
            if (!fullAsset.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Um asset está fora da pasta permitida.");
            }

            var relative = Path.GetRelativePath(assetsRoot, fullAsset)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var archivePath = $"assets/{relative}";
            EnsureAllowedArchivePath(archivePath);
            sources.Add(new BackupSource(fullAsset, archivePath));
        }

        return sources;
    }

    private static void CreateArchive(
        string path,
        IReadOnlyList<BackupSource> sources,
        bool includesAssets,
        bool includesCaptureEmotes)
    {
        var files = new List<BackupManifestFile>();
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var source in sources.OrderBy(
                         item => item.ArchivePath,
                         StringComparer.OrdinalIgnoreCase))
            {
                EnsureAllowedArchivePath(source.ArchivePath);
                var entry = archive.CreateEntry(
                    source.ArchivePath,
                    CompressionLevel.Optimal);
                using var input = new FileStream(
                    source.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.SequentialScan);
                using var output = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long size = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                files.Add(new BackupManifestFile(
                    source.ArchivePath,
                    size,
                    Convert.ToHexString(hash.GetHashAndReset())
                        .ToLowerInvariant()));
            }

            var manifest = archive.CreateEntry(
                ManifestName,
                CompressionLevel.Optimal);
            using var writer = new StreamWriter(manifest.Open());
            writer.Write(JsonSerializer.Serialize(
                new BackupManifest(
                    CurrentSchemaVersion,
                    DateTimeOffset.UtcNow,
                    Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
                        ?? "unknown",
                    AppPaths.Mode.ToString(),
                    includesAssets,
                    includesCaptureEmotes,
                    files),
                ManifestOptions));
        }
    }

    internal static void ValidateSnapshot(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var manifestEntries = archive.Entries
            .Where(entry => NormalizeArchivePath(entry.FullName)
                .Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (manifestEntries.Count != 1)
        {
            throw new InvalidDataException(
                "O backup deve conter exatamente um manifesto.");
        }

        using var document = JsonDocument.Parse(manifestEntries[0].Open());
        if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaElement) ||
            !schemaElement.TryGetInt32(out var schemaVersion))
        {
            throw new InvalidDataException("O manifesto não informa uma versão válida.");
        }

        switch (schemaVersion)
        {
            case 1:
                ValidateLegacySnapshot(archive);
                break;
            case CurrentSchemaVersion:
                ValidateCurrentSnapshot(archive, document.RootElement);
                break;
            default:
                throw new InvalidDataException("Versão de backup não suportada.");
        }
    }

    private static void ValidateLegacySnapshot(ZipArchive archive)
    {
        if (!archive.Entries.Any(entry =>
                RestorableFiles.Contains(entry.Name)))
        {
            throw new InvalidDataException(
                "O backup legado não contém dados restauráveis.");
        }
    }

    private static void ValidateCurrentSnapshot(
        ZipArchive archive,
        JsonElement manifest)
    {
        if (!manifest.TryGetProperty("files", out var filesElement) ||
            filesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("O manifesto não contém a lista de arquivos.");
        }

        var archiveFiles = new Dictionary<string, ZipArchiveEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry =>
                     !string.IsNullOrEmpty(entry.Name) &&
                     !NormalizeArchivePath(entry.FullName)
                         .Equals(ManifestName, StringComparison.OrdinalIgnoreCase)))
        {
            var normalized = NormalizeArchivePath(entry.FullName);
            EnsureAllowedArchivePath(normalized);
            if (!archiveFiles.TryAdd(normalized, entry))
            {
                throw new InvalidDataException(
                    $"O backup contém a entrada duplicada '{normalized}'.");
            }
        }

        var manifestFiles = new Dictionary<string, BackupManifestFile>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var element in filesElement.EnumerateArray())
        {
            var file = element.Deserialize<BackupManifestFile>(ManifestOptions)
                ?? throw new InvalidDataException(
                    "O manifesto contém uma entrada inválida.");
            EnsureAllowedArchivePath(file.Path);
            if (file.Size < 0 ||
                file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit) ||
                !manifestFiles.TryAdd(file.Path, file))
            {
                throw new InvalidDataException(
                    $"O manifesto contém dados inválidos para '{file.Path}'.");
            }
        }

        if (archiveFiles.Count == 0 ||
            archiveFiles.Count != manifestFiles.Count ||
            archiveFiles.Keys.Any(path => !manifestFiles.ContainsKey(path)))
        {
            throw new InvalidDataException(
                "O conteúdo do ZIP não corresponde ao manifesto.");
        }

        foreach (var item in archiveFiles)
        {
            var expected = manifestFiles[item.Key];
            if (item.Value.Length != expected.Size)
            {
                throw new InvalidDataException(
                    $"O tamanho de '{item.Key}' não corresponde ao manifesto.");
            }

            using var stream = item.Value.Open();
            var actualHash = Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
            if (!actualHash.Equals(
                    expected.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"O hash de '{item.Key}' não corresponde ao manifesto.");
            }
        }
    }

    private static void EnsureAllowedArchivePath(string path)
    {
        var normalized = NormalizeArchivePath(path);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith('/') ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(part =>
                string.IsNullOrWhiteSpace(part) || part is "." or ".."))
        {
            throw new InvalidDataException(
                $"O backup contém o caminho inseguro '{path}'.");
        }

        if (RestorableFiles.Contains(normalized))
        {
            return;
        }
        if (normalized.StartsWith("capture-emotes/", StringComparison.OrdinalIgnoreCase))
        {
            var name = normalized["capture-emotes/".Length..];
            if (name == "catalog.json" || (Path.GetExtension(name) == ".png" && Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _) && !name.Contains('/'))) return;
        }

        if (!normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Length <= "assets/".Length)
        {
            throw new InvalidDataException(
                $"O backup contém o arquivo não permitido '{path}'.");
        }
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/');

    private void Prune()
    {
        foreach (var backup in new DirectoryInfo(_backupDirectory)
                     .GetFiles("*-backup-*.zip")
                     .OrderByDescending(item => item.LastWriteTimeUtc)
                     .Skip(RetentionDays))
        {
            backup.Delete();
        }

        foreach (var legacy in Directory.EnumerateFiles(
                     _backupDirectory,
                     "snippets-*.md"))
        {
            File.Delete(legacy);
        }
    }

    private sealed record BackupSource(
        string SourcePath,
        string ArchivePath);

    private sealed record BackupManifest(
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        string SlashDeskVersion,
        string DistributionMode,
        bool IncludesAssets,
        bool IncludesCaptureEmotes,
        IReadOnlyList<BackupManifestFile> Files);

    private sealed record RestorePlan(
        IReadOnlyList<string> Files,
        bool IncludesAssets,
        bool IncludesCaptureEmotes);

    private sealed record AppliedFile(
        string Target,
        string Rollback,
        bool Existed);

    private sealed record AppliedDirectory(
        string Target,
        string Rollback,
        bool Existed);

    private sealed record BackupManifestFile(
        string Path,
        long Size,
        string Sha256);
}
