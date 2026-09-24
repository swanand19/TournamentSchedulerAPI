namespace TournamentScheduler.Api.Models;

/// <summary>One row of a player leaderboard. Every board carries the player's team, as asked for.</summary>
public class PlayerStatRow
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int? JerseyNumber { get; set; }
    public string? Position { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;

    /// <summary>The number this board ranks by.</summary>
    public int Value { get; set; }

    /// <summary>Matches the player actually featured in — context for every board.</summary>
    public int Appearances { get; set; }
    public int MinutesPlayed { get; set; }

    /// <summary>Value per appearance, for boards where a rate is meaningful (goals, assists).</summary>
    public double? PerMatch { get; set; }

    /// <summary>Extra figures a particular board wants to show, e.g. straight vs second-yellow reds.</summary>
    public Dictionary<string, int> Detail { get; set; } = new();

    public int Rank { get; set; }
}

public class TeamStandingRow
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

    public int Played { get; set; }
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDifference => GoalsFor - GoalsAgainst;
    public int Points { get; set; }

    /// <summary>Clean sheets kept — matches completed without conceding.</summary>
    public int CleanSheets { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }

    /// <summary>Shootouts won and lost, for matches that could not be drawn.</summary>
    public int ShootoutsWon { get; set; }
    public int ShootoutsLost { get; set; }

    public int Rank { get; set; }
}

/// <summary>A named leaderboard, ready to render as a tab.</summary>
public class StatBoard
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>What the ranked number means, e.g. "Goals".</summary>
    public string ValueLabel { get; set; } = string.Empty;

    /// <summary>Rule notes shown with the board, e.g. that shootout goals are excluded.</summary>
    public string? Note { get; set; }

    /// <summary>Extra columns present in <see cref="PlayerStatRow.Detail"/>, in display order.</summary>
    public List<string> DetailColumns { get; set; } = new();

    public bool ShowPerMatch { get; set; }
    public List<PlayerStatRow> Rows { get; set; } = new();
}

public class TournamentSummary
{
    public int TotalMatches { get; set; }
    public int MatchesCompleted { get; set; }
    public int MatchesInProgress { get; set; }
    public int MatchesNotStarted { get; set; }

    public int TotalGoals { get; set; }
    public double GoalsPerMatch { get; set; }
    public int TotalAssists { get; set; }
    public int TotalYellowCards { get; set; }
    public int TotalRedCards { get; set; }
    public int TotalSubstitutions { get; set; }
    public int CleanSheets { get; set; }
    public int MatchesDrawn { get; set; }
    public int ShootoutsPlayed { get; set; }
    public int MatchesAbandoned { get; set; }
    public int PlayersUsed { get; set; }
    public int TeamsInvolved { get; set; }

    /// <summary>Highest-scoring completed match, described for display.</summary>
    public string? HighestScoringMatch { get; set; }

    /// <summary>Biggest winning margin in a completed match.</summary>
    public string? BiggestWin { get; set; }
}

public class TournamentStats
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;

    /// <summary>
    /// Stats are built from completed matches only, so every board agrees with every other and
    /// nothing shifts under the reader while a match is still being played.
    /// </summary>
    public string Basis { get; set; } = string.Empty;

    public TournamentSummary Summary { get; set; } = new();
    public List<StatBoard> PlayerBoards { get; set; } = new();
    public List<TeamStandingRow> Standings { get; set; } = new();

    /// <summary>Anything the reader should know about how the numbers were derived.</summary>
    public List<string> Notes { get; set; } = new();
}
