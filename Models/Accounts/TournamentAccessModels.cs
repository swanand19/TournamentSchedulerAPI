using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Accounts;

// Who may do what inside one tournament (phase 3 of docs/accounts-and-access-plan.md). Separate
// from Roles: Roles say what kind of account someone has; these say what they are in *this*
// tournament — the same person can own one tournament and only score in another.

public enum TournamentMemberRole
{
    /// <summary>Runs the tournament: edits everything, manages people, scores, completes it.</summary>
    Owner = 0,

    /// <summary>Sets up and scores matches — nothing else.</summary>
    Scorer = 1
}

/// <summary>One person's role in one tournament. One row per person: making a scorer an owner changes the row.</summary>
public class TournamentMember : AuditedEntity
{
    public int TournamentMemberId { get; set; }
    public int TournamentId { get; set; }
    public int UserId { get; set; }
    public TournamentMemberRole MemberRole { get; set; }

    [JsonIgnore]
    public User? User { get; set; }
}

public enum TournamentStatus
{
    /// <summary>Created, not started.</summary>
    Upcoming = 0,

    /// <summary>Started: its matches exist and are being played.</summary>
    Live = 1,

    /// <summary>Marked complete by an owner, or automatically the day after the end date.</summary>
    Completed = 2,

    /// <summary>Never started, and its end date (plus a day's grace) has passed.</summary>
    Cancelled = 3
}

/// <summary>What one person may do in one tournament — the actions' <c>[TournamentAccess]</c> levels.</summary>
public enum TournamentPermission
{
    /// <summary>See it: any member.</summary>
    View,

    /// <summary>Set up and score matches: owners and scorers (subject to the scorer lock).</summary>
    Score,

    /// <summary>Change the tournament, its teams, groups and schedule; start and complete it: owners.</summary>
    Edit,

    /// <summary>Add and remove owners and scorers: owners.</summary>
    ManageMembers,

    /// <summary>Delete it: only the person who created it.</summary>
    Delete
}

/// <summary>What the apps need to draw a tournament's buttons — the server decides, they only draw.</summary>
public class TournamentAccessDto
{
    /// <summary>"creator", "owner", "scorer", "player" — what the signed-in person is here.</summary>
    public List<string> MyRoles { get; set; } = [];

    /// <summary>Their own squad place here, when they play in it.</summary>
    public int? MyPlayerId { get; set; }
    public int? MyTeamId { get; set; }

    public bool CanEdit { get; set; }
    public bool CanScore { get; set; }
    public bool CanManageMembers { get; set; }
    public bool CanDelete { get; set; }
    public bool CanLeave { get; set; }

    /// <summary>Live, and no match is in progress.</summary>
    public bool CanComplete { get; set; }

    /// <summary>Live and every match is over: the apps ask once "All matches played. Mark the tournament complete?"</summary>
    public bool AllMatchesPlayed { get; set; }
}

public class TournamentMemberDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Shown to owners only (they add people by email); null for everyone else.</summary>
    public string? Email { get; set; }

    /// <summary>Owners only, like <see cref="Email"/>.</summary>
    public string? Phone { get; set; }

    public string Role { get; set; } = string.Empty;
    public bool IsCreator { get; set; }
    public bool IsMe { get; set; }
    public bool CanRemove { get; set; }
}

public class AddTournamentMemberRequest
{
    /// <summary>An email or a mobile number (phase 6C); the server works out which.</summary>
    public string? Contact { get; set; }

    /// <summary>The field before phase 6C: an email only. Used when <see cref="Contact"/> is empty.</summary>
    public string? Email { get; set; }

    public TournamentMemberRole Role { get; set; } = TournamentMemberRole.Scorer;
}

/// <summary>
/// An owner or scorer asked for by an email or number with no account yet (phase 6C). Sent to
/// them; when they sign up (or add it to their account) they get the role. Cancelled =
/// <see cref="AuditedEntity.IsDisabled"/>; <c>CrdBy</c> is who invited them.
/// </summary>
public class TournamentInvite : AuditedEntity
{
    public int TournamentInviteId { get; set; }
    public int TournamentId { get; set; }
    public OtpChannel Channel { get; set; }

    /// <summary>The lower-case email or the E.164 number, as accounts store them.</summary>
    public string Target { get; set; } = string.Empty;

    public TournamentMemberRole MemberRole { get; set; }
    public DateTime LastSentAt { get; set; }

    /// <summary>Sending it again moves this on; until then a sign-up with it still takes the role.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }
    public int? AcceptedByUserId { get; set; }
}

/// <summary>Everyone on People & settings: the members, and (for owners) who is still invited.</summary>
public class TournamentPeopleDto
{
    public List<TournamentMemberDto> Members { get; set; } = [];

    /// <summary>Owners only; empty for everyone else.</summary>
    public List<TournamentInviteDto> Invites { get; set; } = [];

    /// <summary>After an add, resend or cancel: what happened, in a sentence to show.</summary>
    public string? Notice { get; set; }
}

public class TournamentInviteDto
{
    public int InviteId { get; set; }

    /// <summary>The email or E.164 number it went to.</summary>
    public string Contact { get; set; } = string.Empty;

    public OtpChannel Channel { get; set; }
    public string Role { get; set; } = string.Empty;
    public string InvitedBy { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Past its date: it no longer gives the role. Resend renews it.</summary>
    public bool IsExpired { get; set; }
}

// --- The scorer lock -----------------------------------------------------

/// <summary>A person, as the scorer banner names them.</summary>
public class PersonDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ScoringRequestDto
{
    public PersonDto By { get; set; } = new();
    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Who is scoring a match and what the signed-in person may do about it. Every match answer
/// (football's match, cricket's state) carries one as <c>scoring</c>; the apps draw the banner and
/// enable the scoring controls only from it.
/// </summary>
public class ScoringDto
{
    /// <summary>Null until someone sets the match up, and once it is over.</summary>
    public PersonDto? ActiveScorer { get; set; }

    public bool IsActiveScorer { get; set; }

    /// <summary>May record things on this match now (role allows it, nobody else holds the lock, tournament open).</summary>
    public bool CanScore { get; set; }

    /// <summary>A scorer, while someone else is scoring: may ask to take over.</summary>
    public bool CanRequestScoring { get; set; }

    /// <summary>An owner, while someone else is scoring: may take over without asking.</summary>
    public bool CanTakeOverScoring { get; set; }

    /// <summary>A request waiting for the active scorer's answer (expires after two minutes).</summary>
    public ScoringRequestDto? PendingRequest { get; set; }

    /// <summary>The active scorer, with a request waiting: may allow or decline it.</summary>
    public bool CanRespondToRequest { get; set; }
}

public class RespondToScoringRequest
{
    public bool Allow { get; set; }
}
