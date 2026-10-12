-- Accounts, phase 5 (2026-10-05): the admin portal. Users get a portal password (hashed, never the
-- password itself) with a lock after repeated wrong tries, and sessions say whether they are portal
-- sessions (signed in with code + password - the only kind that can use the portal's services).
-- See docs/accounts-and-access-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 05-player-accounts.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: nobody has a portal password yet; each admin chooses one at their first portal sign-in.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddPlayerAccounts AddAdminPortal --context TournamentDbContext --idempotent --output deploy/sql/06-admin-portal.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [UserSessions] ADD [IsPortal] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [FailedPasswordCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [PasswordHash] varchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [PasswordLockedUntil] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [PasswordSetAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005181759_AddAdminPortal', N'10.0.9');
END;

COMMIT;
GO

