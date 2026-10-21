using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services;

public sealed record LifecycleResult(int Completed, int Cancelled);

public interface ITournamentLifecycleService
{
    /// <summary>Closes every tournament whose end date, plus a day's grace, has passed.</summary>
    Task<LifecycleResult> CloseFinishedAsync();

    /// <summary>Reminds owners of tournaments at their end date (inbox and email), once per end date. Returns how many tournaments.</summary>
    Task<int> RemindAsync();
}

/// <summary>
/// For owners who forget: at midnight after the day after a tournament's end date (its own time
/// zone), a live tournament completes and one that never started is cancelled. A match being
/// played at that moment can still be finished. Tournaments without dates (from before dates
/// existed) are left alone.
///
/// Before that, on the end date (from 8 in the morning, the tournament's time), its owners get a
/// reminder (phase 6D) saying what will happen and what's left to play.
/// </summary>
public class TournamentLifecycleService : ServiceBase, ITournamentLifecycleService
{
    /// <summary>Reminders wait for the morning of the end date, not the stroke of midnight.</summary>
    public static readonly TimeOnly RemindFrom = new(8, 0);

    private readonly IDataService<Tournament> _tournaments;
    private readonly INotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public TournamentLifecycleService(IDataService<Tournament> tournaments, INotifier notifier, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _tournaments = tournaments;
        _notifier = notifier;
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

    public async Task<int> RemindAsync()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var open = await _tournaments.QueryAsync(q => q
            .Where(t => (t.Status == TournamentStatus.Upcoming || t.Status == TournamentStatus.Live) && t.EndDate != null)
            .Select(t => new { t.Id, t.Name, t.Sport, t.Status, t.EndDate, t.TimeZoneId }), tracking: false);

        var reminded = 0;
        foreach (var t in open)
        {
            var end = t.EndDate!.Value;
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), TournamentDates.ZoneOf(t.TimeZoneId));
            if (local < end.ToDateTime(RemindFrom) || now >= TournamentDates.ClosesAtUtc(end, t.TimeZoneId)) continue;

            // Keyed on the date: an owner who moves the end date gets reminded again at the new one.
            var key = $"end-date:{t.Id}:{end:yyyy-MM-dd}";
            if (await _notifier.AlreadySentAsync(key)) continue;

            var owners = await _notifier.OwnersOfAsync(t.Id);
            if (owners.Count == 0) continue;

            var lastDay = DateOnly.FromDateTime(local) == end;
            var values = new Dictionary<string, string>
            {
                ["TournamentName"] = t.Name,
                ["EndsWhen"] = lastDay ? "ends today" : "ended yesterday",
                ["ClosesWhen"] = lastDay ? "at the end of tomorrow" : "at midnight tonight"
            };
            var code = NotificationTemplateCode.EndDateNotStarted;
            if (t.Status == TournamentStatus.Live)
            {
                var (total, over) = await _notifier.PlayedAsync(t.Id, t.Sport);
                var left = total - over;
                values["MatchesLeft"] = left == 1 ? "1 match" : $"{left} matches";
                code = left > 0 ? NotificationTemplateCode.EndDateMatchesLeft : NotificationTemplateCode.EndDateAllPlayed;
            }

            // Inbox, and email to owners who have one, as the template says; the email job sends those.
            foreach (var owner in owners)
                await _notifier.NotifyAsync(owner, code, values, new NotificationLink(t.Id), key);
            reminded++;
        }

        if (reminded > 0) await _unitOfWork.SaveChangesAsync();
        return reminded;
    }
}

/// <summary>
/// Runs at start-up and then hourly: closes finished tournaments (<see cref="TournamentLifecycleService"/>),
/// sends end-date reminders, and deletes old notifications.
/// </summary>
public sealed class TournamentLifecycleJob(IServiceScopeFactory scopes, ILogger<TournamentLifecycleJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Run("close finished tournaments", async services =>
            {
                var result = await services.GetRequiredService<ITournamentLifecycleService>().CloseFinishedAsync();
                if (result.Completed + result.Cancelled > 0)
                    logger.LogInformation("Closed finished tournaments: {Completed} completed, {Cancelled} cancelled.", result.Completed, result.Cancelled);
            });
            await Run("send end-date reminders", async services =>
            {
                var reminded = await services.GetRequiredService<ITournamentLifecycleService>().RemindAsync();
                if (reminded > 0) logger.LogInformation("Sent end-date reminders for {Count} tournaments.", reminded);
            });
            await Run("delete old notifications", async services =>
                await services.GetRequiredService<INotificationService>().PurgeOldAsync());

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

    /// <summary>Each step in its own scope, so one failing doesn't stop the others.</summary>
    private async Task Run(string what, Func<IServiceProvider, Task> step)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await step(scope.ServiceProvider);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Couldn't {What}; trying again in an hour.", what);
        }
    }
}
