namespace TournamentScheduler.Api.Models.Watch;

// What someone watching a match sees (Round 2): the match's sides, and what happened in plain
// words. The same shapes will serve the public live page (6E), so nothing here is private — no
// emails, phone numbers, join codes or scoring controls.

/// <summary>Where a match is, as a watcher cares: before the first ball or kick, under way, or over.</summary>
public enum WatchPhase
{
    Preview = 0,
    Live = 1,
    Finished = 2
}

/// <summary>The match as a watcher sees it: the header, and both sides (squads before, line-ups after).</summary>
public class WatchMatchDto
{
    public int MatchId { get; set; }
    public Sport Sport { get; set; }
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int MatchNumber { get; set; }
    public WatchPhase Phase { get; set; }

    /// <summary>"T20 · 20 overs a side", "2 halves of 20 minutes · 7 a side"; null until the match is set up.</summary>
    public string? FormatLine { get; set; }

    /// <summary>"Hilltop Strikers won the toss and chose to bowl." Cricket only, once the toss is in.</summary>
    public string? TossLine { get; set; }

    public string? ResultLine { get; set; }

    public WatchSideDto Home { get; set; } = new();
    public WatchSideDto Away { get; set; } = new();
}

public class WatchSideDto
{
    public int? TeamId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"Squad", "Playing XI" or "Starting line-up".</summary>
    public string Heading { get; set; } = string.Empty;

    public List<WatchPlayerDto> Players { get; set; } = new();

    /// <summary>Football's bench, cricket's twelfth men; empty when there is none.</summary>
    public List<WatchPlayerDto> Bench { get; set; } = new();
}

public class WatchPlayerDto
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? JerseyNumber { get; set; }

    /// <summary>A football position or a cricket role ("Batter", "Wicketkeeper batter").</summary>
    public string? Role { get; set; }

    public bool IsCaptain { get; set; }
    public bool IsWicketKeeper { get; set; }
    public int? BattingOrder { get; set; }

    /// <summary>Football: goals, cards and substitutions against this player's name, in match order.</summary>
    public List<WatchMarkDto> Marks { get; set; } = new();
}

public enum WatchMarkKind
{
    Goal = 0,
    YellowCard = 1,
    RedCard = 2,
    SubbedOn = 3,
    SubbedOff = 4
}

public class WatchMarkDto
{
    public WatchMarkKind Kind { get; set; }

    /// <summary>"55'", "90+2'".</summary>
    public string Minute { get; set; } = string.Empty;
}

/// <summary>
/// One line of match activity. The apps draw by <see cref="Kind"/> and show the words as given, so
/// both apps (and the public page later) say exactly the same thing.
/// </summary>
public enum ActivityKind
{
    // Cricket deliveries
    Dot = 0,
    Run = 1,
    Extra = 2,
    Four = 3,
    Six = 4,
    Wicket = 5,

    // Cricket moments
    Milestone = 6,
    OverEnd = 7,
    Innings = 8,
    Toss = 9,

    // Football
    Goal = 10,
    YellowCard = 11,
    SecondYellow = 12,
    RedCard = 13,
    Substitution = 14,
    Period = 15,
    Clock = 16,
    Penalty = 17,

    // Both
    Result = 18,

    /// <summary>A correction, overs lost to rain, a batter retiring hurt: worth a line, no colour.</summary>
    Note = 19
}

public class ActivityItemDto
{
    /// <summary>Counts up through the match; the paging cursor (<c>before</c>) and a stable key.</summary>
    public int Id { get; set; }

    public ActivityKind Kind { get; set; }

    /// <summary>Where in the match: "13.4" (over.ball) or "67'". Null for section lines.</summary>
    public string? Marker { get; set; }

    /// <summary>The short badge: the ball as a scorer writes it ("4", "W", "wd") or the score after a goal ("2–1").</summary>
    public string? Chip { get; set; }

    public string Text { get; set; } = string.Empty;
    public string? Detail { get; set; }

    public int? TeamId { get; set; }
    public int? InningsNumber { get; set; }
}

public class ActivityPageDto
{
    /// <summary>Newest first.</summary>
    public List<ActivityItemDto> Items { get; set; } = new();

    public bool HasMore { get; set; }
}
