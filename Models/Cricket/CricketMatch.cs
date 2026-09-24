namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// One cricket fixture. The identity block deliberately mirrors <see cref="Match"/> field for field
/// so a tournament's fixtures materialise and list the same way whichever sport it is played under;
/// everything below <see cref="Rules"/> is where the two diverge.
/// </summary>
public class CricketMatch
{
    public int Id { get; set; }

    public int TournamentId { get; set; }
    public Tournament? Tournament { get; set; }

    public int SavedFixtureId { get; set; }
    public SavedFixture? SavedFixture { get; set; }

    public string GroupName { get; set; } = string.Empty;
    public int MatchNumber { get; set; }

    public int? HomeTeamId { get; set; }
    public Team? HomeTeam { get; set; }
    public int? AwayTeamId { get; set; }
    public Team? AwayTeam { get; set; }

    /// <summary>
    /// Denormalised so a fixture still reads correctly if a team is renamed afterwards — the same
    /// reason <see cref="Match"/> carries them.
    /// </summary>
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;

    public CricketMatchStatus Status { get; set; } = CricketMatchStatus.NotStarted;

    /// <summary>Set when the scorer sets the match up; the defaults describe a standard T20.</summary>
    public CricketMatchRules Rules { get; set; } = new();

    // --- Toss ---------------------------------------------------------

    public int? TossWinnerTeamId { get; set; }
    public TossDecision? TossDecision { get; set; }

    // --- Live state ---------------------------------------------------

    /// <summary>1-based; 1 and 2 for limited overs, up to 4 for the multi-innings formats.</summary>
    public int CurrentInningsNumber { get; set; }

    /// <summary>Whether the side with the lead made the other follow on, in a multi-innings match.</summary>
    public bool FollowOnEnforced { get; set; }

    public List<CricketMatchPlayer> Squad { get; set; } = new();
    public List<CricketInnings> Innings { get; set; } = new();
    public List<CricketMatchEvent> Events { get; set; } = new();

    // --- Result -------------------------------------------------------

    public int? WinnerTeamId { get; set; }
    public bool IsTie { get; set; }

    /// <summary>Abandoned without a result — worth a point to each side in the table.</summary>
    public bool IsNoResult { get; set; }

    /// <summary>Time ran out in a timed match. A real result, not a failure to finish.</summary>
    public bool IsDraw { get; set; }

    public bool WonByDls { get; set; }
    public int? ForfeitWinnerTeamId { get; set; }

    /// <summary>e.g. "Mumbai won by 23 runs", "Chennai won by 4 wickets (7 balls remaining)".</summary>
    public string? ResultSummary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Total innings the match can hold, before any super over.</summary>
    public int MaxInnings => Rules.InningsPerSide * 2;

    /// <summary>The toss has been made and both XIs are in. Nothing can be scored before this.</summary>
    public bool IsSetUp => TossWinnerTeamId.HasValue && TossDecision.HasValue;

    /// <summary>The side that batted first, which the toss decides.</summary>
    public int? TeamBattingFirstId =>
        TossWinnerTeamId is null || TossDecision is null
            ? null
            : TossDecision == Cricket.TossDecision.Bat
                ? TossWinnerTeamId
                : TossWinnerTeamId == HomeTeamId ? AwayTeamId : HomeTeamId;
}
