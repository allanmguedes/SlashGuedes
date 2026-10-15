using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using SlashText.Models;

namespace SlashText.Services;

public sealed class UpdateService
{
    internal const string Repository = "lucasllira/SlashText";
    internal const string GuedesRepository = "allanmguedes/SlashGuedes";
    internal static string ActiveRepository => AppPaths.IsSlashGuedes ? GuedesRepository : Repository;
    internal static readonly Uri ReleasesApi =
        new($"https://api.github.com/repos/{Repository}/releases?per_page=20");
    internal static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(6);
    internal static readonly TimeSpan RemindLaterInterval = TimeSpan.FromHours(24);

    private readonly HttpClient _client;
    private readonly JsonFileStore<UpdateState> _stateStore;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _currentVersion;
    private readonly TimeSpan _timeout;
    private readonly bool _isGuedes;
    private readonly string _repository;
    private readonly Uri _releasesApi;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public UpdateService()
        : this(CreateClient(), AppPaths.UpdateStateFile, InstalledVersion(),
            () => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(8), AppPaths.IsSlashGuedes)
    {
    }

    internal UpdateService(
        HttpClient client,
        string stateFile,
        string currentVersion,
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? timeout = null,
        bool isSlashGuedes = false)
    {
        _isGuedes = isSlashGuedes;
        _repository = isSlashGuedes ? GuedesRepository : Repository;
        _releasesApi = isSlashGuedes
            ? new Uri($"https://api.github.com/repos/{GuedesRepository}/releases?per_page=30")
            : ReleasesApi;
        _client = client;
        _stateStore = new JsonFileStore<UpdateState>(stateFile);
        _currentVersion = SemanticVersion.TryParse(currentVersion, out var parsed)
            ? parsed.ToString()
            : "0.0.0";
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _timeout = timeout ?? TimeSpan.FromSeconds(8);
    }

    public Task<UpdateState> LoadStateAsync(CancellationToken cancellationToken = default) =>
        _stateStore.LoadAsync(cancellationToken);

    public async Task<UpdateCheckResult> CheckAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            var state = await _stateStore.LoadAsync(cancellationToken);
            var now = _utcNow();
            if (!force && state.LastCheckedUtc is { } checkedAt &&
                now - checkedAt < AutomaticCheckInterval)
            {
                return FromCachedState(state);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                AppDiagnosticLog.Write("update.check.started", ("repository", _repository), ("manual", force));
                using var response = await _client.GetAsync(_releasesApi, timeout.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return await SaveResultAsync(state, new UpdateCheckResult(
                        UpdateCheckStatus.NoRelease, false, _currentVersion, null,
                        "Ainda não existe uma versão publicada no GitHub Releases.",
                        $"https://github.com/{_repository}/releases", null, null, null), cancellationToken);
                }
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
                var release = SelectLatestRelease(document.RootElement, _isGuedes);
                if (release is null)
                {
                    return await SaveResultAsync(state, new UpdateCheckResult(
                        UpdateCheckStatus.NoRelease, false, _currentVersion, null,
                        _isGuedes ? "Nenhuma Preview do SlashGuedes foi publicada ainda." : "Nenhuma versão estável compatível foi encontrada.",
                        $"https://github.com/{_repository}/releases", null, null, null), cancellationToken);
                }
                if (!SemanticVersion.TryParse(_currentVersion, out var current) ||
                    !SemanticVersion.TryParse(release.Version, out var latest))
                {
                    return await SaveResultAsync(state, new UpdateCheckResult(
                        UpdateCheckStatus.InvalidRelease, false, _currentVersion, release.Version,
                        "A versão publicada não usa um identificador SemVer válido.",
                        release.PageUrl, release.Notes, release.PortableAsset.Size, release), cancellationToken);
                }
                if (latest.CompareTo(current) <= 0)
                {
                    return await SaveResultAsync(state, new UpdateCheckResult(
                        UpdateCheckStatus.UpToDate, false, _currentVersion, release.Version,
                        "Você já está usando a versão mais recente.", release.PageUrl,
                        release.Notes, release.PortableAsset.Size, release), cancellationToken);
                }
                if (string.Equals(state.IgnoredVersion, release.Version, StringComparison.OrdinalIgnoreCase))
                {
                    return await SaveResultAsync(state, new UpdateCheckResult(
                        UpdateCheckStatus.Ignored, false, _currentVersion, release.Version,
                        $"A versão {release.Version} está ignorada neste dispositivo.", release.PageUrl,
                        release.Notes, release.PortableAsset.Size, release), cancellationToken);
                }
                if (!force &&
                    string.Equals(state.DeferredVersion, release.Version, StringComparison.OrdinalIgnoreCase) &&
                    state.DeferredUntilUtc > now)
                {
                    return await SaveResultAsync(state, new UpdateCheckResult(
                        UpdateCheckStatus.Deferred, false, _currentVersion, release.Version,
                        $"A versão {release.Version} será lembrada novamente mais tarde.", release.PageUrl,
                        release.Notes, release.PortableAsset.Size, release), cancellationToken);
                }

                return await SaveResultAsync(state, new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable, true, _currentVersion, release.Version,
                    $"{(_isGuedes ? "SlashGuedes" : "SlashDesk")} {release.Version} está disponível.", release.PageUrl,
                    release.Notes, release.PortableAsset.Size, release), cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException &&
                                               !cancellationToken.IsCancellationRequested)
            {
                AppDiagnosticLog.WriteException("update.check.offline", exception);
                return await SaveResultAsync(state, new UpdateCheckResult(
                    UpdateCheckStatus.Offline, false, _currentVersion, null,
                    "Não foi possível verificar agora. O SlashDesk tentará novamente mais tarde.",
                    null, null, null, null), CancellationToken.None);
            }
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public async Task IgnoreVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.LoadAsync(cancellationToken);
        state.IgnoredVersion = version;
        state.DeferredVersion = null;
        state.DeferredUntilUtc = null;
        await _stateStore.SaveAsync(state, cancellationToken);
        AppDiagnosticLog.Write("update.version.ignored", ("version", version));
    }

    public async Task RemindLaterAsync(string version, CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.LoadAsync(cancellationToken);
        state.DeferredVersion = version;
        state.DeferredUntilUtc = _utcNow() + RemindLaterInterval;
        await _stateStore.SaveAsync(state, cancellationToken);
        AppDiagnosticLog.Write("update.version.deferred", ("version", version));
    }

    private async Task<UpdateCheckResult> SaveResultAsync(
        UpdateState state,
        UpdateCheckResult result,
        CancellationToken cancellationToken)
    {
        state.LastCheckedUtc = _utcNow();
        state.LastResult = result.Message;
        state.LastAvailableVersion = result.LatestVersion;
        state.LastReleaseUrl = result.Url;
        state.LastReleaseNotes = result.Notes;
        state.LastDownloadSize = result.DownloadSize;
        state.LastPortableAssetName = result.Release?.PortableAsset.Name;
        state.LastPortableAssetUrl = result.Release?.PortableAsset.DownloadUrl;
        state.LastChecksumAssetName = result.Release?.ChecksumAsset.Name;
        state.LastChecksumAssetUrl = result.Release?.ChecksumAsset.DownloadUrl;
        await _stateStore.SaveAsync(state, cancellationToken);
        AppDiagnosticLog.Write(
            "update.check.completed",
            ("status", result.Status.ToString()),
            ("version", result.LatestVersion),
            ("downloadBytes", result.DownloadSize));
        return result;
    }

    private UpdateCheckResult FromCachedState(UpdateState state)
    {
        var available = state.LastAvailableVersion is { } latest &&
                        SemanticVersion.TryParse(latest, out var parsedLatest) &&
                        SemanticVersion.TryParse(_currentVersion, out var current) &&
                        parsedLatest.CompareTo(current) > 0 &&
                        !string.Equals(state.IgnoredVersion, latest, StringComparison.OrdinalIgnoreCase) &&
                        !(string.Equals(state.DeferredVersion, latest, StringComparison.OrdinalIgnoreCase) &&
                          state.DeferredUntilUtc > _utcNow());
        ReleaseInfo? release = null;
        if (state.LastAvailableVersion is { } version &&
            state.LastReleaseUrl is { } pageUrl &&
            state.LastPortableAssetName is { } assetName &&
            state.LastPortableAssetUrl is { } assetUrl &&
            state.LastChecksumAssetName is { } checksumName &&
            state.LastChecksumAssetUrl is { } checksumUrl)
        {
            release = new ReleaseInfo(
                version, version, state.LastReleaseNotes ?? string.Empty, pageUrl, null,
                new ReleaseAssetInfo(assetName, assetUrl, state.LastDownloadSize ?? 0),
                new ReleaseAssetInfo(checksumName, checksumUrl, 0));
        }
        return new UpdateCheckResult(
            UpdateCheckStatus.Cached, available, _currentVersion, state.LastAvailableVersion,
            state.LastResult, state.LastReleaseUrl, state.LastReleaseNotes,
            state.LastDownloadSize, release);
    }

    private static ReleaseInfo? SelectLatestStableRelease(JsonElement releases) =>
        SelectLatestRelease(releases, isSlashGuedes: false);

    private static ReleaseInfo? SelectLatestRelease(JsonElement releases, bool isSlashGuedes)
    {
        ReleaseInfo? selected = null;
        SemanticVersion? selectedVersion = null;
        foreach (var item in releases.EnumerateArray())
        {
            if (item.GetProperty("draft").GetBoolean() ||
                (!isSlashGuedes && item.GetProperty("prerelease").GetBoolean()))
            {
                continue;
            }
            var tag = item.GetProperty("tag_name").GetString();
            if (!SemanticVersion.TryParse(tag, out var version) ||
                (!isSlashGuedes && version.IsPrerelease))
            {
                continue;
            }
            if (isSlashGuedes && (!version.IsPrerelease ||
                version.Prerelease.Count != 2 ||
                !version.Prerelease[0].Equals("preview", StringComparison.OrdinalIgnoreCase))) continue;
            var versionText = version.ToString();
            var expectedZip = $"{(isSlashGuedes ? "SlashGuedes" : "SlashDesk")}-{versionText}-portable-win-x64.zip";
            var expectedChecksum = expectedZip + ".sha256";
            ReleaseAssetInfo? portable = null;
            ReleaseAssetInfo? checksum = null;
            foreach (var asset in item.GetProperty("assets").EnumerateArray())
            {
                var assetInfo = new ReleaseAssetInfo(
                    asset.GetProperty("name").GetString() ?? string.Empty,
                    asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    asset.GetProperty("size").GetInt64());
                if (assetInfo.Name.Equals(expectedZip, StringComparison.OrdinalIgnoreCase)) portable = assetInfo;
                if (assetInfo.Name.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase)) checksum = assetInfo;
            }
            if (portable is null || checksum is null || selectedVersion is not null && version.CompareTo(selectedVersion) <= 0)
            {
                continue;
            }
            selectedVersion = version;
            selected = new ReleaseInfo(
                versionText,
                item.TryGetProperty("name", out var name) ? name.GetString() ?? versionText : versionText,
                item.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                item.GetProperty("html_url").GetString() ?? $"https://github.com/{(isSlashGuedes ? GuedesRepository : Repository)}/releases",
                item.TryGetProperty("published_at", out var published) && published.TryGetDateTimeOffset(out var date)
                    ? date : null,
                portable,
                checksum);
        }
        return selected;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SlashDesk", ProductVersion()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal static string InstalledVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        if (AppPaths.IsSlashGuedes)
        {
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (SemanticVersion.TryParse(informational, out var preview)) return preview.ToString();
        }
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string ProductVersion() => InstalledVersion();
}
