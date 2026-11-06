using Microsoft.AspNetCore.SignalR;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Models.Live;
using TournamentScheduler.Api.Realtime;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services.Live;

public interface ILiveService
{
    ServiceResult<LiveTicketDto> Ticket();

    /// <summary>The line hears about the tournament the access check just resolved (View).</summary>
    Task<ServiceResult<object>> WatchTournamentAsync(string connectionId);

    /// <summary>The line hears about the match the access check just resolved (View).</summary>
    Task<ServiceResult<object>> WatchMatchAsync(string connectionId);

    Task<ServiceResult<object>> UnwatchAsync(string connectionId, LiveUnwatchRequest request);
}

/// <summary>
/// Live updates, the gateway side: a ticket to open the line, and what the line hears about. Watching
/// is a gateway call so the usual tournament access check decides it — a line only hears ids of what
/// its person may view. A line can only be pointed by its own person.
/// </summary>
public class LiveService(
    LiveTickets tickets,
    LiveConnections connections,
    IHubContext<LiveHub> hub,
    ITournamentContext context,
    ICurrentUser currentUser) : ServiceBase, ILiveService
{
    public ServiceResult<LiveTicketDto> Ticket() => Success(new LiveTicketDto
    {
        Ticket = tickets.Issue(currentUser.UserId, currentUser.SessionId),
        ExpiresInSeconds = (int)LiveTickets.Lifetime.TotalSeconds,
        Path = LiveHub.Path
    });

    public Task<ServiceResult<object>> WatchTournamentAsync(string connectionId) =>
        Join(connectionId, LiveGroups.Tournament(context.TournamentId));

    public Task<ServiceResult<object>> WatchMatchAsync(string connectionId) =>
        context.Match is { } match
            ? Join(connectionId, LiveGroups.Match(LiveUpdates.SportOf(match), match.Id))
            : Task.FromResult<ServiceResult<object>>(NotFound("Match not found."));

    public async Task<ServiceResult<object>> UnwatchAsync(string connectionId, LiveUnwatchRequest request)
    {
        if (Own(connectionId) is not { } line) return Gone();

        var sport = request.Sport?.Trim().ToLowerInvariant();
        string? group = request switch
        {
            { MatchId: > 0 } when sport is "football" or "cricket" => LiveGroups.Match(sport, request.MatchId.Value),
            { TournamentId: > 0 } => LiveGroups.Tournament(request.TournamentId.Value),
            _ => null
        };
        if (group == null) return BadRequest("Say which tournament or match to stop watching.");

        await hub.Groups.RemoveFromGroupAsync(line.ConnectionId, group);
        return Ok(new { watching = false });
    }

    private async Task<ServiceResult<object>> Join(string connectionId, string group)
    {
        if (Own(connectionId) is not { } line) return Gone();
        await hub.Groups.AddToGroupAsync(line.ConnectionId, group);
        return Ok(new { watching = true });
    }

    private LiveConnection? Own(string connectionId) =>
        connections.Find(connectionId) is { } line && line.UserId == currentUser.UserId ? line : null;

    /// <summary>The line closed (or was never this person's): the app opens a new one and asks again.</summary>
    private static ServiceError Gone() => NotFound("This live updates connection has closed. Reconnect and try again.");
}
