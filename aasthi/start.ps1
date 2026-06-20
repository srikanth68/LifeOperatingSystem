# Aasthi startup script
# Usage: .\start.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "[aasthi] Starting API on http://localhost:5200 ..." -ForegroundColor Green
Set-Location "$root\Aasthi.API"
dotnet run
