using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStartedMatchToMatchPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StartedMatch",
                table: "MatchPlayers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Reconstruct the starting line-up for matches played before this was recorded, so their
            // appearance and minute stats are right. A player started if they are still on the pitch,
            // were substituted off, or were sent off — and did not arrive as a substitute.
            //   SquadStatus: 0 Starting, 1 Bench, 2 Unavailable, 3 SubstitutedOff, 4 SentOff
            //   MatchEventType 3 = SubstitutionIn, whose RelatedPlayerId is the player coming on.
            // Not perfectly recoverable for a rolling-subs match where a starter was taken off: that
            // puts them back on the bench, leaving no trace of the original line-up.
            migrationBuilder.Sql(@"
                UPDATE mp
                SET mp.[StartedMatch] = 1
                FROM [MatchPlayers] mp
                WHERE mp.[SquadStatus] IN (0, 3, 4)
                  AND NOT EXISTS (
                      SELECT 1 FROM [MatchEvents] e
                      WHERE e.[MatchId] = mp.[MatchId]
                        AND e.[EventType] = 3
                        AND e.[RelatedPlayerId] = mp.[PlayerId]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartedMatch",
                table: "MatchPlayers");
        }
    }
}
