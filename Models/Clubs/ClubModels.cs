using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Models.Clubs;

// Teams that last (docs/reusable-teams-plan.md, step 2, 2026-11-20). People only ever see "team" (C-21); underneath it is a
// club: a group of players kept once, outside any tournament, whose squads in tournaments link back to it. Step 2 makes
// them (from scratch, or by keeping a tournament's team); entering tournaments with a code is step 3.

/// <summary>
/// A lasting team. One sport (C-8), a name that needn't be unique (C-9), an area, a logo, 1–3 admins and its players.
/// Anyone signed in may make one (C-13) and look at one (C-15). Its tournament squads are ordinary teams with
/// <see cref="Team.ClubId"/> set, so scoring, stats and schedules never see it.
/// </summary>
public class Club : AuditedEntity
{
    public const int NameMax = 60;
    public const int AreaMax = 60;

    public int ClubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }

    /// <summary>Where it plays, in the admins' words ("Pune", "Karve Nagar").</summary>
    public string? Area { get; set; }

    /// <summary>
    /// What a player types to join (8 characters; team codes are 6 and pool codes 7, so one field takes all three). Never
    /// serialised: admins see it on the club's answer.
    /// </summary>
    [JsonIgnore]
    public string JoinCode { get; set; } = string.Empty;

    /// <summary>The logo's picture id (HLD P-26), like a tournament's.</summary>
    [JsonIgnore]
    public string? LogoId { get; set; }

    /// <summary>
    /// The code in the club's public link (`/team/<code>` on the website, 8 characters; step 5): made the first time the page is opened and
    /// kept for good. Not the join code: the public page must never let a reader join.
    /// </summary>
    [JsonIgnore]
    public string? PublicCode { get; set; }

    [JsonIgnore]
    public List<ClubPlayer> Players { get; set; } = [];

    [JsonIgnore]
    public List<ClubAdmin> Admins { get; set; } = [];
}

/// <summary>Someone who runs a club (C-16): edits it, its players and admins, and its squads in tournaments. 1–3 per club.</summary>
public class ClubAdmin : AuditedEntity
{
    public const int MaxPerClub = 3;

    public int ClubAdminId { get; set; }
    public int ClubId { get; set; }

    [JsonIgnore]
    public Club? Club { get; set; }

    public int UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }
}

/// <summary>
/// A player of a club: like a squad place, but kept between tournaments. Added by an admin (by email or number first,
/// or by name), or by the person with the club's join code. A linked player shows their account's name and (cricket)
/// profile, as a squad place does. A person is in a club once, and in any number of clubs (C-2).
/// </summary>
public class ClubPlayer : AuditedEntity
{
    public int ClubPlayerId { get; set; }
    public int ClubId { get; set; }

    [JsonIgnore]
    public Club? Club { get; set; }

    public string Name { get; set; } = string.Empty;
    public int? JerseyNumber { get; set; }

    /// <summary>Football only, free text, as on a squad place.</summary>
    public string? Position { get; set; }

    /// <summary>The club's usual captain: copied into each new squad as its default captain. One per club.</summary>
    public bool IsCaptain { get; set; }

    public int? UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }

    /// <summary>An email the admin typed: the player links when that person signs up (or now, if they have).</summary>
    public string? EmailId { get; set; }

    /// <summary>A number the admin typed (E.164), the same way.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Cricket clubs only; null until filled in.</summary>
    public ClubPlayerCricket? Cricket { get; set; }

    [NotMapped]
    public bool IsLinked => UserId != null;
}

/// <summary>A club player's cricket fields, stored on the player's own row (an owned type).</summary>
public class ClubPlayerCricket : ICricketProfile
{
    public CricketRole PrimaryRole { get; set; } = CricketRole.Batter;
    public BattingStyle? BattingStyle { get; set; }
    public BowlingArm? BowlingArm { get; set; }
    public BowlingType? BowlingType { get; set; }
    public int? BattingOrderPreference { get; set; }

    [NotMapped]
    public string? BowlingStyleLabel => BowlingStyles.Describe(BowlingArm, BowlingType);

    [NotMapped]
    public string RoleLabel => CricketRoles.Describe(PrimaryRole);

    public static ClubPlayerCricket From(ICricketProfile from)
    {
        var profile = new ClubPlayerCricket();
        CricketProfiles.Copy(from, profile);
        return profile;
    }
}

// --- Requests ------------------------------------------------------------------------------------------------------------

public class CreateClubRequest
{
    public string? Name { get; set; }
    public Sport Sport { get; set; }
    public string? Area { get; set; }

    /// <summary>The person making it plays for it too, so they're its first player as well as its first admin.</summary>
    public bool IPlay { get; set; } = true;
}

public class UpdateClubRequest
{
    public string? Name { get; set; }
    public string? Area { get; set; }

    /// <summary>With <see cref="SetCaptain"/>: the club player who captains (null for nobody).</summary>
    public int? CaptainClubPlayerId { get; set; }
    public bool SetCaptain { get; set; }
}

/// <summary>Adding or editing a club player: the fields of a squad place.</summary>
public class ClubPlayerRequest
{
    public string? Name { get; set; }
    public int? JerseyNumber { get; set; }
    public string? Position { get; set; }

    /// <summary>Null leaves it alone, "" clears it (as on a squad place).</summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }
    public CricketProfileInput? Cricket { get; set; }
}

public class AddClubAdminRequest
{
    /// <summary>One of the club's players who is on the app…</summary>
    public int? ClubPlayerId { get; set; }

    /// <summary>…or anyone with an account, by email or number.</summary>
    public string? Contact { get; set; }
}

public class ClubJoinRequest
{
    public string Code { get; set; } = string.Empty;

    /// <summary>The name to claim; null for "I'm not listed — join as new".</summary>
    public int? ClubPlayerId { get; set; }

    /// <summary>Opened from an invite link (2026-11-23): straight in as new; being in the team already is no error.</summary>
    public bool Link { get; set; }
}

// --- Answers -------------------------------------------------------------------------------------------------------------

/// <summary>A row on Home's Teams tab (C-22): a club the person runs or plays for.</summary>
public class ClubListItemDto
{
    public int ClubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public string? Area { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsPlayer { get; set; }
    public int Players { get; set; }
    public int Tournaments { get; set; }

    /// <summary>Tournaments it's playing in now.</summary>
    public int Live { get; set; }
}

/// <summary>A club's page (C-12, C-15): anyone signed in sees it; emails, numbers and the join code only its admins.</summary>
public class ClubDto
{
    public int ClubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public string? Area { get; set; }
    public string? LogoUrl { get; set; }

    /// <summary>For its admins only.</summary>
    public string? JoinCode { get; set; }

    /// <summary>The join code as a link to share (2026-11-23), for its admins only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Public.InviteLinkDto? Invite { get; set; }

    /// <summary>The caller runs it: may edit it, its players and its admins.</summary>
    public bool CanManage { get; set; }

    /// <summary>The caller's own place in it, if they play for it ("Leave team").</summary>
    public int? MyClubPlayerId { get; set; }

    public List<ClubAdminDto> Admins { get; set; } = [];
    public List<ClubPlayerDto> Players { get; set; } = [];
    public List<ClubTournamentDto> Tournaments { get; set; } = [];
    public ClubTotalsDto Totals { get; set; } = new();

    /// <summary>The public page's link (C-15): anyone may share it; it opens signed out, with names and results only.</summary>
    public Public.PublicShareDto? Share { get; set; }

    /// <summary>After a player is added or leaves: the live tournaments to ask about (C-6). Left out of the JSON otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClubSquadQuestionDto? Question { get; set; }
}

public class ClubAdminDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsMe { get; set; }

    /// <summary>The caller may take them off (an admin, while another stays), or step down themselves.</summary>
    public bool CanRemove { get; set; }
}

public class ClubPlayerDto
{
    public int ClubPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? JerseyNumber { get; set; }
    public string? Position { get; set; }
    public bool IsCaptain { get; set; }
    public bool IsLinked { get; set; }

    /// <summary>An email or number is waiting for this person to sign up.</summary>
    public bool IsWaiting { get; set; }

    public bool IsMe { get; set; }
    public bool IsAdmin { get; set; }
    public ClubPlayerCricket? Cricket { get; set; }
    public string? PhotoUrl { get; set; }

    /// <summary>For the club's admins only.</summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }
}

/// <summary>One tournament the club played in, with its own result.</summary>
public class ClubTournamentDto
{
    public int TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public TournamentStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? LogoUrl { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;

    /// <summary>live = being played, upcoming = still to play, past = played (C-12).</summary>
    public string Section { get; set; } = "upcoming";

    public int Played { get; set; }
    public int Won { get; set; }
    public bool Champion { get; set; }
    public int Squad { get; set; }

    /// <summary>"Champions · 5 played, 5 won", "Played 4 · won 3", "Starts 12 Dec · 14 in the squad".</summary>
    public string Line { get; set; } = string.Empty;

    /// <summary>The caller may open the tournament (they're in it, run it, or run this team).</summary>
    public bool CanOpen { get; set; }

    /// <summary>An entry still waiting for the organiser (step 3): its admins open it to change or withdraw it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EntryId { get; set; }
}

public class ClubTotalsDto
{
    public int Tournaments { get; set; }
    public int Matches { get; set; }
    public int Won { get; set; }
    public int Titles { get; set; }
}

public class ClubPlayerLookupDto
{
    public ContactKind Kind { get; set; }
    public string Contact { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public bool Found { get; set; }
    public string? Name { get; set; }

    /// <summary>They're in this club already.</summary>
    public bool AlreadyIn { get; set; }
}

public class ClubJoinPreviewDto
{
    public int ClubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public string? Area { get; set; }
    public string? LogoUrl { get; set; }
    public int Players { get; set; }

    /// <summary>"Are you one of these?": players an admin added by name that nobody has claimed.</summary>
    public List<ClubJoinPlaceDto> Places { get; set; } = [];
}

public class ClubJoinPlaceDto
{
    public int ClubPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

public class ClubJoinResult
{
    public int ClubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }

    /// <summary>An invite link opened by someone already in the team: nothing changed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AlreadyIn { get; set; }
}
