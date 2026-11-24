-- Following people (2026-11-23, item 6): who follows whom (UserFollows) and the profile a "started following you"
-- notification opens (Notifications.ProfileUserId).
--
-- Run in SSMS AFTER 21-club-entries.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again. One new table and one new column (empty for existing rows); nothing existing changes.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddClubEntries AddFollows --context TournamentDbContext --idempotent --output deploy/sql/22-follows.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    ALTER TABLE [Notifications] ADD [ProfileUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    CREATE TABLE [UserFollows] (
        [FollowerUserId] int NOT NULL,
        [FollowedUserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        CONSTRAINT [PK_UserFollows] PRIMARY KEY ([FollowerUserId], [FollowedUserId]),
        CONSTRAINT [FK_UserFollows_Users_FollowedUserId] FOREIGN KEY ([FollowedUserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserFollows_Users_FollowerUserId] FOREIGN KEY ([FollowerUserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    CREATE INDEX [IX_UserFollows_FollowedUserId] ON [UserFollows] ([FollowedUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123110634_AddFollows', N'10.0.9');
END;

COMMIT;
GO

