using System.Collections.Concurrent;

namespace TournamentScheduler.Api.Services.Access;

/// <summary>
/// Lookups of an email or number before adding a player, per owner. A lookup answers whether an
/// account uses it, so without a limit it could test a list of addresses for accounts; a squad is
/// built well inside <see cref="MaxLookups"/> in <see cref="Window"/>. In memory, like
/// <see cref="JoinCodeAttempts"/> (one server).
/// </summary>
public sealed class PlayerLookups(TimeProvider clock)
{
    public const int MaxLookups = 30;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<int, List<DateTime>> _lookups = new();

    /// <summary>Counts this lookup; answers how long to wait instead when the person has had their fill.</summary>
    public TimeSpan? TryLookup(int userId)
    {
        var list = _lookups.GetOrAdd(userId, _ => []);
        var now = clock.GetUtcNow().UtcDateTime;
        lock (list)
        {
            list.RemoveAll(t => now - t >= Window);
            if (list.Count >= MaxLookups) return list[0] + Window - now;
            list.Add(now);
            return null;
        }
    }
}
