using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Realtime;

/// <summary>
/// A live line outlives the ticket that opened it, so once a minute the lines whose session has
/// ended — signed out (on any device), expired, blocked, deleted — are closed. Every gateway call
/// already checks the session itself; this keeps the nudges from going on for up to a minute longer.
/// </summary>
public sealed class LiveSessionSweep(LiveConnections connections, IServiceScopeFactory scopes, ILogger<LiveSessionSweep> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Every, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await SweepAsync();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Couldn't check the live lines' sessions; trying again in a minute.");
            }
        }
    }

    /// <summary>Closes the lines whose session has ended. Returns how many.</summary>
    public async Task<int> SweepAsync()
    {
        var open = connections.All();
        if (open.Count == 0) return 0;

        using var scope = scopes.CreateScope();
        var active = await scope.ServiceProvider.GetRequiredService<ISessionService>()
            .ActiveAmongAsync(open.Select(c => c.SessionId).Distinct().ToList());

        var closed = 0;
        foreach (var connection in open.Where(c => !active.Contains(c.SessionId)))
        {
            connection.Context.Abort();
            connections.Remove(connection.ConnectionId);
            closed++;
        }
        return closed;
    }
}
