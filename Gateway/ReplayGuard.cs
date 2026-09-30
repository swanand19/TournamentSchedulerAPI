using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Gateway;

/// <summary>
/// Refuses a request whose requestUUID has already been used. A captured request cannot be read,
/// but without this it could be sent again — recording the same goal twice. The timestamp check
/// already rejects anything older than the clock-skew window, so ids only need remembering that long.
///
/// In memory, which is right for the single API instance this runs as. Several instances behind a
/// load balancer would need a shared store instead (Redis, or a table).
/// </summary>
public sealed class ReplayGuard(IOptions<GatewayOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _seen = new();
    private readonly TimeSpan _window = TimeSpan.FromSeconds(options.Value.MaxClockSkewSeconds * 2);
    private int _sincePurge;

    /// <summary>True the first time an id is seen; false for a replay.</summary>
    public bool TryAccept(Guid requestId)
    {
        var now = clock.GetUtcNow();
        if (Interlocked.Increment(ref _sincePurge) >= 500)
        {
            Interlocked.Exchange(ref _sincePurge, 0);
            foreach (var (id, seenAt) in _seen)
                if (now - seenAt > _window) _seen.TryRemove(id, out _);
        }

        return _seen.TryAdd(requestId, now);
    }
}
