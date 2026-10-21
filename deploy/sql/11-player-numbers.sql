-- Any jersey number (2026-10-21): two players on one team may now wear the same number, so the
-- unique index on Players (TeamId, JerseyNumber) becomes a plain one (squads are still listed by number).
-- See docs/phase-6-plan.md ("Next round").
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 10-notification-templates.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddNotificationTemplates AllowSharedJerseyNumbers --context TournamentDbContext --idempotent --output deploy/sql/11-player-numbers.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021050529_AllowSharedJerseyNumbers'
)
BEGIN
    DROP INDEX [IX_Players_TeamId_JerseyNumber] ON [Players];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021050529_AllowSharedJerseyNumbers'
)
BEGIN
    CREATE INDEX [IX_Players_TeamId_JerseyNumber] ON [Players] ([TeamId], [JerseyNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021050529_AllowSharedJerseyNumbers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021050529_AllowSharedJerseyNumbers', N'10.0.9');
END;

COMMIT;
GO

