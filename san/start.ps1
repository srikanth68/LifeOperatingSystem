# San startup script — launches the API and the notification Worker side by side.
# Usage: .\start.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "[san] Starting Worker (reminders/alerts -> Telegram) ..." -ForegroundColor DarkGreen
Start-Process pwsh -ArgumentList '-NoExit', '-Command', "Set-Location '$root\San.Worker'; dotnet run"

Write-Host "[san] Starting API on http://localhost:5300 ..." -ForegroundColor Green
Set-Location "$root\San.API"
dotnet run
