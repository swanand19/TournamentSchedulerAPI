-- Pictures (2026-11-20, HLD P-26): a profile photo per account (Users.PhotoId) and a logo and banner per
-- tournament (Tournaments.LogoId, BannerId). Only file names are stored; the files live in the API's pictures
-- folder (C:\ProgramData\TournamentScheduler\pictures on the laptop, $HOME/data/TournamentScheduler/pictures on Azure).
--
-- Run in SSMS AFTER 17-kickcricket-name.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again. Three nullable columns; nothing existing changes.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script RenameAppToKickCricket AddPictures --context TournamentDbContext --idempotent --output deploy/sql/18-pictures.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    ALTER TABLE [Users] ADD [PhotoId] varchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [BannerId] varchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [LogoId] varchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120031238_AddPictures', N'10.0.9');
END;

COMMIT;
GO

