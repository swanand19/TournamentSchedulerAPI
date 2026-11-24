using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <summary>
    /// The app's public name changed from "Tournament Scheduler" to "KickCricket" (2026-11-18). The built-in wording
    /// lives in NotificationTemplateCatalog, but every database already has a saved copy of each template, which is what
    /// gets sent — so the name is replaced in those rows too. Only the name changes: anything an admin reworded in the
    /// portal keeps their wording.
    /// </summary>
    public partial class RenameAppToKickCricket : Migration
    {
        private static readonly string[] Texts =
            ["EmailSubject", "EmailBody", "SmsBody", "NotificationTitle", "NotificationBody", "PushTitle", "PushBody"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Replace("Tournament Scheduler", "KickCricket"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Replace("KickCricket", "Tournament Scheduler"));

        private static string Replace(string from, string to) =>
            "UPDATE [NotificationTemplates] SET "
            + string.Join(", ", Texts.Select(c => $"[{c}] = REPLACE([{c}], N'{from}', N'{to}')"))
            + ";";
    }
}
