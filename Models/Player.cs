using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Models;

/// <summary>
/// A squad place: this person, in this team, in this tournament, wearing this number. It stays its
/// own row even once a person signs in, because organisers add people who don't have the app yet,
/// the number and position are per tournament, and every goal, ball and card points at it. A place
/// is linked to the person's account by email or by the team's join code (phase 4).
/// </summary>
public class Player
{
    public int Id { get; set; }

    /// <summary>The name the organiser typed; once linked, kept equal to the account's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Football only — a free-text position. Cricket uses <see cref="Cricket"/>.</summary>
    public string? Position { get; set; }

    public int? JerseyNumber { get; set; }
    public int TeamId { get; set; }
    [JsonIgnore]
    public Team? Team { get; set; }

    /// <summary>
    /// The team's tournament, copied here (teams never move) so "one place per tournament" can be
    /// a unique index. No foreign key: Tournament already cascades to Player through Team, and a
    /// second path is what SQL Server rejects.
    /// </summary>
    [JsonIgnore]
    public int TournamentId { get; set; }

    /// <summary>The account this place belongs to, once linked. At most one place per account per tournament.</summary>
    [JsonIgnore]
    public int? UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }

    /// <summary>
    /// The sign-in email the organiser entered, or the linked account's. A place whose email has
    /// no account yet links when that person signs up. Never serialised: owners see it as
    /// <see cref="Email"/>.
    /// </summary>
    [JsonIgnore]
    public string? EmailId { get; set; }

    /// <summary>
    /// The mobile number (E.164) the organiser entered (phase 6B). Like the email, a place waiting
    /// on a number links when that person signs in with it. Never copied from the account: a
    /// player's number is theirs to give. Owners see it as <see cref="Phone"/>.
    /// </summary>
    [JsonIgnore]
    public string? PhoneNumber { get; set; }

    /// <summary>Signed in and linked: shown by the account name, with the person's own cricket profile.</summary>
    [NotMapped]
    public bool IsLinked => UserId != null;

    /// <summary>The signed-in person's own place — filled per request.</summary>
    [NotMapped]
    public bool IsMe { get; set; }

    /// <summary>The linked person's profile photo (a signed, expiring link; HLD P-26). Never on public pages.</summary>
    [NotMapped]
    public string? PhotoUrl { get; set; }

    /// <summary>The email, for the tournament's owners only — filled per request, null for everyone else.</summary>
    [NotMapped]
    public string? Email { get; set; }

    /// <summary>The mobile number, for the tournament's owners only — filled per request, null for everyone else.</summary>
    [NotMapped]
    public string? Phone { get; set; }

    /// <summary>
    /// When the player was left out of the squad after they had played (2026-11-20). Their goals, balls and cards
    /// point at this row, so it stays: it leaves the squad and every picker, but scorecards, stats and their
    /// profile keep it. A player who never played is simply deleted instead. Teams answer these separately
    /// as <c>leftPlayers</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LeftOn { get; set; }

    /// <summary>The club player this squad place came from, when its team is a lasting team (step 2 of the lasting-teams plan).</summary>
    [JsonIgnore]
    public int? ClubPlayerId { get; set; }

    [JsonIgnore]
    public Clubs.ClubPlayer? ClubPlayer { get; set; }

    /// <summary>Null for a football player, and for a cricketer who has not filled it in.</summary>
    public PlayerCricketProfile? Cricket { get; set; }
}
