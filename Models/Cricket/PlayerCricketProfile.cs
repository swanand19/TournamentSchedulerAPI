using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// The cricketing half of a player: what they are, how they bat, how they bowl.
///
/// A separate table rather than columns on <see cref="Player"/> so <see cref="Player"/> stays
/// sport-neutral — a football squad carries none of these — and so that when players sign in for
/// themselves this is the table handed to them to own. Every field below is something the player
/// is the authority on, which is exactly the boundary sign-in will need.
/// </summary>
public class PlayerCricketProfile
{
    /// <summary>Primary key as well as the foreign key: one profile per player, at most.</summary>
    public int PlayerId { get; set; }

    [JsonIgnore]
    public Player? Player { get; set; }

    public CricketRole PrimaryRole { get; set; } = CricketRole.Batter;

    public BattingStyle? BattingStyle { get; set; }

    /// <summary>Null together with <see cref="BowlingType"/> for someone who does not bowl.</summary>
    public BowlingArm? BowlingArm { get; set; }
    public BowlingType? BowlingType { get; set; }

    /// <summary>
    /// Where in the order the player usually bats, 1-based. A hint for the setup screen only —
    /// the order that counts is the one saved on the XI, match by match.
    /// </summary>
    public int? BattingOrderPreference { get; set; }

    /// <summary>"Right-arm leg break", "Left-arm wrist spin (chinaman)", or null if not stated.</summary>
    [NotMapped]
    public string? BowlingStyleLabel => BowlingStyles.Describe(BowlingArm, BowlingType);

    [NotMapped]
    public string RoleLabel => CricketRoles.Describe(PrimaryRole);
}
