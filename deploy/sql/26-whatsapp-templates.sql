-- WhatsApp templates in the database (2026-11-26): NotificationTemplates.WhatsAppTemplateName, WhatsAppLanguage,
-- WhatsAppParameters. Which approved Meta template each message goes out as moves from appsettings
-- (WhatsApp:CodeTemplate / InviteTemplate) to the message's own row, edited on the admin portal's Messages page.
-- The three code messages are filled with otp_template_kickcricket / en (the template already in use), so numbers
-- keep signing in across the release; the tournament invite stays empty until its template is approved.
--
-- Run in SSMS AFTER 25-retire-between-balls.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA, or the
--     portal's Query editor).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script RetireBetweenBalls AddWhatsAppTemplates --context TournamentDbContext --idempotent --output deploy/sql/26-whatsapp-templates.sql
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126062907_AddWhatsAppTemplates'
)
BEGIN
    ALTER TABLE [NotificationTemplates] ADD [WhatsAppLanguage] nvarchar(15) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126062907_AddWhatsAppTemplates'
)
BEGIN
    ALTER TABLE [NotificationTemplates] ADD [WhatsAppParameters] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126062907_AddWhatsAppTemplates'
)
BEGIN
    ALTER TABLE [NotificationTemplates] ADD [WhatsAppTemplateName] nvarchar(512) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126062907_AddWhatsAppTemplates'
)
BEGIN
    EXEC(N'UPDATE [NotificationTemplates] SET [WhatsAppTemplateName] = N''otp_template_kickcricket'', [WhatsAppLanguage] = N''en'' WHERE [Code] IN (N''SignInCode'', N''AddContactCode'', N''DeleteAccountCode'') AND [WhatsAppTemplateName] IS NULL')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261126062907_AddWhatsAppTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261126062907_AddWhatsAppTemplates', N'10.0.9');
END;

COMMIT;
GO

