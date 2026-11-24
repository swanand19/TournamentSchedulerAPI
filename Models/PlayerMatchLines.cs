namespace TournamentScheduler.Api.Models;

/// <summary>
/// One player's own lines in a tournament, match by match: what unfolds under their row on a stats
/// leaderboard (2026-11-20). Cricket fills <see cref="Batting"/> ("45* (32)") and <see cref="Bowling"/>
/// ("4-0-28-2"); football fills <see cref="Goals"/> and <see cref="Assists"/> (the minute). Loaded on
/// demand, one player at a time, so the stats pages stay the size they are.
/// </summary>
public class PlayerMatchLines
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public List<PlayerMatchLine> Batting { get; set; } = new();
    public List<PlayerMatchLine> Bowling { get; set; } = new();
    public List<PlayerMatchLine> Goals { get; set; } = new();
    public List<PlayerMatchLine> Assists { get; set; } = new();
}

public class PlayerMatchLine
{
    public int MatchId { get; set; }
    public int MatchNumber { get; set; }
    public string Opponent { get; set; } = string.Empty;
    /// <summary>The figure itself, formatted: "45* (32)", "4-0-28-2", "67'".</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>A short addition when there is one: "6 fours · 2 sixes", "from Arjun", "for Ravi".</summary>
    public string? Detail { get; set; }
    /// <summary>The match is still being played, so the line can still change.</summary>
    public bool InPlay { get; set; }
}
