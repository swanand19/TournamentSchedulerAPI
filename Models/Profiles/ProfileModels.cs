namespace TournamentScheduler.Api.Models.Profiles;

// A person's profile and their career in each sport. Career figures count completed matches only,
// across every tournament where the person's account is linked to a squad place. Stats travel as
// label + display value so both apps show the same wording, and a new sport needs no app change to
// list its figures.

/// <summary>The base profile: who the person is and one card per sport.</summary>
public class ProfileDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsMe { get; set; }

    /// <summary>When the account was created (UTC).</summary>
    public DateTime MemberSince { get; set; }

    /// <summary>Only on your own profile; null for anyone else's.</summary>
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Tournaments this person owns or scores — a count only, for the header.</summary>
    public int TournamentsRun { get; set; }

    /// <summary>Every sport the app supports, played or not, in a fixed order.</summary>
    public List<SportCardDto> Sports { get; set; } = new();
}

public class SportCardDto
{
    public string Sport { get; set; } = string.Empty;
    public int Matches { get; set; }
    public int Tournaments { get; set; }

    /// <summary>Two or three headline figures ("Runs 312", "Wickets 9"); empty until a match is played.</summary>
    public List<StatDto> Headline { get; set; } = new();
}

/// <summary>One figure. <see cref="Sub"/> is a short supporting line ("2 not out", "of 4 taken").</summary>
public class StatDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Sub { get; set; }
}

/// <summary>A named set of figures — football has one ("Overview"); cricket has batting, bowling and fielding.</summary>
public class StatGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<StatDto> Stats { get; set; } = new();
}

/// <summary>A sport profile: the Stats, Teams, Tournaments and Honours tabs.</summary>
public class CareerDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsMe { get; set; }
    public string Sport { get; set; } = string.Empty;

    /// <summary>What the figures cover, in a sentence.</summary>
    public string Basis { get; set; } = string.Empty;

    public int Matches { get; set; }
    public List<StatGroupDto> StatGroups { get; set; } = new();
    public List<CareerTeamDto> Teams { get; set; } = new();
    public List<CareerTournamentDto> Tournaments { get; set; } = new();

    /// <summary>Totals shown above the honours list ("Player of the match 3", "MVP points 482").</summary>
    public List<StatDto> HonourStats { get; set; } = new();
    public List<HonourDto> Honours { get; set; } = new();
}

public class CareerTeamDto
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public string TournamentStatus { get; set; } = string.Empty;
    public int Matches { get; set; }
    public bool IsCaptain { get; set; }

    /// <summary>The viewer may open this tournament (they own, score or play in it), so the row is a link.</summary>
    public bool CanOpen { get; set; }
}

public class CareerTournamentDto
{
    public int TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Matches { get; set; }

    /// <summary>The viewer may open this tournament, so the row is a link.</summary>
    public bool CanOpen { get; set; }

    /// <summary>The person's key figures in this tournament.</summary>
    public List<StatDto> Highlights { get; set; } = new();
}

public class HonourDto
{
    /// <summary>PlayerOfMatch, TournamentMvp, TopScorer, MostAssists.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>The figures behind it ("54* (31) · 2/18", "7 goals").</summary>
    public string Detail { get; set; } = string.Empty;

    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public int? MatchId { get; set; }
    /// <summary>"Match 7 · Rovers v United" for a match award.</summary>
    public string? Context { get; set; }
    public DateTime? Date { get; set; }
}
