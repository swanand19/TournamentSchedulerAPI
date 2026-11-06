using Microsoft.AspNetCore.SignalR;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Realtime;

/// <summary>
/// Sends nudges down the live lines. Never throws and never makes a request wait for long: a nudge
/// that doesn't arrive only means the app finds out on its next (slower) poll.
/// </summary>
public interface ILiveUpdates
{
    /// <summary>These people's inboxes changed (a new notification, or one read on another device).</summary>
    Task InboxChangedAsync(IEnumerable<int> userIds);

    /// <summary>Something in the tournament changed; <paramref name="match"/> when it was one match.</summary>
    Task TournamentChangedAsync(int tournamentId, IScoredMatch? match = null);
}

public sealed class LiveUpdates(IHubContext<LiveHub> hub, ILogger<LiveUpdates> logger) : ILiveUpdates
{
    /// <summary>The sport as the apps' routes spell it ("football", "cricket").</summary>
    public static string SportOf(IScoredMatch match) => match is CricketMatch ? "cricket" : "football";

    public static string SportName(Sport sport) => sport == Sport.Cricket ? "cricket" : "football";

    public async Task InboxChangedAsync(IEnumerable<int> userIds)
    {
        var groups = userIds.Distinct().Select(LiveGroups.User).ToList();
        if (groups.Count == 0) return;
        await Send(() => hub.Clients.Groups(groups).SendAsync(LiveHub.NudgeMethod, new LiveNudge(LiveNudge.Inbox)));
    }

    public async Task TournamentChangedAsync(int tournamentId, IScoredMatch? match = null)
    {
        if (match == null)
        {
            await Send(() => hub.Clients.Group(LiveGroups.Tournament(tournamentId))
                .SendAsync(LiveHub.NudgeMethod, new LiveNudge(LiveNudge.TournamentKind, tournamentId)));
            return;
        }

        var sport = SportOf(match);
        await Send(() => hub.Clients.Group(LiveGroups.Match(sport, match.Id))
            .SendAsync(LiveHub.NudgeMethod, new LiveNudge(LiveNudge.MatchKind, tournamentId, sport, match.Id)));
        await Send(() => hub.Clients.Group(LiveGroups.Tournament(tournamentId))
            .SendAsync(LiveHub.NudgeMethod, new LiveNudge(LiveNudge.TournamentKind, tournamentId, sport, match.Id)));
    }

    private async Task Send(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception e)
        {
            // Not an error: the apps' polling catches up. Logged so a broken hub is noticed.
            logger.LogWarning(e, "Couldn't send a live nudge.");
        }
    }
}
