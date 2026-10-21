-- Knockouts (2026-10-21): a knockout stage after the groups, or instead of them. KnockoutFixtures holds
-- each knockout match as planned (its round, name, and where each side comes from: a team, a group
-- place, or the winner / loser of an earlier knockout match). Matches and CricketMatches gain
-- KnockoutFixtureId and RoundLabel, and SavedFixtureId becomes optional (a knockout match has no group
-- fixture). See docs/phase-6-plan.md ("Next round").
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 11-player-numbers.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: existing matches stay group matches.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AllowSharedJerseyNumbers AddKnockouts --context TournamentDbContext --idempotent --output deploy/sql/12-knockouts.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Matches]') AND [c].[name] = N'SavedFixtureId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Matches] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Matches] ALTER COLUMN [SavedFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [Matches] ADD [KnockoutFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [Matches] ADD [RoundLabel] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CricketMatches]') AND [c].[name] = N'SavedFixtureId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [CricketMatches] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [CricketMatches] ALTER COLUMN [SavedFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [KnockoutFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [RoundLabel] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE TABLE [KnockoutFixtures] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Order] int NOT NULL,
        [Round] int NOT NULL,
        [Label] nvarchar(60) NOT NULL,
        [Home_Kind] int NOT NULL,
        [Home_TeamId] int NULL,
        [Home_GroupName] nvarchar(20) NULL,
        [Home_Place] int NULL,
        [Home_FromOrder] int NULL,
        [Home_OverrideTeamId] int NULL,
        [Away_Kind] int NOT NULL,
        [Away_TeamId] int NULL,
        [Away_GroupName] nvarchar(20) NULL,
        [Away_Place] int NULL,
        [Away_FromOrder] int NULL,
        [Away_OverrideTeamId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByUserId] int NOT NULL,
        CONSTRAINT [PK_KnockoutFixtures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_KnockoutFixtures_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE INDEX [IX_Matches_KnockoutFixtureId] ON [Matches] ([KnockoutFixtureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE INDEX [IX_CricketMatches_KnockoutFixtureId] ON [CricketMatches] ([KnockoutFixtureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_KnockoutFixtures_TournamentId_Order] ON [KnockoutFixtures] ([TournamentId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD CONSTRAINT [FK_CricketMatches_KnockoutFixtures_KnockoutFixtureId] FOREIGN KEY ([KnockoutFixtureId]) REFERENCES [KnockoutFixtures] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [Matches] ADD CONSTRAINT [FK_Matches_KnockoutFixtures_KnockoutFixtureId] FOREIGN KEY ([KnockoutFixtureId]) REFERENCES [KnockoutFixtures] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021052937_AddKnockouts', N'10.0.9');
END;

COMMIT;
GO

