using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodStateAndFixtureRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "SavedFixtures",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinPlayersPerSide",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PeriodState",
                table: "Matches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill so matches already under way keep working. Before this migration a paused
            // match could mean either a mid-period stoppage or the interval; the safe reading is
            // "stopped mid-period", which lets the referee end the period explicitly.
            //   MatchStatus: 0 NotStarted, 1 InProgress, 2 Paused, 3 Completed, 4 PenaltyShootout
            //   PeriodState: 0 NotStarted, 1 InPlay,     2 Stopped, 3 Ended
            migrationBuilder.Sql(@"
                UPDATE [Matches]
                SET [PeriodState] = CASE [Status]
                    WHEN 1 THEN 1
                    WHEN 2 THEN 2
                    WHEN 3 THEN 3
                    WHEN 4 THEN 3
                    ELSE 0
                END;");

            // Previously any send-off only abandoned the match once a side had nobody left.
            migrationBuilder.Sql(@"
                UPDATE [Matches]
                SET [MinPlayersPerSide] = 1
                WHERE [Status] <> 0 AND [MinPlayersPerSide] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Round",
                table: "SavedFixtures");

            migrationBuilder.DropColumn(
                name: "MinPlayersPerSide",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PeriodState",
                table: "Matches");
        }
    }
}
