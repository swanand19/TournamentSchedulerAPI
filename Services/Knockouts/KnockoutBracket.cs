using TournamentScheduler.Api.Models.Knockouts;

namespace TournamentScheduler.Api.Services.Knockouts;

/// <summary>A knockout match before it's saved: what a preset builds and previews.</summary>
public sealed record PlannedFixture(int Order, KnockoutRound Round, string Label, KnockoutSlot Home, KnockoutSlot Away);

/// <summary>A group a knockout side can come from, and how many teams are in it.</summary>
public sealed record KnockoutGroup(string Name, int Teams);

public sealed record KnockoutPreset(string Id, string Name, string Description, bool OffersThirdPlace, List<PlannedFixture> Fixtures);

/// <summary>
/// The knockout presets and the bracket they make — pure, so the tests can check every pairing.
///
/// A bracket is seeded the usual way: group winners first (A1, B1, C1 …), then runners-up, and seed
/// i meets seed N+1−i, so 1st plays the lowest qualifier and teams from the same group meet as late
/// as the draw allows (A1 v B2, B1 v A2). When the qualifiers aren't a power of two, the top seeds
/// get byes into the next round.
/// </summary>
public static class KnockoutBracket
{
    public const string Ipl = "ipl";
    public const string AllTeams = "all-teams";
    private const string TopPrefix = "top-";

    /// <summary>The largest bracket a preset builds (a round of 32).</summary>
    public const int MaxQualifiers = 32;

    /// <summary>
    /// The presets that fit: from groups, every "top k of each group" that the smallest group can
    /// supply, and IPL playoffs when there's a top four; with no groups, a knockout of every team.
    /// </summary>
    public static List<KnockoutPreset> Presets(IReadOnlyList<KnockoutGroup> groups, IReadOnlyList<int> teamIds)
    {
        var presets = new List<KnockoutPreset>();
        if (groups.Count == 0)
        {
            if (teamIds.Count >= 2 && teamIds.Count <= MaxQualifiers)
                presets.Add(Build(AllTeams, false, groups, teamIds, 1)!);
            return presets;
        }

        var smallest = groups.Min(g => g.Teams);
        for (var k = 1; k <= smallest; k++)
        {
            var total = k * groups.Count;
            if (total < 2) continue;
            if (total > MaxQualifiers) break;
            presets.Add(Build(TopPrefix + k, false, groups, teamIds, 1)!);
        }

        // IPL playoffs sit beside the semi-finals they're the alternative to.
        if (Build(Ipl, false, groups, teamIds, 1) is { } ipl)
        {
            var semis = presets.FindIndex(p => p.Id == TopPrefix + (4 / groups.Count));
            presets.Insert(semis >= 0 ? semis + 1 : presets.Count, ipl);
        }
        return presets;
    }

    /// <summary>The preset's matches, numbered from <paramref name="firstOrder"/>; null when it doesn't fit these groups.</summary>
    public static KnockoutPreset? Build(string presetId, bool thirdPlace, IReadOnlyList<KnockoutGroup> groups, IReadOnlyList<int> teamIds, int firstOrder)
    {
        var ordered = groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (presetId == Ipl)
        {
            List<KnockoutSlot>? seeds = ordered.Count switch
            {
                1 when ordered[0].Teams >= 4 => [Place(ordered[0], 1), Place(ordered[0], 2), Place(ordered[0], 3), Place(ordered[0], 4)],
                2 when ordered.All(g => g.Teams >= 2) => [Place(ordered[0], 1), Place(ordered[1], 1), Place(ordered[0], 2), Place(ordered[1], 2)],
                _ => null
            };
            if (seeds == null) return null;
            var o = firstOrder;
            var fixtures = new List<PlannedFixture>
            {
                new(o, KnockoutRound.Qualifier, "Qualifier 1", seeds[0], seeds[1]),
                new(o + 1, KnockoutRound.Eliminator, "Eliminator", seeds[2], seeds[3]),
                new(o + 2, KnockoutRound.Qualifier, "Qualifier 2", From(KnockoutSlotKind.LoserOf, o), From(KnockoutSlotKind.WinnerOf, o + 1)),
                new(o + 3, KnockoutRound.Final, "Final", From(KnockoutSlotKind.WinnerOf, o), From(KnockoutSlotKind.WinnerOf, o + 2)),
            };
            return new KnockoutPreset(Ipl, "IPL playoffs",
                "The top two get a second chance: Qualifier 1's winner goes to the final, its loser plays the Eliminator's winner in Qualifier 2.",
                false, fixtures);
        }

        if (presetId == AllTeams)
        {
            if (ordered.Count > 0 || teamIds.Count < 2 || teamIds.Count > MaxQualifiers) return null;
            var (fixtures, byes) = Bracket(teamIds.Select(id => new KnockoutSlot { Kind = KnockoutSlotKind.Team, TeamId = id }).ToList(), thirdPlace, firstOrder);
            return new KnockoutPreset(AllTeams, $"{FirstRoundName(teamIds.Count)}: all {teamIds.Count} teams",
                $"Every team, in the order drawn. {Matches(fixtures.Count)}{Byes(byes, "top seeds")}.", teamIds.Count >= 4, fixtures);
        }

        if (presetId.StartsWith(TopPrefix) && int.TryParse(presetId[TopPrefix.Length..], out var k) && k >= 1
            && ordered.Count > 0 && ordered.All(g => g.Teams >= k) && k * ordered.Count is >= 2 and <= MaxQualifiers)
        {
            var seeds = GroupSeeds(ordered, k);
            var (fixtures, byes) = Bracket(seeds, thirdPlace, firstOrder);
            var who = ordered.Count == 1
                ? $"top {k}"
                : k == 1 ? "group winners" : k == 2 ? "top 2 of each group" : $"top {k} of each group";
            return new KnockoutPreset(presetId, $"{FirstRoundName(seeds.Count)}: {who}",
                $"{Matches(fixtures.Count)}{Byes(byes, ordered.Count == 1 ? "top seeds" : "group winners")}.", seeds.Count >= 4, fixtures);
        }

        return null;
    }

    /// <summary>
    /// The bracket for these seeds (best first), round by round, each match numbered in order. The
    /// 3rd-place match, when asked for, goes just before the final.
    /// </summary>
    public static (List<PlannedFixture> Fixtures, int Byes) Bracket(List<KnockoutSlot> seeds, bool thirdPlace, int firstOrder)
    {
        var size = 1;
        while (size < seeds.Count) size *= 2;
        var byes = size - seeds.Count;

        List<KnockoutSlot?> current = SeedOrder(size).Select(s => s <= seeds.Count ? seeds[s - 1] : null).ToList();
        var fixtures = new List<PlannedFixture>();
        var order = firstOrder;
        List<int> semiOrders = [];

        while (current.Count > 1)
        {
            var round = RoundFor(current.Count);
            var pairs = Enumerable.Range(0, current.Count / 2).Select(i => (A: current[2 * i], B: current[2 * i + 1])).ToList();
            var real = pairs.Count(p => p.A != null && p.B != null);

            if (round == KnockoutRound.Final && thirdPlace && semiOrders.Count == 2)
                fixtures.Add(new PlannedFixture(order++, KnockoutRound.ThirdPlace, "3rd-place match",
                    From(KnockoutSlotKind.LoserOf, semiOrders[0]), From(KnockoutSlotKind.LoserOf, semiOrders[1])));

            var next = new List<KnockoutSlot?>();
            var n = 0;
            foreach (var (a, b) in pairs)
            {
                if (a == null || b == null)
                {
                    next.Add(a ?? b);   // a bye: straight through
                    continue;
                }
                n++;
                var label = RoundName(round, current.Count) + (real > 1 ? $" {n}" : "");
                fixtures.Add(new PlannedFixture(order, round, label, a, b));
                if (round == KnockoutRound.SemiFinal) semiOrders.Add(order);
                next.Add(From(KnockoutSlotKind.WinnerOf, order));
                order++;
            }
            current = next;
        }
        return (fixtures, byes);
    }

    /// <summary>
    /// The top <paramref name="k"/> of each group as seeds: winners first, then runners-up, and so on.
    /// Within each place after the first, the groups' order is the one (of a few simple ones: as
    /// listed, reversed, neighbours swapped, rotated by half) that keeps teams from the same group
    /// apart longest — so with four groups A1 and A2 land in opposite halves and can only meet in
    /// the final.
    /// </summary>
    public static List<KnockoutSlot> GroupSeeds(IReadOnlyList<KnockoutGroup> groups, int k)
    {
        var g = groups.Count;
        var size = 1;
        while (size < g * k) size *= 2;
        var leafOf = SeedOrder(size).Select((seed, leaf) => (seed, leaf)).ToDictionary(x => x.seed, x => x.leaf);

        List<int> identity = Enumerable.Range(0, g).ToList();
        var candidates = new List<List<int>>
        {
            identity,
            Enumerable.Reverse(identity).ToList(),
            identity.Select(i => i % 2 == 0 ? Math.Min(i + 1, g - 1) : i - 1).ToList(),
            identity.Select(i => (i + g / 2) % g).ToList()
        }.Where(c => c.Distinct().Count() == g).DistinctBy(c => string.Join(",", c)).ToList();

        // Search the combinations for the later places (kept small: four places at most).
        var places = Math.Min(k, 4);
        List<List<int>> best = Enumerable.Repeat(identity, k).ToList();
        var bestCost = double.MaxValue;
        foreach (var combo in Combinations(candidates, places - 1))
        {
            var orders = new List<List<int>> { identity };
            orders.AddRange(combo);
            while (orders.Count < k) orders.Add(identity);

            // Seed number → group, then the cost of every same-group pair meeting early.
            var groupOfSeed = new List<int>();
            foreach (var order in orders) groupOfSeed.AddRange(order);
            var cost = 0.0;
            for (var a = 0; a < groupOfSeed.Count; a++)
                for (var b = a + 1; b < groupOfSeed.Count; b++)
                    if (groupOfSeed[a] == groupOfSeed[b])
                        cost += Math.Pow(2, -MeetingRound(leafOf[a + 1], leafOf[b + 1]));
            if (cost < bestCost - 1e-9)
            {
                bestCost = cost;
                best = orders;
            }
        }

        var seeds = new List<KnockoutSlot>();
        for (var place = 1; place <= k; place++)
            seeds.AddRange(best[place - 1].Select(i => Place(groups[i], place)));
        return seeds;
    }

    /// <summary>The round (1 = first) in which two bracket positions can first meet.</summary>
    private static int MeetingRound(int leafA, int leafB)
    {
        var round = 0;
        for (var diff = leafA ^ leafB; diff > 0; diff >>= 1) round++;
        return round;
    }

    private static IEnumerable<List<List<int>>> Combinations(List<List<int>> options, int length)
    {
        if (length <= 0)
        {
            yield return [];
            yield break;
        }
        foreach (var head in options)
            foreach (var tail in Combinations(options, length - 1))
                yield return [head, .. tail];
    }

    /// <summary>Bracket positions for a draw of this size: 1 v 8, 4 v 5, 2 v 7, 3 v 6 — 1 and 2 can only meet in the final.</summary>
    public static List<int> SeedOrder(int size)
    {
        List<int> order = [1];
        while (order.Count < size)
        {
            var span = order.Count * 2 + 1;
            order = order.SelectMany(s => new[] { s, span - s }).ToList();
        }
        return order;
    }

    private static KnockoutRound RoundFor(int sides) => sides switch
    {
        2 => KnockoutRound.Final,
        4 => KnockoutRound.SemiFinal,
        8 => KnockoutRound.QuarterFinal,
        16 => KnockoutRound.RoundOf16,
        _ => KnockoutRound.Other
    };

    /// <summary>The round's usual name: "Semi-final", "Round of 32".</summary>
    public static string RoundName(KnockoutRound round, int sides = 0) => round switch
    {
        KnockoutRound.Final => "Final",
        KnockoutRound.SemiFinal => "Semi-final",
        KnockoutRound.QuarterFinal => "Quarter-final",
        KnockoutRound.RoundOf16 => "Round of 16",
        KnockoutRound.ThirdPlace => "3rd-place match",
        KnockoutRound.Qualifier => "Qualifier",
        KnockoutRound.Eliminator => "Eliminator",
        _ => sides > 0 ? $"Round of {sides}" : "Knockout match"
    };

    private static string FirstRoundName(int qualifiers) => qualifiers switch
    {
        2 => "Final",
        <= 4 => "Semi-finals",
        <= 8 => "Quarter-finals",
        <= 16 => "Round of 16",
        _ => "Round of 32"
    };

    private static string Matches(int n) => n == 1 ? "1 match" : $"{n} matches";

    private static string Byes(int byes, string who) =>
        byes == 0 ? "" : $", with {(byes == 1 ? "a bye" : $"{byes} byes")} for the {who}";

    private static KnockoutSlot Place(KnockoutGroup group, int place) =>
        new() { Kind = KnockoutSlotKind.GroupPlace, GroupName = group.Name, Place = place };

    private static KnockoutSlot From(KnockoutSlotKind kind, int order) => new() { Kind = kind, FromOrder = order };

    /// <summary>
    /// A slot in words: "Group A winner", "Group B runner-up", "Group A, 3rd", "Winner of Semi-final 1",
    /// or the team's name.
    /// </summary>
    public static string Describe(KnockoutSlot slot, Func<int, string?> teamName, Func<int, string?> labelOf) => slot.Kind switch
    {
        KnockoutSlotKind.Team => slot.TeamId is int id ? teamName(id) ?? "A team" : "A team",
        KnockoutSlotKind.GroupPlace => slot.Place switch
        {
            1 => $"Group {slot.GroupName} winner",
            2 => $"Group {slot.GroupName} runner-up",
            int p => $"Group {slot.GroupName}, {Ordinal(p)}",
            _ => $"Group {slot.GroupName}"
        },
        KnockoutSlotKind.WinnerOf => $"Winner of {(slot.FromOrder is int w ? labelOf(w) : null) ?? "an earlier match"}",
        KnockoutSlotKind.LoserOf => $"Loser of {(slot.FromOrder is int l ? labelOf(l) : null) ?? "an earlier match"}",
        _ => "To be decided"
    };

    public static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" }
    };
}
