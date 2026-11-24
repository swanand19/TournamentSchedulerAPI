-- Team admins (2026-11-20, step 1 of docs/reusable-teams-plan.md): owners can name up to 3 people to run a team's
-- squad (TeamAdmins), and a player who has played is left out of the squad instead of deleted (Players.LeftOn), so
-- their goals, balls and cards stay on the scorecards.
--
-- Run in SSMS AFTER 18-pictures.sql and BEFORE publishing the new API, on each database:
--   * the laptop: WJLP-3571\SUBSMANAGEMENTDB, database TournamentSchedulerDB, as your admin login;
--   * Azure: kickcricket-sql.database.windows.net, database TournamentSchedulerDB (Microsoft Entra MFA).
-- Choose the database in SSMS's toolbar first (no USE line, so the same file works on Azure SQL).
-- Safe to run again. One new table and one nullable column; nothing existing changes.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script AddPictures AddTeamAdmins --context TournamentDbContext --idempotent --output deploy/sql/19-team-admins.sql

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    ALTER TABLE [Players] ADD [LeftOn] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE TABLE [TeamAdmins] (
        [TeamAdminId] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [TournamentId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_TeamAdmins] PRIMARY KEY ([TeamAdminId]),
        CONSTRAINT [FK_TeamAdmins_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TeamAdmins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TeamAdmins_TeamId_UserId] ON [TeamAdmins] ([TeamId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE INDEX [IX_TeamAdmins_TournamentId_UserId] ON [TeamAdmins] ([TournamentId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE INDEX [IX_TeamAdmins_UserId] ON [TeamAdmins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120121343_AddTeamAdmins', N'10.0.9');
END;

COMMIT;
GO

