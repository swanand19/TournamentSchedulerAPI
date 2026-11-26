using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppLanguage",
                table: "NotificationTemplates",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppParameters",
                table: "NotificationTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppTemplateName",
                table: "NotificationTemplates",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            // The code messages' WhatsApp template moves here from appsettings (WhatsApp:CodeTemplate), so
            // their rows start with the one already in use: numbers keep signing in across the release. The
            // invite is left empty until its template is approved. EXEC: the script the user runs is one batch,
            // and SQL Server rejects a batch naming a column that doesn't exist yet (CLAUDE.md, Gotchas).
            migrationBuilder.Sql(
                "EXEC(N'UPDATE [NotificationTemplates] SET [WhatsAppTemplateName] = N''otp_template_kickcricket'', [WhatsAppLanguage] = N''en'' "
                + "WHERE [Code] IN (N''SignInCode'', N''AddContactCode'', N''DeleteAccountCode'') AND [WhatsAppTemplateName] IS NULL')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WhatsAppLanguage",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "WhatsAppParameters",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateName",
                table: "NotificationTemplates");
        }
    }
}
