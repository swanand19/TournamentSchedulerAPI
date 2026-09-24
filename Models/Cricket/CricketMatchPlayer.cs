using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// One player in one match's squad — the cricket counterpart of <see cref="MatchPlayer"/>.
///
/// Captaincy and the gloves live here rather than on the player because both are appointments for
/// a given XI: a keeper who does not keep this week, or a stand-in captain, is ordinary cricket and
/// a player-level flag cannot express it.
/// </summary>
public class CricketMatchPlayer
{
    public int Id { get; set; }

    public int CricketMatchId { get; set; }
    [JsonIgnore]
    public CricketMatch? Match { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public int TeamId { get; set; }
    [JsonIgnore]
    public Team? Team { get; set; }

    /// <summary>
    /// Denormalised for the same reason the fixture carries team names: a scorecard is a record of
    /// what happened, and must still read correctly if the squad list changes afterwards.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    public CricketSquadStatus SquadStatus { get; set; } = CricketSquadStatus.Playing;

    public bool IsCaptain { get; set; }
    public bool IsWicketKeeper { get; set; }

    /// <summary>1-based, and only a plan — the order actually used is whatever the scorer sends in.</summary>
    public int? BattingOrder { get; set; }
}
