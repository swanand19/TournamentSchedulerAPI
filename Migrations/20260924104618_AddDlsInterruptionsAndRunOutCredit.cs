using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDlsInterruptionsAndRunOutCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDirectHit",
                table: "CricketBalls",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RunOutReceiverId",
                table: "CricketBalls",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CricketInterruptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InningsId = table.Column<int>(type: "int", nullable: false),
                    AtLegalBalls = table.Column<int>(type: "int", nullable: false),
                    WicketsAtTime = table.Column<int>(type: "int", nullable: false),
                    BallsAllowedBefore = table.Column<int>(type: "int", nullable: false),
                    BallsAllowedAfter = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CricketInterruptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CricketInterruptions_CricketInnings_InningsId",
                        column: x => x.InningsId,
                        principalTable: "CricketInnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CricketInterruptions_InningsId",
                table: "CricketInterruptions",
                column: "InningsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CricketInterruptions");

            migrationBuilder.DropColumn(
                name: "IsDirectHit",
                table: "CricketBalls");

            migrationBuilder.DropColumn(
                name: "RunOutReceiverId",
                table: "CricketBalls");
        }
    }
}
