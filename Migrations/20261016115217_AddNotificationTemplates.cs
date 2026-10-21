using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailAttempts",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmailBody",
                table: "Notifications",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailSentAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSubject",
                table: "Notifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailTo",
                table: "Notifications",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InInbox",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: true); // notifications made before templates were all inbox ones

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    NotificationTemplateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsEmail = table.Column<bool>(type: "bit", nullable: false),
                    EmailSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmailBody = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsSms = table.Column<bool>(type: "bit", nullable: false),
                    SmsBody = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SmsDltTemplateId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsNotification = table.Column<bool>(type: "bit", nullable: false),
                    NotificationTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NotificationBody = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsPush = table.Column<bool>(type: "bit", nullable: false),
                    PushTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PushBody = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.NotificationTemplateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EmailSentAt",
                table: "Notifications",
                column: "EmailSentAt",
                filter: "[EmailTo] IS NOT NULL AND [EmailSentAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_Code",
                table: "NotificationTemplates",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_EmailSentAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailAttempts",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailBody",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailSentAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailSubject",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailTo",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "InInbox",
                table: "Notifications");
        }
    }
}
