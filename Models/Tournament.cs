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
    public List<SavedSchedule> Schedules { get; set; } = new();
    public List<Team> Teams { get; set; } = new();

    /// <summary>Football fixtures. Populated only when <see cref="Sport"/> is Football.</summary>
    public List<Match> Matches { get; set; } = new();

    /// <summary>Cricket fixtures. Populated only when <see cref="Sport"/> is Cricket.</summary>
    public List<Cricket.CricketMatch> CricketMatches { get; set; } = new();
}