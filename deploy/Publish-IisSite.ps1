#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Redeploys the API to the IIS site after code changes. Run Setup-IisSite.ps1 once first.

.EXAMPLE
  Right-click PowerShell > Run as administrator, then:
    cd "D:\Swanand\Football tournament\TournamentScheduler.Api\deploy"
    .\Publish-IisSite.ps1
#>
param(
    [string]$SiteName = "TournamentSchedulerApi",
    [int]$Port = 5080,
    [string]$SitePath = "C:\inetpub\TournamentSchedulerApi"
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path $PSScriptRoot -Parent
$offline = Join-Path $SitePath "app_offline.htm"

# app_offline.htm makes IIS stop the app and release its files while they are replaced.
Set-Content -Path $offline -Value "Updating, back in a moment."
try {
    dotnet publish (Join-Path $projectDir "TournamentScheduler.Api.csproj") -c Release -o $SitePath --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed; the site stays offline until you fix it and run this again." }
}
finally {
    if ($LASTEXITCODE -eq 0) { Remove-Item $offline -ErrorAction SilentlyContinue }
}

$health = Invoke-RestMethod "http://localhost:$Port/api/health" -TimeoutSec 30
Write-Host "Deployed. Health: $($health.status), database: $($health.database)" -ForegroundColor Green
