using Microsoft.AspNetCore.Mvc;

namespace TournamentScheduler.Api.Models;



public class SavedSchedule
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int MatchesPerTeam { get; set; }
    public int TotalMatches { get; set; }
    public bool IsActive { get; set; } = true;
    public int TournamentId { get; set; }
    public Tournament? Tournament { get; set; }
    public List<SavedGroup> Groups { get; set; } = new();
}

public class SavedGroup
{
    public int Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int SavedScheduleId { get; set; }
    public SavedSchedule? SavedSchedule { get; set; }
    public List<SavedFixture> Fixtures { get; set; } = new();
}

public class SavedFixture
{
    public int Id { get; set; }
    public int MatchNumber { get; set; }
    public string Home { get; set; } = string.Empty;
    public string Away { get; set; } = string.Empty;
    public int? HomeTeamId { get; set; }
    public int? AwayTeamId { get; set; }
    public int SavedGroupId { get; set; }
    public SavedGroup? SavedGroup { get; set; }
}