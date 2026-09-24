using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models
{
    public class Team
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int TournamentId { get; set; }
        [JsonIgnore]
        public Tournament? Tournament { get; set; }

        /// <summary>
        /// Who normally captains the side, used to pre-select the captain when an XI is picked.
        /// The appointment that counts is the per-match one on CricketMatchPlayer; this is only a
        /// default. No foreign key: Tournament already cascades to Team and to Player, and a third
        /// path into Player is what SQL Server rejects.
        /// </summary>
        public int? DefaultCaptainPlayerId { get; set; }

        public List<Player> Players { get; set; } = new();
    }
}