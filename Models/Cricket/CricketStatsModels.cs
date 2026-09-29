namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// Everything the cricket stats page shows for a tournament: the points table with net run rate,
/// the batting, bowling and fielding leaderboards, the fantasy-points MVP race and team records.
/// All of it is folded from the ball-by-ball ledger when asked for; none of it is stored.
/// </summary>
public class CricketTournamentStats
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;

    /// <summary>What the numbers cover, in a sentence, for the page header.</summary>
    public string Basis { get; set; } = string.Empty;

    public CricketStatsSummary Summary { get; set; } = new();
    public List<CricketPointsTableGroup> PointsTable { get; set; } = new();

    public List<CricketBoard> BattingBoards { get; set; } = new();
    public List<CricketBoard> BowlingBoards { get; set; } = new();
    public List<CricketBoard> FieldingBoards { get; set; } = new();

    public CricketBoard Mvp { get; set; } = new();
    public List<CricketPlayerOfMatch> PlayersOfMatch { get; set; } = new();
    public List<CricketPointsRule> PointsSystem { get; set; } = new();

    public CricketTeamRecords Records { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public class CricketStatsSummary
{
    public int TotalMatches { get; set; }
    public int MatchesCompleted { get; set; }
    public int MatchesInProgress { get; set; }
    public int TotalRuns { get; set; }
    public int TotalWickets { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public int Fifties { get; set; }
    public int Hundreds { get; set; }
    public int Extras { get; set; }
    public string? HighestTeamTotal { get; set; }
    public string? HighestScore { get; set; }
    public string? BestBowling { get; set; }
    public string? TopRunScorer { get; set; }
    public string? TopWicketTaker { get; set; }
    public string? Mvp { get; set; }
}

public class CricketPointsTableGroup
{
    public string GroupName { get; set; } = string.Empty;
    public List<CricketStandingRow> Rows { get; set; } = new();
}

public class CricketStandingRow
{
    public int Rank { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Drawn { get; set; }
    public int NoResult { get; set; }
    public int Points { get; set; }
    public double NetRunRate { get; set; }
    public int RunsFor { get; set; }
    public string OversFor { get; set; } = "0.0";
    public int RunsAgainst { get; set; }
    public string OversAgainst { get; set; } = "0.0";
    /// <summary>Latest results, oldest first: W, L, T, D or NR.</summary>
    public List<string> Form { get; set; } = new();
}

/// <summary>One leaderboard: its rows are ranked on <see cref="ValueLabel"/>.</summary>
public class CricketBoard
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ValueLabel { get; set; } = string.Empty;
    /// <summary>Who qualifies, when a minimum applies ("Minimum 20 balls faced").</summary>
    public string? Qualification { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<CricketBoardRow> Rows { get; set; } = new();
}

public class CricketBoardRow
{
    public int Rank { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    /// <summary>Formatted for display: "112*", "5/23", "45.50".</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>The supporting columns, keyed by <see cref="CricketBoard.Columns"/>.</summary>
    public Dictionary<string, string> Detail { get; set; } = new();
    /// <summary>Context for a single-innings record, such as the opposition.</summary>
    public string? Note { get; set; }
}

public class CricketPlayerOfMatch
{
    public int MatchId { get; set; }
    public int MatchNumber { get; set; }
    public string Fixture { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class CricketPointsRule
{
    public string Category { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public int Points { get; set; }
}

public class CricketTeamRecords
{
    public List<CricketTeamRecord> HighestTotals { get; set; } = new();
    public List<CricketTeamRecord> LowestTotals { get; set; } = new();
    public List<CricketTeamRecord> BiggestWins { get; set; } = new();
    public List<CricketTeamRecord> HighestPartnerships { get; set; } = new();
}

public class CricketTeamRecord
{
    public string TeamName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
