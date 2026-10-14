using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace SlashText.Services;

public sealed class BackupService
{
    private const int RetainedSnapshots = 7;
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
    internal Action<string, int>? RestoreCheckpoint { get; set; }

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

    public string CreateManualSnapshot() => CreateManualSnapshot(default);

    public string CreateManualSnapshot(CancellationToken cancellationToken)
    {
        var name = $"SlashDesk-backup-manual-{DateTime.Now:yyyyMMdd-HHmmss-fff}.zip";
        var path = CreateSnapshot(name, skipWhenExisting: false, cancellationToken);
        Prune();
        return path;
    }

    public IReadOnlyList<FileInfo> ListSnapshots() =>
        Directory.Exists(_backupDirectory)
            ? new DirectoryInfo(_backupDirectory)
                .GetFiles("*.zip")
                .Where(item => IsManagedSnapshotName(item.Name) && (item.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(item => item.LastWriteTimeUtc)
                .ToList()
            : [];

    public void RestoreSnapshot(string archivePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                UiText.Get("O backup selecionado não foi encontrado."),
                archivePath);
        }

        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        BackupInputBudget.CheckDirectory(file, cancellationToken);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        ValidateSnapshot(archive, cancellationToken);
        Directory.CreateDirectory(_backupDirectory);
        RestoreRecovery.ThrowIfProtected(_dataDirectory);
        using var transaction = RestoreRecovery.Begin(_dataDirectory, _backupDirectory, _assetsDirectory, _captureEmotesDirectory);
        transaction.Checkpoint = RestoreCheckpoint;
        try
        {
            var plan = ExtractToStaging(archive, transaction.Staging, cancellationToken);
            if (CollectSources().Count > 0)
                CreateSnapshot($"SlashDesk-backup-before-restore-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.zip", false, cancellationToken);
            transaction.Prepare(plan.Files, plan.IncludesAssets, plan.IncludesCaptureEmotes, cancellationToken);
            transaction.Apply(BeforeRestoreApply, cancellationToken);
        }
        catch { transaction.Abort(); throw; }
        archive.Dispose(); file.Dispose();
        // A pruning failure must not report the durable commit as an aborted restore.
        try { Prune(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private RestorePlan ExtractToStaging(
        ZipArchive archive,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = archive.Entries
            .Where(entry =>
                !string.IsNullOrEmpty(entry.Name) &&
                !NormalizeArchivePath(entry.FullName)
                    .Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0 || entries.Count > MaximumRestoreEntries)
        {
            throw new InvalidDataException(
                UiText.Get("O backup não contém uma quantidade válida de arquivos."));
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var restoredFiles = new List<string>();
        var includesAssets = ReadIncludesAssets(archive, cancellationToken);
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeArchivePath(entry.FullName);
            EnsureAllowedArchivePath(relative);
            if (!paths.Add(relative))
            {
                throw new InvalidDataException(
                    UiText.Format($"O backup contém a entrada duplicada '{relative}'."));
            }
            if (IsLinkEntry(entry) ||
                entry.Length < 0 ||
                entry.Length > MaximumRestoreEntryBytes ||
                totalBytes > MaximumRestoreTotalBytes - entry.Length)
            {
                throw new InvalidDataException(
                    UiText.Format($"A entrada '{relative}' não é segura para restauração."));
            }
            totalBytes += entry.Length;

            var destination = ResolveWithin(stagingDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = new BoundedInputStream(entry.Open(), entry.Length, cancellationToken);
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
                        UiText.Format($"A entrada '{relative}' excedeu o tamanho declarado."));
                }
                output.Write(buffer, 0, read);
            }
            output.Flush(flushToDisk: true);
            if (written != entry.Length)
            {
                throw new InvalidDataException(
                    UiText.Format($"A entrada '{relative}' foi extraída de forma incompleta."));
            }

            if (RestorableFiles.Contains(relative))
            {
                restoredFiles.Add(relative);
            }
        }

        var includesEmotes = ReadIncludesCaptureEmotes(archive, cancellationToken);
        if (includesEmotes) CaptureStampCatalog.ValidateDirectory(Path.Combine(stagingDirectory, "capture-emotes"));
        if (restoredFiles.Contains("capture-history.json", StringComparer.OrdinalIgnoreCase))
        {
            var historyPath = ResolveWithin(stagingDirectory,
                restoredFiles.Single(p => p.Equals("capture-history.json", StringComparison.OrdinalIgnoreCase)));
            try
            {
                using var historyFile = File.OpenRead(historyPath);
                using var historyInput = new BoundedInputStream(historyFile, BackupInputBudget.MetadataBytes, cancellationToken);
                using var history = JsonDocument.Parse(historyInput);
                if (history.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException(UiText.Get("O histórico do backup não é válido."));
                var transformed = historyPath + ".untrusted";
                using (var output = File.Create(transformed))
                using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartArray();
                    foreach (var item in history.RootElement.EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (item.ValueKind != JsonValueKind.Object) { item.WriteTo(writer); continue; }
                        writer.WriteStartObject();
                        foreach (var property in item.EnumerateObject())
                            if (!property.Name.Equals("requiresFileApproval", StringComparison.OrdinalIgnoreCase)) property.WriteTo(writer);
                        writer.WriteBoolean("requiresFileApproval", true); writer.WriteEndObject();
                    }
                    writer.WriteEndArray(); writer.Flush(); output.Flush(flushToDisk: true);
                }
                // Archive bytes were integrity-checked. Only this local trust decision is rewritten.
                historyInput.Dispose(); historyFile.Dispose();
                cancellationToken.ThrowIfCancellationRequested(); File.Move(transformed, historyPath, overwrite: true);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
            {
                throw BackupContentValidator.IncompatibleFile("capture-history.json");
            }
        }
        // Validate the bytes actually applied, including the established local history trust transform.
        BackupContentValidator.Validate(stagingDirectory, restoredFiles, cancellationToken);
        return new RestorePlan(restoredFiles, includesAssets, includesEmotes);
    }

    private static bool ReadIncludesAssets(ZipArchive archive, CancellationToken cancellationToken)
    {
        var manifest = archive.Entries.Single(entry =>
            NormalizeArchivePath(entry.FullName)
                .Equals(ManifestName, StringComparison.OrdinalIgnoreCase));
        using var input = new BoundedInputStream(manifest.Open(), Math.Min(manifest.Length, BackupInputBudget.ManifestBytes), cancellationToken);
        using var document = JsonDocument.Parse(input);
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

    private static bool ReadIncludesCaptureEmotes(ZipArchive archive, CancellationToken cancellationToken)
    {
        var manifest = archive.Entries.Single(entry => NormalizeArchivePath(entry.FullName).Equals(ManifestName, StringComparison.OrdinalIgnoreCase));
        using var input = new BoundedInputStream(manifest.Open(), Math.Min(manifest.Length, BackupInputBudget.ManifestBytes), cancellationToken);
        using var document = JsonDocument.Parse(input);
        if (document.RootElement.TryGetProperty("includesCaptureEmotes", out var flag))
        {
            if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException(UiText.Get("Indicador da coleção de emojis inválido."));
            var includes = flag.GetBoolean();
            if (!includes && archive.Entries.Any(entry => NormalizeArchivePath(entry.FullName).StartsWith("capture-emotes/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(UiText.Get("A coleção de emojis não corresponde ao manifesto."));
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
                UiText.Get("A pasta de assets está fora de SlashDeskData."));
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
                UiText.Format($"O backup contém o caminho inseguro '{relative}'."));
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

    private string CreateSnapshot(string fileName, bool skipWhenExisting, CancellationToken cancellationToken = default)
    {
        RestoreRecovery.ThrowIfProtected(_dataDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var sources = CollectSources();
        if (sources.Count == 0)
        {
            throw new InvalidOperationException(UiText.Get("Ainda não há dados do SlashDesk para incluir no backup."));
        }

        var duplicate = sources
            .GroupBy(item => item.ArchivePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                UiText.Format($"Mais de um arquivo produziria a entrada '{duplicate.Key}' no backup."));
        }

        if (sources.Count > BackupInputBudget.Entries) throw ExternalInputBudget.Exceeded();
        long sourceBytes = 0;
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = new FileInfo(source.SourcePath).Length;
            var limit = source.ArchivePath.Contains('/') ? BackupInputBudget.EntryBytes : BackupInputBudget.MetadataBytes;
            if (size > limit || sourceBytes > BackupInputBudget.TotalBytes - size) throw ExternalInputBudget.Exceeded();
            sourceBytes += size;
        }
        Directory.CreateDirectory(_backupDirectory);
        var destination = Path.Combine(_backupDirectory, fileName);
        if (skipWhenExisting && File.Exists(destination))
        {
            ValidateSnapshot(destination, cancellationToken);
            return destination;
        }

        var temporary = Path.Combine(
            _backupDirectory,
            $".{fileName}-{Guid.NewGuid():N}.tmp");
        try
        {
            CreateArchive(temporary, sources, _includeAssets, _includeCaptureEmotes, cancellationToken);
            ValidateSnapshot(temporary, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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
                throw new InvalidDataException(UiText.Get("Um asset está fora da pasta permitida."));
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
        bool includesCaptureEmotes,
        CancellationToken cancellationToken)
    {
        var files = new List<BackupManifestFile>();
        long actualSourceBytes = 0;
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
                var sourceLimit = source.ArchivePath.Contains('/') ? BackupInputBudget.EntryBytes : BackupInputBudget.MetadataBytes;
                if (input.Length > sourceLimit || actualSourceBytes > BackupInputBudget.TotalBytes - input.Length)
                    throw ExternalInputBudget.Exceeded();
                actualSourceBytes += input.Length;
                using var output = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long size = 0;
                using var bounded = new BoundedInputStream(input, input.Length, cancellationToken, leaveOpen: true);
                int read;
                while ((read = bounded.Read(buffer, 0, buffer.Length)) > 0)
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

    internal static void ValidateSnapshot(string path, CancellationToken cancellationToken = default)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BackupInputBudget.CheckDirectory(file, cancellationToken);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        ValidateSnapshot(archive, cancellationToken);
    }

    private static void ValidateSnapshot(ZipArchive archive, CancellationToken cancellationToken)
    {
        BackupInputBudget.CheckEntries(archive, cancellationToken);
        var manifestEntries = archive.Entries
            .Where(entry => NormalizeArchivePath(entry.FullName)
                .Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (manifestEntries.Count != 1)
        {
            throw new InvalidDataException(
                UiText.Get("O backup deve conter exatamente um manifesto."));
        }

        using var manifestInput = new BoundedInputStream(manifestEntries[0].Open(), Math.Min(manifestEntries[0].Length, BackupInputBudget.ManifestBytes), cancellationToken);
        using var document = JsonDocument.Parse(manifestInput);
        if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaElement) ||
            !schemaElement.TryGetInt32(out var schemaVersion))
        {
            throw new InvalidDataException(UiText.Get("O manifesto não informa uma versão válida."));
        }

        switch (schemaVersion)
        {
            case 1:
                ValidateLegacySnapshot(archive);
                break;
            case CurrentSchemaVersion:
                ValidateCurrentSnapshot(archive, document.RootElement, cancellationToken);
                break;
            default:
                throw new InvalidDataException(UiText.Get("Versão de backup não suportada."));
        }
    }

    private static void ValidateLegacySnapshot(ZipArchive archive)
    {
        if (!archive.Entries.Any(entry =>
                RestorableFiles.Contains(entry.Name)))
        {
            throw new InvalidDataException(
                UiText.Get("O backup legado não contém dados restauráveis."));
        }
    }

    private static void ValidateCurrentSnapshot(
        ZipArchive archive,
        JsonElement manifest,
        CancellationToken cancellationToken)
    {
        if (!manifest.TryGetProperty("files", out var filesElement) ||
            filesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(UiText.Get("O manifesto não contém a lista de arquivos."));
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
                    UiText.Format($"O backup contém a entrada duplicada '{normalized}'."));
            }
        }

        var manifestFiles = new Dictionary<string, BackupManifestFile>(
            StringComparer.OrdinalIgnoreCase);
        if (filesElement.GetArrayLength() > MaximumRestoreEntries) throw ExternalInputBudget.Exceeded();
        foreach (var element in filesElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = element.Deserialize<BackupManifestFile>(ManifestOptions)
                ?? throw new InvalidDataException(
                    UiText.Get("O manifesto contém uma entrada inválida."));
            if (string.IsNullOrEmpty(file.Path)) throw ExternalInputBudget.Exceeded();
            EnsureAllowedArchivePath(file.Path);
            if (file.Size < 0 ||
                string.IsNullOrEmpty(file.Path) || file.Sha256 is null ||
                file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit) ||
                !manifestFiles.TryAdd(file.Path, file))
            {
                throw new InvalidDataException(
                    UiText.Format($"O manifesto contém dados inválidos para '{file.Path}'."));
            }
        }

        if (archiveFiles.Count == 0 ||
            archiveFiles.Count != manifestFiles.Count ||
            archiveFiles.Keys.Any(path => !manifestFiles.ContainsKey(path)))
        {
            throw new InvalidDataException(
                UiText.Get("O conteúdo do ZIP não corresponde ao manifesto."));
        }

        foreach (var item in archiveFiles)
        {
            var expected = manifestFiles[item.Key];
            if (item.Value.Length != expected.Size)
            {
                throw new InvalidDataException(
                    UiText.Format($"O tamanho de '{item.Key}' não corresponde ao manifesto."));
            }

            using var stream = new BoundedInputStream(item.Value.Open(), item.Value.Length, cancellationToken);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
            if (stream.BytesRead != expected.Size) throw ExternalInputBudget.Exceeded();
            if (!actualHash.Equals(
                    expected.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    UiText.Format($"O hash de '{item.Key}' não corresponde ao manifesto."));
            }
        }
    }

    private static void EnsureAllowedArchivePath(string path)
    {
        if (path.Length > 32_767) throw ExternalInputBudget.Exceeded();
        var normalized = NormalizeArchivePath(path);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith('/') ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(part =>
                string.IsNullOrWhiteSpace(part) || part is "." or ".."))
        {
            throw new InvalidDataException(
                UiText.Format($"O backup contém o caminho inseguro '{path}'."));
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
                UiText.Format($"O backup contém o arquivo não permitido '{path}'."));
        }
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/');

    // Only names emitted by the current writer or the historical SlashText daily writer
    // belong to automatic retention. Unknown files remain available for explicit recovery.
    internal static bool IsManagedSnapshotName(string name)
    {
        if (!Path.GetExtension(name).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = Path.GetFileNameWithoutExtension(name);
        var historical = stem.StartsWith("SlashText-backup-", StringComparison.OrdinalIgnoreCase);
        var prefix = historical ? "SlashText-backup-" : "SlashDesk-backup-";
        if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var suffix = stem[prefix.Length..];
        if (DateTime.TryParseExact(suffix, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return true;
        if (historical) return false; // The historical writer generated daily snapshots only.
        if (suffix.StartsWith("manual-", StringComparison.OrdinalIgnoreCase))
            return DateTime.TryParseExact(suffix[7..], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        if (!suffix.StartsWith("before-restore-", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = suffix[15..].Split('-');
        return parts.Length == 4 && Guid.TryParseExact(parts[3], "N", out _) &&
            DateTime.TryParseExact(string.Join("-", parts.Take(3)), "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private void Prune()
    {
        foreach (var backup in ListSnapshots().Skip(RetainedSnapshots)) backup.Delete();
        // Historical snippets-*.md may be the sole surviving version. Current writers no
        // longer produce them: keep the finite legacy set until an explicit user deletion.
        // Do not read, normalize, deduplicate, convert, or include references in new ZIPs.
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

    private sealed record BackupManifestFile(
        string Path,
        long Size,
        string Sha256);
}
