using System.ComponentModel.DataAnnotations.Schema;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Models.Accounts;

// Players are people (phase 4 of docs/accounts-and-access-plan.md): a squad place links to the
// person's account, by the email the organiser typed or by the team's join code; the person then
// owns their name and cricket profile; and they can delete their account.

/// <summary>
/// A person's own cricket profile. Once their squad places are linked, each place's
/// <see cref="PlayerCricketProfile"/> is kept a copy of this one.
/// </summary>
public class UserCricketProfile : ICricketProfile
{
    public int UserId { get; set; }
    public CricketRole PrimaryRole { get; set; } = CricketRole.Batter;
    public BattingStyle? BattingStyle { get; set; }
    public BowlingArm? BowlingArm { get; set; }
    public BowlingType? BowlingType { get; set; }
    public int? BattingOrderPreference { get; set; }

    [NotMapped]
    public string? BowlingStyleLabel => BowlingStyles.Describe(BowlingArm, BowlingType);

    [NotMapped]
    public string RoleLabel => CricketRoles.Describe(PrimaryRole);
}

// --- Joining a team with its code -----------------------------------------

public class JoinTeamPreviewRequest
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>What a code opens: the team, and the names in it nobody has claimed yet.</summary>
public class JoinTeamPreviewDto
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;

    /// <summary>"Are you one of these?" — places the organiser added that no account has claimed.</summary>
    public List<JoinPlaceDto> Places { get; set; } = [];
}

public class JoinPlaceDto
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>What tells two Rahuls apart: "#7 · Batter". Null when the organiser entered nothing more.</summary>
    public string? Detail { get; set; }
}

public class JoinTeamRequest
{
    public string Code { get; set; } = string.Empty;

    /// <summary>The place to claim; null for "I'm not listed — join as a new player".</summary>
    public int? PlayerId { get; set; }
}

public class JoinTeamResult
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int PlayerId { get; set; }
}

// --- Adding a player by their email or number ----------------------------------

public class PlayerLookupRequest
{
    /// <summary>An email or a mobile number, in one field (anything with an @ is an email).</summary>
    public string Contact { get; set; } = string.Empty;
}

/// <summary>
/// Who an email or number belongs to, before an owner adds them to a team. Only the name: never
/// the account's other contact or its id, so a lookup tells an owner no more than the squad list will.
/// </summary>
public class PlayerLookupDto
{
    /// <summary>"Email" or "Phone": which field of <c>PLAYER_CREATE</c> to send <see cref="Contact"/> in.</summary>
    public ContactKind Kind { get; set; }

    /// <summary>As stored: a lower-case email, or the number in E.164 ("+919876543210").</summary>
    public string Contact { get; set; } = string.Empty;

    /// <summary>For the screen: "+91 98765 43210", or the email.</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary>An account uses it, so the player can be added without typing a name.</summary>
    public bool Found { get; set; }

    /// <summary>The account's name, when <see cref="Found"/>.</summary>
    public string? Name { get; set; }

    /// <summary>The team they already play for in this tournament (they can't be in two).</summary>
    public string? PlaysFor { get; set; }

    /// <summary>A squad place in this tournament is already waiting on this email or number.</summary>
    public string? WaitingIn { get; set; }
}

// --- Deleting an account ------------------------------------------------------

/// <summary>
/// What deleting the account would do, shown before the person confirms with the code just sent
/// to their email.
/// </summary>
public class AccountDeletionPreviewDto
{
    public string CodeSentTo { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    public int ResendAfterSeconds { get; set; }

    /// <summary>Ones they're the only owner of: deleted for everyone in them.</summary>
    public List<DeletedTournamentDto> TournamentsDeleted { get; set; } = [];

    /// <summary>Ones they created that have other owners: the longest-standing of those takes over.</summary>
    public List<HandedOverTournamentDto> TournamentsHandedOver { get; set; } = [];

    /// <summary>Other tournaments they own or score in: they're taken off.</summary>
    public int TournamentsLeft { get; set; }

    /// <summary>Squad places that stop being linked (the names stay on the scorecards).</summary>
    public int SquadPlaces { get; set; }
}

public class DeletedTournamentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }

    /// <summary>Other owners, scorers and linked players who will lose it too.</summary>
    public int OtherPeople { get; set; }
}

public class HandedOverTournamentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NewCreator { get; set; } = string.Empty;
}

public class DeleteAccountRequest
{
    public string Otp { get; set; } = string.Empty;
}
