using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services;

public sealed record LifecycleResult(int Completed, int Cancelled);

public interface ITournamentLifecycleService
{
    /// <summary>Closes every tournament whose end date, plus a day's grace, has passed.</summary>
    Task<LifecycleResult> CloseFinishedAsync();
}

/// <summary>
/// For owners who forget: at midnight after the day after a tournament's end date (its own time
/// zone), a live tournament completes and one that never started is cancelled. A match being
/// played at that moment can still be finished. Tournaments without dates (from before dates
/// existed) are left alone.
/// </summary>
public class TournamentLifecycleService : ServiceBase, ITournamentLifecycleService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public TournamentLifecycleService(IDataService<Tournament> tournaments, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _tournaments = tournaments;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<LifecycleResult> CloseFinishedAsync()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var open = await _tournaments.ListAsync(t =>
            (t.Status == TournamentStatus.Upcoming || t.Status == TournamentStatus.Live) && t.EndDate != null);

        int completed = 0, cancelled = 0;
        foreach (var tournament in open)
        {
            if (now < TournamentDates.ClosesAtUtc(tournament.EndDate!.Value, tournament.TimeZoneId)) continue;

            if (tournament.Status == TournamentStatus.Live)
            {
                tournament.Status = TournamentStatus.Completed;
                tournament.CompletedAt = now;
                tournament.CompletedByUserId = SystemUser.Id;
                completed++;
            }
            else
            {
                tournament.Status = TournamentStatus.Cancelled;
                cancelled++;
            }
        }

        if (completed + cancelled > 0) await _unitOfWork.SaveChangesAsync();
        return new LifecycleResult(completed, cancelled);
    }
}

/// <summary>Runs <see cref="TournamentLifecycleService"/> at start-up and then hourly.</summary>
public sealed class TournamentLifecycleJob(IServiceScopeFactory scopes, ILogger<TournamentLifecycleJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<ITournamentLifecycleService>().CloseFinishedAsync();
                if (result.Completed + result.Cancelled > 0)
                    logger.LogInformation("Closed finished tournaments: {Completed} completed, {Cancelled} cancelled.", result.Completed, result.Cancelled);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Couldn't close finished tournaments; trying again in an hour.");
            }

            try
            {
                await Task.Delay(Every, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
