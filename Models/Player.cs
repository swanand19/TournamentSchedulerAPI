using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Models;

public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Football only — a free-text position. Cricket uses <see cref="Cricket"/>.</summary>
    public string? Position { get; set; }

    public int? JerseyNumber { get; set; }
    public int TeamId { get; set; }
    [JsonIgnore]
    public Team? Team { get; set; }

    /// <summary>
    /// The seam for player sign-in, and nothing more today: a player row belongs to one team in
    /// one tournament, so the same human is a separate row in every tournament they play. When a
    /// global Person is introduced, those rows point at it through this column instead of being
    /// reshaped. Deliberately no foreign key — there is nothing to point at yet.
    /// </summary>
    public int? PersonId { get; set; }

    /// <summary>Null for a football player, and for a cricketer who has not filled it in.</summary>
    public PlayerCricketProfile? Cricket { get; set; }
}
