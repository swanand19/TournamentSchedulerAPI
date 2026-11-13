# Production on Azure — set-up and releases

What this folder is for: putting the API, the web app and the admin portal on Azure (HLD Part B, decisions
P-01…P-25), and every release after that. The laptop's IIS site is untouched by all of it: it runs as the
`Production` environment, Azure runs as `Azure` (`appsettings.Azure.json`), so neither picks up the other's
settings.

| File | What it does |
|---|---|
| `01-game-schema.sql` | Every game table on an empty database (all migrations, idempotent) |
| `02-log-schema.sql` | The four log tables, in the **same** database |
| `03-app-identity.sql` | Lets the API's Azure identity read and write (no password anywhere) |
| `Publish-Azure.ps1` | Tests → publish → upload → health check, for the API |
| `Publish-StaticSite.ps1` | Lint → build (`.env.azure`) → upload, for the web app or the portal |

Tested on 2026-11-06 against a fresh LocalDB database: 01 and 02 run twice cleanly, then the API started
as `Azure` (key from a setting, no key folder) synced 145 permissions, made the message templates, wrote
all four log tables, answered the gateway, and refused a direct call with 403.

## Names used below

Pick them once and use them everywhere. These are examples — App Service names are global, so add
something of your own if a name is taken.

| Thing | Example |
|---|---|
| Resource group | `rg-tournament-prod` (region **Central India** for everything) |
| App Service plan | `plan-tournament-prod` (Linux, **B1**) |
| API web app | `tournament-api-prod` |
| SQL server / database | `sql-tournament-prod` / `TournamentSchedulerDB` |
| Key Vault | `kv-tournament-prod` |
| Application Insights | `ai-tournament-prod` |
| Static Web Apps | `swa-tournament-web`, `swa-tournament-portal` |

Azure gives new web apps a **default domain** like `tournament-api-prod-abc123.centralindia-01.azurewebsites.net`
(App Service → Overview). Every "API address" below means `https://` + that.

## One-time set-up (tracker "Production: Azure setup")

1. **Account and budget.** Pay-as-you-go subscription. Cost Management → Budgets: ₹1,500 a month, alerts at
   80% and 100%.
2. **Azure CLI on the laptop:** `winget install Microsoft.AzureCLI`, open a new PowerShell, `az login`.
3. **API web app.** Create a Web App: Code, **.NET 10 (LTS)**, **Linux**, Central India, a new **B1** plan.
   Then, in the web app:
   - Configuration → General settings: **Always On** on, **Web sockets** on, HTTPS Only on,
     Minimum TLS 1.2, Startup Command `dotnet TournamentScheduler.Api.dll`.
   - Health check: path `/api/health/live`.
   - Identity → System assigned: **On**.
4. **SQL.** Create an Azure SQL server with **Microsoft Entra authentication only**, yourself as the Entra
   admin; then a database `TournamentSchedulerDB`, compute + storage **Basic** (5 DTU, 2 GB), backup
   redundancy Locally-redundant. Networking: "Allow Azure services and resources" **on**, and "Add your
   client IPv4 address".
5. **Schema.** In SSMS: server `sql-tournament-prod.database.windows.net`, Authentication "Microsoft Entra
   MFA", Options → database `TournamentSchedulerDB`. Open and run, in this order, with the database chosen
   in the toolbar: `01-game-schema.sql`, `02-log-schema.sql`, then `03-app-identity.sql` after putting the
   web app's name in it.
6. **Key Vault** (permission model: Azure RBAC). Access control (IAM): yourself **Key Vault Secrets
   Officer**, the web app's identity **Key Vault Secrets User**. Secrets:
   - `smtp-password` — a Gmail **app password** (Google Account → Security → App passwords), not your
     own password.
   - `gateway-key-<id>` — the production gateway key, made like this on the laptop (it is a new key; the
     laptop's `20260929` key never goes to Azure):
     ```powershell
     cd "D:\Swanand\Football tournament\TournamentScheduler.Api"
     dotnet run -- gateway-keys new --dir C:\Temp\prod-key     # prints the key id and public key
     ```
     Copy the `.pem` file's whole text (with the BEGIN/END lines) into the secret, keep the printed id
     and public key, then **delete `C:\Temp\prod-key`**.
7. **Application Insights** (workspace-based, Central India). Usage and estimated costs → Daily cap
   **0.15 GB**. In the web app: Application Insights → turn on → pick it (this adds
   `APPLICATIONINSIGHTS_CONNECTION_STRING` for you).
8. **App settings** — web app → Environment variables. Key Vault references look like
   `@Microsoft.KeyVault(SecretUri=https://kv-tournament-prod.vault.azure.net/secrets/<name>/)`; after
   saving, each should show a green "Key vault reference" tick.

   App settings:

   | Name | Value |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Azure` |
   | `Gateway__Keys__<id>` | Key Vault reference to `gateway-key-<id>` |
   | `Gateway__CurrentKeyId` | `<id>` |
   | `Smtp__Password` | Key Vault reference to `smtp-password` |
   | `Hosting__AllowedOrigins__0` | `https://<web Static Web App's domain>` |
   | `Hosting__AllowedOrigins__1` | `https://<portal Static Web App's domain>` |
   | `Public__WebAppUrl` | `https://<web Static Web App's domain>` |

   Connection strings (type **SQLAzure**), both the same — game and log tables share the database:

   | Name | Value |
   |---|---|
   | `DefaultConnection` | `Server=tcp:sql-tournament-prod.database.windows.net,1433;Database=TournamentSchedulerDB;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=30` |
   | `LogConnection` | the same |

   Not set, on purpose: `Sms__Provider` (None until WhatsApp is ready — the apps hide number fields), any
   password for SQL (the identity signs in).
9. **Static Web Apps** ×2, plan **Free**, deployment source **Other**. Note each one's domain and its
   deployment token (Manage deployment token). Then fill in, with the API address and the key from step 6:
   - `tournament-scheduler-ui/.env.azure` (also `VITE_CONTACT_EMAIL`, shown on /privacy)
   - `tournament-scheduler-admin/.env.azure`
   - `tournament-scheduler-mobile/eas.json` (the `azure` profile, also `EXPO_PUBLIC_WEB_URL`)

   Then go back to step 8 for the two allowed origins and `Public__WebAppUrl`.

## First deploy

```powershell
cd "D:\Swanand\Football tournament\TournamentScheduler.Api\deploy\azure"
.\Publish-Azure.ps1 -ResourceGroup rg-tournament-prod -AppName tournament-api-prod
.\Publish-StaticSite.ps1 -App web -DeploymentToken "<web token>"
.\Publish-StaticSite.ps1 -App portal -DeploymentToken "<portal token>"
```

Then your admin account, from the laptop (a browser window asks you to sign in to Microsoft):

```powershell
cd "D:\Swanand\Football tournament\TournamentScheduler.Api"
$env:ASPNETCORE_ENVIRONMENT = "Azure"
$env:ConnectionStrings__DefaultConnection = "Server=tcp:sql-tournament-prod.database.windows.net,1433;Database=TournamentSchedulerDB;Authentication=Active Directory Interactive;Encrypt=True"
dotnet run -- users create-admin <your email> "<your name>"
```

Sign in to the portal (code by email, then choose a password) → App versions → add **Android 1.0.0**, store
link = the APK's download link for now. Then the smoke test in the tracker.

## Releasing (every time after)

Quiet hour, not during a match. App Service restarts the API (~20 s); the apps retry and the live lines
reconnect on their own.

1. **Database changes first**, when the release has a migration. Generate a script from the last migration
   that is already on Azure, check it, run it in SSMS as in step 5:
   ```powershell
   dotnet ef migrations script <LastMigrationOnAzure> --context TournamentDbContext --idempotent --output deploy\azure\NN-<name>.sql
   ```
   Changes must be *expand, then contract* (HLD B.10.3): the running API must still work on the new
   schema, so a release never needs the database rolled back. A data fix touching a column the same
   migration adds goes in `EXEC(N'…')` (CLAUDE.md, Gotchas).
2. `.\Publish-Azure.ps1 …` (runs the tests first).
3. Web app / portal changed? `.\Publish-StaticSite.ps1 …`.
4. Phone app changed? A new EAS build (mobile `eas.json`), then the App versions page.

**Rolling back the API:** check out the previous release's commit and run `Publish-Azure.ps1` again.

## Turning things on later

- **WhatsApp:** tracker group "Set up: WhatsApp codes and invites" — settings only, no script.
- **Google Play's reviewer account** (while the app is in review): `Auth__ReviewAccount__Email` and
  `Auth__ReviewAccount__Code` (6 digits). Clear the code when the review is done.
- **The store link** once on Google Play: `AppVersions__StoreUrls__Android`, and the App versions row.

## When something's wrong

- **Log stream** (web app → Log stream) shows start-up errors live.
- `https://<API address>/api/health`: `database: unreachable` → the connection string, or
  `03-app-identity.sql` not run for this web app's name.
- Gateway answers 503 "no key" → `Gateway__Keys__<id>` has no green Key Vault tick (the identity lacks
  "Key Vault Secrets User", or the secret name is wrong).
- Browser says CORS → the Static Web App's domain isn't in `Hosting__AllowedOrigins__*` exactly
  (`https://`, no trailing slash needed).
- Your laptop's IP changed and SSMS can't connect → SQL server → Networking → add your client IP again.
