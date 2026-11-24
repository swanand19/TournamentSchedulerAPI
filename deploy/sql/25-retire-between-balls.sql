-- Retiring between balls (2026-11-24): CricketBalls.BetweenBalls. A batter who retires (hurt or out) between
-- deliveries is recorded with nothing bowled - no ball for the bowler or the batter, no runs. Existing balls are
-- all deliveries (the new column is 0), so no data changes.
--
-- Run in SSMS AFTER 24-leagues.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddLeagues RetireBetweenBalls --context TournamentDbContext --idempotent --output deploy/sql/25-retire-between-balls.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124104004_RetireBetweenBalls'
)
BEGIN
    ALTER TABLE [CricketBalls] ADD [BetweenBalls] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124104004_RetireBetweenBalls'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124104004_RetireBetweenBalls', N'10.0.9');
END;

COMMIT;
GO

