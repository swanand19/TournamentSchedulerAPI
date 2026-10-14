-- Phase 6B (2026-10-13): sign in with an email or a mobile number. Users get PhoneVerifiedAt (when
-- the number was proved with a texted code; Users.PhoneNumber and its unique index already exist),
-- and squad places get the mobile number an organiser can give them (Players.PhoneNumber, indexed
-- for linking when that person signs in). See docs/phase-6-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 06-admin-portal.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes. Mobile numbers stay off in the apps until the API's appsettings has an Sms
-- section with a Provider (Log for testing: texted codes go to the otp-codes.txt file).
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddAdminPortal AddPhoneIdentity --context TournamentDbContext --idempotent --output deploy/sql/07-phone-identity.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    ALTER TABLE [Users] ADD [PhoneVerifiedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    ALTER TABLE [Players] ADD [PhoneNumber] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    CREATE INDEX [IX_Players_PhoneNumber] ON [Players] ([PhoneNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013181209_AddPhoneIdentity', N'10.0.9');
END;

COMMIT;
GO

