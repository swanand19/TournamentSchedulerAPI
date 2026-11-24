-- Lasting teams (2026-11-20, step 2 of docs/reusable-teams-plan.md; people see "team", the tables say club): a team kept
-- between tournaments (Clubs), its admins (ClubAdmins) and players (ClubPlayers); a tournament's team and squad places
-- can point back at it (Teams.ClubId, Players.ClubPlayerId); notifications can open a team or a lasting team
-- (Notifications.TeamId, ClubId).
--
-- Run in SSMS AFTER 19-team-admins.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again. Three new tables and four nullable columns; nothing existing changes.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddTeamAdmins AddClubs --context TournamentDbContext --idempotent --output deploy/sql/20-clubs.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Teams] ADD [ClubId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Players] ADD [ClubPlayerId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Notifications] ADD [ClubId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Notifications] ADD [TeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE TABLE [Clubs] (
        [ClubId] int NOT NULL IDENTITY,
        [Name] nvarchar(60) NOT NULL,
        [Sport] nvarchar(20) NOT NULL,
        [Area] nvarchar(60) NULL,
        [JoinCode] varchar(8) NOT NULL,
        [LogoId] varchar(40) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Clubs] PRIMARY KEY ([ClubId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE TABLE [ClubAdmins] (
        [ClubAdminId] int NOT NULL IDENTITY,
        [ClubId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_ClubAdmins] PRIMARY KEY ([ClubAdminId]),
        CONSTRAINT [FK_ClubAdmins_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClubAdmins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE TABLE [ClubPlayers] (
        [ClubPlayerId] int NOT NULL IDENTITY,
        [ClubId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [JerseyNumber] int NULL,
        [Position] nvarchar(50) NULL,
        [IsCaptain] bit NOT NULL,
        [UserId] int NULL,
        [EmailId] nvarchar(256) NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [Cricket_PrimaryRole] int NULL,
        [Cricket_BattingStyle] int NULL,
        [Cricket_BowlingArm] int NULL,
        [Cricket_BowlingType] int NULL,
        [Cricket_BattingOrderPreference] int NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_ClubPlayers] PRIMARY KEY ([ClubPlayerId]),
        CONSTRAINT [FK_ClubPlayers_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClubPlayers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_Teams_ClubId] ON [Teams] ([ClubId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_Players_ClubPlayerId] ON [Players] ([ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClubAdmins_ClubId_UserId] ON [ClubAdmins] ([ClubId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubAdmins_UserId] ON [ClubAdmins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ClubPlayers_ClubId_UserId] ON [ClubPlayers] ([ClubId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubPlayers_EmailId] ON [ClubPlayers] ([EmailId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubPlayers_PhoneNumber] ON [ClubPlayers] ([PhoneNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubPlayers_UserId] ON [ClubPlayers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clubs_JoinCode] ON [Clubs] ([JoinCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Players] ADD CONSTRAINT [FK_Players_ClubPlayers_ClubPlayerId] FOREIGN KEY ([ClubPlayerId]) REFERENCES [ClubPlayers] ([ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Teams] ADD CONSTRAINT [FK_Teams_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120172036_AddClubs', N'10.0.9');
END;

COMMIT;
GO

