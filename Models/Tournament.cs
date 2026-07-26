namespace TournamentScheduler.Api.Models;

public class Tournament
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsStarted { get; set; }
    public DateTime? StartedAt { get; set; }
    public List<SavedSchedule> Schedules { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
}