using System.Net;
using System.Net.Http;
using System.Text;
using SlashText.Models;
using SlashText.Services;

internal static class SlashGuedesUpdateChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "slashguedes-update-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, root, root, isSlashGuedes: true));
        var version = "3.2.1-preview.8";
        static string Release(string version, string product = "SlashGuedes") =>
            "{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v" + version +
            "\",\"name\":\"Preview\",\"body\":\"Teste\",\"html_url\":\"https://github.com/allanmguedes/SlashGuedes/releases/tag/v" + version +
            "\",\"assets\":[{\"name\":\"" + product + "-" + version + "-portable-win-x64.zip\",\"size\":123," +
            "\"browser_download_url\":\"https://github.com/allanmguedes/SlashGuedes/releases/download/v" + version +
            "/" + product + "-" + version + "-portable-win-x64.zip\"}," +
            "{\"name\":\"" + product + "-" + version + "-portable-win-x64.zip.sha256\",\"size\":88," +
            "\"browser_download_url\":\"https://github.com/allanmguedes/SlashGuedes/releases/download/v" + version +
            "/" + product + "-" + version + "-portable-win-x64.zip.sha256\"}]}";

        var json = "[" + Release("3.2.1-preview.99", "SlashDesk") + "," +
                   Release("3.2.1-preview.3") + "," +
                   Release(version) + "]";
        using var client = new HttpClient(new FakeHandler(json));
        var store = Path.Combine(root, "guedes-test-update-state.json");
        var service = new UpdateService(client, store, "3.2.1-preview.7",
            () => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(8), isSlashGuedes: true);
        var available = await service.CheckAsync(force: true);
        Require(available.Status == UpdateCheckStatus.UpdateAvailable && available.UpdateAvailable,
            "Preview nova deve ser detectada");
        Require(available.LatestVersion == version,
            "Não aceitar pacote com nome SlashDesk mesmo que tenha maior número");
        Require(available.Release?.PortableAsset.Name ==
            "SlashGuedes-" + version + "-portable-win-x64.zip",
            "Apenas ZIP do Guedes pode atualizar Guedes");
        Require(available.Release.ChecksumAsset.Name.EndsWith(".sha256", StringComparison.Ordinal),
            "SHA-256 deve fazer parte da Release");

        var newer = new UpdateService(client, Path.Combine(root, "newer-state.json"),
            "3.2.1-preview.10", isSlashGuedes: true);
        var noUpdate = await newer.CheckAsync(force: true);
        Require(noUpdate.Status == UpdateCheckStatus.UpToDate && !noUpdate.UpdateAvailable,
            "Nunca instalar Preview mais antiga");

        var operation = Path.Combine(AppPaths.DataDirectory, "Updates", Guid.NewGuid().ToString("N"));
        var manifest = new PortableUpdateManifest
        {
            ExpectedVersion = version,
            DataDirectory = AppPaths.DataDirectory,
            TargetExecutable = Path.Combine(root, "SlashGuedes.exe"),
            StagedExecutable = Path.Combine(operation, "SlashGuedes.new.exe"),
            BackupExecutable = Path.Combine(operation, "SlashGuedes.previous.exe"),
            FailedExecutable = Path.Combine(operation, "SlashGuedes.failed.exe"),
            HelperExecutable = Path.Combine(operation, "SlashGuedes.Updater.exe"),
            ConfirmationFile = Path.Combine(operation, "update-confirmed.json")
        };
        PortableUpdateService.ValidateManifest(manifest, Path.Combine(operation, "update-manifest.json"));
        manifest.TargetExecutable = Path.Combine(root, "SlashDesk.exe");
        try
        {
            PortableUpdateService.ValidateManifest(manifest, Path.Combine(operation, "update-manifest.json"));
            throw new InvalidOperationException("Um pacote do SlashDesk foi aceito para substituir SlashGuedes.exe");
        }
        catch (InvalidDataException) { }

        Console.WriteLine("SlashGuedes updater: canal próprio, ordem de versões, ZIP e manifesto OK.");
    }

    private sealed class FakeHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri !=
                "https://api.github.com/repos/allanmguedes/SlashGuedes/releases?per_page=30")
                throw new InvalidOperationException("Updater tentou consultar o repositório errado.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("Falha de atualização: " + reason);
    }
}
