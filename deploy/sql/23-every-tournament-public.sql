-- Every tournament is public (2026-11-23, the user's choice): the owners' switch is gone. Switches every existing
-- tournament on (IsPublic = 1) and gives each one without a link its 8-character code. Only an admin can hide one now
-- (the portal's Tournaments page, "Hide from the public").
--
-- Run in SSMS AFTER 22-follows.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again. Data only: no table or column changes.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddFollows EveryTournamentPublic --context TournamentDbContext --idempotent --output deploy/sql/23-every-tournament-public.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123174025_EveryTournamentPublic'
)
BEGIN
    EXEC(N'
    DECLARE @alphabet varchar(32) = ''ABCDEFGHJKLMNPQRSTUVWXYZ23456789'';
    DECLARE @id int, @code varchar(8), @i int;
    UPDATE [Tournaments] SET [IsPublic] = CAST(1 AS bit) WHERE [IsPublic] = CAST(0 AS bit);
    WHILE 1 = 1
    BEGIN
        SET @id = NULL;
        SELECT TOP 1 @id = [Id] FROM [Tournaments] WHERE [PublicCode] IS NULL ORDER BY [Id];
        IF @id IS NULL BREAK;
        SET @code = '''';
        SET @i = 0;
        WHILE @i < 8
        BEGIN
            SET @code = @code + SUBSTRING(@alphabet, 1 + CAST(CRYPT_GEN_RANDOM(1) AS int) % 32, 1);
            SET @i = @i + 1;
        END;
        IF NOT EXISTS (SELECT 1 FROM [Tournaments] WHERE [PublicCode] = @code)
            UPDATE [Tournaments] SET [PublicCode] = @code WHERE [Id] = @id;
    END;
    ')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123174025_EveryTournamentPublic'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123174025_EveryTournamentPublic', N'10.0.9');
END;

COMMIT;
GO

