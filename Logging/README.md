# Request and error logging

Every request leaves rows in a separate logs database, `TournamentSchedulerLogs`, on the same SQL
Server. One `RequestUUID` ties a request's rows together across all four tables — for gateway calls
it is the app's own `requestUUID`, and every response carries it back as `X-Request-Id`.

| Table | Written by | What it holds |
|---|---|---|
| `MBMiddlewareLogs` | `GatewayMiddleware` | Every `POST /api/gateway`: the envelope as it arrived (encrypted), as it left, the clear header fields, and `RejectedReason` when the gateway refused it itself (bad envelope, tampered, wrong key, replay, clock, unknown service). |
| `MBActivityLogs` | `ActivityLogMiddleware` (after routing) | Every call that reached an action, through the gateway or direct: controller, action, the action's own URL (route values and query), the plain request body, the `{ status, data }` answer, status, success, message, timing. |
| `ErrorLogs` | `ErrorLogProvider` (an `ILoggerProvider`) | Everything logged at Error or Critical anywhere — our code, ASP.NET's exception handler, EF when SQL Server doesn't answer — with the request's id, URL and plain body. |
| `RemoteLogs` | nothing yet | Reserved for calls to third-party APIs (see *Future: RemoteLogs*). |

## How it works

```
request ─► RequestCorrelation (id) ─► ... ─► Gateway ─► Routing ─► ActivityLog ─► action
                                              │                      │
                                              ▼                      ▼            errors ─► ErrorLogProvider
                                           LogQueue (in memory, never blocks) ◄─────────────────┘
                                              │
                               LogWriterService (background): batches of 100 / every 1 s
                                              │
                          logs database  ── or, if it can't take them ──  fallback file
```

- **Requests never wait for logging.** Rows go into an in-memory queue; a background writer inserts
  them in batches. A slow or missing logs database can't slow a scorer's tap.
- **The logs database being down never breaks a request.** A failed batch goes to
  `C:\ProgramData\TournamentScheduler\logs\logs-yyyyMMdd.jsonl` and the database is left alone for
  30 s before it is tried again. If even the file fails, the rows are lost (and a warning logged).
- **On shutdown** whatever is still queued is written first. A hard crash can lose about a second.
- **Queue full** (the writer far behind): new rows are dropped and counted, and a warning says how many.
- **Its own failures are only warnings** and its database context has no logger, so a failing log
  write can never feed itself into ErrorLogs.

## What is stored

- **Reads (GET) keep no response body** — the live screens poll every few seconds, and every copy of
  the same match state would be stored. Their status, message, size and timing are still recorded.
  Switch `LogStore:LogReadResponseBodies` on while chasing what a screen was sent.
- **Credentials are masked**: JSON fields named `password`, `pin`, `otp`, `token`, `secret`,
  `apiKey`, `authorization`, … (any depth, any case) become `"***"`. Everything else is plain on
  purpose — that's what makes the logs useful. The list is `LogStore:MaskedFields`.
  *Why mask even hashed values:* a hash the app sends to log in works exactly like the password
  itself. Passwords should reach the server as typed (the gateway encrypts them in transit) and be
  hashed there with a slow salted hash (ASP.NET Core's `PasswordHasher`); PINs also need lock-out
  after a few wrong tries.
- **Bodies are capped** at `LogStore:MaxBodyChars` (64 KB), with a note of how much was cut.
- **Over-long header values** (journeyId, deviceId…) are trimmed to their column rather than failing
  the batch.
- **All times are UTC** (`datetime2(3)`).

## Retention and size

- Rows older than `LogStore:RetentionHours` (48) are deleted hourly, in batches of 5,000, and old
  fallback files with them. The first purge runs at start-up.
- The database is capped at **2 GB** data + **512 MB** log, in SIMPLE recovery (script 01). A heavy
  test day is roughly 300 MB with read bodies off. If the cap is hit, inserts fail and rows go to the
  fallback file until the purge frees space.

## Settings

```json
"ConnectionStrings": { "LogConnection": "Server=…;Database=TournamentSchedulerLogs;…" },
"LogStore": {
  "Enabled": true,                 // false: nothing queued or written
  "RetentionHours": 48,
  "PurgeIntervalMinutes": 60,
  "LogReadResponseBodies": false,
  "MaxBodyChars": 65536,
  "MaskedFields": ["password", "pin", "otp", "token", "…"],
  "QueueCapacity": 10000, "BatchSize": 100, "FlushIntervalMs": 1000, "RetryAfterSeconds": 30,
  "FallbackDirectory": "C:\\ProgramData\\TournamentScheduler\\logs"
}
```

Without a `LogConnection`, every row goes to the fallback file (with a warning every 30 s).

## Setting it up (once)

Scripts in `Logging/Sql/`, run in SSMS against `WJLP-3571\SUBSMANAGEMENTDB` as an admin. Each is
safe to run again. All three were tested on SQL Server LocalDB.

1. **`01-create-log-database.sql`** — creates `TournamentSchedulerLogs`, SIMPLE recovery, 2 GB / 512 MB caps.
2. **`02-create-log-tables.sql`** — the four tables and their indexes (generated from the EF migration).
3. **`03-grant-log-permissions.sql`** — read + write for the API's login. `@Login` defaults to
   `AdminDBA` (the IIS site's login); a sysadmin login needs nothing, and the script says so.

Then, for the **IIS site** (`D:\Swanand\IIS Websites\inetpub\TournamentSchedulerAPI`):

4. In its `appsettings.json`, add under `ConnectionStrings` (same login and password as `DefaultConnection`):
   ```json
   "LogConnection": "Server=WJLP-3571\\SUBSMANAGEMENTDB;Database=TournamentSchedulerLogs;User ID=AdminDBA;Password=<same as DefaultConnection>;TrustServerCertificate=True"
   ```
5. Let the site write fallback files (PowerShell, no admin needed if you create the folder):
   ```powershell
   New-Item -ItemType Directory -Force C:\ProgramData\TournamentScheduler\logs
   icacls C:\ProgramData\TournamentScheduler\logs /grant "IIS APPPOOL\TournamentSchedulerAPI:(OI)(CI)M"
   ```
6. Publish the new build to the site (keeping its `appsettings.json`) and restart it.

For **`dotnet run`**, the repo's `appsettings.json` already points `LogConnection` at the same
database with Windows authentication.

## Useful queries

```sql
-- Everything about one request (the id from X-Request-Id, or the app's requestUUID)
DECLARE @id uniqueidentifier = '…';
SELECT * FROM MBMiddlewareLogs WHERE RequestUUID = @id;
SELECT * FROM MBActivityLogs   WHERE RequestUUID = @id;
SELECT * FROM ErrorLogs        WHERE RequestUUID = @id;

-- Latest errors
SELECT TOP 50 Crd, ServiceRequestId, ExceptionType, Message, LogMessage, Url FROM ErrorLogs ORDER BY Crd DESC;

-- Requests the gateway refused, and why
SELECT TOP 50 StartDate, ServiceRequestId, HttpStatusCode, RejectedReason, Channel, DeviceId
FROM MBMiddlewareLogs WHERE RejectedReason IS NOT NULL ORDER BY StartDate DESC;

-- Slowest services in the last hour
SELECT ServiceRequestId, COUNT(*) AS Calls, AVG(DurationMs) AS AvgMs, MAX(DurationMs) AS MaxMs
FROM MBActivityLogs WHERE StartDate > DATEADD(hour, -1, SYSUTCDATETIME())
GROUP BY ServiceRequestId ORDER BY AvgMs DESC;

-- One phone's session, in order
SELECT StartDate, ServiceRequestId, HttpStatusCode, StatusMessage FROM MBActivityLogs
WHERE JourneyId = '…' ORDER BY StartDate;
```

## Changing the tables

The EF migrations in `Logging/Migrations` are the source of truth:

```
dotnet ef migrations add <Name> --context LogDbContext --output-dir Logging/Migrations
dotnet ef migrations script --context LogDbContext --idempotent --output Logging/Sql/02-create-log-tables.sql
```

then put the header (with `USE [TournamentSchedulerLogs]`) back on top of the regenerated script. The
game database's migrations now need `--context TournamentDbContext`.

## Future: RemoteLogs

The table exists; nothing writes to it. When a third-party API is added, the plan is a
`DelegatingHandler` attached to its `HttpClient` (`AddHttpClient(...).AddHttpMessageHandler<…>()`)
that records method, URL, masked headers (`Authorization`, API keys), the plain and encrypted
request and response, status and timing, under the current request's `RequestUUID` — so every
integration logs the same way without its own code.
