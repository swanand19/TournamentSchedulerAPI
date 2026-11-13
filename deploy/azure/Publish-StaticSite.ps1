<#
.SYNOPSIS
  Builds the web app or the admin portal for Azure and uploads it to its Static Web App.

.DESCRIPTION
  Builds with `npm run build:azure` (the app's .env.azure: the Azure API address and the production
  gateway key), then uploads dist/ with the Static Web Apps CLI through npx (nothing to install). The
  deployment token is the Static Web App's own (Azure portal -> the Static Web App -> Manage deployment
  token); keep it out of files and git - pass it here or set SWA_CLI_DEPLOYMENT_TOKEN.

.EXAMPLE
  .\Publish-StaticSite.ps1 -App web -DeploymentToken "<token of the web app's Static Web App>"
  .\Publish-StaticSite.ps1 -App portal -DeploymentToken "<token of the portal's Static Web App>"
#>
param(
    [Parameter(Mandatory)] [ValidateSet("web", "portal")] [string]$App,
    [string]$DeploymentToken = $env:SWA_CLI_DEPLOYMENT_TOKEN
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($DeploymentToken)) { throw "Pass -DeploymentToken (the Static Web App's deployment token)." }

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$folder = Join-Path $root ($(if ($App -eq "web") { "tournament-scheduler-ui" } else { "tournament-scheduler-admin" }))

# A build with the template's placeholders would load but reach no API - refuse it.
$envFile = Get-Content (Join-Path $folder ".env.azure") -Raw
if ($envFile -match "=<") { throw "$folder\.env.azure still has <placeholders>. Fill in the API address and the production key first." }

Push-Location $folder
try {
    Write-Host "1/3 Lint and gateway check..." -ForegroundColor Cyan
    npm run lint --silent
    if ($LASTEXITCODE -ne 0) { throw "Lint failed; nothing was deployed." }
    npm run verify:gateway --silent
    if ($LASTEXITCODE -ne 0) { throw "The gateway crypto doesn't match the shared vectors; nothing was deployed." }

    Write-Host "2/3 Building for Azure..." -ForegroundColor Cyan
    npm run build:azure
    if ($LASTEXITCODE -ne 0) { throw "The build failed; nothing was deployed." }

    Write-Host "3/3 Uploading..." -ForegroundColor Cyan
    npx --yes @azure/static-web-apps-cli deploy ./dist --env production --deployment-token $DeploymentToken
    if ($LASTEXITCODE -ne 0) { throw "The upload failed. The previous version is still being served." }
}
finally {
    Pop-Location
}
Write-Host "Done. Open the Static Web App's address and sign in to check." -ForegroundColor Green
