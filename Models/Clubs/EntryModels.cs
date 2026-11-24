using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models.Clubs;

// Entering a tournament with its code (docs/reusable-teams-plan.md section 6, step 3, 2026-11-21) and a club's changes
// reaching its squads (section 8.2, step 4). People see "team" (C-21); the code says club and entry.

public enum EntryStatus
{
    Waiting = 0,
    Accepted = 1,
    Declined = 2,
    Withdrawn = 3,

    /// <summary>Entries closed (schedule approved, knockout set up, tournament started) before the organiser answered.</summary>
    Cancelled = 4
}

/// <summary>
/// A club asking to play in one tournament, sent by one of its admins with the tournament's entry code (C-4, C-11). The
/// organiser accepts or declines it (C-7); accepting makes an ordinary <see cref="Team"/> with <see cref="Team.ClubId"/>
/// and a squad place per ticked player. A club has at most one waiting-or-accepted entry per tournament.
/// </summary>
public class TournamentEntry
{
    public const int TeamNameMax = 100;

    public int TournamentEntryId { get; set; }
    public int TournamentId { get; set; }
    public int ClubId { get; set; }

    [JsonIgnore]
    public Club? Club { get; set; }

    /// <summary>The team's name in this tournament: the club's unless another team there has it already.</summary>
    public string TeamName { get; set; } = string.Empty;

    public EntryStatus Status { get; set; }
    public int RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public int? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>The team made when it was accepted. A plain number, not a link: the team may be deleted later, the entry stays.</summary>
    public int? TeamId { get; set; }

    [JsonIgnore]
    public List<TournamentEntryPlayer> Players { get; set; } = [];

    [JsonIgnore]
    public bool IsOpen => Status is EntryStatus.Waiting or EntryStatus.Accepted;
}

/// <summary>A club player ticked into an entry's squad, while the entry waits.</summary>
public class TournamentEntryPlayer
{
    public int TournamentEntryPlayerId { get; set; }
    public int TournamentEntryId { get; set; }

    [JsonIgnore]
    public TournamentEntry? Entry { get; set; }

    /// <summary>A plain number, not a link (Club already reaches this row through the entry); removed with the club player.</summary>
    public int ClubPlayerId { get; set; }
}

// --- Requests ------------------------------------------------------------------------------------------------------------

public class EntryCodeRequest
{
    /// <summary>"Teams can enter with a code": on makes (or keeps) the code, off stops it working.</summary>
    public bool Open { get; set; }
}

public class EntryPreviewRequest
{
    public int ClubId { get; set; }

    /// <summary>The code the organiser sent (or the whole shared link)…</summary>
    public string? Code { get; set; }

    /// <summary>…or an entry already sent, to change it.</summary>
    public int? EntryId { get; set; }
}

public class EntryRequest
{
    public int ClubId { get; set; }
    public string? Code { get; set; }
    public string? TeamName { get; set; }
    public List<int> ClubPlayerIds { get; set; } = [];
}

public class EntryUpdateRequest
{
    public string? TeamName { get; set; }
    public List<int> ClubPlayerIds { get; set; } = [];
}

/// <summary>The answer to the live-tournaments sheet (C-6): add the player to these squads, or leave these places out.</summary>
public class ClubSquadsApplyRequest
{
    public int? ClubPlayerId { get; set; }

    /// <summary>Squads (teams) to add <see cref="ClubPlayerId"/> to.</summary>
    public List<int> TeamIds { get; set; } = [];

    /// <summary>Squad places to leave out (a player who left the club), by squad place id.</summary>
    public List<int> PlayerIds { get; set; } = [];
}

// --- Answers -------------------------------------------------------------------------------------------------------------

/// <summary>What an admin sees after typing the code: the tournament, who's in, and which of the club's players can play.</summary>
public class EntryPreviewDto
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public TournamentStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? LogoUrl { get; set; }
    public string Organiser { get; set; } = string.Empty;
    public List<string> Teams { get; set; } = [];

    public int ClubId { get; set; }
    public string ClubName { get; set; } = string.Empty;

    /// <summary>The name to start from: the club's, or (if a team there has it) the one already sent.</summary>
    public string TeamName { get; set; } = string.Empty;

    /// <summary>Why the name must change, while another team in the tournament has it.</summary>
    public string? TeamNameProblem { get; set; }

    public List<EntryPlayerDto> Players { get; set; } = [];

    /// <summary>The entry already sent by this club, if any: waiting, or accepted.</summary>
    public int? EntryId { get; set; }
    public EntryStatus? EntryStatus { get; set; }

    /// <summary>The entry code, kept so the screen can send it back.</summary>
    public string? Code { get; set; }
}

public class EntryPlayerDto
{
    public int ClubPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public bool IsCaptain { get; set; }
    public bool Ticked { get; set; }

    /// <summary>Why they can't play here ("Plays for Tigers in this tournament"); null when they can.</summary>
    public string? Reason { get; set; }
}

/// <summary>The organiser's side (board Org-Entries): the code and the entries waiting for an answer.</summary>
public class TournamentEntriesDto
{
    public bool Open { get; set; }
    public string? Code { get; set; }

    /// <summary>What Share sends: how to use the code (and the web link, when the web app's address is set).</summary>
    public string? ShareText { get; set; }

    public string? Url { get; set; }

    /// <summary>Why the switch can't be turned on ("The tournament has started…"); null while it can.</summary>
    public string? Closed { get; set; }

    public List<EntryRowDto> Waiting { get; set; } = [];

    /// <summary>Said once after an answer ("Falcons is in. Amit already plays for Tigers, so he isn't in its squad.").</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notice { get; set; }
}

public class EntryRowDto
{
    public int EntryId { get; set; }
    public int ClubId { get; set; }
    public string ClubName { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string? LogoUrl { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }

    /// <summary>"3 tournaments · 1 title", from the club's own record.</summary>
    public string Record { get; set; } = string.Empty;

    public List<EntryPlayerDto> Players { get; set; } = [];

    /// <summary>Why it can't be accepted as it is (a team took its name since); null when it can.</summary>
    public string? Problem { get; set; }
}

/// <summary>
/// The one-sheet question (C-5, C-6, C-20) after a club player is added or leaves: what was done to upcoming squads by
/// itself, and the live tournaments to tick. Only sent when there is a live tournament to ask about.
/// </summary>
public class ClubSquadQuestionDto
{
    /// <summary>"add" or "remove".</summary>
    public string Change { get; set; } = "add";

    public int? ClubPlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Upcoming tournaments (and waiting entries) already changed: "Monsoon Cup, Diwali T10".</summary>
    public List<string> Done { get; set; } = [];

    public List<ClubSquadChoiceDto> Live { get; set; } = [];
}

public class ClubSquadChoiceDto
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;

    /// <summary>For "remove": the squad place to leave out.</summary>
    public int? PlayerId { get; set; }

    /// <summary>Why this row can't be ticked; null when it can.</summary>
    public string? Reason { get; set; }
}

/// <summary>A club's public page (C-15, section 8.3): names and results only; never emails, numbers, codes or photos.</summary>
public class PublicClubDto
{
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public string? Area { get; set; }
    public string? LogoUrl { get; set; }
    /// <summary>Its admins' names ("Run by …"); not "admins", which the public pages strip.</summary>
    public List<string> RunBy { get; set; } = [];
    public ClubTotalsDto Totals { get; set; } = new();
    public List<PublicClubTournamentDto> Tournaments { get; set; } = [];
    public List<PublicClubPlayerDto> Players { get; set; } = [];
}

public class PublicClubTournamentDto
{
    public string Name { get; set; } = string.Empty;
    public TournamentStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string Section { get; set; } = "past";
    public string Line { get; set; } = string.Empty;
    public bool Champion { get; set; }

    /// <summary>The tournament's public code while it is public, so the page links to its live scores ("publicCode" is stripped).</summary>
    public string? LiveCode { get; set; }
}

public class PublicClubPlayerDto
{
    public string Name { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public bool IsCaptain { get; set; }
}
