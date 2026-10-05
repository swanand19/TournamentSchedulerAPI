-- Accounts, phase 2 (2026-10-01): the Permissions and RolePermissions tables. See
-- docs/accounts-and-access-plan.md and Auth/README.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, AFTER 01-accounts.sql and BEFORE
-- publishing the new API. Safe to run again. (Same as `dotnet ef database update --context TournamentDbContext`.)
--
-- The tables start empty: the API fills Permissions from the code when it starts, and ticks Admin
-- and EndUser for every service the code marks for them. After that, change ticks with
-- grant-permission.sql (until the admin portal exists).
--
-- Generated from the EF migrations — don't edit by hand; regenerate with
--   dotnet ef migrations script AddAccounts AddPermissions --context TournamentDbContext --idempotent --output deploy/sql/02-permissions.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE TABLE [Permissions] (
        [PermissionId] int NOT NULL IDENTITY,
        [ServiceRequestId] nvarchar(100) NOT NULL,
        [ControllerName] nvarchar(100) NOT NULL,
        [ActionName] nvarchar(100) NOT NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [URL] nvarchar(300) NOT NULL,
        [IsAnonymous] bit NOT NULL,
        [IsAdminOnly] bit NOT NULL,
        [IsProtected] bit NOT NULL,
        [IsObsolete] bit NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([PermissionId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RolePermissionId] int NOT NULL IDENTITY,
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RolePermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([PermissionId]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_ServiceRequestId] ON [Permissions] ([ServiceRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RolePermissions_RoleId_PermissionId] ON [RolePermissions] ([RoleId], [PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001065758_AddPermissions', N'10.0.9');
END;

COMMIT;
GO

