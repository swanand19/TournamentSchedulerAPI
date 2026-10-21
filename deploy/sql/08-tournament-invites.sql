-- Phase 6C (2026-10-16): invites. An owner who adds an owner or scorer by an email or mobile number
-- with no account yet makes a TournamentInvites row; when that person signs up with it they get
-- the role. See docs/phase-6-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 07-phone-identity.sql and
-- BEFORE publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- No data changes: a new, empty table.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddPhoneIdentity AddTournamentInvites --context TournamentDbContext --idempotent --output deploy/sql/08-tournament-invites.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    CREATE TABLE [TournamentInvites] (
        [TournamentInviteId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [Target] nvarchar(256) NOT NULL,
        [MemberRole] nvarchar(20) NOT NULL,
        [LastSentAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [AcceptedAt] datetime2 NULL,
        [AcceptedByUserId] int NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_TournamentInvites] PRIMARY KEY ([TournamentInviteId]),
        CONSTRAINT [FK_TournamentInvites_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    CREATE INDEX [IX_TournamentInvites_Target] ON [TournamentInvites] ([Target]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TournamentInvites_TournamentId_Target] ON [TournamentInvites] ([TournamentId], [Target]) WHERE [AcceptedAt] IS NULL AND [IsDisabled] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016061051_AddTournamentInvites', N'10.0.9');
END;

COMMIT;
GO

