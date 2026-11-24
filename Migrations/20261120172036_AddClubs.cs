using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClubs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "Teams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClubPlayerId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    ClubId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Sport = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    JoinCode = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    LogoId = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.ClubId);
                });

            migrationBuilder.CreateTable(
                name: "ClubAdmins",
                columns: table => new
                {
                    ClubAdminId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubAdmins", x => x.ClubAdminId);
                    table.ForeignKey(
                        name: "FK_ClubAdmins_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "ClubId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClubAdmins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubPlayers",
                columns: table => new
                {
                    ClubPlayerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JerseyNumber = table.Column<int>(type: "int", nullable: true),
                    Position = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsCaptain = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    EmailId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Cricket_PrimaryRole = table.Column<int>(type: "int", nullable: true),
                    Cricket_BattingStyle = table.Column<int>(type: "int", nullable: true),
                    Cricket_BowlingArm = table.Column<int>(type: "int", nullable: true),
                    Cricket_BowlingType = table.Column<int>(type: "int", nullable: true),
                    Cricket_BattingOrderPreference = table.Column<int>(type: "int", nullable: true),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubPlayers", x => x.ClubPlayerId);
                    table.ForeignKey(
                        name: "FK_ClubPlayers_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "ClubId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClubPlayers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ClubId",
                table: "Teams",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ClubPlayerId",
                table: "Players",
                column: "ClubPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubAdmins_ClubId_UserId",
                table: "ClubAdmins",
                columns: new[] { "ClubId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubAdmins_UserId",
                table: "ClubAdmins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubPlayers_ClubId_UserId",
                table: "ClubPlayers",
                columns: new[] { "ClubId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClubPlayers_EmailId",
                table: "ClubPlayers",
                column: "EmailId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubPlayers_PhoneNumber",
                table: "ClubPlayers",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ClubPlayers_UserId",
                table: "ClubPlayers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_JoinCode",
                table: "Clubs",
                column: "JoinCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_ClubPlayers_ClubPlayerId",
                table: "Players",
                column: "ClubPlayerId",
                principalTable: "ClubPlayers",
                principalColumn: "ClubPlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_Clubs_ClubId",
                table: "Teams",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "ClubId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_ClubPlayers_ClubPlayerId",
                table: "Players");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_Clubs_ClubId",
                table: "Teams");

            migrationBuilder.DropTable(
                name: "ClubAdmins");

            migrationBuilder.DropTable(
                name: "ClubPlayers");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropIndex(
                name: "IX_Teams_ClubId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Players_ClubPlayerId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ClubPlayerId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Notifications");
        }
    }
}
