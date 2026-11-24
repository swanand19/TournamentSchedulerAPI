# Releasing KickCricket to Azure — step by step

How to put a new version of the **API**, the **web app** and the **admin portal** on Azure yourself. Follow the parts in
order. Each part says how to check that it worked and what to do if it didn't.

The one-time set-up of Azure (creating the services) is in `README.md` beside the release scripts, in `TournamentScheduler.Api\deploy\azure\`. You don't need it for a normal release.

<!-- Source of docs/Azure-Release-Guide.docx: after editing, rebuild with docs/tools (npm run build). -->

## Contents

---

## What is where

| Thing | Name on Azure | Address |
|---|---|---|
| Resource group | `rg-tournament-prod` (Central India) | — |
| API (App Service, B1 Linux) | `kickcricket-api` | https://kickcricket-api-bxdpaxf6a5cthzbg.centralindia-01.azurewebsites.net |
| Database (Azure SQL, Basic) | server `kickcricket-sql`, database `TournamentSchedulerDB` | `kickcricket-sql.database.windows.net` |
| Web app (Static Web App) | `swa-kickcricket-web` | https://ambitious-coast-06d7fac00.3.azurestaticapps.net |
| Admin portal (Static Web App) | `swa-kickcricket-portal` | https://green-plant-071580f00.3.azurestaticapps.net |
| Secrets (gateway key `20261119`, mail password) | Key Vault `kv-kickcricket` | — |
| Logs and errors | Application Insights `ai-kickcricket` | — |

Folders on the laptop (all under `D:\Swanand\Football tournament\`):

| Folder | What |
|---|---|
| `TournamentScheduler.Api\` | the API; the release scripts are in `deploy\azure\`, the database scripts in `deploy\sql\` |
| `TournamentScheduler.Tests\` | the tests (the API's release script runs them first) |
| `tournament-scheduler-ui\` | the web app (its Azure settings are in `.env.azure`) |
| `tournament-scheduler-admin\` | the admin portal (its own `.env.azure`) |

---

## Part 0 — Once per laptop (skip if already done)

These are already set up on your current laptop. You only need this part on a new or rebuilt laptop.

1. **Install the tools** (then open a new PowerShell window so it finds them):
   - .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`
   - Node.js 24 LTS: `winget install OpenJS.NodeJS.LTS`
   - Azure CLI: `winget install Microsoft.AzureCLI`
   - SQL Server Management Studio (SSMS), for the database scripts.
2. **Install the apps' packages**, once in each folder:
   ```powershell
   cd "D:\Swanand\Football tournament\tournament-scheduler-ui";    npm install
   cd "D:\Swanand\Football tournament\tournament-scheduler-admin"; npm install
   ```
3. **Check the two `.env.azure` files exist** (they aren't in git): `tournament-scheduler-ui\.env.azure` and
   `tournament-scheduler-admin\.env.azure`. Each has `VITE_API_BASE_URL` (the API address above + `/api`),
   `VITE_GATEWAY_KEY_ID=20261119` and `VITE_GATEWAY_PUBLIC_KEY`; the web app's also has `VITE_CONTACT_EMAIL`. If
   one is missing, copy it from a backup or from the other app (the three values are the same). The release
   script refuses a file that still has `<placeholders>` in it.
4. **Office network only (Zscaler) — the certificate file.** The office network re-signs Azure's traffic with its
   own certificate. Windows trusts it, but the Azure CLI doesn't, so uploads fail with `CERTIFICATE_VERIFY_FAILED`.
   The fix is a file that lists the CLI's own certificates plus Zscaler's. It already exists at
   `C:\Users\Swanand.ingale\.azure\ca-bundle-with-zscaler.pem`. If it's ever missing, make it again:
   ```powershell
   $certifi = "C:\Program Files\Microsoft SDKs\Azure\CLI2\Lib\site-packages\certifi\cacert.pem"
   $z = Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -match "CN=Zscaler Root CA" | Select-Object -First 1
   $pem = "-----BEGIN CERTIFICATE-----`n" + [Convert]::ToBase64String($z.RawData, "InsertLineBreaks") + "`n-----END CERTIFICATE-----`n"
   (Get-Content $certifi -Raw) + "`n" + $pem | Set-Content "$env:USERPROFILE\.azure\ca-bundle-with-zscaler.pem" -Encoding ascii
   ```

---

## Part 1 — Before you start

1. **Pick a quiet time**, not during a match. Restarting the API takes 2–3 minutes on B1. The old copy keeps
   serving until the new one is ready, so usually nobody notices.
2. **Open PowerShell** (Windows PowerShell or PowerShell 7, either works). You don't need to run it as
   administrator.
3. **Sign in to Azure** (once a day is usually enough):
   ```powershell
   az login
   ```
   A browser opens. Sign in with the account that owns the subscription. If it asks you to pick one, choose
   **"Azure subscription"**. Check it worked:
   ```powershell
   az account show --query "{name:name, user:user.name}" -o table
   ```
4. **On the office network**, set these two lines in the **same** PowerShell window before every release. They
   only last for that window; at home or on mobile data you don't need them, but they do no harm:
   ```powershell
   $env:REQUESTS_CA_BUNDLE = "$env:USERPROFILE\.azure\ca-bundle-with-zscaler.pem"
   $env:NODE_OPTIONS = "--use-system-ca"
   ```
5. **Know what's in the release:**
   - Did the API change? Do Part 3.
   - Is there a new database script (`TournamentScheduler.Api\deploy\sql\NN-….sql`)? Do Part 2 **first**.
   - Did the web app change? Do Part 4.
   - Did the portal change? Do Part 5.

   When in doubt, release all three. Releasing something that didn't change does no harm.

---

## Part 2 — Database changes (only when the release has a new script)

> **Always before the API.** The running API keeps working with the new tables and columns, but the new API doesn't
work without them.

1. **Find out which script is next.** In SSMS, connected to the Azure database (step 2 below), run:
   ```sql
   SELECT TOP 3 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
   ```
   The top row is the last change Azure has, for example `20261124060334_AddLeagues` = script 24. Run every script
   in `deploy\sql\` with a higher number, **one at a time, in number order**.
2. **Connect SSMS to Azure:**
   - Server name: `kickcricket-sql.database.windows.net`
   - Authentication: **Microsoft Entra MFA**, user name = your Azure sign-in.
   - Options → Connection Properties → Connect to database: `TournamentSchedulerDB`.
   - Options → Encryption: Mandatory (the default), Trust server certificate **ticked** if it complains about the
     certificate on the office network.
3. **If SSMS says your IP address isn't allowed** (this happens often on the office network, where the address keeps
   changing): SSMS usually offers **"Add my client IP"**. Sign in and accept. If it doesn't offer that, go to
   portal.azure.com → SQL servers → `kickcricket-sql` → Security → Networking → **Add your client IPv4 address** →
   Save. Wait a minute, then connect again.
4. **Run the script:** File → Open → the `.sql` file. Check that the database box in the toolbar says
   **`TournamentSchedulerDB`**, not `master`; Azure SQL has no `USE` line, so the toolbar is what counts. Press
   **Execute** (F5).
5. **Check:** the Messages tab ends with "Commands completed successfully" and no red text. Run the `SELECT` from
   step 1 again; the new migration should be on top. The scripts are safe to run twice, so if you aren't sure
   whether one ran, run it again.
6. **Do the same on the laptop's database** (`WJLP-3571\SUBSMANAGEMENTDB`, Windows authentication) so both stay
   alike.

> **If it fails halfway:** each script runs in one transaction, so a failure changes nothing. Read the red message and
fix the cause (usually the wrong database in the toolbar, or an earlier script not yet run), then run it again.

---

## Part 3 — Release the API

1. **Go to the release scripts' folder:**
   ```powershell
   cd "D:\Swanand\Football tournament\TournamentScheduler.Api\deploy\azure"
   ```
2. **Run the release script:**
   ```powershell
   .\Publish-Azure.ps1 -ResourceGroup rg-tournament-prod -AppName kickcricket-api
   ```
   If Windows refuses with "running scripts is disabled on this system", run this once, then try again:
   `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.

   It does four things and stops at the first problem:
   - **1/4 Tests:** about 700 tests, 2–4 minutes. "Failed" anywhere → nothing is deployed. Read which test failed.
     A dozen *skipped* tests are normal; they need a local SQL Server connection.
   - **2/4 Publishing:** builds a Release copy, packed into a zip in your Temp folder.
   - **3/4 Uploading:** sends the zip to `kickcricket-api`. The API restarts in the background.
   - **4/4 Health check:** waits up to two minutes for `/api/health` to answer.
3. **Read the last line.** Good:
   ```
   Deployed to https://kickcricket-api-…azurewebsites.net - health: ok, database: ok, environment: Azure
   ```
   The details matter:
   - `environment: Azure`: right. Anything else means the app setting `ASPNETCORE_ENVIRONMENT` changed.
   - `database: ok`: right. `unreachable` means the API can't reach SQL (see "When something goes wrong").
4. **If the health check timed out** ("didn't answer in two minutes"), it usually isn't broken. On B1 a start can
   take up to about 3 minutes, and the old copy keeps serving meanwhile. Wait two more minutes, then open
   https://kickcricket-api-bxdpaxf6a5cthzbg.centralindia-01.azurewebsites.net/api/health in a browser. A JSON answer
   with `"database":"ok"` means it's fine. If it's still not answering after 5 minutes, open portal.azure.com →
   App Services → `kickcricket-api` → **Log stream**, and read the error.
5. **Re-deploying the same code again** (say the upload failed on a network blip and the tests already passed):
   ```powershell
   .\Publish-Azure.ps1 -ResourceGroup rg-tournament-prod -AppName kickcricket-api -SkipTests
   ```
   Use `-SkipTests` only when nothing changed since the tests passed.

> **What the script never touches:** the database (Part 2 does that), the app settings and Key Vault secrets on Azure,
and the laptop's IIS site.

---

## Part 4 — Release the web app

1. **Stay in the same folder** (`...\TournamentScheduler.Api\deploy\azure`) and the same PowerShell window (it
   still has the office-network lines from Part 1).
2. **Put the web app's upload token in this window only.** It's fetched from Azure and never shown on screen:
   ```powershell
   $env:SWA_CLI_DEPLOYMENT_TOKEN = az staticwebapp secrets list -n swa-kickcricket-web -g rg-tournament-prod --query properties.apiKey -o tsv
   ```
   Check it's there without printing it: `$env:SWA_CLI_DEPLOYMENT_TOKEN.Length` should show a number around 100.
   Zero, or an error, means you aren't signed in (`az login`) or the name is mistyped.
3. **Run the release script:**
   ```powershell
   .\Publish-StaticSite.ps1 -App web
   ```
   Don't add `2>&1` to the end. In Windows PowerShell that turns a harmless build warning into an error, and the
   release stops.

   It does three things:
   - **1/3 Lint and gateway check:** 0 errors needed. A few *warnings* are normal (4 today).
   - **2/3 Building for Azure:** `npm run build:azure`, using `.env.azure`. A warning about large chunks is normal.
   - **3/3 Uploading:** "✔ Project deployed to https://ambitious-coast-06d7fac00.3.azurestaticapps.net 🚀" means
     done.
4. **Clear the token:**
   ```powershell
   $env:SWA_CLI_DEPLOYMENT_TOKEN = $null
   ```
5. **If the upload fails** with `SELF_SIGNED_CERT_IN_CHAIN` or "unable to get local issuer certificate", the
   `NODE_OPTIONS` line from Part 1 is missing in this window. For "fetch failed", "ECONNRESET" or a timeout (a
   network blip), just run step 3 again; the previous version keeps being served until an upload succeeds.

---

## Part 5 — Release the admin portal

The same as Part 4, with the portal's token and `-App portal`:

```powershell
$env:SWA_CLI_DEPLOYMENT_TOKEN = az staticwebapp secrets list -n swa-kickcricket-portal -g rg-tournament-prod --query properties.apiKey -o tsv
.\Publish-StaticSite.ps1 -App portal
$env:SWA_CLI_DEPLOYMENT_TOKEN = $null
```

Done when it shows "Project deployed to https://green-plant-071580f00.3.azurestaticapps.net".

---

## Part 6 — Check the release

1. **API:** https://kickcricket-api-bxdpaxf6a5cthzbg.centralindia-01.azurewebsites.net/api/health shows
   `"status":"ok"` and `"database":"ok"`.
2. **Web app:** open https://ambitious-coast-06d7fac00.3.azurestaticapps.net in a **private / incognito window**, so
   the browser doesn't show an old copy. Sign in with your email code. Home loads your tournaments. Then open one
   thing that's new in this release (for example a league page).
3. **Public page:** open a tournament's share link (`/live/<code>`) while signed out; matches and stats show.
4. **Portal:** open https://green-plant-071580f00.3.azurestaticapps.net, sign in (code, then your portal
   password), and open Tournaments and App versions.
5. **Phone app:** open the installed APK and pull Home down to refresh. It talks to the same API, so new API
   features appear as soon as the phone app version that shows them is installed.
6. **In the first hour, keep an eye on errors:** portal.azure.com → Application Insights `ai-kickcricket` →
   **Failures**. Or, in the API's database, the newest rows of the `ErrorLogs` table.

---

## Part 7 — Afterwards (housekeeping)

- **Commit and push** each changed repo yourself (API, web, portal, mobile each have their own GitHub repo), so you
  can find the exact code of this release later. Tagging it, for example `git tag release-2026-11-24`, makes
  rolling back easier.
- **Fresh-database script:** when a release added a migration, `deploy\azure\01-game-schema.sql` should be
  regenerated, so a brand-new database would get every table. It's only used for a fresh database, never for a
  release, so this can wait. Generate it (from `TournamentScheduler.Api\`):
  ```powershell
  dotnet ef migrations script --context TournamentDbContext --idempotent --output deploy/azure/01-game-schema.raw.sql
  ```
  then put the old file's header comment on top, save it as `01-game-schema.sql` and delete the `.raw.sql` file.
- **Tracker / notes:** tick the release on the hand-over tracker.

---

## Rolling back

- **API:** check out the previous release's commit in the API repo (`git checkout <commit or tag>`), run Part 3
  again, then `git checkout main`. The database stays as it is; the scripts only ever add things, so the older API
  works on the newer database.
- **Web app / portal:** check out the previous commit in that app's repo and run Part 4 or 5 again. Then
  `git checkout main`.
- **Database:** never rolled back. If a script did something wrong, the fix is a new script.

---

## When something goes wrong

| What you see | Why | What to do |
|---|---|---|
| `Not signed in to Azure. Run: az login` | The Azure sign-in expired | `az login`, then run the script again |
| `CERTIFICATE_VERIFY_FAILED` (API upload) | Office network, `REQUESTS_CA_BUNDLE` not set in this window | Part 1 step 4, then run again |
| `SELF_SIGNED_CERT_IN_CHAIN` (web / portal upload) | Office network, `NODE_OPTIONS` not set in this window | Part 1 step 4, then run again |
| `Tests failed; nothing was deployed.` | A test broke | Scroll up for the failing test's name and message; fix it, or ask Claude |
| `Lint failed; nothing was deployed.` | An error in the web or portal code | Run `npm run lint` in that app's folder to see it |
| `.env.azure still has <placeholders>` | The Azure settings file isn't filled in | Part 0 step 3 |
| Health: `database: unreachable` | The API can't reach SQL | Check that the SQL server is running (portal → `kickcricket-sql`) and that the web app's connection strings weren't changed. If the web app was ever re-created, re-run `03-app-identity.sql` |
| Gateway answers 503 "no key" | The API can't read the key from Key Vault | portal → `kickcricket-api` → Environment variables: `Gateway__Keys__20261119` must show a green "Key vault reference" tick |
| The browser says "CORS" | The web app's address isn't in `Hosting__AllowedOrigins__*` | Only changes if a Static Web App was re-created; set the new address there |
| SSMS: "Client with IP address … is not allowed" | The laptop's public IP changed | Part 2 step 3 |
| Web app shows the old version | The browser cached it | A private window, or Ctrl+F5 |
| Upload: "fetch failed" / timeout | Network blip | Run the same step again |

> **Never:** put a token, password or the gateway's private key in a file or a chat; run `Setup-IisSite.ps1`; or
publish over the IIS folder's `appsettings.json` (that's the laptop's site, separate from Azure).

---

## The whole release on one screen

```powershell
# 1. Sign in (and the office-network lines)
az login
$env:REQUESTS_CA_BUNDLE = "$env:USERPROFILE\.azure\ca-bundle-with-zscaler.pem"
$env:NODE_OPTIONS = "--use-system-ca"

# 2. Database: new deploy\sql\NN-*.sql scripts in SSMS, on Azure and the laptop (Part 2)

# 3. API
cd "D:\Swanand\Football tournament\TournamentScheduler.Api\deploy\azure"
.\Publish-Azure.ps1 -ResourceGroup rg-tournament-prod -AppName kickcricket-api

# 4. Web app
$env:SWA_CLI_DEPLOYMENT_TOKEN = az staticwebapp secrets list -n swa-kickcricket-web -g rg-tournament-prod --query properties.apiKey -o tsv
.\Publish-StaticSite.ps1 -App web

# 5. Portal
$env:SWA_CLI_DEPLOYMENT_TOKEN = az staticwebapp secrets list -n swa-kickcricket-portal -g rg-tournament-prod --query properties.apiKey -o tsv
.\Publish-StaticSite.ps1 -App portal
$env:SWA_CLI_DEPLOYMENT_TOKEN = $null

# 6. Check (Part 6)
```
