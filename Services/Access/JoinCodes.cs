using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TournamentScheduler.Api.Services.Access;

/// <summary>
/// Team join codes: six characters from an alphabet without look-alikes (no 0/O, no 1/I), so a code
/// read out across a ground is typed right. 32 symbols, about a billion codes.
/// </summary>
public static class JoinCodes
{
    public const int Length = 6;
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New()
    {
        Span<char> code = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(code);
    }

    /// <summary>Upper-case without spaces or dashes ("abc-23x" → "ABC23X"), or null if it can't be a code.</summary>
    public static string? Normalise(string? typed)
    {
        var code = new string((typed ?? "").Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray()).ToUpperInvariant();
        return code.Length == Length && code.All(Alphabet.Contains) ? code : null;
    }
}

/// <summary>
/// Wrong join codes per person, so nobody can walk the code space: after <see cref="MaxMisses"/>
/// misses in <see cref="Window"/> they wait. In memory, like the gateway's replay cache (one server).
/// </summary>
public sealed class JoinCodeAttempts(TimeProvider clock)
{
    public const int MaxMisses = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<int, List<DateTime>> _misses = new();

    /// <summary>How long until they may try again; null if they may now.</summary>
    public TimeSpan? WaitFor(int userId)
    {
        if (!_misses.TryGetValue(userId, out var list)) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        lock (list)
        {
            list.RemoveAll(t => now - t >= Window);
            return list.Count >= MaxMisses ? list[0] + Window - now : null;
        }
    }

    public void Missed(int userId)
    {
        var list = _misses.GetOrAdd(userId, _ => []);
        lock (list) list.Add(clock.GetUtcNow().UtcDateTime);
    }
}
