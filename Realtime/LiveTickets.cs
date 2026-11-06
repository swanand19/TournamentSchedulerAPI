using System.Collections.Concurrent;
using TournamentScheduler.Api.Auth;

namespace TournamentScheduler.Api.Realtime;

/// <summary>Who a redeemed ticket was given to: the person and the device's session.</summary>
public sealed record LiveTicketHolder(int UserId, Guid SessionId);

/// <summary>
/// One-minute, single-use tickets that open the live line. A browser can't put a header on a
/// WebSocket, so whatever proves who you are travels in the address (<c>?access_token=</c>) — where
/// server logs and proxies can see it. The session token must never go there; a ticket may, because
/// by the time anyone reads it, it has been used or has expired. Only its fingerprint is kept, in
/// memory (one server, like the gateway's replay guard).
/// </summary>
public sealed class LiveTickets(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>Enough for every device at once; a flood of requests can't grow it without bound.</summary>
    private const int MaxWaiting = 10_000;

    private readonly ConcurrentDictionary<string, (LiveTicketHolder Holder, DateTimeOffset ExpiresAt)> _waiting = new();

    public string Issue(int userId, Guid sessionId)
    {
        var now = clock.GetUtcNow();
        if (_waiting.Count >= MaxWaiting) DropExpired(now);

        var (token, hash) = SessionTokens.NewToken();
        _waiting[hash] = (new LiveTicketHolder(userId, sessionId), now + Lifetime);
        return token;
    }

    /// <summary>Who the ticket belongs to, or null when it's unknown, used or expired. Using it ends it.</summary>
    public LiveTicketHolder? Redeem(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        if (!_waiting.TryRemove(SessionTokens.Hash(token), out var ticket)) return null;
        return ticket.ExpiresAt > clock.GetUtcNow() ? ticket.Holder : null;
    }

    private void DropExpired(DateTimeOffset now)
    {
        foreach (var (hash, ticket) in _waiting)
            if (ticket.ExpiresAt <= now) _waiting.TryRemove(hash, out _);
    }
}
