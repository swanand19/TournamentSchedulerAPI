using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// Everything that happened to the match that was not a delivery. The cricket counterpart of
/// <see cref="MatchEvent"/>: deliveries are the ledger, this is the commentary around them.
/// </summary>
public enum CricketMatchEventType
{
    TossCompleted = 0,
    InningsStarted = 1,
    InningsEnded = 2,
    Declared = 3,
    FollowOnEnforced = 4,
    BatterIn = 5,
    BowlerChanged = 6,
    SuperOverStarted = 7,
    MatchCompleted = 8,
    MatchAbandoned = 9,
    BallUndone = 10,
    OversReduced = 11
}

public class CricketMatchEvent
{
    public int Id { get; set; }

    public int CricketMatchId { get; set; }
    [JsonIgnore]
    public CricketMatch? Match { get; set; }

    public CricketMatchEventType EventType { get; set; }

    /// <summary>Which innings this happened in, where that means anything.</summary>
    public int? InningsNumber { get; set; }

    public int? TeamId { get; set; }
    public int? PlayerId { get; set; }

    /// <summary>Ready to read: "Mumbai won the toss and chose to bat".</summary>
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
