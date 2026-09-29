namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// The honours from one finished match, all decided by the same fantasy points as the MVP race.
/// Every award is null until the match is completed, and best fielder stays null when nobody
/// took a catch, a stumping or a run-out.
/// </summary>
public class CricketMatchAwardsDto
{
    public int MatchId { get; set; }

    /// <summary>The most points on the winning side; on a tie or no result, the most overall.</summary>
    public CricketAward? PlayerOfMatch { get; set; }

    public CricketAward? BestBatter { get; set; }
    public CricketAward? BestBowler { get; set; }
    public CricketAward? BestFielder { get; set; }
}

public class CricketAward
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;

    /// <summary>The points the award was decided on: the total for player of the match, otherwise
    /// the batting, bowling or fielding share.</summary>
    public int Points { get; set; }

    /// <summary>"45* (30) · 3/18" — the figures behind it.</summary>
    public string Summary { get; set; } = string.Empty;
}
