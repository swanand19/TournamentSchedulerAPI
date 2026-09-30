-- Step 1 of 3: the logs database, capped in size. Run once in SSMS, connected to
-- WJLP-3571\SUBSMANAGEMENTDB as an admin (sysadmin or dbcreator). Safe to run again.
--
-- Sizes: the data file stops at 2 GB and the transaction log at 512 MB. With 2-day retention and
-- read responses not stored, a heavy test day is roughly 300 MB, so 2 GB leaves plenty of room.
-- If the cap is ever reached, inserts fail, the API keeps working, and new log rows go to the
-- fallback files in C:\ProgramData\TournamentScheduler\logs until the hourly purge frees space.
--
-- SIMPLE recovery: logs are disposable, so there is nothing to restore to a point in time — and it
-- lets the transaction log reuse its space after each batch instead of growing until backed up.

IF DB_ID(N'TournamentSchedulerLogs') IS NULL
BEGIN
    CREATE DATABASE [TournamentSchedulerLogs];
END
GO

ALTER DATABASE [TournamentSchedulerLogs] SET RECOVERY SIMPLE;
GO

-- Logical file names are the defaults SQL Server gives a new database.
ALTER DATABASE [TournamentSchedulerLogs]
    MODIFY FILE (NAME = N'TournamentSchedulerLogs', MAXSIZE = 2048MB, FILEGROWTH = 128MB);
GO

ALTER DATABASE [TournamentSchedulerLogs]
    MODIFY FILE (NAME = N'TournamentSchedulerLogs_log', MAXSIZE = 512MB, FILEGROWTH = 64MB);
GO

-- Check: both files and their caps.
SELECT name AS [File], type_desc AS [Type],
       size * 8 / 1024 AS [CurrentMB],
       CASE max_size WHEN -1 THEN NULL ELSE CAST(max_size AS bigint) * 8 / 1024 END AS [MaxMB]
FROM [TournamentSchedulerLogs].sys.database_files;
GO
