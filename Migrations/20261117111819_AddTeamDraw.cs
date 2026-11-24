using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamDraw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "PlayerPools",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MovedFromTeamId",
                table: "AuctionLots",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamDraws",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TeamOrder = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DrawerUserId = table.Column<int>(type: "int", nullable: true),
                    AutoSeconds = table.Column<int>(type: "int", nullable: true),
                    NextAutoAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Times = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDraws", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamDraws_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DrawPicks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DrawId = table.Column<int>(type: "int", nullable: false),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    PoolPlayerId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    TierId = table.Column<int>(type: "int", nullable: true),
                    PickedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LandsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Announced = table.Column<bool>(type: "bit", nullable: false),
                    MovedFromTeamId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawPicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawPicks_TeamDraws_DrawId",
                        column: x => x.DrawId,
                        principalTable: "TeamDraws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrawPicks_DrawId_Order",
                table: "DrawPicks",
                columns: new[] { "DrawId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_DrawPicks_DrawId_PoolPlayerId",
                table: "DrawPicks",
                columns: new[] { "DrawId", "PoolPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawPicks_TournamentId",
                table: "DrawPicks",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDraws_Status_NextAutoAt",
                table: "TeamDraws",
                columns: new[] { "Status", "NextAutoAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamDraws_TournamentId",
                table: "TeamDraws",
                column: "TournamentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrawPicks");

            migrationBuilder.DropTable(
                name: "TeamDraws");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "PlayerPools");

            migrationBuilder.DropColumn(
                name: "MovedFromTeamId",
                table: "AuctionLots");
        }
    }
}
