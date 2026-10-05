-- Accounts, phase 3 (2026-10-01): tournament owners and scorers (TournamentMembers), tournament
-- dates and status, and the scorer lock on football and cricket matches. See
-- docs/accounts-and-access-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 02-permissions.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- Existing tournaments: started ones become Live, the rest Upcoming; they get no dates (so they
-- never complete by themselves), and the System user owns them - which hides them from everyone
-- until you run 04-take-over-existing-tournaments.sql with your own email.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddPermissions AddTournamentAccess --context TournamentDbContext --idempotent --output deploy/sql/03-tournament-access.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [CompletedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [CompletedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [CreatedByUserId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [EndDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [StartDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [Status] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [TimeZoneId] nvarchar(64) NOT NULL DEFAULT N'Asia/Kolkata';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ActiveScorerUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ScorerLastSeenAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ScoringRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ScoringRequestedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ActiveScorerUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ScorerLastSeenAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ScoringRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ScoringRequestedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE TABLE [TournamentMembers] (
        [TournamentMemberId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [UserId] int NOT NULL,
        [MemberRole] nvarchar(20) NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_TournamentMembers] PRIMARY KEY ([TournamentMemberId]),
        CONSTRAINT [FK_TournamentMembers_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TournamentMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE INDEX [IX_Tournaments_Status_EndDate] ON [Tournaments] ([Status], [EndDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TournamentMembers_TournamentId_UserId] ON [TournamentMembers] ([TournamentId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE INDEX [IX_TournamentMembers_UserId] ON [TournamentMembers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    EXEC(N'UPDATE [Tournaments] SET [Status] = 1 WHERE [IsStarted] = 1;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    EXEC(N'INSERT INTO [TournamentMembers] ([TournamentId], [UserId], [MemberRole], [Crd], [CrdBy], [Lmd], [LmdBy], [IsDisabled])
    SELECT t.[Id], 1, N''Owner'', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 1, 0
    FROM [Tournaments] t
    WHERE NOT EXISTS (SELECT 1 FROM [TournamentMembers] m WHERE m.[TournamentId] = t.[Id] AND m.[UserId] = 1);');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001085439_AddTournamentAccess', N'10.0.9');
END;

COMMIT;
GO

