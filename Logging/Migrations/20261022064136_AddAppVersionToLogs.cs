using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Logging.Migrations
{
    /// <inheritdoc />
    public partial class AddAppVersionToLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                table: "MBActivityLogs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                table: "ErrorLogs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "ErrorLogs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppVersion",
                table: "MBActivityLogs");

            migrationBuilder.DropColumn(
                name: "AppVersion",
                table: "ErrorLogs");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "ErrorLogs");
        }
    }
}
