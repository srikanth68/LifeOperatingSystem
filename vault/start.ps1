# Maaya Startup Script
# Loads credentials from .env, then starts the API and Worker side-by-side.
# Usage: .\start.ps1

$envFile = Join-Path $PSScriptRoot ".env"

if (-not (Test-Path $envFile)) {
    Write-Host "ERROR: .env file not found." -ForegroundColor Red
    Write-Host "Copy .env.template to .env and fill in your credentials." -ForegroundColor Yellow
    exit 1
}

# Parse .env
foreach ($line in Get-Content $envFile) {
    $line = $line.Trim()
    if ($line -eq "" -or $line.StartsWith("#")) { continue }
    $parts = $line -split "=", 2
    if ($parts.Length -eq 2) {
        $key   = $parts[0].Trim()
        $value = $parts[1].Trim()
        [System.Environment]::SetEnvironmentVariable($key, $value, "Process")
        Write-Host "  $key = $(if ($key -like '*SECRET*' -or $key -like '*KEY*') { '***' } else { $value })"
    }
}

Write-Host ""
Write-Host "Starting Maaya services..." -ForegroundColor Cyan
Write-Host "  Plaid environment : $env:PLAID_ENV" -ForegroundColor Green
Write-Host "  API               : http://localhost:5000" -ForegroundColor Green
Write-Host "  Frontend          : http://localhost:3000 (run npm run dev separately)" -ForegroundColor Green
Write-Host ""

# Start API
$apiDir = Join-Path $PSScriptRoot "Vault.API"
$apiJob = Start-Job -Name "VaultAPI" -ScriptBlock {
    param($dir, $clientId, $apiKey, $plaidEnv)
    $env:PLAID_CLIENT_ID = $clientId
    $env:PLAID_API_KEY   = $apiKey
    $env:PLAID_ENV       = $plaidEnv
    Set-Location $dir
    dotnet run
} -ArgumentList $apiDir, $env:PLAID_CLIENT_ID, $env:PLAID_API_KEY, $env:PLAID_ENV

# Start Worker
$workerDir = Join-Path $PSScriptRoot "Vault.Worker"
$workerJob = Start-Job -Name "VaultWorker" -ScriptBlock {
    param($dir, $clientId, $apiKey, $plaidEnv)
    $env:PLAID_CLIENT_ID = $clientId
    $env:PLAID_API_KEY   = $apiKey
    $env:PLAID_ENV       = $plaidEnv
    Set-Location $dir
    dotnet run
} -ArgumentList $workerDir, $env:PLAID_CLIENT_ID, $env:PLAID_API_KEY, $env:PLAID_ENV

Write-Host "Both services started. Streaming logs (Ctrl+C to stop):" -ForegroundColor Yellow
Write-Host "---------------------------------------------------------" -ForegroundColor DarkGray

try {
    while ($true) {
        $apiJob    | Receive-Job | ForEach-Object { Write-Host "[API]    $_" -ForegroundColor Cyan }
        $workerJob | Receive-Job | ForEach-Object { Write-Host "[WORKER] $_" -ForegroundColor Magenta }
        Start-Sleep -Milliseconds 300
    }
} finally {
    Stop-Job $apiJob, $workerJob
    Remove-Job $apiJob, $workerJob
    Write-Host "Services stopped." -ForegroundColor Red
}
