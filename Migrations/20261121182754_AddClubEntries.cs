using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClubEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EntriesOpen",
                table: "Tournaments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EntryCode",
                table: "Tournaments",
                type: "varchar(9)",
                unicode: false,
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClubPlayerId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicCode",
                table: "Clubs",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentEntries",
                columns: table => new
                {
                    TournamentEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TeamId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentEntries", x => x.TournamentEntryId);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "ClubId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TournamentEntryPlayers",
                columns: table => new
                {
                    TournamentEntryPlayerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentEntryId = table.Column<int>(type: "int", nullable: false),
                    ClubPlayerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentEntryPlayers", x => x.TournamentEntryPlayerId);
                    table.ForeignKey(
                        name: "FK_TournamentEntryPlayers_TournamentEntries_TournamentEntryId",
                        column: x => x.TournamentEntryId,
                        principalTable: "TournamentEntries",
                        principalColumn: "TournamentEntryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_EntryCode",
                table: "Tournaments",
                column: "EntryCode",
                unique: true,
                filter: "[EntryCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_PublicCode",
                table: "Clubs",
                column: "PublicCode",
                unique: true,
                filter: "[PublicCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_ClubId",
                table: "TournamentEntries",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TeamId",
                table: "TournamentEntries",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TournamentId_ClubId",
                table: "TournamentEntries",
                columns: new[] { "TournamentId", "ClubId" },
                unique: true,
                filter: "[Status] IN ('Waiting', 'Accepted')");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntryPlayers_ClubPlayerId",
                table: "TournamentEntryPlayers",
                column: "ClubPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntryPlayers_TournamentEntryId_ClubPlayerId",
                table: "TournamentEntryPlayers",
                columns: new[] { "TournamentEntryId", "ClubPlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentEntryPlayers");

            migrationBuilder.DropTable(
                name: "TournamentEntries");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_EntryCode",
                table: "Tournaments");

            migrationBuilder.DropIndex(
                name: "IX_Clubs_PublicCode",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "EntriesOpen",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "EntryCode",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "ClubPlayerId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "PublicCode",
                table: "Clubs");
        }
    }
}
