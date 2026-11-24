using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LeftOn",
                table: "Players",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamAdmins",
                columns: table => new
                {
                    TeamAdminId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAdmins", x => x.TeamAdminId);
                    table.ForeignKey(
                        name: "FK_TeamAdmins_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamAdmins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamAdmins_TeamId_UserId",
                table: "TeamAdmins",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamAdmins_TournamentId_UserId",
                table: "TeamAdmins",
                columns: new[] { "TournamentId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamAdmins_UserId",
                table: "TeamAdmins",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamAdmins");

            migrationBuilder.DropColumn(
                name: "LeftOn",
                table: "Players");
        }
    }
}
