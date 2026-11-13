<#
.SYNOPSIS
  Deploys the API to Azure App Service from this laptop: tests, publish, upload, health check.

.DESCRIPTION
  The first releases go out from the laptop (README.md, "Releasing"); GitHub Actions takes over once the
  tests live in the API's repository. Needs the Azure CLI (winget install Microsoft.AzureCLI) and
  `az login` once. Release in a quiet hour, not during matches: App Service restarts the API (about 20 s;
  phones retry, live lines reconnect by themselves).

  Database changes are NOT made here. If the release adds a migration, run its script in SSMS first
  (README.md, "Database changes") - the old API keeps working on the new schema (expand, then contract).

.EXAMPLE
  cd "D:\Swanand\Football tournament\TournamentScheduler.Api\deploy\azure"
  .\Publish-Azure.ps1 -ResourceGroup rg-tournament-prod -AppName tournament-api-prod
#>
param(
    [Parameter(Mandatory)] [string]$ResourceGroup,
    [Parameter(Mandatory)] [string]$AppName,
    # Only for a re-deploy of something already tested (the same commit).
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$apiDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$testsDir = Join-Path (Split-Path $apiDir -Parent) "TournamentScheduler.Tests"
$work = Join-Path ([IO.Path]::GetTempPath()) "tournament-api-publish"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "The Azure CLI isn't installed. Run: winget install Microsoft.AzureCLI, open a new PowerShell, then: az login"
}
az account show --output none 2>$null
if ($LASTEXITCODE -ne 0) { throw "Not signed in to Azure. Run: az login" }

if (-not $SkipTests) {
    Write-Host "1/4 Tests..." -ForegroundColor Cyan
    dotnet test $testsDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed; nothing was deployed." }
}

Write-Host "2/4 Publishing..." -ForegroundColor Cyan
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
$out = Join-Path $work "out"
# Framework-dependent: App Service's .NET 10 runtime runs it on Linux. The laptop's own settings
# (appsettings.Development.json) are left behind; the Azure ones come from App Service and Key Vault.
dotnet publish (Join-Path $apiDir "TournamentScheduler.Api.csproj") -c Release -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed; nothing was deployed." }
Remove-Item (Join-Path $out "appsettings.Development.json") -ErrorAction SilentlyContinue
$zip = Join-Path $work "api.zip"
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

Write-Host "3/4 Uploading to $AppName (the API restarts)..." -ForegroundColor Cyan
az webapp deploy --resource-group $ResourceGroup --name $AppName --src-path $zip --type zip --output none
if ($LASTEXITCODE -ne 0) { throw "The upload failed. The previous release is still running." }

Write-Host "4/4 Health check..." -ForegroundColor Cyan
$hostName = az webapp show --resource-group $ResourceGroup --name $AppName --query defaultHostName --output tsv
$health = $null
foreach ($try in 1..12) {
    try { $health = Invoke-RestMethod "https://$hostName/api/health" -TimeoutSec 20; break }
    catch { Start-Sleep -Seconds 10 }
}
if ($null -eq $health) { throw "https://$hostName/api/health didn't answer in two minutes. Look at App Service -> Log stream." }
Write-Host "Deployed to https://$hostName - health: $($health.data.status), database: $($health.data.database), environment: $($health.data.environment)" -ForegroundColor Green
if ($health.data.database -ne "ok") { Write-Warning "The API is up but can't reach its database: check the connection string and 03-app-identity.sql." }
