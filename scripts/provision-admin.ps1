# Provisions the initial administrator by running db/scripts/provision-admin.sql.
# Works in Windows PowerShell 5.1 and PowerShell 7.
#
# Usage: scripts/provision-admin.ps1 [-Email EMAIL] [-Name NAME] [-Connection CONNECTION]
#
#   ADMIN_EMAIL / ADMIN_NAME   used when the parameters are not given (also read from .env)
#   ADMIN_PASSWORD_HASH        optional; skips the password prompt when you already have a hash
#   -Connection / PSQL_CONNECTION
#                              run the local psql client against this connection string or URI instead of
#                              'docker compose exec postgres psql' (e.g. an externally managed database)
#
# The password is typed into the masked prompt of 'hash-password'; it is never stored, logged or passed on a
# command line. Running the script again is safe: an existing user is never changed.
[CmdletBinding()]
param(
    [string]$Email,
    [string]$Name,
    [string]$Connection
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')
Set-Location $script:RootDir

$sqlFile = 'db/scripts/provision-admin.sql'
$toolsProject = 'backend/tools/ContentReview.Tools'

if (Test-Path '.env') { Import-DotEnv '.env' }

if (-not $Email) { $Email = $env:ADMIN_EMAIL }
if (-not $Name) { $Name = $env:ADMIN_NAME }
if (-not $Connection) { $Connection = $env:PSQL_CONNECTION }

if (-not $Email -or -not $Name) {
    Write-Warn 'ADMIN_EMAIL and ADMIN_NAME are required: set them in .env (see .env.example) or pass -Email and -Name.'
    exit 2
}

$hash = $env:ADMIN_PASSWORD_HASH
if (-not $hash) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Warn '.NET SDK 10 is required to generate the password hash.'
        exit 1
    }
    Write-Info 'Choose the administrator password (input is hidden).'
    # stdout carries only the hash; the prompts and errors of hash-password go to the terminal via stderr.
    $output = @(& dotnet run --no-launch-profile --project $toolsProject -- hash-password)
    if ($LASTEXITCODE -ne 0 -or $output.Count -eq 0) {
        Write-Warn 'Could not generate the password hash.'
        exit 1
    }
    $hash = [string]$output[-1]
}

$psqlArgs = @(
    '-X', '-q',
    '-v', 'ON_ERROR_STOP=1',
    '-v', "admin_email=$Email",
    '-v', "admin_name=$Name",
    '-v', "admin_password_hash=$hash"
)

if ($Connection) {
    if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
        Write-Warn 'psql is not installed; omit -Connection to use the Docker container.'
        exit 1
    }
    & psql $Connection @psqlArgs -f $sqlFile
}
else {
    if (-not (Test-NativeSuccess { docker info })) {
        Write-Warn 'Docker is not running. Start it (scripts/start.ps1) or use -Connection.'
        exit 1
    }
    $user = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { 'content_review' }
    $database = if ($env:POSTGRES_DB) { $env:POSTGRES_DB } else { 'content_review' }
    Get-Content -Raw $sqlFile | docker compose exec -T postgres psql -U $user -d $database @psqlArgs -f -
}

if ($LASTEXITCODE -ne 0) {
    Write-Warn 'Provisioning failed; nothing was written.'
    exit $LASTEXITCODE
}
Write-Ok "Done. Sign in to the Admin Portal (http://localhost:5173) as $Email."
