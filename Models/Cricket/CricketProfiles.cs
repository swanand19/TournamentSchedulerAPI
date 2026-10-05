namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// The cricketing fields of a person: what they are, how they bat, how they bowl. Held by a squad
/// place (<see cref="PlayerCricketProfile"/>), by an account (<see cref="UserCricketProfile"/>) once
/// the person signs in and owns it, and posted as <see cref="CricketProfileInput"/>.
/// </summary>
public interface ICricketProfile
{
    CricketRole PrimaryRole { get; set; }
    BattingStyle? BattingStyle { get; set; }
    BowlingArm? BowlingArm { get; set; }
    BowlingType? BowlingType { get; set; }
    int? BattingOrderPreference { get; set; }
}

/// <summary>One set of rules for every copy of a cricket profile, wherever it is edited.</summary>
public static class CricketProfiles
{
    /// <summary>
    /// An arm without a type (or the reverse) is half a bowling style, and would render as
    /// nothing at all. Rejected rather than stored as a profile that cannot be described.
    /// </summary>
    public static string? Problem(ICricketProfile? input)
    {
        if (input == null) return null;

        if (input.BowlingArm.HasValue != input.BowlingType.HasValue)
            return "A bowling style needs both an arm and a type, or neither.";

        if (input.BattingOrderPreference is < 1 or > 11)
            return "Batting order preference must be between 1 and 11.";

        return null;
    }

    public static void Copy(ICricketProfile from, ICricketProfile to)
    {
        to.PrimaryRole = from.PrimaryRole;
        to.BattingStyle = from.BattingStyle;
        to.BattingOrderPreference = from.BattingOrderPreference;

        // A pure batter keeps no bowling style: the forms hide those fields, so anything still
        // sitting in the payload is stale rather than intended.
        var bowls = CricketRoles.Bowls(from.PrimaryRole);
        to.BowlingArm = bowls ? from.BowlingArm : null;
        to.BowlingType = bowls ? from.BowlingType : null;
    }

    public static PlayerCricketProfile ForPlayer(ICricketProfile from)
    {
        var profile = new PlayerCricketProfile();
        Copy(from, profile);
        return profile;
    }
}
