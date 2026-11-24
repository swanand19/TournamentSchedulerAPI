using System.Collections.Concurrent;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Auctions;

namespace TournamentScheduler.Api.Services.Auctions;

/// <summary>
/// One change at a time per tournament's auction: two bids at the same moment are taken in order,
/// and the second is checked against the first (plan 4.4). In memory, like the gateway's replay
/// cache, so it holds for one server; the bid's "amount I saw" check still refuses a stale bid.
/// </summary>
public sealed class AuctionLocks
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> EnterAsync(int tournamentId)
    {
        var gate = _locks.GetOrAdd(tournamentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) gate.Release();
        }
    }
}

/// <summary>
/// When each running auction's bid clock runs out, kept in memory so the clock job only touches the
/// database when a lot is actually due. Filled from the database at start-up (a restart mid-lot loses
/// nothing), and every read closes an overdue lot as well.
/// </summary>
public sealed class AuctionClock
{
    private readonly ConcurrentDictionary<int, DateTime> _due = new();

    /// <summary>The lot under the hammer in this tournament closes then (null: no clock running).</summary>
    public void Track(int tournamentId, DateTime? closesAt)
    {
        if (closesAt is { } at) _due[tournamentId] = at;
        else _due.TryRemove(tournamentId, out _);
    }

    public List<int> DueAt(DateTime now) => _due.Where(d => d.Value <= now).Select(d => d.Key).ToList();
}

/// <summary>
/// Sells the player under the hammer when the bid clock runs out, even if every phone is closed
/// (plan 5: "a background job closes lots whose time is up"). Checks four times a second, in memory;
/// a due lot is closed through <see cref="IAuctionService.CloseDueAsync"/>, which re-reads it under the lock.
/// </summary>
public sealed class AuctionClockJob(IServiceScopeFactory scopes, AuctionClock clock, DrawClock drawClock, TimeProvider time, ILogger<AuctionClockJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadRunningAsync();
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var tournamentId in clock.DueAt(time.GetUtcNow().UtcDateTime))
            {
                clock.Track(tournamentId, null); // closing it (or finding it moved) tracks it again
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IAuctionService>().CloseDueAsync(tournamentId);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Couldn't close an auction lot in tournament {TournamentId}; the next look at it will.", tournamentId);
                }
            }

            // The team draw: a name landing (its player told, every screen nudged), or the next drawn by itself.
            foreach (var tournamentId in drawClock.DueAt(time.GetUtcNow().UtcDateTime))
            {
                drawClock.Track(tournamentId, null);
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IDrawService>().TickAsync(tournamentId);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Couldn't move the team draw on in tournament {TournamentId}; the next look at it will.", tournamentId);
                }
            }

            try
            {
                await Task.Delay(Every, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>After a restart: every lot under the hammer with a clock.</summary>
    private async Task LoadRunningAsync()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var lots = scope.ServiceProvider.GetRequiredService<IDataService<AuctionLot>>();
            var auctions = scope.ServiceProvider.GetRequiredService<IDataService<Auction>>();
            var draws = scope.ServiceProvider.GetRequiredService<IDataService<TeamDraw>>();
            var picks = scope.ServiceProvider.GetRequiredService<IDataService<DrawPick>>();
            var running = await draws.QueryAsync(q => q.Where(d => d.Status == DrawStatus.Live).Select(d => new { d.Id, d.TournamentId, d.NextAutoAt }), tracking: false);
            foreach (var d in running)
            {
                var landing = await picks.QueryAsync(q => q.Where(p => p.DrawId == d.Id && !p.Announced).Select(p => (DateTime?)p.LandsAt), tracking: false);
                var due = landing.Append(d.NextAutoAt).Where(x => x != null).Min();
                drawClock.Track(d.TournamentId, due);
            }

            var open = await lots.QueryAsync(q => q.Where(l => l.Status == AuctionLotStatus.Open && l.ClosesAt != null)
                .Select(l => new { l.AuctionId, l.ClosesAt }), tracking: false);
            if (open.Count == 0) return;
            var ids = open.Select(o => o.AuctionId).Distinct().ToList();
            var tournaments = (await auctions.QueryAsync(q => q.Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.TournamentId }), tracking: false))
                .ToDictionary(a => a.Id, a => a.TournamentId);
            foreach (var o in open)
                if (tournaments.TryGetValue(o.AuctionId, out var t)) clock.Track(t, o.ClosesAt);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Couldn't read the running auctions at start-up; their lots close when next looked at.");
        }
    }
}
