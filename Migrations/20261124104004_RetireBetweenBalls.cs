using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class RetireBetweenBalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BetweenBalls",
                table: "CricketBalls",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BetweenBalls",
                table: "CricketBalls");
        }
    }
}
