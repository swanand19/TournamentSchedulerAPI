-- Step 2 of 3: the four log tables. Run after 01-create-log-database.sql, as a login that can
-- create tables (your own admin login). Safe to run again: each migration is applied only once.
--
-- Generated from the EF migrations — don't edit by hand. After adding a migration, regenerate with
--   dotnet ef migrations script --context LogDbContext --idempotent --output Logging/Sql/02-create-log-tables.sql
-- and put this header (with the USE line) back on top.

USE [TournamentSchedulerLogs];
GO
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE TABLE [ErrorLogs] (
        [ErrorLogId] bigint NOT NULL IDENTITY,
        [Crd] datetime2(3) NOT NULL,
        [RequestUUID] uniqueidentifier NULL,
        [ServiceRequestId] nvarchar(100) NULL,
        [Url] nvarchar(2048) NULL,
        [RequestJson] nvarchar(max) NULL,
        [ExceptionType] nvarchar(500) NULL,
        [Message] nvarchar(max) NULL,
        [LogMessage] nvarchar(2000) NULL,
        [StackTrace] nvarchar(max) NULL,
        [InnerException] nvarchar(max) NULL,
        [Source] nvarchar(500) NULL,
        [Severity] nvarchar(20) NOT NULL,
        CONSTRAINT [PK_ErrorLogs] PRIMARY KEY ([ErrorLogId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE TABLE [MBActivityLogs] (
        [MBActivityLogId] bigint NOT NULL IDENTITY,
        [RequestUUID] uniqueidentifier NOT NULL,
        [SessionId] nvarchar(100) NULL,
        [JourneyId] nvarchar(100) NULL,
        [DeviceId] nvarchar(100) NULL,
        [Channel] nvarchar(30) NULL,
        [ServiceRequestId] nvarchar(100) NULL,
        [ControllerName] nvarchar(100) NULL,
        [ActionName] nvarchar(100) NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [Url] nvarchar(2048) NOT NULL,
        [RequestJson] nvarchar(max) NULL,
        [ResponseJson] nvarchar(max) NULL,
        [HttpStatusCode] int NOT NULL,
        [IsSuccess] bit NULL,
        [StatusMessage] nvarchar(1000) NULL,
        [ViaGateway] bit NOT NULL,
        [StartDate] datetime2(3) NOT NULL,
        [EndDate] datetime2(3) NOT NULL,
        [DurationMs] int NOT NULL,
        CONSTRAINT [PK_MBActivityLogs] PRIMARY KEY ([MBActivityLogId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE TABLE [MBMiddlewareLogs] (
        [MBMiddlewareLogId] bigint NOT NULL IDENTITY,
        [RequestUUID] uniqueidentifier NOT NULL,
        [ServiceRequestId] nvarchar(100) NULL,
        [JourneyId] nvarchar(100) NULL,
        [SessionId] nvarchar(100) NULL,
        [DeviceId] nvarchar(100) NULL,
        [Channel] nvarchar(30) NULL,
        [AppVersion] nvarchar(50) NULL,
        [KeyId] nvarchar(50) NULL,
        [ClientIp] nvarchar(64) NULL,
        [EncryptedRequestJson] nvarchar(max) NULL,
        [EncryptedResponseJson] nvarchar(max) NULL,
        [HttpStatusCode] int NOT NULL,
        [RejectedReason] nvarchar(500) NULL,
        [StartDate] datetime2(3) NOT NULL,
        [EndDate] datetime2(3) NOT NULL,
        [DurationMs] int NOT NULL,
        CONSTRAINT [PK_MBMiddlewareLogs] PRIMARY KEY ([MBMiddlewareLogId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE TABLE [RemoteLogs] (
        [RemoteLogId] bigint NOT NULL IDENTITY,
        [RequestUUID] uniqueidentifier NULL,
        [ProviderName] nvarchar(100) NULL,
        [HttpMethod] nvarchar(10) NULL,
        [Url] nvarchar(2048) NULL,
        [RequestHeaders] nvarchar(max) NULL,
        [RequestJson] nvarchar(max) NULL,
        [EncryptedRequestJson] nvarchar(max) NULL,
        [ResponseHeaders] nvarchar(max) NULL,
        [ResponseJson] nvarchar(max) NULL,
        [EncryptedResponseJson] nvarchar(max) NULL,
        [HttpStatusCode] int NULL,
        [ErrorMessage] nvarchar(2000) NULL,
        [StartDate] datetime2(3) NOT NULL,
        [EndDate] datetime2(3) NOT NULL,
        [DurationMs] int NOT NULL,
        CONSTRAINT [PK_RemoteLogs] PRIMARY KEY ([RemoteLogId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_ErrorLogs_Crd] ON [ErrorLogs] ([Crd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_ErrorLogs_RequestUUID] ON [ErrorLogs] ([RequestUUID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_MBActivityLogs_RequestUUID] ON [MBActivityLogs] ([RequestUUID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_MBActivityLogs_StartDate] ON [MBActivityLogs] ([StartDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_MBMiddlewareLogs_RequestUUID] ON [MBMiddlewareLogs] ([RequestUUID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_MBMiddlewareLogs_StartDate] ON [MBMiddlewareLogs] ([StartDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_RemoteLogs_RequestUUID] ON [RemoteLogs] ([RequestUUID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    CREATE INDEX [IX_RemoteLogs_StartDate] ON [RemoteLogs] ([StartDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930064637_InitialLogs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260930064637_InitialLogs', N'10.0.9');
END;

COMMIT;
GO

