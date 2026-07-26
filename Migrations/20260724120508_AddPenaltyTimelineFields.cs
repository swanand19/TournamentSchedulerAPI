using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPenaltyTimelineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PenaltyAwayScoreAfter",
                table: "MatchEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyHomeScoreAfter",
                table: "MatchEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PenaltyScored",
                table: "MatchEvents",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinalWhistleMinute",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPenaltyShootout",
                table: "Matches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyTakersPerSide",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyWinnerTeamId",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PenaltyKicks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<int>(type: "int", nullable: true),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    IsSuddenDeath = table.Column<bool>(type: "bit", nullable: false),
                    Scored = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyKicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenaltyKicks_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyKicks_MatchId",
                table: "PenaltyKicks",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenaltyKicks");

            migrationBuilder.DropColumn(
                name: "PenaltyAwayScoreAfter",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "PenaltyHomeScoreAfter",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "PenaltyScored",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "FinalWhistleMinute",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "IsPenaltyShootout",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PenaltyTakersPerSide",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PenaltyWinnerTeamId",
                table: "Matches");
        }
    }
}
