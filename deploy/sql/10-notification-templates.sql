-- Phase 6D, part 2 (2026-10-16): message templates. NotificationTemplates holds the wording of every
-- message (codes, invites, notifications) per channel, edited in the admin portal; the API adds a row
-- with the built-in wording for each one the first time it runs. Notifications gains InInbox and the
-- columns for a notification that also goes by email. See docs/phase-6-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 09-notifications.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: a new, empty table; existing notifications stay in the inbox.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddNotifications AddNotificationTemplates --context TournamentDbContext --idempotent --output deploy/sql/10-notification-templates.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailAttempts] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailBody] nvarchar(4000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailSentAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailSubject] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailTo] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [InInbox] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    CREATE TABLE [NotificationTemplates] (
        [NotificationTemplateId] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [IsEmail] bit NOT NULL,
        [EmailSubject] nvarchar(200) NULL,
        [EmailBody] nvarchar(4000) NULL,
        [IsSms] bit NOT NULL,
        [SmsBody] nvarchar(1000) NULL,
        [SmsDltTemplateId] nvarchar(50) NULL,
        [IsNotification] bit NOT NULL,
        [NotificationTitle] nvarchar(200) NULL,
        [NotificationBody] nvarchar(1000) NULL,
        [IsPush] bit NOT NULL,
        [PushTitle] nvarchar(100) NULL,
        [PushBody] nvarchar(500) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_NotificationTemplates] PRIMARY KEY ([NotificationTemplateId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Notifications_EmailSentAt] ON [Notifications] ([EmailSentAt]) WHERE [EmailTo] IS NOT NULL AND [EmailSentAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationTemplates_Code] ON [NotificationTemplates] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016115217_AddNotificationTemplates', N'10.0.9');
END;

COMMIT;
GO

