-- App versions (2026-10-22, HLD P-25): the admin portal's "App versions" page keeps which phone-app
-- versions are released and whether older ones must update. AppStarts holds one row per release and
-- platform; UserSessions gains AppVersion (the version each signed-in device last used), so the portal
-- can say how many devices a forced update would stop. One backend still serves every version.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 13-public-tournaments.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: AppStarts starts empty, so nobody is asked to update until a release is added in the portal.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddPublicTournaments AddAppStarts --context TournamentDbContext --idempotent --output deploy/sql/14-app-starts.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    ALTER TABLE [UserSessions] ADD [AppVersion] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    CREATE TABLE [AppStarts] (
        [AppStartId] int NOT NULL IDENTITY,
        [Platform] nvarchar(20) NOT NULL,
        [AppVersion] nvarchar(20) NOT NULL,
        [ApiVersion] nvarchar(20) NULL,
        [Description] nvarchar(500) NULL,
        [IsForceUpdate] bit NOT NULL,
        [ReleaseDate] datetime2 NOT NULL,
        [StoreUrl] nvarchar(500) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_AppStarts] PRIMARY KEY ([AppStartId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppStarts_Platform_AppVersion] ON [AppStarts] ([Platform], [AppVersion]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022060521_AddAppStarts', N'10.0.9');
END;

COMMIT;
GO

