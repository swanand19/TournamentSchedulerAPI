using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Auth.Permissions;

/// <summary>
/// Runs the permission sync when the API starts, so a deploy can never leave a new service out of
/// the Permissions table. The first try happens before the API takes requests; if the database
/// isn't reachable then, the API starts anyway and keeps trying every minute in the background
/// (until it succeeds, new services answer "not available yet").
/// </summary>
public sealed class PermissionSyncHostedService(
    IServiceScopeFactory scopes,
    ServiceRegistry registry,
    PermissionCache cache,
    ILogger<PermissionSyncHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);
    private bool _synced;

    /// <summary>The first sync, before the server starts taking requests.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _synced = await TrySync();
        await base.StartAsync(cancellationToken);
    }

    /// <summary>Only if the first sync failed: keep trying until it works or the API stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!_synced && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Retry, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _synced = await TrySync();
        }
    }

    private async Task<bool> TrySync()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IPermissionService>().SyncAsync(registry.All);
            logger.LogInformation("Permissions synced with the code: {Result}.", result);
            cache.Invalidate();
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Couldn't sync the permissions with the code; trying again in a minute.");
            return false;
        }
    }
}
