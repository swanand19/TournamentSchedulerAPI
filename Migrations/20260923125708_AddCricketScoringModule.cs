using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCricketScoringModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FollowOnEnforced",
                table: "CricketMatches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CricketInnings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CricketMatchId = table.Column<int>(type: "int", nullable: false),
                    InningsNumber = table.Column<int>(type: "int", nullable: false),
                    BattingTeamId = table.Column<int>(type: "int", nullable: false),
                    BowlingTeamId = table.Column<int>(type: "int", nullable: false),
                    BattingTeamName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BowlingTeamName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BattingTeamInningsIndex = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EndReason = table.Column<int>(type: "int", nullable: true),
                    BallsPerOver = table.Column<int>(type: "int", nullable: false),
                    OversLimit = table.Column<int>(type: "int", nullable: true),
                    WicketsToEndInnings = table.Column<int>(type: "int", nullable: false),
                    BattingSideSize = table.Column<int>(type: "int", nullable: false),
                    MaxOversPerBowler = table.Column<int>(type: "int", nullable: true),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    Wickets = table.Column<int>(type: "int", nullable: false),
                    LegalBalls = table.Column<int>(type: "int", nullable: false),
                    Wides = table.Column<int>(type: "int", nullable: false),
                    NoBalls = table.Column<int>(type: "int", nullable: false),
                    Byes = table.Column<int>(type: "int", nullable: false),
                    LegByes = table.Column<int>(type: "int", nullable: false),
                    PenaltyRuns = table.Column<int>(type: "int", nullable: false),
                    OpeningStrikerId = table.Column<int>(type: "int", nullable: true),
                    OpeningNonStrikerId = table.Column<int>(type: "int", nullable: true),
                    OpeningBowlerId = table.Column<int>(type: "int", nullable: true),
                    StrikerId = table.Column<int>(type: "int", nullable: true),
                    NonStrikerId = table.Column<int>(type: "int", nullable: true),
                    CurrentBowlerId = table.Column<int>(type: "int", nullable: true),
                    PreviousBowlerId = table.Column<int>(type: "int", nullable: true),
                    FreeHitPending = table.Column<bool>(type: "bit", nullable: false),
                    LoneBatter = table.Column<bool>(type: "bit", nullable: false),
                    Target = table.Column<int>(type: "int", nullable: true),
                    IsFollowOn = table.Column<bool>(type: "bit", nullable: false),
                    IsSuperOver = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CricketInnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CricketInnings_CricketMatches_CricketMatchId",
                        column: x => x.CricketMatchId,
                        principalTable: "CricketMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CricketMatchEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CricketMatchId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    InningsNumber = table.Column<int>(type: "int", nullable: true),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    PlayerId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CricketMatchEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CricketMatchEvents_CricketMatches_CricketMatchId",
                        column: x => x.CricketMatchId,
                        principalTable: "CricketMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CricketMatchPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CricketMatchId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    PlayerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SquadStatus = table.Column<int>(type: "int", nullable: false),
                    IsCaptain = table.Column<bool>(type: "bit", nullable: false),
                    IsWicketKeeper = table.Column<bool>(type: "bit", nullable: false),
                    BattingOrder = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CricketMatchPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CricketMatchPlayers_CricketMatches_CricketMatchId",
                        column: x => x.CricketMatchId,
                        principalTable: "CricketMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CricketMatchPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CricketMatchPlayers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CricketBalls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InningsId = table.Column<int>(type: "int", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    OverNumber = table.Column<int>(type: "int", nullable: false),
                    BallInOver = table.Column<int>(type: "int", nullable: false),
                    BowlerId = table.Column<int>(type: "int", nullable: false),
                    StrikerId = table.Column<int>(type: "int", nullable: false),
                    NonStrikerId = table.Column<int>(type: "int", nullable: true),
                    RunsOffBat = table.Column<int>(type: "int", nullable: false),
                    IsWide = table.Column<bool>(type: "bit", nullable: false),
                    IsNoBall = table.Column<bool>(type: "bit", nullable: false),
                    WideExtraRuns = table.Column<int>(type: "int", nullable: false),
                    Byes = table.Column<int>(type: "int", nullable: false),
                    LegByes = table.Column<int>(type: "int", nullable: false),
                    PenaltyRuns = table.Column<int>(type: "int", nullable: false),
                    WidePenalty = table.Column<int>(type: "int", nullable: false),
                    NoBallPenalty = table.Column<int>(type: "int", nullable: false),
                    IsFreeHit = table.Column<bool>(type: "bit", nullable: false),
                    WicketType = table.Column<int>(type: "int", nullable: true),
                    DismissedPlayerId = table.Column<int>(type: "int", nullable: true),
                    FielderId = table.Column<int>(type: "int", nullable: true),
                    BattersCrossed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CricketBalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CricketBalls_CricketInnings_InningsId",
                        column: x => x.InningsId,
                        principalTable: "CricketInnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CricketBalls_InningsId_SequenceNumber",
                table: "CricketBalls",
                columns: new[] { "InningsId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CricketInnings_CricketMatchId_InningsNumber",
                table: "CricketInnings",
                columns: new[] { "CricketMatchId", "InningsNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatchEvents_CricketMatchId",
                table: "CricketMatchEvents",
                column: "CricketMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatchPlayers_CricketMatchId_PlayerId",
                table: "CricketMatchPlayers",
                columns: new[] { "CricketMatchId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatchPlayers_PlayerId",
                table: "CricketMatchPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatchPlayers_TeamId",
                table: "CricketMatchPlayers",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CricketBalls");

            migrationBuilder.DropTable(
                name: "CricketMatchEvents");

            migrationBuilder.DropTable(
                name: "CricketMatchPlayers");

            migrationBuilder.DropTable(
                name: "CricketInnings");

            migrationBuilder.DropColumn(
                name: "FollowOnEnforced",
                table: "CricketMatches");
        }
    }
}
