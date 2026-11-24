-- The app's public name is now KickCricket (2026-11-18). The message templates (sign-in codes, invites,
-- notifications) are saved in the database, so "Tournament Scheduler" is replaced with "KickCricket" in every
-- saved template's texts. Wording an admin changed in the portal is kept; only the name is replaced.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 16-team-draw.sql and BEFORE
-- publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No schema change. Messages already in people's inboxes keep the old name.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddAuctionHistory RenameAppToKickCricket --context TournamentDbContext --idempotent --output deploy/sql/17-kickcricket-name.sql

USE [TournamentSchedulerDB];
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118085306_RenameAppToKickCricket'
)
BEGIN
    UPDATE [NotificationTemplates] SET [EmailSubject] = REPLACE([EmailSubject], N'Tournament Scheduler', N'KickCricket'), [EmailBody] = REPLACE([EmailBody], N'Tournament Scheduler', N'KickCricket'), [SmsBody] = REPLACE([SmsBody], N'Tournament Scheduler', N'KickCricket'), [NotificationTitle] = REPLACE([NotificationTitle], N'Tournament Scheduler', N'KickCricket'), [NotificationBody] = REPLACE([NotificationBody], N'Tournament Scheduler', N'KickCricket'), [PushTitle] = REPLACE([PushTitle], N'Tournament Scheduler', N'KickCricket'), [PushBody] = REPLACE([PushBody], N'Tournament Scheduler', N'KickCricket');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118085306_RenameAppToKickCricket'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261118085306_RenameAppToKickCricket', N'10.0.9');
END;

COMMIT;
GO

