using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Logging.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "MBMiddlewareLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "MBActivityLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "ErrorLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MBActivityLogs_UserId",
                table: "MBActivityLogs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MBActivityLogs_UserId",
                table: "MBActivityLogs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "MBMiddlewareLogs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "MBActivityLogs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ErrorLogs");
        }
    }
}
