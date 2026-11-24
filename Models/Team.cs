using System.ComponentModel.DataAnnotations.Schema;
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

        /// <summary>
        /// Six characters a player types to join this team (no 0/O or 1/I). Made with the team
        /// (older teams got one in the migration that added it); owners can reset it. Never
        /// serialised: owners see it as <see cref="VisibleJoinCode"/>.
        /// </summary>
        [JsonIgnore]
        public string? JoinCode { get; set; }

        /// <summary>The join code, for the tournament's owners only — filled per request.</summary>
        [NotMapped]
        [JsonPropertyName("joinCode")]
        public string? VisibleJoinCode { get; set; }

        /// <summary>
        /// The join code as a link to share (2026-11-23), for whoever may change the squad while the tournament isn't
        /// over. A team kept from a lasting one shares the lasting team's link instead (one link per real team).
        /// </summary>
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Public.InviteLinkDto? Invite { get; set; }

        /// <summary>The squad: everyone still in it (players who left are in <see cref="LeftPlayers"/>).</summary>
        public List<Player> Players { get; set; } = new();

        /// <summary>
        /// Players left out after they had played (<see cref="Player.LeftOn"/>): kept for the scorecards, shown
        /// apart so a team's admins can bring one back. Filled per request.
        /// </summary>
        /// Null (left out of the JSON) wherever a team is only part of another answer, such as a match.
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Player>? LeftPlayers { get; set; }

        /// <summary>The people who run this team inside the tournament (2026-11-20, step 1 of the lasting-teams plan).</summary>
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Accounts.TeamAdminDto>? Admins { get; set; }

        /// <summary>The caller may change this squad: an owner, or one of its admins. The apps draw the squad's buttons from it.</summary>
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CanManage { get; set; }

        /// <summary>The caller may add or remove this team's admins: the tournament's owners.</summary>
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CanManageAdmins { get; set; }

        /// <summary>
        /// The lasting team (club) this squad belongs to (2026-11-20, step 2 of the lasting-teams plan): kept from this
        /// tournament, or (step 3) entered with a code. Its admins manage this squad too. Null for a one-off team.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ClubId { get; set; }

        [JsonIgnore]
        public Clubs.Club? Club { get; set; }

        /// <summary>
        /// The earlier season's team this one was brought back from (leagues, 2026-11-24): what carries a team's league
        /// record and titles from season to season. A team with an old name but no link starts its own record. No foreign
        /// key, like <see cref="DefaultCaptainPlayerId"/>: Tournament already cascades to Team, and an earlier team may go.
        /// </summary>
        [JsonIgnore]
        public int? BroughtBackFromTeamId { get; set; }

        /// <summary>The caller may keep this team for other tournaments (C-23): one of its admins, while it isn't kept yet.</summary>
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CanKeep { get; set; }
    }
}
