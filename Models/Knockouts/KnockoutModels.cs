using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Knockouts;

// The knockout stage (2026-10-21): matches after (or instead of) the groups, each side filled by a
// team, a place in a group, or the winner or loser of an earlier knockout match. Set up any time —
// from a preset or one match at a time — and filled in by itself as groups and matches finish.

/// <summary>
/// What a football <see cref="Match"/> and a <c>CricketMatch</c> share as fixtures: who plays, and
/// where in the tournament it sits. Lets the knockout stage fill sides in without caring about the sport.
/// </summary>
public interface ITournamentMatch
{
    int Id { get; }
    int TournamentId { get; }
    string GroupName { get; set; }
    int MatchNumber { get; set; }
    int? HomeTeamId { get; set; }
    int? AwayTeamId { get; set; }
    string HomeTeamName { get; set; }
    string AwayTeamName { get; set; }
    int? KnockoutFixtureId { get; set; }
    string? RoundLabel { get; set; }
}

/// <summary>Which round a knockout match belongs to; decides its order on the bracket and its default name.</summary>
public enum KnockoutRound
{
    Eliminator = 0,
    Qualifier = 1,
    RoundOf16 = 2,
    QuarterFinal = 3,
    SemiFinal = 4,
    ThirdPlace = 5,
    Final = 6,
    /// <summary>Anything else the organiser names themselves ("Plate final", "First round").</summary>
    Other = 7
}

/// <summary>Where a side of a knockout match comes from.</summary>
public enum KnockoutSlotKind
{
    /// <summary>A team chosen outright.</summary>
    Team = 0,
    /// <summary>A finishing place in a group ("Group A, 2nd"), known once that group's last match ends.</summary>
    GroupPlace = 1,
    /// <summary>The winner of another knockout match.</summary>
    WinnerOf = 2,
    /// <summary>The loser of another knockout match (a 3rd-place match, IPL's Qualifier 2).</summary>
    LoserOf = 3
}

/// <summary>One side of a knockout match: where its team comes from. Owned by <see cref="KnockoutFixture"/>.</summary>
public class KnockoutSlot
{
    public KnockoutSlotKind Kind { get; set; }

    /// <summary><see cref="KnockoutSlotKind.Team"/>: the team.</summary>
    public int? TeamId { get; set; }

    /// <summary><see cref="KnockoutSlotKind.GroupPlace"/>: the group ("A") and the place (1 = winner).</summary>
    public string? GroupName { get; set; }
    public int? Place { get; set; }

    /// <summary>
    /// <see cref="KnockoutSlotKind.WinnerOf"/> / <see cref="KnockoutSlotKind.LoserOf"/>: the earlier
    /// knockout match, by its <see cref="KnockoutFixture.Order"/> — stable for the fixture's life,
    /// and known before anything is saved, so a whole bracket is built in one go.
    /// </summary>
    public int? FromOrder { get; set; }

    /// <summary>
    /// The owner's choice over what the slot would work out to (a coin toss for teams level on
    /// everything, a no-result decided off the field). Kept apart so the source still reads right.
    /// </summary>
    public int? OverrideTeamId { get; set; }
}

/// <summary>One knockout match as planned. Its playable <see cref="Match"/> / <c>CricketMatch</c> points back here.</summary>
public class KnockoutFixture
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    [JsonIgnore] public Tournament? Tournament { get; set; }

    /// <summary>Its place on the bracket, 1 upwards, unique in the tournament; other slots refer to it.</summary>
    public int Order { get; set; }

    public KnockoutRound Round { get; set; }

    /// <summary>What it's called everywhere: "Semi-final 1", "Qualifier 2", "Final".</summary>
    public string Label { get; set; } = string.Empty;

    public KnockoutSlot Home { get; set; } = new();
    public KnockoutSlot Away { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }
}

// --- Requests ---------------------------------------------------------------------

public class KnockoutSlotInput
{
    public KnockoutSlotKind Kind { get; set; }
    public int? TeamId { get; set; }
    public string? GroupName { get; set; }
    public int? Place { get; set; }
    public int? FromOrder { get; set; }
}

public class KnockoutFixtureRequest
{
    public KnockoutRound Round { get; set; } = KnockoutRound.Other;

    /// <summary>Optional: the round's own name is used ("Semi-final 3") when empty.</summary>
    public string? Label { get; set; }

    public KnockoutSlotInput Home { get; set; } = new();
    public KnockoutSlotInput Away { get; set; } = new();
}

public class KnockoutApplyRequest
{
    /// <summary>One of the ids <c>KNOCKOUT_PRESETS</c> offered ("top-2", "ipl", "all-teams").</summary>
    public string Preset { get; set; } = string.Empty;

    /// <summary>Add a match for 3rd place between the losing semi-finalists.</summary>
    public bool ThirdPlace { get; set; }

    /// <summary>
    /// A knockout-only tournament's dates, when it has none yet (the groups' approve step usually
    /// sets them). Both or neither.
    /// </summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public class KnockoutTeamRequest
{
    /// <summary>"Home" or "Away".</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>The team to put there; null hands the slot back to its source.</summary>
    public int? TeamId { get; set; }
}

// --- Answers ------------------------------------------------------------------

public class KnockoutStageDto
{
    /// <summary>Every knockout match, in bracket order.</summary>
    public List<KnockoutFixtureDto> Fixtures { get; set; } = [];

    /// <summary>The groups a side can come from (none in a knockout-only tournament).</summary>
    public List<KnockoutGroupDto> Groups { get; set; } = [];

    /// <summary>True once any knockout match has started: presets can no longer replace the stage.</summary>
    public bool Started { get; set; }

    /// <summary>The Final's winner, once there is one.</summary>
    public int? ChampionTeamId { get; set; }
    public string? ChampionName { get; set; }
}

public class KnockoutGroupDto
{
    public string GroupName { get; set; } = string.Empty;
    public int Teams { get; set; }

    /// <summary>Every match in the group is over, so its places are known.</summary>
    public bool Finished { get; set; }
}

public class KnockoutFixtureDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public KnockoutRound Round { get; set; }
    public string Label { get; set; } = string.Empty;
    public KnockoutSideDto Home { get; set; } = new();
    public KnockoutSideDto Away { get; set; } = new();

    /// <summary>The playable match, once the tournament has started.</summary>
    public int? MatchId { get; set; }
    public int? MatchNumber { get; set; }

    /// <summary>The match's status ("NotStarted", "InProgress", "Completed", …); null before the start.</summary>
    public string? Status { get; set; }

    /// <summary>"2–1", "2–2 (4–3 pens)", "Lions won by 12 runs"; null until there's a score.</summary>
    public string? ResultLine { get; set; }

    public int? WinnerTeamId { get; set; }

    /// <summary>Its sides may still be changed and it may be deleted (it hasn't started).</summary>
    public bool CanChange { get; set; }
}

public class KnockoutSideDto
{
    /// <summary>The team, once known.</summary>
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }

    /// <summary>Where it comes from, in words: "Group A winner", "Winner of Semi-final 1", or the team's name.</summary>
    public string Source { get; set; } = string.Empty;

    public KnockoutSlotInput Slot { get; set; } = new();

    /// <summary>The owner put this team here by hand.</summary>
    public bool SetByOwner { get; set; }
}

public class KnockoutPresetDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>A 3rd-place match can be added (there are semi-finals).</summary>
    public bool OffersThirdPlace { get; set; }

    public List<KnockoutPreviewDto> Fixtures { get; set; } = [];
}

public class KnockoutPreviewDto
{
    public KnockoutRound Round { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Home { get; set; } = string.Empty;
    public string Away { get; set; } = string.Empty;
}
