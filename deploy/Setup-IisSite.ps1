#Requires -RunAsAdministrator
<#
.SYNOPSIS
  One-time setup: hosts the Tournament Scheduler API in IIS on this laptop so phones on the same
  Wi-Fi can reach it over HTTP.

.DESCRIPTION
  Safe to run again: every step checks before it changes anything.
    1. Checks the ASP.NET Core Hosting Bundle is installed.
    2. Creates the app pool (no managed code, always running, never idles out).
    3. Publishes the API to the site folder.
    4. Creates the IIS site on the chosen port.
    5. Makes sure the secure gateway has a key, and lets the app pool read it (and nothing else can).
    6. Opens that port in Windows Firewall for Private networks only.
    7. Gives the app pool's identity read/write access to the database (Windows authentication,
       so no password is stored anywhere).
    8. Starts the site and calls /api/health to prove it works.

.EXAMPLE
  Right-click PowerShell > Run as administrator, then:
    cd "D:\Swanand\Football tournament\TournamentScheduler.Api\deploy"
    .\Setup-IisSite.ps1
#>
param(
    [string]$SiteName = "TournamentSchedulerApi",
    [int]$Port = 5080,
    [string]$SitePath = "C:\inetpub\TournamentSchedulerApi",
    [string]$SqlInstance = "WJLP-3571\SUBSMANAGEMENTDB",
    [string]$Database = "TournamentSchedulerDB",
    [string]$KeyDirectory = (Join-Path $env:ProgramData "TournamentScheduler\keys")
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path $PSScriptRoot -Parent
$poolIdentity = "IIS APPPOOL\$SiteName"

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# 1. Hosting Bundle ------------------------------------------------------------
Step "Checking the ASP.NET Core Hosting Bundle"
$ancm = Join-Path $env:ProgramFiles "IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
if (-not (Test-Path $ancm)) {
    throw "The ASP.NET Core Hosting Bundle for .NET 10 isn't installed. Download 'Hosting Bundle' from https://dotnet.microsoft.com/download/dotnet/10.0, install it, then run this script again."
}
Write-Host "Found $ancm"

Import-Module WebAdministration

# 2. App pool ------------------------------------------------------------------
Step "App pool '$SiteName'"
if (-not (Test-Path "IIS:\AppPools\$SiteName")) {
    New-WebAppPool -Name $SiteName | Out-Null
    Write-Host "Created."
}
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name startMode -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.identityType -Value "ApplicationPoolIdentity"
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Write-Host "No managed code, always running, no idle timeout, identity $poolIdentity."

# 3. Publish -------------------------------------------------------------------
Step "Publishing the API to $SitePath"
if (Test-Path $SitePath) {
    # Tells IIS to release the app's files while they are replaced.
    Set-Content -Path (Join-Path $SitePath "app_offline.htm") -Value "Updating, back in a moment."
}
dotnet publish (Join-Path $projectDir "TournamentScheduler.Api.csproj") -c Release -o $SitePath --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
Remove-Item (Join-Path $SitePath "app_offline.htm") -ErrorAction SilentlyContinue

# 4. Site ----------------------------------------------------------------------
Step "IIS site '$SiteName' on port $Port"
if (-not (Get-Website -Name $SiteName)) {
    New-Website -Name $SiteName -Port $Port -PhysicalPath $SitePath -ApplicationPool $SiteName | Out-Null
    Write-Host "Created."
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true

# 5. Gateway key ---------------------------------------------------------------
# Every app request arrives encrypted for this key (see Gateway/README.md). `ensure` creates one only
# when the folder has none, so re-running this script never changes the key the phones are using.
Step "Secure gateway key in $KeyDirectory"
dotnet (Join-Path $SitePath "TournamentScheduler.Api.dll") gateway-keys ensure --dir $KeyDirectory
if ($LASTEXITCODE -ne 0) { throw "Creating the gateway key failed." }
# The folder is created readable only by SYSTEM, Administrators and you; the site needs to read it too.
icacls $KeyDirectory /grant "${poolIdentity}:(OI)(CI)R" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Letting $poolIdentity read $KeyDirectory failed." }
Write-Host "$poolIdentity can read the key. Copy the two lines above into each app's env file if they aren't there yet."

# 6. Firewall ------------------------------------------------------------------
Step "Windows Firewall"
$ruleName = "Tournament Scheduler API (TCP $Port)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port `
        -Action Allow -Profile Private | Out-Null
    Write-Host "Opened port $Port for Private networks."
}
$wifi = Get-NetConnectionProfile | Where-Object { $_.InterfaceAlias -like "Wi-Fi*" } | Select-Object -First 1
if ($wifi -and $wifi.NetworkCategory -ne "Private") {
    Write-Warning ("Your Wi-Fi '$($wifi.Name)' is marked $($wifi.NetworkCategory), so phones can't reach port $Port. " +
        "If this is your home network, run: Set-NetConnectionProfile -InterfaceAlias '$($wifi.InterfaceAlias)' -NetworkCategory Private")
}

# 7. Database access -----------------------------------------------------------
Step "Database access for $poolIdentity"
$sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$poolIdentity')
    CREATE LOGIN [$poolIdentity] FROM WINDOWS;
USE [$Database];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$poolIdentity')
    CREATE USER [$poolIdentity] FOR LOGIN [$poolIdentity];
ALTER ROLE db_datareader ADD MEMBER [$poolIdentity];
ALTER ROLE db_datawriter ADD MEMBER [$poolIdentity];
"@
sqlcmd -S $SqlInstance -E -b -Q $sql
if ($LASTEXITCODE -ne 0) {
    throw "Granting database access failed. Your Windows account needs rights to create logins on $SqlInstance."
}
Write-Host "Read and write access granted (no schema rights; run migrations from your own account)."

# 8. Start and check -----------------------------------------------------------
Step "Starting and checking"
# A restart, not just a start: the site reads the gateway keys once, when it starts.
Restart-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue
Start-Website -Name $SiteName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
try {
    # /api/health is the one service callable without encryption. Its answer is the { status, data } envelope.
    $health = Invoke-RestMethod "http://localhost:$Port/api/health" -TimeoutSec 30
    Write-Host "Health: $($health.data.status), database: $($health.data.database)" -ForegroundColor Green
}
catch {
    Write-Warning "The health check failed: $($_.Exception.Message). Check Event Viewer > Windows Logs > Application."
}

$ip = (Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias "Wi-Fi*" -ErrorAction SilentlyContinue |
    Select-Object -First 1).IPAddress
Write-Host "`nIn the mobile app, set the server to:  http://$($ip):$Port" -ForegroundColor Green
Write-Host "(If the laptop's IP changes, update it in the app's Server screen.)"
