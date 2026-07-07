# Maaya OS - start all backend services + frontend in one shot.
# Usage: .\maaya-start.ps1
# Press Ctrl+C to stop everything.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$services = @(
    @{ Name = 'vault-api';     Dir = "$root\vault\Vault.API";          Env = "$root\vault\.env";    Color = 'Cyan'      }
    @{ Name = 'vault-worker';  Dir = "$root\vault\Vault.Worker";       Env = "$root\vault\.env";    Color = 'DarkCyan'  }
    @{ Name = 'vitara-api';    Dir = "$root\vitara\Vitara.API";        Env = "$root\vitara\.env";   Color = 'Magenta'   }
    @{ Name = 'vitara-worker'; Dir = "$root\vitara\Vitara.Worker";     Env = "$root\vitara\.env";   Color = 'DarkMagenta' }
    @{ Name = 'aasthi-api';    Dir = "$root\aasthi\Aasthi.API";        Env = "$root\aasthi\.env";   Color = 'Yellow'    }
    @{ Name = 'san-api';       Dir = "$root\san\San.API";              Env = "$root\san\.env";       Color = 'DarkYellow'}
    @{ Name = 'san-worker';    Dir = "$root\san\San.Worker";           Env = "$root\san\.env";       Color = 'DarkGreen' }
    @{ Name = 'northstar-api'; Dir = "$root\northstar\NorthStar.API";  Env = "$root\northstar\.env"; Color = 'Blue'      }
    @{ Name = 'sutra-api';     Dir = "$root\sutra\Sutra.API";          Env = "$root\sutra\.env";     Color = 'DarkBlue'  }
    @{ Name = 'karma-api';     Dir = "$root\karma\Karma.API";          Env = "$root\karma\.env";     Color = 'Magenta'   }
    @{ Name = 'frontend';      Dir = "$root\vault\frontend";           Env = $null;                  Color = 'Green'     }
)

Write-Host ""
Write-Host " +======================================+" -ForegroundColor White
Write-Host " |          M A A Y A   O S             |" -ForegroundColor White
Write-Host " +======================================+" -ForegroundColor White
Write-Host ""
Write-Host "  Vault     http://localhost:5000  (API + Worker)" -ForegroundColor Cyan
Write-Host "  Vitara    http://localhost:5100  (API + Worker)" -ForegroundColor Magenta
Write-Host "  Aasthi    http://localhost:5200  (API)"          -ForegroundColor Yellow
Write-Host "  San       http://localhost:5300  (API + Worker)" -ForegroundColor DarkYellow
Write-Host "  NorthStar http://localhost:5500  (API)"          -ForegroundColor Blue
Write-Host "  Sutra     http://localhost:5400  (API)"          -ForegroundColor DarkBlue
Write-Host "  Karma     http://localhost:5600  (API)"          -ForegroundColor Magenta
Write-Host "  Frontend  http://localhost:5173"                 -ForegroundColor Green
Write-Host ""

$jobs = @()

foreach ($svc in $services) {
    $dir  = $svc.Dir
    $envFile = $svc.Env
    $name = $svc.Name

    if (-not (Test-Path $dir)) {
        Write-Host "  SKIP $name - $dir not found" -ForegroundColor DarkGray
        continue
    }

    if ($name -eq 'frontend') {
        $jobs += Start-Job -Name $name -ScriptBlock {
            param($d)
            Set-Location $d
            npm run dev 2>&1
        } -ArgumentList $dir
    } else {
        $jobs += Start-Job -Name $name -ScriptBlock {
            param($d, $e)
            if ($e -and (Test-Path $e)) {
                Get-Content $e | ForEach-Object {
                    if ($_ -match '^\s*#' -or $_ -match '^\s*$') { return }
                    $p = $_ -split '=', 2
                    if ($p.Count -eq 2) { [Environment]::SetEnvironmentVariable($p[0].Trim(), $p[1].Trim(), 'Process') }
                }
            }
            Set-Location $d
            dotnet run 2>&1
        } -ArgumentList $dir, $envFile
    }

    Write-Host "  OK $name" -ForegroundColor $svc.Color
}

Write-Host ""
Write-Host "All services launched. Streaming logs (Ctrl+C to stop all):" -ForegroundColor White
Write-Host "-------------------------------------------------------------" -ForegroundColor DarkGray

$colorMap = @{}
foreach ($svc in $services) { $colorMap[$svc.Name] = $svc.Color }

try {
    while ($true) {
        foreach ($job in $jobs) {
            $color = $colorMap[$job.Name]
            if (-not $color) { $color = 'Gray' }
            Receive-Job $job | ForEach-Object {
                $tag = ($job.Name).PadRight(14)
                Write-Host "[$tag] $_" -ForegroundColor $color
            }
        }
        Start-Sleep -Milliseconds 300
    }
} finally {
    Write-Host ""
    Write-Host "Stopping all services..." -ForegroundColor Red
    $jobs | Stop-Job -ErrorAction SilentlyContinue
    $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
    Write-Host "All services stopped." -ForegroundColor Red
}
