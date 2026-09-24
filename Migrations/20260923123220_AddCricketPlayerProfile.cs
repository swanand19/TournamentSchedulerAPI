using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCricketPlayerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultCaptainPlayerId",
                table: "Teams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerCricketProfiles",
                columns: table => new
                {
                    PlayerId = table.Column<int>(type: "int", nullable: false),
                    PrimaryRole = table.Column<int>(type: "int", nullable: false),
                    BattingStyle = table.Column<int>(type: "int", nullable: true),
                    BowlingArm = table.Column<int>(type: "int", nullable: true),
                    BowlingType = table.Column<int>(type: "int", nullable: true),
                    BattingOrderPreference = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCricketProfiles", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_PlayerCricketProfiles_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCricketProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultCaptainPlayerId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "Players");
        }
    }
}
