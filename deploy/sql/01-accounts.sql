-- Accounts, phase 1 (2026-10-01): Users, Roles, UserSessions, OtpChallenges, with the Admin and
-- EndUser roles and the System user (UserId 1) seeded. See docs/accounts-and-access-plan.md.
--
-- Run in SSMS on WJLP-3571\SUBSMANAGEMENTDB as your admin login, BEFORE publishing the new API.
-- Safe to run again: it only applies what the database doesn't have yet. (Same as running
-- `dotnet ef database update --context TournamentDbContext` from TournamentScheduler.Api.)
--
-- AdminDBA (the IIS site's login) needs no new permissions: it reads and writes these tables like
-- the rest of the database.
--
-- Generated from the EF migrations — don't edit by hand; regenerate with
--   dotnet ef migrations script 20260924160123_NewChanges_CricketStats AddAccounts --context TournamentDbContext --idempotent --output deploy/sql/01-accounts.sql

USE [TournamentSchedulerDB];
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [OtpChallenges] (
        [OtpChallengeId] bigint NOT NULL IDENTITY,
        [Purpose] nvarchar(30) NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [Target] nvarchar(256) NOT NULL,
        [CodeHash] varchar(64) NOT NULL,
        [Attempts] int NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [ConsumedAt] datetime2 NULL,
        [DeviceId] nvarchar(100) NULL,
        [ClientIp] nvarchar(64) NULL,
        [Crd] datetime2 NOT NULL,
        CONSTRAINT [PK_OtpChallenges] PRIMARY KEY ([OtpChallengeId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [Roles] (
        [RoleId] int NOT NULL IDENTITY,
        [RoleName] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([RoleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [Users] (
        [UserId] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [EmailId] nvarchar(256) NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [EmailVerifiedAt] datetime2 NULL,
        [RoleId] int NOT NULL,
        [LastLoginAt] datetime2 NULL,
        [DeletedOn] datetime2 NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_Users_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [UserSessions] (
        [SessionId] uniqueidentifier NOT NULL,
        [UserId] int NOT NULL,
        [TokenHash] varchar(64) NOT NULL,
        [Channel] nvarchar(30) NULL,
        [DeviceId] nvarchar(100) NULL,
        [Crd] datetime2 NOT NULL,
        [LastSeenAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedReason] nvarchar(100) NULL,
        CONSTRAINT [PK_UserSessions] PRIMARY KEY ([SessionId]),
        CONSTRAINT [FK_UserSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'Crd', N'CrdBy', N'Description', N'IsDisabled', N'Lmd', N'LmdBy', N'RoleName') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] ON;
    EXEC(N'INSERT INTO [Roles] ([RoleId], [Crd], [CrdBy], [Description], [IsDisabled], [Lmd], [LmdBy], [RoleName])
    VALUES (1, ''2026-10-01T00:00:00.0000000Z'', 1, N''Runs the admin portal.'', CAST(0 AS bit), ''2026-10-01T00:00:00.0000000Z'', 1, N''Admin''),
    (2, ''2026-10-01T00:00:00.0000000Z'', 1, N''Everyone who signs up in the apps.'', CAST(0 AS bit), ''2026-10-01T00:00:00.0000000Z'', 1, N''EndUser'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'Crd', N'CrdBy', N'Description', N'IsDisabled', N'Lmd', N'LmdBy', N'RoleName') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'UserId', N'Crd', N'CrdBy', N'DeletedOn', N'EmailId', N'EmailVerifiedAt', N'IsDisabled', N'LastLoginAt', N'Lmd', N'LmdBy', N'Name', N'PhoneNumber', N'RoleId') AND [object_id] = OBJECT_ID(N'[Users]'))
        SET IDENTITY_INSERT [Users] ON;
    EXEC(N'INSERT INTO [Users] ([UserId], [Crd], [CrdBy], [DeletedOn], [EmailId], [EmailVerifiedAt], [IsDisabled], [LastLoginAt], [Lmd], [LmdBy], [Name], [PhoneNumber], [RoleId])
    VALUES (1, ''2026-10-01T00:00:00.0000000Z'', 1, NULL, NULL, NULL, CAST(0 AS bit), NULL, ''2026-10-01T00:00:00.0000000Z'', 1, N''System'', NULL, 1)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'UserId', N'Crd', N'CrdBy', N'DeletedOn', N'EmailId', N'EmailVerifiedAt', N'IsDisabled', N'LastLoginAt', N'Lmd', N'LmdBy', N'Name', N'PhoneNumber', N'RoleId') AND [object_id] = OBJECT_ID(N'[Users]'))
        SET IDENTITY_INSERT [Users] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_OtpChallenges_ClientIp_Crd] ON [OtpChallenges] ([ClientIp], [Crd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_OtpChallenges_DeviceId_Crd] ON [OtpChallenges] ([DeviceId], [Crd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_OtpChallenges_Target_Crd] ON [OtpChallenges] ([Target], [Crd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_RoleName] ON [Roles] ([RoleName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_EmailId] ON [Users] ([EmailId]) WHERE [EmailId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_PhoneNumber] ON [Users] ([PhoneNumber]) WHERE [PhoneNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_Users_RoleId] ON [Users] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserSessions_TokenHash] ON [UserSessions] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_UserSessions_UserId] ON [UserSessions] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260930184354_AddAccounts', N'10.0.9');
END;

COMMIT;
GO

