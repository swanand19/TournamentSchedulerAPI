-- Step 3 of 3: who may write logs. Run after step 2, as an admin. Safe to run again.
--
-- The API only ever inserts rows and deletes old ones (the hourly purge), so it needs read and write
-- on the logs database and nothing more: no table changes, and no access to anything else.
--
-- Which login? The one in the API's connection string:
--   * The IIS site (D:\Swanand\IIS Websites\inetpub\TournamentSchedulerAPI\appsettings.json) connects
--     as the SQL login AdminDBA — set @Login below to that (the default).
--   * `dotnet run` on this laptop connects as your Windows account (Trusted_Connection); if that
--     account created the database it already owns it and needs nothing here.
--
-- A login that is sysadmin can already do everything, so the script only reports that and moves on.
-- (Recommended later: give the API its own login with just these rights, rather than an admin one.)

USE [TournamentSchedulerLogs];
GO

DECLARE @Login sysname = N'AdminDBA';

IF SUSER_ID(@Login) IS NULL
BEGIN
    RAISERROR(N'There is no login named %s on this server. Set @Login to the login in the API''s connection string.', 16, 1, @Login);
    RETURN;
END

IF IS_SRVROLEMEMBER(N'sysadmin', @Login) = 1
BEGIN
    PRINT @Login + N' is a sysadmin and can already read and write the logs database. Nothing to grant.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @Login)
BEGIN
    DECLARE @create nvarchar(max) = N'CREATE USER ' + QUOTENAME(@Login) + N' FOR LOGIN ' + QUOTENAME(@Login) + N';';
    EXEC (@create);
END

DECLARE @grant nvarchar(max) =
    N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@Login) + N'; ' +
    N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@Login) + N';';
EXEC (@grant);

PRINT @Login + N' can now read and write the logs database.';
GO
