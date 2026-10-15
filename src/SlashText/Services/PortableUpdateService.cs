using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlashText.Models;

namespace SlashText.Services;

internal sealed class PortableUpdateService
{
    internal static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _client;
    private readonly string _currentExecutable;
    private readonly int _currentProcessId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PortableUpdateService() : this(CreateClient(), Environment.ProcessPath, Environment.ProcessId)
    {
    }

    internal PortableUpdateService(
        HttpClient client,
        string? currentExecutable = null,
        int currentProcessId = 0)
    {
        _client = client;
        _currentExecutable = Path.GetFullPath(currentExecutable
            ?? throw new InvalidOperationException(UiText.Get("Não foi possível localizar SlashDesk.exe.")));
        _currentProcessId = currentProcessId;
    }

    internal async Task<PreparedPortableUpdate> PrepareAsync(
        ReleaseInfo release,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!AppPaths.IsPortable)
        {
            throw new InvalidOperationException(
                UiText.Get("A atualização automática desta compilação está disponível somente no modo portátil."));
        }
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException(UiText.Get("Já existe uma atualização em andamento."));
        }

        string? operationDirectory = null;
        try
        {
            ValidateRelease(release);
            var operationId = Guid.NewGuid().ToString("N");
            operationDirectory = Path.Combine(AppPaths.UpdatesDirectory, operationId);
            Directory.CreateDirectory(operationDirectory);
            var zipPath = Path.Combine(operationDirectory, release.PortableAsset.Name);
            var checksumPath = Path.Combine(operationDirectory, release.ChecksumAsset.Name);
            await DownloadAsync(release.ChecksumAsset, checksumPath, null, cancellationToken);
            await DownloadAsync(release.PortableAsset, zipPath, progress, cancellationToken);

            progress?.Report(new UpdateProgress("Validando SHA-256", 0, null));
            var expectedHash = ParseChecksum(await File.ReadAllTextAsync(checksumPath, cancellationToken));
            var actualHash = await ComputeSha256Async(zipPath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
            {
                throw new InvalidDataException(UiText.Get("O SHA-256 do pacote baixado não corresponde à Release."));
            }

            var product = AppPaths.IsSlashGuedes ? "SlashGuedes" : "SlashDesk";
            var stagedExecutable = Path.Combine(operationDirectory, product + ".new.exe");
            ExtractSingleExecutable(zipPath, stagedExecutable);
            ValidateX64Executable(stagedExecutable);
            ValidateProductVersion(stagedExecutable, release.Version);

            var currentExecutable = _currentExecutable;
            var helperExecutable = Path.Combine(operationDirectory, product + ".Updater.exe");
            File.Copy(currentExecutable, helperExecutable, overwrite: false);
            var manifest = new PortableUpdateManifest
            {
                OperationId = operationId,
                MainProcessId = _currentProcessId,
                ExpectedVersion = release.Version,
                DataDirectory = AppPaths.DataDirectory,
                TargetExecutable = currentExecutable,
                StagedExecutable = stagedExecutable,
                BackupExecutable = Path.Combine(operationDirectory, product + ".previous.exe"),
                FailedExecutable = Path.Combine(operationDirectory, product + ".failed.exe"),
                HelperExecutable = helperExecutable,
                ConfirmationFile = Path.Combine(operationDirectory, "update-confirmed.json"),
                CreatedUtc = DateTimeOffset.UtcNow
            };
            var manifestPath = Path.Combine(operationDirectory, "update-manifest.json");
            ValidateManifest(manifest, manifestPath);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false),
                cancellationToken);
            AppDiagnosticLog.Write(
                "update.prepared",
                ("operationId", operationId),
                ("version", release.Version),
                ("sha256", actualHash));
            return new PreparedPortableUpdate(manifest, manifestPath);
        }
        catch
        {
            if (operationDirectory is not null)
            {
                TryDeleteDirectory(operationDirectory);
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static void LaunchHelper(PreparedPortableUpdate prepared)
    {
        ValidateManifest(prepared.Manifest, prepared.ManifestPath);
        AppDiagnosticLog.Write(
            "update.helper.starting",
            ("operationId", prepared.Manifest.OperationId),
            ("version", prepared.Manifest.ExpectedVersion));
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = prepared.Manifest.HelperExecutable,
            UseShellExecute = false,
            ArgumentList =
            {
                "--apply-portable-update",
                prepared.ManifestPath
            }
        });
        if (process is null)
        {
            throw new InvalidOperationException(UiText.Get("Não foi possível iniciar o processo auxiliar de atualização."));
        }
    }

    internal static bool TryRunHelper(string[] args, out int exitCode)
    {
        exitCode = 0;
        var index = Array.FindIndex(args, argument =>
            argument.Equals("--apply-portable-update", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }
        if (index + 1 >= args.Length)
        {
            exitCode = 10;
            return true;
        }

        PortableUpdateManifest? manifest = null;
        Process? updated = null;
        var replaced = false;
        try
        {
            var manifestPath = Path.GetFullPath(args[index + 1]);
            manifest = ReadManifest(manifestPath);
            ValidateManifest(manifest, manifestPath);
            WriteJournal(manifest, "update.helper.started");
            WaitForProcessExit(manifest.MainProcessId, ProcessExitTimeout);
            PortableUpdateFileTransaction.Apply(
                manifest.TargetExecutable,
                manifest.StagedExecutable,
                manifest.BackupExecutable);
            replaced = true;
            WriteJournal(manifest, "update.executable.replaced");

            updated = Process.Start(new ProcessStartInfo
            {
                FileName = manifest.TargetExecutable,
                UseShellExecute = false,
                ArgumentList =
                {
                    "--confirm-portable-update",
                    manifestPath,
                    Environment.ProcessId.ToString()
                }
            }) ?? throw new InvalidOperationException(UiText.Get("A nova versão não pôde ser iniciada."));

            if (!WaitForConfirmation(manifest.ConfirmationFile, updated, ConfirmationTimeout))
            {
                TryStop(updated);
                PortableUpdateFileTransaction.Rollback(
                    manifest.TargetExecutable,
                    manifest.BackupExecutable,
                    manifest.FailedExecutable);
                WriteJournal(manifest, "update.rollback.completed");
                Process.Start(new ProcessStartInfo(manifest.TargetExecutable)
                {
                    UseShellExecute = true
                });
                exitCode = 12;
                return true;
            }

            WriteJournal(manifest, "update.confirmed");
            exitCode = 0;
        }
        catch (Exception exception)
        {
            if (manifest is not null && replaced)
            {
                TryStop(updated);
                try
                {
                    PortableUpdateFileTransaction.Rollback(
                        manifest.TargetExecutable,
                        manifest.BackupExecutable,
                        manifest.FailedExecutable);
                    WriteJournal(manifest, "update.rollback.completed", exception);
                    Process.Start(new ProcessStartInfo(manifest.TargetExecutable)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception rollbackException)
                {
                    WriteJournal(manifest, "update.rollback.failed", rollbackException);
                }
            }
            else if (manifest is not null && File.Exists(manifest.TargetExecutable))
            {
                try
                {
                    WriteJournal(manifest, "update.replacement.failed", exception);
                    Process.Start(new ProcessStartInfo(manifest.TargetExecutable)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception restartException)
                {
                    WriteJournal(manifest, "update.previous-version.restart.failed", restartException);
                }
            }
            TryWriteHelperFailure(args, exception);
            exitCode = 11;
        }
        finally
        {
            updated?.Dispose();
        }
        return true;
    }

    internal static void ConfirmAndScheduleCleanup(string[] args)
    {
        var index = Array.FindIndex(args, argument =>
            argument.Equals("--confirm-portable-update", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
        {
            return;
        }
        var manifestPath = Path.GetFullPath(args[index + 1]);
        var manifest = ReadManifest(manifestPath);
        ValidateManifest(manifest, manifestPath);
        var currentVersion = ProductVersion();
        if (!string.Equals(currentVersion, manifest.ExpectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                UiText.Format($"A atualização iniciou a versão {currentVersion}, mas esperava {manifest.ExpectedVersion}."));
        }
        File.WriteAllText(
            manifest.ConfirmationFile,
            JsonSerializer.Serialize(new
            {
                manifest.OperationId,
                version = currentVersion,
                confirmedUtc = DateTimeOffset.UtcNow
            }),
            new UTF8Encoding(false));
        var helperProcessId = index + 2 < args.Length && int.TryParse(args[index + 2], out var parsed)
            ? parsed : 0;
        _ = Task.Run(() => CleanupAfterHelperExit(manifest, helperProcessId));
    }

    private async Task DownloadAsync(
        ReleaseAssetInfo asset,
        string destination,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateAssetUrl(asset.DownloadUrl);
        var partial = destination + ".partial";
        try
        {
            using var response = await _client.GetAsync(
                asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : null);
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(new UpdateProgress(UiText.Get("Baixando atualização"), received, total));
                }
                await output.FlushAsync(cancellationToken);
            }
            if (asset.Size > 0 && received != asset.Size)
            {
                throw new EndOfStreamException(
                    $"Download incompleto: esperado {asset.Size} bytes, recebido {received}.");
            }
            File.Move(partial, destination);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private static void ExtractSingleExecutable(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        var product = AppPaths.IsSlashGuedes ? "SlashGuedes" : "SlashDesk";
        if (files.Count != 1 ||
            !files[0].FullName.Replace('\\', '/').Equals(product + ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"O ZIP deve conter somente {product}.exe na raiz.");
        }
        files[0].ExtractToFile(destination, overwrite: false);
    }

    internal static void ValidateX64Executable(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 256 || reader.ReadUInt16() != 0x5A4D)
        {
            throw new InvalidDataException(UiText.Get("O arquivo baixado não é um executável PE válido."));
        }
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset < 0 || peOffset + 6 > stream.Length)
        {
            throw new InvalidDataException(UiText.Get("O cabeçalho PE do arquivo está inválido."));
        }
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664)
        {
            throw new InvalidDataException(UiText.Get("O pacote não contém um executável Windows x64."));
        }
    }

    private static void ValidateProductVersion(string path, string expectedVersion)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        if (AppPaths.IsSlashGuedes)
        {
            // Numerical file versions cannot distinguish preview.10 from preview.11.
            if (!SemanticVersion.TryParse(info.ProductVersion, out var actual) ||
                !actual.ToString().Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"O executável baixado não corresponde à Preview {expectedVersion}.");
        }
        else if (string.IsNullOrWhiteSpace(info.FileVersion) ||
                 !info.FileVersion.StartsWith(expectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                UiText.Format($"A versão do executável ({info.FileVersion ?? "desconhecida"}) não corresponde a {expectedVersion}."));
        }
    }

    private static void ValidateRelease(ReleaseInfo release)
    {
        if (!SemanticVersion.TryParse(release.Version, out var version) ||
            (!AppPaths.IsSlashGuedes && version.IsPrerelease) ||
            (AppPaths.IsSlashGuedes &&
                (!version.IsPrerelease || version.Prerelease.Count != 2 ||
                 !version.Prerelease[0].Equals("preview", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Canal ou versão de atualização incompatível.");
        var expected = $"{(AppPaths.IsSlashGuedes ? "SlashGuedes" : "SlashDesk")}-{version}-portable-win-x64.zip";
        if (!release.PortableAsset.Name.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
            !release.ChecksumAsset.Name.Equals(expected + ".sha256", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(UiText.Get("Os artefatos da Release não correspondem ao portátil win-x64."));
        }
        ValidateAssetUrl(release.PortableAsset.DownloadUrl);
        ValidateAssetUrl(release.ChecksumAsset.DownloadUrl);
        if (AppPaths.IsSlashGuedes)
        {
            var prefix = $"https://github.com/{UpdateService.GuedesRepository}/releases/download/v{version}/";
            if (!release.PortableAsset.DownloadUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !release.ChecksumAsset.DownloadUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pacote não pertence à Release verificada do SlashGuedes.");
        }
    }

    private static void ValidateAssetUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(UiText.Get("A URL do artefato não pertence ao GitHub via HTTPS."));
        }
    }

    internal static void ValidateManifest(PortableUpdateManifest manifest, string manifestPath)
    {
        var data = Path.GetFullPath(manifest.DataDirectory);
        var updates = Path.GetFullPath(Path.Combine(data, "Updates"));
        var target = Path.GetFullPath(manifest.TargetExecutable);
        var executableDirectory = Path.GetDirectoryName(target)
            ?? throw new InvalidDataException(UiText.Get("Destino do executável inválido."));
        var product = AppPaths.IsSlashGuedes ? "SlashGuedes" : "SlashDesk";
        if (!Path.GetFileName(target).Equals(product + ".exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(Path.Combine(executableDirectory, product + "Data"))
                .Equals(data, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Manifesto não aponta para {product}.exe com {product}Data.");
        foreach (var path in new[]
                 {
                     manifestPath, manifest.StagedExecutable, manifest.BackupExecutable,
                     manifest.FailedExecutable, manifest.HelperExecutable, manifest.ConfirmationFile
                 })
        {
            if (!IsInside(Path.GetFullPath(path), updates))
            {
                throw new InvalidDataException(UiText.Get("O manifesto contém um caminho fora de SlashDeskData\\Updates."));
            }
        }
        if (!SemanticVersion.TryParse(manifest.ExpectedVersion, out _))
        {
            throw new InvalidDataException(UiText.Get("Versão esperada inválida no manifesto."));
        }
    }

    private static bool IsInside(string path, string directory)
    {
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static PortableUpdateManifest ReadManifest(string manifestPath) =>
        JsonSerializer.Deserialize<PortableUpdateManifest>(File.ReadAllText(manifestPath), JsonOptions)
        ?? throw new InvalidDataException(UiText.Get("Manifesto de atualização inválido."));

    private static string ParseChecksum(string content)
    {
        var hash = content.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (hash is null || hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(UiText.Get("O arquivo SHA-256 publicado é inválido."));
        }
        return hash.ToUpperInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void WaitForProcessExit(int processId, TimeSpan timeout)
    {
        if (processId <= 0) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                throw new TimeoutException(UiText.Get("O SlashDesk não encerrou no prazo da atualização."));
            }
        }
        catch (ArgumentException)
        {
            // O processo já foi encerrado.
        }
    }

    private static bool WaitForConfirmation(string confirmationFile, Process updated, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(confirmationFile)) return true;
            if (updated.HasExited) return false;
            Thread.Sleep(100);
        }
        return File.Exists(confirmationFile);
    }

    private static void TryStop(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
        }
    }

    private static void CleanupAfterHelperExit(PortableUpdateManifest manifest, int helperProcessId)
    {
        try
        {
            WaitForProcessExit(helperProcessId, TimeSpan.FromMinutes(1));
            Thread.Sleep(250);
            var directory = Path.GetDirectoryName(manifest.HelperExecutable);
            if (directory is not null)
            {
                TryDeleteDirectory(directory);
            }
            AppDiagnosticLog.Write(
                "update.cleanup.completed",
                ("operationId", manifest.OperationId));
        }
        catch (Exception exception)
        {
            AppDiagnosticLog.WriteException("update.cleanup.failed", exception);
        }
    }

    private static void WriteJournal(PortableUpdateManifest manifest, string stage, Exception? exception = null)
    {
        try
        {
            var logs = Path.Combine(manifest.DataDirectory, "Logs");
            Directory.CreateDirectory(logs);
            var entry = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                stage,
                operationId = manifest.OperationId,
                version = manifest.ExpectedVersion,
                processId = Environment.ProcessId,
                errorType = exception?.GetType().FullName,
                error = exception?.Message
            });
            File.AppendAllText(
                Path.Combine(logs, $"slashdesk-update-{DateTimeOffset.Now:yyyyMMdd}.jsonl"),
                entry + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static void TryWriteHelperFailure(string[] args, Exception exception)
    {
        try
        {
            var index = Array.FindIndex(args, argument =>
                argument.Equals("--apply-portable-update", StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < args.Length)
            {
                var manifest = ReadManifest(Path.GetFullPath(args[index + 1]));
                WriteJournal(manifest, "update.helper.failed", exception);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static HttpClient CreateClient() => new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static string ProductVersion() => UpdateService.InstalledVersion();
}

internal static class PortableUpdateFileTransaction
{
    internal static void Apply(
        string targetExecutable,
        string stagedExecutable,
        string backupExecutable,
        Action<string, string, string>? replace = null)
    {
        replace ??= static (source, target, backup) => File.Replace(source, target, backup, true);
        replace(stagedExecutable, targetExecutable, backupExecutable);
    }

    internal static void Rollback(
        string targetExecutable,
        string backupExecutable,
        string failedExecutable,
        Action<string, string, string>? replace = null)
    {
        replace ??= static (source, target, backup) => File.Replace(source, target, backup, true);
        replace(backupExecutable, targetExecutable, failedExecutable);
    }
}
