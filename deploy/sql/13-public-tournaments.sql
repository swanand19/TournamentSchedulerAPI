-- Public live scores (phase 6E, 2026-10-21): an owner can switch a tournament public ("anyone with the
-- link can watch"). Tournaments gain IsPublic (off for every existing tournament) and PublicCode (the
-- code in the link, made when it is first switched on). TournamentFollows records signed-in people
-- following a public tournament. See docs/phase-6-plan.md (6E).
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 12-knockouts.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: every tournament stays private until an owner switches it on.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddKnockouts AddPublicTournaments --context TournamentDbContext --idempotent --output deploy/sql/13-public-tournaments.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [IsPublic] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [PublicCode] nvarchar(8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    CREATE TABLE [TournamentFollows] (
        [TournamentFollowId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        CONSTRAINT [PK_TournamentFollows] PRIMARY KEY ([TournamentFollowId]),
        CONSTRAINT [FK_TournamentFollows_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TournamentFollows_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Tournaments_PublicCode] ON [Tournaments] ([PublicCode]) WHERE [PublicCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TournamentFollows_TournamentId_UserId] ON [TournamentFollows] ([TournamentId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    CREATE INDEX [IX_TournamentFollows_UserId] ON [TournamentFollows] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021123835_AddPublicTournaments', N'10.0.9');
END;

COMMIT;
GO

