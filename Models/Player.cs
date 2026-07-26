using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models;

public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Position { get; set; }
    public int? JerseyNumber { get; set; }
    public int TeamId { get; set; }
    [JsonIgnore]
    public Team? Team { get; set; }
}