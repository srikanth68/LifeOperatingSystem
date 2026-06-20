# Vitara startup script
# Usage: .\start.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Ensure .env exists
if (-not (Test-Path "$root\.env")) {
    Write-Host "[vitara] .env not found. Creating from template..." -ForegroundColor Yellow
    Copy-Item "$root\.env.template" "$root\.env"
    Write-Host "[vitara] Edit $root\.env and add OURA_CLIENT_SECRET, then re-run." -ForegroundColor Cyan
    exit 1
}

# Load .env into session
Get-Content "$root\.env" | ForEach-Object {
    if ($_ -match '^\s*#' -or $_ -match '^\s*$') { return }
    $parts = $_ -split '=', 2
    if ($parts.Count -eq 2) {
        [System.Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim(), 'Process')
    }
}

Write-Host "[vitara] Starting API on http://localhost:5100 ..." -ForegroundColor Green
$apiJob = Start-Job -ScriptBlock {
    Set-Location "$using:root\Vitara.API"
    dotnet run
}

Write-Host "[vitara] Starting Sync Worker ..." -ForegroundColor Green
$workerJob = Start-Job -ScriptBlock {
    Set-Location "$using:root\Vitara.Worker"
    dotnet run
}

Write-Host "[vitara] Both services running. Press Ctrl+C to stop." -ForegroundColor Cyan
Write-Host "[vitara] Link Oura Ring: http://localhost:5100/api/oura/auth" -ForegroundColor Cyan

try {
    while ($true) {
        Receive-Job $apiJob    | ForEach-Object { Write-Host "[api]    $_" }
        Receive-Job $workerJob | ForEach-Object { Write-Host "[worker] $_" }
        Start-Sleep -Milliseconds 500
    }
} finally {
    Stop-Job $apiJob, $workerJob
    Remove-Job $apiJob, $workerJob
    Write-Host "[vitara] Stopped."
}
