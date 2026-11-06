using Microsoft.AspNetCore.SignalR;

namespace TournamentScheduler.Api.Realtime;

/// <summary>
/// The live line. It only ever says "something changed" — <see cref="LiveNudge"/> carries ids, never
/// data — and the apps then fetch through the encrypted gateway as they always have, so what
/// anyone sees is still decided there. Clients call nothing on it: what a line hears about is chosen
/// through gateway services (<c>LIVE_WATCH_*</c>), where the usual tournament access check runs.
///
/// Who is on the line was settled before the hub ran, by the ticket (<see cref="LiveHubAuthentication"/>).
/// </summary>
public sealed class LiveHub(LiveConnections connections) : Hub
{
    public const string Path = "/hubs/live";

    /// <summary>Server → app: something changed (<see cref="LiveNudge"/>).</summary>
    public const string NudgeMethod = "nudge";

    /// <summary>Server → app, once on connecting: the line's id, which watch requests name.</summary>
    public const string HelloMethod = "hello";

    public override async Task OnConnectedAsync()
    {
        if (Context.GetHttpContext()?.Items[LiveHubAuthentication.HolderKey] is not LiveTicketHolder holder)
        {
            Context.Abort();
            return;
        }

        connections.Add(new LiveConnection(Context.ConnectionId, holder.UserId, holder.SessionId, Context));
        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.User(holder.UserId));
        await Clients.Caller.SendAsync(HelloMethod, Context.ConnectionId);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// What changed: <c>inbox</c> (yours), <c>tournament</c> (its matches list, bracket, people…), or
/// <c>match</c> (one match's score or scoring). A match change also reaches the tournament's watchers.
/// </summary>
public sealed record LiveNudge(string Kind, int? TournamentId = null, string? Sport = null, int? MatchId = null)
{
    public const string Inbox = "inbox";
    public const string TournamentKind = "tournament";
    public const string MatchKind = "match";
}
