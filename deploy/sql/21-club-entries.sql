-- Lasting teams, steps 3-5 (2026-11-21, docs/reusable-teams-plan.md; people see "team", the tables say club): entering a
-- tournament with its code (Tournaments.EntryCode, EntriesOpen; TournamentEntries, TournamentEntryPlayers), the "joined"
-- notification's player (Notifications.ClubPlayerId) and a lasting team's public page (Clubs.PublicCode).
--
-- Run in SSMS AFTER 20-clubs.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again. Two new tables and four new columns (one not null, defaulting to off); nothing existing changes.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddClubs AddClubEntries --context TournamentDbContext --idempotent --output deploy/sql/21-club-entries.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [EntriesOpen] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [EntryCode] varchar(9) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Notifications] ADD [ClubPlayerId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Clubs] ADD [PublicCode] varchar(8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE TABLE [TournamentEntries] (
        [TournamentEntryId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [ClubId] int NOT NULL,
        [TeamName] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [RequestedByUserId] int NOT NULL,
        [RequestedAt] datetime2 NOT NULL,
        [DecidedByUserId] int NULL,
        [DecidedAt] datetime2 NULL,
        [TeamId] int NULL,
        CONSTRAINT [PK_TournamentEntries] PRIMARY KEY ([TournamentEntryId]),
        CONSTRAINT [FK_TournamentEntries_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]) ON DELETE CASCADE,
        CONSTRAINT [FK_TournamentEntries_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE TABLE [TournamentEntryPlayers] (
        [TournamentEntryPlayerId] int NOT NULL IDENTITY,
        [TournamentEntryId] int NOT NULL,
        [ClubPlayerId] int NOT NULL,
        CONSTRAINT [PK_TournamentEntryPlayers] PRIMARY KEY ([TournamentEntryPlayerId]),
        CONSTRAINT [FK_TournamentEntryPlayers_TournamentEntries_TournamentEntryId] FOREIGN KEY ([TournamentEntryId]) REFERENCES [TournamentEntries] ([TournamentEntryId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Tournaments_EntryCode] ON [Tournaments] ([EntryCode]) WHERE [EntryCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Clubs_PublicCode] ON [Clubs] ([PublicCode]) WHERE [PublicCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE INDEX [IX_TournamentEntries_ClubId] ON [TournamentEntries] ([ClubId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE INDEX [IX_TournamentEntries_TeamId] ON [TournamentEntries] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TournamentEntries_TournamentId_ClubId] ON [TournamentEntries] ([TournamentId], [ClubId]) WHERE [Status] IN (''Waiting'', ''Accepted'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE INDEX [IX_TournamentEntryPlayers_ClubPlayerId] ON [TournamentEntryPlayers] ([ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TournamentEntryPlayers_TournamentEntryId_ClubPlayerId] ON [TournamentEntryPlayers] ([TournamentEntryId], [ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261121182754_AddClubEntries', N'10.0.9');
END;

COMMIT;
GO

