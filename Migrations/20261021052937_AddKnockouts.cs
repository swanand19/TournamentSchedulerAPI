using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddKnockouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SavedFixtureId",
                table: "Matches",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "KnockoutFixtureId",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoundLabel",
                table: "Matches",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SavedFixtureId",
                table: "CricketMatches",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "KnockoutFixtureId",
                table: "CricketMatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoundLabel",
                table: "CricketMatches",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KnockoutFixtures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Home_Kind = table.Column<int>(type: "int", nullable: false),
                    Home_TeamId = table.Column<int>(type: "int", nullable: true),
                    Home_GroupName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Home_Place = table.Column<int>(type: "int", nullable: true),
                    Home_FromOrder = table.Column<int>(type: "int", nullable: true),
                    Home_OverrideTeamId = table.Column<int>(type: "int", nullable: true),
                    Away_Kind = table.Column<int>(type: "int", nullable: false),
                    Away_TeamId = table.Column<int>(type: "int", nullable: true),
                    Away_GroupName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Away_Place = table.Column<int>(type: "int", nullable: true),
                    Away_FromOrder = table.Column<int>(type: "int", nullable: true),
                    Away_OverrideTeamId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnockoutFixtures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnockoutFixtures_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_KnockoutFixtureId",
                table: "Matches",
                column: "KnockoutFixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_CricketMatches_KnockoutFixtureId",
                table: "CricketMatches",
                column: "KnockoutFixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_KnockoutFixtures_TournamentId_Order",
                table: "KnockoutFixtures",
                columns: new[] { "TournamentId", "Order" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CricketMatches_KnockoutFixtures_KnockoutFixtureId",
                table: "CricketMatches",
                column: "KnockoutFixtureId",
                principalTable: "KnockoutFixtures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_KnockoutFixtures_KnockoutFixtureId",
                table: "Matches",
                column: "KnockoutFixtureId",
                principalTable: "KnockoutFixtures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CricketMatches_KnockoutFixtures_KnockoutFixtureId",
                table: "CricketMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_KnockoutFixtures_KnockoutFixtureId",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "KnockoutFixtures");

            migrationBuilder.DropIndex(
                name: "IX_Matches_KnockoutFixtureId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_CricketMatches_KnockoutFixtureId",
                table: "CricketMatches");

            migrationBuilder.DropColumn(
                name: "KnockoutFixtureId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RoundLabel",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "KnockoutFixtureId",
                table: "CricketMatches");

            migrationBuilder.DropColumn(
                name: "RoundLabel",
                table: "CricketMatches");

            migrationBuilder.AlterColumn<int>(
                name: "SavedFixtureId",
                table: "Matches",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SavedFixtureId",
                table: "CricketMatches",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
