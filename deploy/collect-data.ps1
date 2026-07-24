# Collects the current live SQLite DBs + Sutra file storage into deploy/data/
# in the exact layout the Docker bind mount expects (/data/<module>/...).
# Run on the machine that currently hosts Maaya, BEFORE transferring to Everest.
# Stop the running stack first so the .db files aren't mid-write.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path $PSScriptRoot 'data'

$map = @(
    # NOTE: Vault historically had TWO vault.db files (API + Worker). The API one
    # is what the dashboard shows — that's the one we take. Docker unifies them.
    @{ Module = 'vault';     Src = "$root\vault\Vault.API\vault.db"; Dest = 'vault.db' }
    @{ Module = 'vitara';    Src = "$root\vitara\vitara.db";         Dest = 'vitara.db' }
    @{ Module = 'aasthi';    Src = "$root\aasthi\aasthi.db";         Dest = 'aasthi.db' }
    @{ Module = 'san';       Src = "$root\san\san.db";               Dest = 'san.db' }
    @{ Module = 'sutra';     Src = "$root\sutra\sutra.db";           Dest = 'sutra.db' }
    @{ Module = 'northstar'; Src = "$root\northstar\northstar.db";   Dest = 'northstar.db' }
    @{ Module = 'karma';     Src = "$root\karma\karma.db";           Dest = 'karma.db' }
)

foreach ($m in $map) {
    $destDir = Join-Path $data $m.Module
    New-Item -ItemType Directory -Force (Join-Path $destDir 'run') | Out-Null
    if (Test-Path $m.Src) {
        Copy-Item $m.Src (Join-Path $destDir $m.Dest) -Force
        Write-Host "  OK  $($m.Module): $($m.Dest)"
    } else {
        Write-Host "  --  $($m.Module): no db yet (fresh start)" -ForegroundColor DarkGray
    }
}

# Modules without local dbs still need their dirs
foreach ($extra in 'nexus', 'mcp') {
    New-Item -ItemType Directory -Force (Join-Path $data "$extra\run") | Out-Null
}

# Sutra file storage (uploaded documents)
$sutraStorage = "$root\sutra\storage"
if (Test-Path $sutraStorage) {
    Copy-Item $sutraStorage (Join-Path $data 'sutra\storage') -Recurse -Force
    Write-Host "  OK  sutra: storage/"
}

# Aasthi legacy storage (pre-Sutra documents), if any remain
$aasthiStorage = "$root\aasthi\storage"
if (Test-Path $aasthiStorage) {
    Copy-Item $aasthiStorage (Join-Path $data 'aasthi\storage') -Recurse -Force
    Write-Host "  OK  aasthi: storage/"
}

Write-Host ""
Write-Host "Done. Transfer the whole maaya/ folder (including deploy/env and deploy/data) to Everest." -ForegroundColor Green
