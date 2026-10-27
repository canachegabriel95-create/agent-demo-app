# Stops what scripts\start.ps1 started: the API and both portals, then the Docker containers.
# The database data is kept (docker compose stop, not down). Safe to run more than once.
# Usage: .\scripts\stop.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')
Set-Location $script:RootDir

$stoppedAny = $false
foreach ($component in @(@('admin', 'Admin Portal'), @('author', 'Author Portal'), @('api', 'API'))) {
    if (Stop-Component $component[0]) {
        Write-Ok "Stopped the $($component[1])"
        $stoppedAny = $true
    }
}
if (-not $stoppedAny) { Write-Info 'No API or portal processes were running.' }

if ((Get-Command docker -ErrorAction SilentlyContinue) -and (Test-NativeSuccess { docker info })) {
    if (Test-NativeSuccess { docker compose stop }) {
        Write-Ok "Stopped PostgreSQL and pgAdmin (data is kept; 'docker compose down -v' deletes it)"
    }
    else {
        Write-Warn 'Could not stop the containers. Check: docker compose ps'
    }
}
else {
    Write-Info 'Docker is not running, so there are no containers to stop.'
}

$leftover = $false
foreach ($entry in @(@($script:ApiPort, 'API'), @($script:AdminPort, 'Admin Portal'), @($script:AuthorPort, 'Author Portal'))) {
    if (Test-PortInUse $entry[0]) {
        Write-Warn "Port $($entry[0]) ($($entry[1])) is still in use by a process that was not started by scripts\start.ps1."
        $leftover = $true
    }
}

if (-not $leftover) {
    Write-Ok "Ports $($script:ApiPort), $($script:AdminPort) and $($script:AuthorPort) are free"
    exit 0
}
exit 1
