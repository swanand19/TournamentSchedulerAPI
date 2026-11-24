-- The player pool and the auction (2026-11-17, docs/player-pool-and-auction-plan.md): seven new
-- tables. PlayerPools (a tournament's registration code), PoolTiers (Marquee, A, B… with base prices
-- and "at most N a team"), PoolPlayers (everyone waiting for a team), Auctions (the rules and where
-- it stands), AuctionTeams (who bids for each team), AuctionLots (a player offered in a round) and
-- AuctionBids (every bid, the ledger purses are worked out from).
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 14-app-starts.sql and BEFORE
-- publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: nothing existing is touched, and every tournament starts without a pool or an auction.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddAppStarts AddPlayerAuction --context TournamentDbContext --idempotent --output deploy/sql/15-player-auction.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [Auctions] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Unit] nvarchar(8) NOT NULL,
        [Purse] int NOT NULL,
        [MinSquad] int NOT NULL,
        [MaxSquad] int NOT NULL,
        [KeptPlayerPrice] int NOT NULL,
        [NoTierBasePrice] int NOT NULL,
        [BidSteps] nvarchar(200) NOT NULL,
        [ClockOn] bit NOT NULL,
        [FirstBidSeconds] int NOT NULL,
        [NextBidSeconds] int NOT NULL,
        [Order] nvarchar(20) NOT NULL,
        [UnsoldPlan] nvarchar(20) NOT NULL,
        [Round] int NOT NULL,
        [AuctioneerUserId] int NULL,
        [OpenLotId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [FinishedAt] datetime2 NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_Auctions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Auctions_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [PlayerPools] (
        [TournamentId] int NOT NULL,
        [JoinCode] varchar(7) NULL,
        [IsOpen] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PlayerPools] PRIMARY KEY ([TournamentId]),
        CONSTRAINT [FK_PlayerPools_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [PoolPlayers] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [UserId] int NULL,
        [EmailId] nvarchar(256) NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [Position] nvarchar(40) NULL,
        [CricketRole] nvarchar(30) NULL,
        [TierId] int NULL,
        [Order] int NOT NULL,
        [JoinedByCode] bit NOT NULL,
        [IsWithdrawn] bit NOT NULL,
        [PlayerId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByUserId] int NOT NULL,
        CONSTRAINT [PK_PoolPlayers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PoolPlayers_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PoolPlayers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [PoolTiers] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Name] nvarchar(30) NOT NULL,
        [Order] int NOT NULL,
        [BasePrice] int NOT NULL,
        [MaxPerTeam] int NULL,
        CONSTRAINT [PK_PoolTiers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PoolTiers_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [AuctionLots] (
        [Id] int NOT NULL IDENTITY,
        [AuctionId] int NOT NULL,
        [PoolPlayerId] int NOT NULL,
        [Round] int NOT NULL,
        [Order] int NOT NULL,
        [BasePrice] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [OpenedAt] datetime2 NULL,
        [ClosesAt] datetime2 NULL,
        [PausedMsLeft] int NULL,
        [ClosedAt] datetime2 NULL,
        [SoldTeamId] int NULL,
        [Price] int NULL,
        [Assigned] bit NOT NULL,
        CONSTRAINT [PK_AuctionLots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuctionLots_Auctions_AuctionId] FOREIGN KEY ([AuctionId]) REFERENCES [Auctions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [AuctionTeams] (
        [Id] int NOT NULL IDENTITY,
        [AuctionId] int NOT NULL,
        [TournamentId] int NOT NULL,
        [TeamId] int NOT NULL,
        [BidderUserId] int NULL,
        [KeptPlayers] int NOT NULL,
        CONSTRAINT [PK_AuctionTeams] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuctionTeams_Auctions_AuctionId] FOREIGN KEY ([AuctionId]) REFERENCES [Auctions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [AuctionBids] (
        [Id] bigint NOT NULL IDENTITY,
        [LotId] int NOT NULL,
        [TeamId] int NOT NULL,
        [Amount] int NOT NULL,
        [ByUserId] int NOT NULL,
        [FromRoom] bit NOT NULL,
        [At] datetime2 NOT NULL,
        [IsUndone] bit NOT NULL,
        CONSTRAINT [PK_AuctionBids] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuctionBids_AuctionLots_LotId] FOREIGN KEY ([LotId]) REFERENCES [AuctionLots] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionBids_LotId_Id] ON [AuctionBids] ([LotId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionLots_AuctionId_Round_Order] ON [AuctionLots] ([AuctionId], [Round], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionLots_Status_ClosesAt] ON [AuctionLots] ([Status], [ClosesAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Auctions_TournamentId] ON [Auctions] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AuctionTeams_AuctionId_TeamId] ON [AuctionTeams] ([AuctionId], [TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionTeams_TournamentId_BidderUserId] ON [AuctionTeams] ([TournamentId], [BidderUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PlayerPools_JoinCode] ON [PlayerPools] ([JoinCode]) WHERE [JoinCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolPlayers_EmailId] ON [PoolPlayers] ([EmailId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolPlayers_PhoneNumber] ON [PoolPlayers] ([PhoneNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PoolPlayers_TournamentId_UserId] ON [PoolPlayers] ([TournamentId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolPlayers_UserId] ON [PoolPlayers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolTiers_TournamentId_Order] ON [PoolTiers] ([TournamentId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117064541_AddPlayerAuction', N'10.0.9');
END;

COMMIT;
GO

