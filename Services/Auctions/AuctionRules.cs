using System.Globalization;
using TournamentScheduler.Api.Models.Auctions;

namespace TournamentScheduler.Api.Services.Auctions;

/// <summary>
/// The bid ladder: how much each bid goes up by, by amount ("up to 1,000: +50 · up to 3,000: +100 ·
/// above: +250"). Bids never get typed, so every bid is a rung on this ladder above the base price.
/// </summary>
public sealed class BidLadder
{
    private readonly List<(int? UpTo, int Step)> _rungs;

    private BidLadder(List<(int? UpTo, int Step)> rungs) => _rungs = rungs;

    public static readonly BidLadder Standard = new([(1000, 50), (3000, 100), (null, 250)]);

    public IReadOnlyList<BidStepDto> Steps => _rungs.Select(r => new BidStepDto { UpTo = r.UpTo, Step = r.Step }).ToList();

    /// <summary>"1000:50,3000:100,0:250" (0 = above the last). Falls back to the standard ladder if it can't be read.</summary>
    public static BidLadder Parse(string? text)
    {
        var rungs = new List<(int?, int)>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':');
            if (bits.Length != 2
                || !int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var upTo)
                || !int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var step)
                || step <= 0)
                return Standard;
            rungs.Add((upTo <= 0 ? null : upTo, step));
        }
        return rungs.Count == 0 ? Standard : new BidLadder(rungs);
    }

    /// <summary>Why these steps can't be used, or null. Rising limits, positive steps, the last open-ended.</summary>
    public static string? Problem(IReadOnlyList<BidStepDto> steps)
    {
        if (steps.Count is 0 or > 6) return "Give between one and six bid steps.";
        if (steps.Any(s => s.Step <= 0 || s.Step > 100_000)) return "Each bid step must be more than 0.";
        if (steps[^1].UpTo != null) return "The last bid step must be for everything above the others.";
        int? last = null;
        foreach (var s in steps.Take(steps.Count - 1))
        {
            if (s.UpTo is not int upTo || upTo <= 0) return "Each bid step but the last needs an amount it goes up to.";
            if (last is int before && upTo <= before) return "Bid steps must go up in order: up to 1,000, then up to 3,000, and so on.";
            last = upTo;
        }
        return null;
    }

    public static string Format(IReadOnlyList<BidStepDto> steps) =>
        string.Join(",", steps.Select(s => $"{(s.UpTo ?? 0).ToString(CultureInfo.InvariantCulture)}:{s.Step.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>How much a bid of <paramref name="amount"/> is raised by.</summary>
    public int StepAt(int amount)
    {
        foreach (var (upTo, step) in _rungs)
            if (upTo is not int limit || amount < limit) return step;
        return _rungs[^1].Step;
    }

    /// <summary>The bid after <paramref name="amount"/>.</summary>
    public int Next(int amount) => amount + StepAt(amount);

    /// <summary>The bid <paramref name="steps"/> rungs above <paramref name="amount"/>.</summary>
    public int Up(int amount, int steps)
    {
        for (var i = 0; i < steps; i++) amount = Next(amount);
        return amount;
    }

    /// <summary>Whether <paramref name="amount"/> is a rung at or above <paramref name="from"/> (bids land only on rungs).</summary>
    public bool IsRung(int from, int amount)
    {
        var at = from;
        for (var i = 0; i < 1000 && at <= amount; i++)
        {
            if (at == amount) return true;
            at = Next(at);
        }
        return false;
    }
}

/// <summary>
/// The arithmetic that keeps an auction fair (plan 4.4), on numbers only so it can be tested alone.
/// </summary>
public static class AuctionMath
{
    /// <summary>
    /// The most a team may bid and still be able to complete its minimum squad: what's left of its
    /// purse, less the cheapest base price kept back for each other place it still has to fill.
    /// </summary>
    public static int MaxBid(int purseLeft, int needs, int cheapestBase)
    {
        var keepBack = Math.Max(0, needs - 1) * Math.Max(0, cheapestBase);
        return Math.Max(0, purseLeft - keepBack);
    }

    /// <summary>"38 players for 4 teams: two get 10, two get 9" — what a pool's size means before the start.</summary>
    public static (string? Problem, string? Warning) PoolCheck(int poolPlayers, IReadOnlyList<(string Team, int Kept)> teams, int minSquad, int maxSquad,
        IReadOnlyList<(string Tier, int Players, int? MaxPerTeam)> tierLimits)
    {
        if (teams.Count < 2) return ("Add at least two teams before the auction.", null);
        if (poolPlayers == 0) return ("Add players to the pool first.", null);

        var needed = teams.Sum(t => Math.Max(0, minSquad - t.Kept));
        var room = teams.Sum(t => Math.Max(0, maxSquad - t.Kept));
        if (room == 0) return ("Every team already has its maximum squad. Raise the maximum squad.", null);
        if (poolPlayers < needed)
            return ($"{Plural(poolPlayers, "player")} can't give {Plural(teams.Count, "team")} their minimum squad of {minSquad}: they need {needed}. Lower the minimum squad or add players.", null);

        var warnings = new List<string>();
        if (poolPlayers > room)
            warnings.Add($"At least {Plural(poolPlayers - room, "player")} won't get a team. Raise the maximum squad, or remove players.");
        foreach (var (tier, players, max) in tierLimits)
        {
            if (max is not int limit) continue;
            var tierRoom = limit * teams.Count;
            if (players > tierRoom)
                warnings.Add($"{tier}: {Plural(players, "player")}, room for {tierRoom} at {limit} a team. At least {players - tierRoom} will go unsold, or to a later round.");
        }
        return (null, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    public static string Plural(int n, string one, string? many = null) =>
        $"{n.ToString("N0", CultureInfo.InvariantCulture)} {(n == 1 ? one : many ?? one + "s")}";

    public static string Amount(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
}
