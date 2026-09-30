-- One-off: renames the game database FootballTournament -> TournamentSchedulerDB (2026-09-30).
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as an admin. Safe to run again.
--
-- Everything inside moves with it: tables, data, users and their permissions (AdminDBA), and EF's
-- migration history. Only the name changes.
--
-- BEFORE running:
--   1. Stop the IIS site: IIS Manager > Application Pools > TournamentSchedulerAPI > Stop.
--   2. Stop any `dotnet run` of the API, and close SSMS query windows that use the database.
--   The rename needs the database to itself; step 1 below disconnects anything still connected
--   (rolling back its open work), which is why the API should be stopped first rather than cut off.
--
-- AFTER running:
--   3. In the IIS site's appsettings.json, change DefaultConnection to Database=TournamentSchedulerDB.
--      (The repo's appsettings.json already says TournamentSchedulerDB.)
--   4. Start the app pool, then open http://localhost:5080/api/health — "database": "ok" means it worked.
--
-- To undo: run this again with the two names swapped.
--
-- The files on disk keep their old names (FootballTournament.mdf / _log.ldf). That is cosmetic;
-- renaming them means taking the database offline and moving the files, so it is left alone.

USE [master];
GO

IF DB_ID(N'TournamentSchedulerDB') IS NOT NULL AND DB_ID(N'FootballTournament') IS NULL
BEGIN
    PRINT N'Already renamed: TournamentSchedulerDB exists and FootballTournament does not.';
END
ELSE IF DB_ID(N'TournamentSchedulerDB') IS NOT NULL
BEGIN
    RAISERROR(N'Both FootballTournament and TournamentSchedulerDB exist. Nothing was changed - check which one holds the data.', 16, 1);
END
ELSE IF DB_ID(N'FootballTournament') IS NULL
BEGIN
    RAISERROR(N'There is no database named FootballTournament on this server. Nothing was changed.', 16, 1);
END
ELSE
BEGIN
    -- 1. Take the database for ourselves (disconnects anyone else), rename it, open it up again.
    ALTER DATABASE [FootballTournament] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    ALTER DATABASE [FootballTournament] MODIFY NAME = [TournamentSchedulerDB];
    ALTER DATABASE [TournamentSchedulerDB] SET MULTI_USER;
    PRINT N'Renamed FootballTournament to TournamentSchedulerDB.';
END
GO

-- 2. Logical file names, to match (online, no effect on the files themselves).
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER DATABASE [TournamentSchedulerDB] MODIFY FILE (NAME = ' + QUOTENAME(name)
             + N', NEWNAME = ' + QUOTENAME(REPLACE(name, N'FootballTournament', N'TournamentSchedulerDB')) + N'); '
FROM sys.master_files
WHERE database_id = DB_ID(N'TournamentSchedulerDB') AND name LIKE N'FootballTournament%';
IF @sql <> N'' BEGIN EXEC (@sql); PRINT N'Renamed the logical file names.'; END
GO

-- 3. Logins that opened the old name by default would fail to connect from tools that don't name a
--    database (SSMS, for one). Point them at the new name.
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER LOGIN ' + QUOTENAME(name) + N' WITH DEFAULT_DATABASE = [TournamentSchedulerDB]; '
FROM sys.server_principals
WHERE default_database_name = N'FootballTournament';
IF @sql <> N'' BEGIN EXEC (@sql); PRINT N'Updated logins whose default database was FootballTournament.'; END
GO

-- Check: the database, its files, and that nothing still points at the old name.
SELECT name AS [Database], state_desc AS [State], user_access_desc AS [Access]
FROM sys.databases WHERE name IN (N'FootballTournament', N'TournamentSchedulerDB');
SELECT name AS [LogicalFile], physical_name AS [File] FROM sys.master_files WHERE database_id = DB_ID(N'TournamentSchedulerDB');
SELECT name AS [LoginStillDefaultingToOldName] FROM sys.server_principals WHERE default_database_name = N'FootballTournament';
GO
