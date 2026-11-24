-- Leagues and seasons (2026-11-24): the Leagues and LeagueOrganisers tables, Tournaments.LeagueId (which league a
-- tournament is a season of), Teams.BroughtBackFromTeamId (the earlier season's team a team was brought back from) and
-- Notifications.LeagueId (the "made an organiser" news opens the league). No data changes: every tournament stays a
-- one-off until an organiser ticks it into a league.
--
-- Run in SSMS AFTER 23-every-tournament-public.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script EveryTournamentPublic AddLeagues --context TournamentDbContext --idempotent --output deploy/sql/24-leagues.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [LeagueId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Teams] ADD [BroughtBackFromTeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Notifications] ADD [LeagueId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE TABLE [Leagues] (
        [LeagueId] int NOT NULL IDENTITY,
        [Name] nvarchar(60) NOT NULL,
        [ShortName] nvarchar(8) NOT NULL,
        [Sport] nvarchar(20) NOT NULL,
        [Area] nvarchar(60) NULL,
        [LogoId] varchar(40) NULL,
        [PublicCode] varchar(8) NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Leagues] PRIMARY KEY ([LeagueId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE TABLE [LeagueOrganisers] (
        [LeagueOrganiserId] int NOT NULL IDENTITY,
        [LeagueId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_LeagueOrganisers] PRIMARY KEY ([LeagueOrganiserId]),
        CONSTRAINT [FK_LeagueOrganisers_Leagues_LeagueId] FOREIGN KEY ([LeagueId]) REFERENCES [Leagues] ([LeagueId]) ON DELETE CASCADE,
        CONSTRAINT [FK_LeagueOrganisers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE INDEX [IX_Tournaments_LeagueId] ON [Tournaments] ([LeagueId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE INDEX [IX_Teams_BroughtBackFromTeamId] ON [Teams] ([BroughtBackFromTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LeagueOrganisers_LeagueId_UserId] ON [LeagueOrganisers] ([LeagueId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE INDEX [IX_LeagueOrganisers_UserId] ON [LeagueOrganisers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Leagues_PublicCode] ON [Leagues] ([PublicCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Tournaments] ADD CONSTRAINT [FK_Tournaments_Leagues_LeagueId] FOREIGN KEY ([LeagueId]) REFERENCES [Leagues] ([LeagueId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124060334_AddLeagues', N'10.0.9');
END;

COMMIT;
GO

