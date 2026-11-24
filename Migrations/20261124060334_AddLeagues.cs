using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeagueId",
                table: "Tournaments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BroughtBackFromTeamId",
                table: "Teams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeagueId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    LeagueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Sport = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    LogoId = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    PublicCode = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.LeagueId);
                });

            migrationBuilder.CreateTable(
                name: "LeagueOrganisers",
                columns: table => new
                {
                    LeagueOrganiserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueOrganisers", x => x.LeagueOrganiserId);
                    table.ForeignKey(
                        name: "FK_LeagueOrganisers_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueOrganisers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_LeagueId",
                table: "Tournaments",
                column: "LeagueId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_BroughtBackFromTeamId",
                table: "Teams",
                column: "BroughtBackFromTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueOrganisers_LeagueId_UserId",
                table: "LeagueOrganisers",
                columns: new[] { "LeagueId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueOrganisers_UserId",
                table: "LeagueOrganisers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_PublicCode",
                table: "Leagues",
                column: "PublicCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tournaments_Leagues_LeagueId",
                table: "Tournaments",
                column: "LeagueId",
                principalTable: "Leagues",
                principalColumn: "LeagueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tournaments_Leagues_LeagueId",
                table: "Tournaments");

            migrationBuilder.DropTable(
                name: "LeagueOrganisers");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_LeagueId",
                table: "Tournaments");

            migrationBuilder.DropIndex(
                name: "IX_Teams_BroughtBackFromTeamId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "LeagueId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "BroughtBackFromTeamId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "LeagueId",
                table: "Notifications");
        }
    }
}
