using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Versions;

namespace TournamentScheduler.Api.Services.Versions;

/// <summary>The <c>AppVersions</c> settings: the store pages a release links to when it has no link of its own.</summary>
public sealed class AppVersionOptions
{
    public const string Section = "AppVersions";

    /// <summary>"Android" / "iOS" → the app's store page. Empty until the app is in a store.</summary>
    public Dictionary<string, string> StoreUrls { get; set; } = [];

    /// <summary>How often the releases are re-read; a save in the portal applies at once anyway.</summary>
    public int RefreshSeconds { get; set; } = 60;

    public string? StoreUrlFor(AppPlatform platform) =>
        StoreUrls.TryGetValue(platform.ToString(), out var url) && !string.IsNullOrWhiteSpace(url) ? url : null;
}

/// <summary>
/// The releases, held in memory so the gateway can check every request's app version without a
/// database trip. Reloaded every <see cref="AppVersionOptions.RefreshSeconds"/> and at once after a
/// save in the portal. If a reload fails the previous list stays; with none at all nobody is asked to
/// update — a database hiccup must never lock people out of the app.
/// </summary>
public sealed class AppVersionCache(IServiceScopeFactory scopes, IOptions<AppVersionOptions> options, TimeProvider clock, ILogger<AppVersionCache> logger)
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _refresh = TimeSpan.FromSeconds(Math.Max(1, options.Value.RefreshSeconds));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AppRelease> _releases = [];
    private bool _loaded;
    private DateTimeOffset _nextLoad = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<AppRelease>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded && clock.GetUtcNow() < _nextLoad) return _releases;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_loaded && clock.GetUtcNow() < _nextLoad) return _releases;

            using var scope = scopes.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IDataService<AppStart>>()
                .QueryAsync(q => q.Select(a => new AppRelease(a.AppStartId, a.Platform, a.AppVersion, a.IsForceUpdate, a.ReleaseDate, a.IsDisabled, a.Description, a.StoreUrl)), tracking: false);
            _releases = rows;
            _loaded = true;
            _nextLoad = clock.GetUtcNow() + _refresh;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Couldn't load the app versions; {Using}.", _loaded ? "the previous list stays in use" : "nobody is asked to update until they load");
            _nextLoad = clock.GetUtcNow() + RetryAfterFailure;
        }
        finally
        {
            _gate.Release();
        }

        return _releases;
    }

    /// <summary>The next request reloads — after a save in the portal.</summary>
    public void Invalidate() => _nextLoad = DateTimeOffset.MinValue;

    /// <summary>What an app on this channel and version should be told, now.</summary>
    public async Task<ClientUpdateDto> EvaluateAsync(string? channel, string? appVersion, CancellationToken cancellationToken = default)
    {
        var platform = AppVersionRules.PlatformOf(channel);
        if (platform == null) return new ClientUpdateDto { Status = ClientUpdateStatus.None };
        return AppVersionRules.Evaluate(await GetAsync(cancellationToken), platform, appVersion, clock.GetUtcNow().UtcDateTime,
            options.Value.StoreUrlFor(platform.Value));
    }
}
