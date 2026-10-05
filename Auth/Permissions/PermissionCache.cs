using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Auth.Permissions;

/// <summary>
/// The permissions and ticks, held in memory so checking a request costs no database trip. Reloaded
/// every <see cref="AuthOptions.PermissionRefreshSeconds"/> (60), so a change made in SQL — or later
/// in the portal, which will also call <see cref="Invalidate"/> — applies within a minute.
///
/// If a reload fails the previous snapshot stays in use; if there has never been one, the check
/// answers 503 rather than guessing.
/// </summary>
public sealed class PermissionCache(IServiceScopeFactory scopes, IOptions<AuthOptions> options, TimeProvider clock, ILogger<PermissionCache> logger)
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _refresh = TimeSpan.FromSeconds(Math.Max(1, options.Value.PermissionRefreshSeconds));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PermissionSnapshot? _snapshot;
    private DateTimeOffset _nextLoad = DateTimeOffset.MinValue;

    public async Task<PermissionSnapshot?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot != null && clock.GetUtcNow() < _nextLoad) return _snapshot;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot != null && clock.GetUtcNow() < _nextLoad) return _snapshot;

            using var scope = scopes.CreateScope();
            _snapshot = await scope.ServiceProvider.GetRequiredService<IPermissionService>().LoadSnapshotAsync();
            _nextLoad = clock.GetUtcNow() + _refresh;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Couldn't load the permissions; {Using}.", _snapshot == null ? "requests needing them are refused" : "the previous ones stay in use");
            _nextLoad = clock.GetUtcNow() + RetryAfterFailure;
        }
        finally
        {
            _gate.Release();
        }

        return _snapshot;
    }

    /// <summary>The next request reloads — after a sync, or (later) a save in the portal.</summary>
    public void Invalidate() => _nextLoad = DateTimeOffset.MinValue;
}
