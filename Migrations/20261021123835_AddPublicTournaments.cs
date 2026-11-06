using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicTournaments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Tournaments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicCode",
                table: "Tournaments",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentFollows",
                columns: table => new
                {
                    TournamentFollowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentFollows", x => x.TournamentFollowId);
                    table.ForeignKey(
                        name: "FK_TournamentFollows_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentFollows_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_PublicCode",
                table: "Tournaments",
                column: "PublicCode",
                unique: true,
                filter: "[PublicCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentFollows_TournamentId_UserId",
                table: "TournamentFollows",
                columns: new[] { "TournamentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentFollows_UserId",
                table: "TournamentFollows",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentFollows");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_PublicCode",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "PublicCode",
                table: "Tournaments");
        }
    }
}
