using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models;

public class Tournament
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fixed when the tournament is created. Tournaments created before cricket existed default to
    /// Football, which is what the migration backfills them to.
    /// </summary>
    public Sport Sport { get; set; } = Sport.Football;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsStarted { get; set; }
    public DateTime? StartedAt { get; set; }

    /// <summary>Who created it — the only one who may delete it, and never removable. System (1) for tournaments from before accounts.</summary>
    public int CreatedByUserId { get; set; } = SystemUser.Id;

    /// <summary>
    /// The days it runs, in its own time zone. Required for new tournaments; null on ones from
    /// before dates existed, which is what exempts those from completing by themselves.
    /// </summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>IANA id. Every tournament is in India for now; the column is here so others can follow.</summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public const string DefaultTimeZoneId = "Asia/Kolkata";

    public TournamentStatus Status { get; set; } = TournamentStatus.Upcoming;
    public DateTime? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }

    /// <summary>What the signed-in person may do here — filled in per request, never stored.</summary>
    [NotMapped]
    public TournamentAccessDto? Access { get; set; }

    [JsonIgnore]
    public bool IsClosed => Status is TournamentStatus.Completed or TournamentStatus.Cancelled;

    public List<SavedSchedule> Schedules { get; set; } = new();
    public List<Team> Teams { get; set; } = new();

    /// <summary>Football fixtures. Populated only when <see cref="Sport"/> is Football.</summary>
    public List<Match> Matches { get; set; } = new();

    /// <summary>Cricket fixtures. Populated only when <see cref="Sport"/> is Cricket.</summary>
    public List<Cricket.CricketMatch> CricketMatches { get; set; } = new();
}
