using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCricketMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CricketMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    SavedFixtureId = table.Column<int>(type: "int", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchNumber = table.Column<int>(type: "int", nullable: false),
                    HomeTeamId = table.Column<int>(type: "int", nullable: true),
                    AwayTeamId = table.Column<int>(type: "int", nullable: true),
                    HomeTeamName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AwayTeamName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Rules_Format = table.Column<int>(type: "int", nullable: false),
                    Rules_InningsPerSide = table.Column<int>(type: "int", nullable: false),
                    Rules_OversPerInnings = table.Column<int>(type: "int", nullable: true),
                    Rules_BallsPerOver = table.Column<int>(type: "int", nullable: false),
                    Rules_MaxOversPerBowler = table.Column<int>(type: "int", nullable: true),
                    Rules_PlayersPerSide = table.Column<int>(type: "int", nullable: false),
                    Rules_LastManStanding = table.Column<bool>(type: "bit", nullable: false),
                    Rules_WidePenaltyRuns = table.Column<int>(type: "int", nullable: false),
                    Rules_NoBallPenaltyRuns = table.Column<int>(type: "int", nullable: false),
                    Rules_FreeHitAfterNoBall = table.Column<bool>(type: "bit", nullable: false),
                    Rules_FreeHitAfterWide = table.Column<bool>(type: "bit", nullable: false),
                    Rules_ByesAllowed = table.Column<bool>(type: "bit", nullable: false),
                    Rules_LegByesAllowed = table.Column<bool>(type: "bit", nullable: false),
                    Rules_PenaltyRunsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    Rules_OverthrowsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    Rules_LbwEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Rules_PowerplayOvers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rules_TieResolution = table.Column<int>(type: "int", nullable: false),
                    Rules_DrawAllowed = table.Column<bool>(type: "bit", nullable: false),
                    Rules_FollowOnMargin = table.Column<int>(type: "int", nullable: true),
                    Rules_DlsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Rules_BallType = table.Column<int>(type: "int", nullable: false),
                    Rules_PitchType = table.Column<int>(type: "int", nullable: false),
                    TossWinnerTeamId = table.Column<int>(type: "int", nullable: true),
                    TossDecision = table.Column<int>(type: "int", nullable: true),
                    CurrentInningsNumber = table.Column<int>(type: "int", nullable: false),
                    WinnerTeamId = table.Column<int>(type: "int", nullable: true),
                    IsTie = table.Column<bool>(type: "bit", nullable: false),
                    IsNoResult = table.Column<bool>(type: "bit", nullable: false),
                    IsDraw = table.Column<bool>(type: "bit", nullable: false),
                    WonByDls = table.Column<bool>(type: "bit", nullable: false),
                    ForfeitWinnerTeamId = table.Column<int>(type: "int", nullable: true),
                    ResultSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CricketMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CricketMatches_SavedFixtures_SavedFixtureId",
                        column: x => x.SavedFixtureId,
                        principalTable: "SavedFixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CricketMatches_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CricketMatches_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CricketMatches_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatches_AwayTeamId",
                table: "CricketMatches",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatches_HomeTeamId",
                table: "CricketMatches",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatches_SavedFixtureId",
                table: "CricketMatches",
                column: "SavedFixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatches_TournamentId",
                table: "CricketMatches",
                column: "TournamentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CricketMatches");
        }
    }
}
