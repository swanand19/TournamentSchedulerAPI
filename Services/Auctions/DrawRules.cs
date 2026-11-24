namespace TournamentScheduler.Api.Services.Auctions;

/// <summary>
/// The draw's dealing rule (plan 9.2, agreed 2026-11-17 as E-2), pure so it can be tested on its own.
/// Which player is always random; which team is decided so the teams end within one player of each
/// other: the next team round the table, starting after the one that got the last player, among the
/// teams with the fewest players. A team that starts with more (a captain plus someone added by hand)
/// waits until the others catch up. With every team starting equal it is plain dealing round the table,
/// carried on across tiers (plan 3.1).
/// </summary>
public static class DrawRules
{
    /// <summary>The team that gets the next player.</summary>
    public static int NextTeam(IReadOnlyList<int> order, IReadOnlyDictionary<int, int> players, int? lastTeamId)
    {
        if (order.Count == 0) throw new InvalidOperationException("A draw needs teams.");
        var start = lastTeamId is int last && order.Contains(last) ? (IndexOf(order, last) + 1) % order.Count : 0;
        var fewest = order.Min(t => players.GetValueOrDefault(t));
        for (var i = 0; i < order.Count; i++)
        {
            var team = order[(start + i) % order.Count];
            if (players.GetValueOrDefault(team) == fewest) return team;
        }
        return order[start];
    }

    /// <summary>How many players each team ends with after <paramref name="count"/> more are dealt.</summary>
    public static Dictionary<int, int> Deal(IReadOnlyList<int> order, IReadOnlyDictionary<int, int> players, int? lastTeamId, int count)
    {
        var now = order.ToDictionary(t => t, t => players.GetValueOrDefault(t));
        var last = lastTeamId;
        for (var i = 0; i < count; i++)
        {
            var team = NextTeam(order, now, last);
            now[team]++;
            last = team;
        }
        return now;
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }

    /// <summary>"Every team ends with 7." / "Two teams end with 10, two with 9." for the totals a deal gives.</summary>
    public static string Ending(IReadOnlyCollection<int> totals)
    {
        if (totals.Count == 0) return "";
        var groups = totals.GroupBy(t => t).OrderByDescending(g => g.Key).ToList();
        if (groups.Count == 1) return $"Every team ends with {groups[0].Key}.";
        var parts = groups.Select((g, i) => i == 0
            ? $"{Capital(Words(g.Count()))} {(g.Count() == 1 ? "team ends" : "teams end")} with {g.Key}"
            : $"{Words(g.Count())} with {g.Key}");
        return string.Join(", ", parts) + ".";
    }

    public static string Words(int n) => n switch
    {
        1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight", 9 => "nine", 10 => "ten",
        _ => n.ToString()
    };

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
