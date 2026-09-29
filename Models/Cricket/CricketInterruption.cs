using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// Overs taken off an innings while it was being played — rain, bad light, a late start.
///
/// Kept as a row per cut rather than just lowering <see cref="CricketInnings.OversLimit"/>, because
/// Duckworth-Lewis-Stern prices a cut by *when* it happened: overs lost with eight wickets in hand
/// are worth far more than the same overs lost with two. Each row records the moment exactly.
/// </summary>
public class CricketInterruption
{
    public int Id { get; set; }

    public int InningsId { get; set; }
    [JsonIgnore]
    public CricketInnings? Innings { get; set; }

    /// <summary>Legal balls bowled when play stopped.</summary>
    public int AtLegalBalls { get; set; }

    public int WicketsAtTime { get; set; }

    /// <summary>The innings' allowance in balls before the cut, and after it.</summary>
    public int BallsAllowedBefore { get; set; }
    public int BallsAllowedAfter { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
