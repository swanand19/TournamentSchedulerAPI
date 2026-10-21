-- Phase 6D (2026-10-16): notifications. Each row is one line in a person's inbox (the bell on Home):
-- scoring handovers, being added to a tournament or a team, invites taken up, all matches played,
-- end-date reminders. See docs/phase-6-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 08-tournament-invites.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: a new, empty table.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddTournamentInvites AddNotifications --context TournamentDbContext --idempotent --output deploy/sql/09-notifications.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    CREATE TABLE [Notifications] (
        [NotificationId] bigint NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Body] nvarchar(1000) NOT NULL,
        [TournamentId] int NULL,
        [Sport] nvarchar(20) NULL,
        [MatchId] int NULL,
        [DedupeKey] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ReadAt] datetime2 NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([NotificationId]),
        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_CreatedAt] ON [Notifications] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Notifications_DedupeKey] ON [Notifications] ([DedupeKey]) WHERE [DedupeKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_NotificationId] ON [Notifications] ([UserId], [NotificationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016102757_AddNotifications', N'10.0.9');
END;

COMMIT;
GO

