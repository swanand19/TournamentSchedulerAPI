using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace TournamentScheduler.Api.Realtime;

public sealed record LiveConnection(string ConnectionId, int UserId, Guid SessionId, HubCallerContext Context);

/// <summary>
/// Every open live line on this server and whose it is. Lets a watch request check the line is the
/// caller's own, and lets <see cref="LiveSessionSweep"/> close the lines of sessions that have ended.
/// </summary>
public sealed class LiveConnections
{
    private readonly ConcurrentDictionary<string, LiveConnection> _open = new(StringComparer.Ordinal);

    public void Add(LiveConnection connection) => _open[connection.ConnectionId] = connection;

    public void Remove(string connectionId) => _open.TryRemove(connectionId, out _);

    public LiveConnection? Find(string? connectionId) =>
        connectionId != null && _open.TryGetValue(connectionId, out var found) ? found : null;

    public IReadOnlyList<LiveConnection> All() => _open.Values.ToList();

    public int Count => _open.Count;
}

/// <summary>Group names: a person's own news, a tournament's, a match's.</summary>
public static class LiveGroups
{
    public static string User(int userId) => $"u:{userId}";
    public static string Tournament(int tournamentId) => $"t:{tournamentId}";
    public static string Match(string sport, int matchId) => $"m:{sport}:{matchId}";
}
