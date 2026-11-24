using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models.Leagues;

// Leagues and seasons (item 7, 2026-11-24; canvas "Leagues and seasons", the user's decisions in the memory note): a league
// is the tournament an organiser runs again and again, each time a season. Every season stays an ordinary tournament
// (Tournaments.LeagueId); seasons are numbered in date order, worked out on every read, never stored, so ticking an
// earlier tournament in or taking one out renumbers by itself. Leagues are public, like every tournament.

/// <summary>
/// A league: a name, a short name shown on its tournaments ("CPL"), one sport, an area and a logo, and its organisers.
/// Anyone signed in may look at one, and anyone with its link without signing in; only its organisers change it.
/// </summary>
public class League : AuditedEntity
{
    public const int NameMax = 60;
    public const int ShortNameMax = 8;
    public const int AreaMax = 60;

    public int LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public string? Area { get; set; }

    /// <summary>The logo's picture id (HLD P-26), like a tournament's.</summary>
    [JsonIgnore]
    public string? LogoId { get; set; }

    /// <summary>The code in the league's public link (`/leagues/<code>` on the website), made with the league and kept for good.</summary>
    [JsonIgnore]
    public string PublicCode { get; set; } = string.Empty;

    [JsonIgnore]
    public List<LeagueOrganiser> Organisers { get; set; } = [];
}

/// <summary>
/// Someone who runs a league: starts its seasons, ticks earlier tournaments in, edits it. As many as the league likes (the
/// user's choice, 2026-11-24). A season's own owners and scorers are its tournament's, as ever.
/// </summary>
public class LeagueOrganiser : AuditedEntity
{
    public int LeagueOrganiserId { get; set; }
    public int LeagueId { get; set; }

    [JsonIgnore]
    public League? League { get; set; }

    public int UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }
}

// --- Requests ------------------------------------------------------------------------------------------------------------

public class CreateLeagueRequest
{
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public Sport Sport { get; set; }
    public string? Area { get; set; }

    /// <summary>Tournaments already run on KickCricket that become its first seasons (the organiser's own, this sport, in no other league).</summary>
    public List<int>? TournamentIds { get; set; }
}

public class UpdateLeagueRequest
{
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public string? Area { get; set; }
}

public class AddLeagueOrganiserRequest
{
    /// <summary>An email or a mobile number of someone with an account.</summary>
    public string? Contact { get; set; }
}

public class LeagueSeasonRequest
{
    public int TournamentId { get; set; }
}

public class BringBackTeamsRequest
{
    public List<int>? TeamIds { get; set; }
}

// --- Answers -------------------------------------------------------------------------------------------------------------

/// <summary>A row of "Part of a league?": the leagues the caller runs.</summary>
public class LeagueListItemDto
{
    public int LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public string? LogoUrl { get; set; }
    public int Seasons { get; set; }

    /// <summary>"3 seasons · last: CPL 3, Falcons won".</summary>
    public string Line { get; set; } = string.Empty;
}

/// <summary>A tournament the organiser could tick in as an earlier season.</summary>
public class LeagueCandidateDto
{
    public int TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TournamentStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }

    /// <summary>"Mar 2025 · Wakad Warriors won".</summary>
    public string Line { get; set; } = string.Empty;
}

public class LeagueDto
{
    public int LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public string? Area { get; set; }
    public string? LogoUrl { get; set; }

    /// <summary>The organisers' names, for "Run by …".</summary>
    public List<string> RunBy { get; set; } = [];

    public List<LeagueOrganiserDto> Organisers { get; set; } = [];
    public LeagueTotalsDto Totals { get; set; } = new();
    public List<LeagueSeasonDto> Seasons { get; set; } = [];

    /// <summary>The number "Start Season N" gives the next one.</summary>
    public int NextSeason { get; set; }

    /// <summary>The caller runs it: settings, Start Season N, ticking tournaments in.</summary>
    public bool CanManage { get; set; }

    public Public.PublicShareDto? Share { get; set; }
}

public class LeagueOrganiserDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsMe { get; set; }
    public bool CanRemove { get; set; }
}

public class LeagueTotalsDto
{
    public int Seasons { get; set; }
    public int Teams { get; set; }
    public int Matches { get; set; }
    public int Champions { get; set; }
}

public class LeagueSeasonDto
{
    public int Number { get; set; }
    public int TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TournamentStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? LogoUrl { get; set; }

    /// <summary>"live" (being played), "upcoming" or "past".</summary>
    public string Section { get; set; } = "past";

    /// <summary>The champions' name once it has them, else the tournament's.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>"CPL 2 · beat Falcons in the final", "8 teams · 11 of 31 played", "Starts 12 Dec · 6 teams".</summary>
    public string Line { get; set; } = string.Empty;

    public string? ChampionName { get; set; }
    public int Teams { get; set; }

    /// <summary>The caller is already in it (owns, scores, plays, follows…): it opens straight away. Otherwise the apps follow it first.</summary>
    public bool CanOpen { get; set; }

    /// <summary>Its public code, so the apps can follow it and the website can link to its live page.</summary>
    public string? LiveCode { get; set; }

    /// <summary>An admin hid the tournament (organisers only see such a season).</summary>
    public bool Hidden { get; set; }
}

/// <summary>What a season's tournament page shows above its tabs: "Season 3 of Corporate Premier League".</summary>
public class LeagueBadgeDto
{
    public int LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public int Season { get; set; }
    public string? LogoUrl { get; set; }

    /// <summary>The code in the league's public link, so a season's public page can link to its league's.</summary>
    public string LeagueCode { get; set; } = string.Empty;

    /// <summary>"Last season: Kothrud Kings won · all-time stats" (on the tournament's own answer only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Line { get; set; }
}

/// <summary>The Stats tab: groups (Batting, Bowling, Fielding, MVP; football's Scoring, Playing, Discipline), each a set of leaderboards.</summary>
public class LeagueStatsDto
{
    public int LeagueId { get; set; }
    public Sport Sport { get; set; }
    public List<LeagueSeasonChipDto> Seasons { get; set; } = [];

    /// <summary>The season shown, or null for every season.</summary>
    public int? Season { get; set; }

    /// <summary>"Seasons 1–3 · completed matches".</summary>
    public string Basis { get; set; } = string.Empty;

    public List<LeagueStatGroupDto> Groups { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public class LeagueSeasonChipDto
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class LeagueStatGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<LeagueBoardDto> Boards { get; set; } = [];
}

public class LeagueBoardDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ValueLabel { get; set; } = string.Empty;
    public string? Qualification { get; set; }

    /// <summary>How many qualify in all; the rows are the top of them.</summary>
    public int Total { get; set; }

    public List<LeagueBoardRowDto> Rows { get; set; } = [];
}

public class LeagueBoardRowDto
{
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"Falcons · 3 seasons · 24 innings", "Season 2 · v Kothrud Kings · 58 balls".</summary>
    public string Line { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>The person's account, when they have one: a row opens their profile.</summary>
    public int? UserId { get; set; }

    /// <summary>Their latest squad place, for someone without an account (their profile opens by it).</summary>
    public int PlayerId { get; set; }
}

/// <summary>The Teams tab: titles, and every team's record across the seasons it played.</summary>
public class LeagueTeamsDto
{
    public List<LeagueTitleDto> Titles { get; set; } = [];
    public List<LeagueTeamRecordDto> Teams { get; set; } = [];
    public string Note { get; set; } = string.Empty;
}

public class LeagueTitleDto
{
    public string Name { get; set; } = string.Empty;
    public List<int> Seasons { get; set; } = [];
    public int Count { get; set; }
}

public class LeagueTeamRecordDto
{
    public string Name { get; set; } = string.Empty;
    public int Seasons { get; set; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }

    /// <summary>Football draws; cricket ties and no-results.</summary>
    public int Drawn { get; set; }

    public int WinPercent { get; set; }
    public int Titles { get; set; }
}

/// <summary>"Bring back teams": an earlier season's teams, each with what bringing it back would bring.</summary>
public class BringBackOptionsDto
{
    public List<BringBackSeasonDto> Seasons { get; set; } = [];
}

public class BringBackSeasonDto
{
    public int TournamentId { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<BringBackTeamDto> Teams { get; set; } = [];
}

public class BringBackTeamDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"Won Season 3 · 14 players", "12 players · 2 without an account".</summary>
    public string Line { get; set; } = string.Empty;

    public int Players { get; set; }

    /// <summary>Why it can't come back (already here, a team with its name is here); null while it can.</summary>
    public string? Reason { get; set; }
}

public class BringBackResultDto
{
    public int Added { get; set; }
    public string Notice { get; set; } = string.Empty;
}
