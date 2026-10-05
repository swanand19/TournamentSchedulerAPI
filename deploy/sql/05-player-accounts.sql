-- Accounts, phase 4 (2026-10-01): players are people. Squad places (Players) can be linked to an
-- account (UserId, EmailId; one place per person per tournament), every team gets a join code
-- (Teams.JoinCode), and a person's own cricket profile (UserCricketProfiles). See
-- docs/accounts-and-access-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 03-tournament-access.sql
-- and BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- Existing data: every player stays unlinked (owners add emails, or share the team's code), and
-- every existing team gets a random join code.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddTournamentAccess AddPlayerAccounts --context TournamentDbContext --idempotent --output deploy/sql/05-player-accounts.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC sp_rename N'[Players].[PersonId]', N'UserId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Teams] ADD [JoinCode] varchar(6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Players] ADD [EmailId] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Players] ADD [TournamentId] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    CREATE TABLE [UserCricketProfiles] (
        [UserId] int NOT NULL,
        [PrimaryRole] int NOT NULL,
        [BattingStyle] int NULL,
        [BowlingArm] int NULL,
        [BowlingType] int NULL,
        [BattingOrderPreference] int NULL,
        CONSTRAINT [PK_UserCricketProfiles] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_UserCricketProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'UPDATE [Players] SET [UserId] = NULL WHERE [UserId] IS NOT NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'UPDATE p SET p.[TournamentId] = t.[TournamentId] FROM [Players] p JOIN [Teams] t ON t.[Id] = p.[TeamId];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'
    DECLARE @alphabet varchar(32) = ''ABCDEFGHJKLMNPQRSTUVWXYZ23456789'';
    DECLARE @id int, @code varchar(6), @bytes varbinary(6), @i int;
    DECLARE teams CURSOR LOCAL FAST_FORWARD FOR SELECT [Id] FROM [Teams] WHERE [JoinCode] IS NULL;
    OPEN teams;
    FETCH NEXT FROM teams INTO @id;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        WHILE 1 = 1
        BEGIN
            SET @bytes = CRYPT_GEN_RANDOM(6);
            SET @code = '''';
            SET @i = 1;
            WHILE @i <= 6
            BEGIN
                SET @code = @code + SUBSTRING(@alphabet, CAST(SUBSTRING(@bytes, @i, 1) AS int) % 32 + 1, 1);
                SET @i = @i + 1;
            END
            IF NOT EXISTS (SELECT 1 FROM [Teams] WHERE [JoinCode] = @code) BREAK;
        END
        UPDATE [Teams] SET [JoinCode] = @code WHERE [Id] = @id;
        FETCH NEXT FROM teams INTO @id;
    END
    CLOSE teams;
    DEALLOCATE teams;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Teams_JoinCode] ON [Teams] ([JoinCode]) WHERE [JoinCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    CREATE INDEX [IX_Players_EmailId] ON [Players] ([EmailId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Players_TournamentId_UserId] ON [Players] ([TournamentId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    CREATE INDEX [IX_Players_UserId] ON [Players] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Players] ADD CONSTRAINT [FK_Players_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001112247_AddPlayerAccounts', N'10.0.9');
END;

COMMIT;
GO

