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

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @app)
    EXEC (N'CREATE USER ' + QUOTENAME(@app) + N' FROM EXTERNAL PROVIDER;');

IF IS_ROLEMEMBER(N'db_datareader', @app) = 0
    EXEC (N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@app) + N';');
IF IS_ROLEMEMBER(N'db_datawriter', @app) = 0
    EXEC (N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@app) + N';');
EXEC (N'GRANT EXECUTE TO ' + QUOTENAME(@app) + N';');

SELECT name, type_desc, authentication_type_desc FROM sys.database_principals WHERE name = @app;
