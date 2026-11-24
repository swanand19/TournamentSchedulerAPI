-- Production (Azure SQL): lets the API's own Azure identity use the database, so the connection string
-- holds no password (HLD P-07). Run after 01 and 02, the same way (Entra admin, the database selected).
--
-- Before running: turn on the web app's system-assigned identity (App Service -> Identity) and put the web
-- app's name below. The identity's user in the database has the web app's name.
--
-- What it gets: read and write every table (the API never changes the schema itself - releases do that with
-- the generated scripts), and EXECUTE for stored procedures (none in use yet). Safe to run again.

DECLARE @app sysname = N'<your-web-app-name>';   -- e.g. N'tournament-api-prod'

IF @app LIKE N'<%'
    THROW 50000, N'Put the web app''s name in @app first.', 1;

-- EXEC (...) takes only literals and variables, not QUOTENAME(), so each statement is built in @sql first.
DECLARE @user nvarchar(300) = QUOTENAME(@app);
DECLARE @sql nvarchar(max);

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @app)
BEGIN
    SET @sql = N'CREATE USER ' + @user + N' FROM EXTERNAL PROVIDER;';
    EXEC (@sql);
END

IF IS_ROLEMEMBER(N'db_datareader', @app) = 0
BEGIN
    SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + @user + N';';
    EXEC (@sql);
END
IF IS_ROLEMEMBER(N'db_datawriter', @app) = 0
BEGIN
    SET @sql = N'ALTER ROLE db_datawriter ADD MEMBER ' + @user + N';';
    EXEC (@sql);
END
SET @sql = N'GRANT EXECUTE TO ' + @user + N';';
EXEC (@sql);

SELECT name, type_desc, authentication_type_desc FROM sys.database_principals WHERE name = @app;
