-- The team draw and the auction's last pieces (2026-11-17, docs/player-pool-and-auction-plan.md sections 3
-- and 9): the other way to fill teams from the player pool. Two new tables, TeamDraws (how it's drawn,
-- where it stands, who draws, the order round the table) and DrawPicks (every name drawn: the team, the
-- order, when it lands, moved from); and new columns: PlayerPools.Method (Draw or Auction, null = not
-- chosen), AuctionLots.MovedFromTeamId (a bought player moved or swapped after the auction),
-- AuctionLots.ByDraw ("Fill the gaps by draw") and AuctionLots.CancelledAt (a cancelled auction's lots and
-- bids are kept as history instead of deleted).
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 15-player-auction.sql and BEFORE
-- publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: nothing existing is touched; the new columns start empty (ByDraw = 0).
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddPlayerAuction AddAuctionHistory --context TournamentDbContext --idempotent --output deploy/sql/16-team-draw.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    ALTER TABLE [PlayerPools] ADD [Method] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    ALTER TABLE [AuctionLots] ADD [MovedFromTeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE TABLE [TeamDraws] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [TeamOrder] nvarchar(1000) NOT NULL,
        [DrawerUserId] int NULL,
        [AutoSeconds] int NULL,
        [NextAutoAt] datetime2 NULL,
        [Times] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_TeamDraws] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TeamDraws_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE TABLE [DrawPicks] (
        [Id] int NOT NULL IDENTITY,
        [DrawId] int NOT NULL,
        [TournamentId] int NOT NULL,
        [PoolPlayerId] int NOT NULL,
        [TeamId] int NOT NULL,
        [Order] int NOT NULL,
        [TierId] int NULL,
        [PickedAt] datetime2 NOT NULL,
        [LandsAt] datetime2 NOT NULL,
        [Announced] bit NOT NULL,
        [MovedFromTeamId] int NULL,
        CONSTRAINT [PK_DrawPicks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DrawPicks_TeamDraws_DrawId] FOREIGN KEY ([DrawId]) REFERENCES [TeamDraws] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE INDEX [IX_DrawPicks_DrawId_Order] ON [DrawPicks] ([DrawId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DrawPicks_DrawId_PoolPlayerId] ON [DrawPicks] ([DrawId], [PoolPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE INDEX [IX_DrawPicks_TournamentId] ON [DrawPicks] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE INDEX [IX_TeamDraws_Status_NextAutoAt] ON [TeamDraws] ([Status], [NextAutoAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TeamDraws_TournamentId] ON [TeamDraws] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117111819_AddTeamDraw', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117121006_AddAuctionHistory'
)
BEGIN
    ALTER TABLE [AuctionLots] ADD [ByDraw] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117121006_AddAuctionHistory'
)
BEGIN
    ALTER TABLE [AuctionLots] ADD [CancelledAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117121006_AddAuctionHistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117121006_AddAuctionHistory', N'10.0.9');
END;

COMMIT;
GO

