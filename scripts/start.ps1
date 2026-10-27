# Starts the whole local environment: PostgreSQL + pgAdmin (Docker), the API and both portals.
# Usage: .\scripts\start.ps1      Stop it again with .\scripts\stop.ps1
# Works in Windows PowerShell 5.1 and PowerShell 7.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')
Set-Location $script:RootDir

$RequiredDotnetMajor = 10
$RequiredNodeMajor = 22
$PostgresTimeoutSeconds = 90
$ApiTimeoutSeconds = 120
$PortalTimeoutSeconds = 90

function Get-MajorVersion([string]$Version) {
    $major = 0
    [void][int]::TryParse(($Version.Trim().TrimStart('v').Split('.')[0]), [ref]$major)
    return $major
}

function Get-Problems {
    $problems = New-Object System.Collections.Generic.List[string]

    # Docker
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        $problems.Add('Docker is not installed. Install Docker Desktop or Docker Engine: https://docs.docker.com/get-docker/')
    }
    elseif (-not (Test-NativeSuccess { docker info })) {
        $problems.Add('Docker is installed but not running. Start Docker Desktop (or the Docker daemon) and try again.')
    }
    elseif (-not (Test-NativeSuccess { docker compose version })) {
        $problems.Add("Docker Compose v2 is not available ('docker compose'). Update Docker or install the Compose plugin.")
    }

    # .NET SDK
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $problems.Add(".NET SDK $RequiredDotnetMajor is not installed: https://dotnet.microsoft.com/download")
    }
    else {
        $version = (Invoke-NativeCaptured { dotnet --version }).Trim()
        if ((Get-MajorVersion $version) -lt $RequiredDotnetMajor) {
            $problems.Add(".NET SDK $RequiredDotnetMajor or newer is required (found $version): https://dotnet.microsoft.com/download")
        }
    }

    # Node.js
    if (-not (Get-Command node -ErrorAction SilentlyContinue) -or -not (Get-Command npm -ErrorAction SilentlyContinue)) {
        $problems.Add("Node.js $RequiredNodeMajor LTS (with npm) is not installed: https://nodejs.org/")
    }
    else {
        $version = (Invoke-NativeCaptured { node --version }).Trim().TrimStart('v')
        if ((Get-MajorVersion $version) -lt $RequiredNodeMajor) {
            $problems.Add("Node.js $RequiredNodeMajor LTS or newer is required (found $version): https://nodejs.org/")
        }
    }

    # Configuration
    $envFile = Join-Path $script:RootDir '.env'
    if (-not (Test-Path $envFile)) {
        $problems.Add('Missing .env file. Create it with: Copy-Item .env.example .env  (then replace the placeholder passwords).')
    }
    else {
        Import-DotEnv $envFile
        foreach ($key in @('POSTGRES_USER', 'POSTGRES_PASSWORD', 'PGADMIN_DEFAULT_EMAIL', 'PGADMIN_DEFAULT_PASSWORD', 'ConnectionStrings__Default')) {
            if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($key))) {
                $problems.Add("$key is not set in .env (see .env.example).")
            }
        }
    }

    # Portal dependencies
    foreach ($app in @('admin-portal', 'author-portal')) {
        if (-not (Test-Path (Join-Path $script:RootDir "frontend\$app\node_modules"))) {
            $problems.Add("Dependencies of $app are not installed. Run: cd frontend\$app; npm ci")
        }
    }

    # Ports used by the apps (skip the ones our own earlier run is still holding)
    foreach ($entry in @(@('api', $script:ApiPort, 'API'), @('admin', $script:AdminPort, 'Admin Portal'), @('author', $script:AuthorPort, 'Author Portal'))) {
        if (-not (Get-RunningComponent $entry[0]) -and (Test-PortInUse $entry[1])) {
            $problems.Add("Port $($entry[1]) ($($entry[2])) is already in use. Stop the program using it, or run scripts\stop.ps1 if it is a previous run.")
        }
    }

    return , $problems
}

function Wait-ForPostgres {
    $container = (Invoke-NativeCaptured { docker compose ps -q postgres }).Trim()
    $deadline = (Get-Date).AddSeconds($PostgresTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $status = (Invoke-NativeCaptured { docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $container }).Trim()
        if ($status -eq 'healthy') { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

# Starts a process in the background with its output in .run\<name>.log and records its PID.
function Start-Background([string]$Name, [string]$WorkingDirectory, [string]$FilePath, [string[]]$Arguments) {
    $log = Get-LogFile $Name
    Set-Content -Path $log -Value $null
    $parameters = @{
        FilePath               = $FilePath
        ArgumentList           = $Arguments
        WorkingDirectory       = $WorkingDirectory
        RedirectStandardOutput = $log
        RedirectStandardError  = "$log.err"
        PassThru               = $true
    }
    # -WindowStyle only exists on Windows; it keeps the background processes from opening console windows.
    if ($script:OnWindows) { $parameters.WindowStyle = 'Hidden' }
    $process = Start-Process @parameters
    Set-Content -Path (Get-PidFile $Name) -Value $process.Id
}

# Waits until the URL returns 200 while the component stays alive. Returns 'ok', 'timeout' or 'died'.
function Wait-ForHttp([string]$Name, [string]$Url, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $script:LastStatus = 0
    while ((Get-Date) -lt $deadline) {
        $script:LastStatus = Get-HttpStatus $Url
        if ($script:LastStatus -eq 200) { return 'ok' }
        if (-not (Get-RunningComponent $Name)) {
            Write-Warn "$Name stopped unexpectedly. Last log lines ($(Get-LogFile $Name)):"
            Get-Content (Get-LogFile $Name), ("$(Get-LogFile $Name).err") -Tail 20 -ErrorAction SilentlyContinue | Write-Host
            return 'died'
        }
        Start-Sleep -Seconds 2
    }
    return 'timeout'
}

# 1. Prerequisites ----------------------------------------------------------------------------------------------
Write-Info 'Checking prerequisites...'
$problems = Get-Problems
if ($problems.Count -gt 0) {
    Write-Warn 'Cannot start. Fix the following and run scripts\start.ps1 again:'
    foreach ($problem in $problems) { Write-Host "   - $problem" }
    exit 1
}
Write-Ok 'Docker, .NET SDK, Node.js, configuration, dependencies and ports are ready'

New-Item -ItemType Directory -Force -Path $script:RunDir | Out-Null

# 2. PostgreSQL and pgAdmin -----------------------------------------------------------------------------------
Write-Info 'Starting PostgreSQL and pgAdmin...'
$composeOutput = Invoke-NativeCaptured { docker compose up -d }
if ($script:NativeExitCode -ne 0) {
    Write-Warn 'docker compose up failed:'
    Write-Host $composeOutput
    Write-Warn 'A port conflict on 5432 or 5050? Change POSTGRES_PORT / PGADMIN_PORT in .env.'
    exit 1
}
if (-not (Wait-ForPostgres)) {
    Write-Warn "PostgreSQL did not become healthy within ${PostgresTimeoutSeconds}s. Check: docker compose logs postgres"
    exit 1
}
Write-Ok 'PostgreSQL is healthy'

# 3. API (applies migrations at startup) ------------------------------------------------------------------------
Write-Info 'Building the API...'
$buildOutput = Invoke-NativeCaptured { dotnet build backend/src/ContentReview.Api -nologo -v q }
if ($script:NativeExitCode -ne 0) {
    Write-Host $buildOutput
    Write-Warn 'The API did not build. Fix the errors above and run scripts\start.ps1 again.'
    exit 1
}
Write-Ok 'API built'

if (Get-RunningComponent 'api') {
    Write-Ok 'API is already running'
}
else {
    Write-Info 'Starting the API (migrations are applied at startup)...'
    if ([string]::IsNullOrEmpty($env:ASPNETCORE_ENVIRONMENT)) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
    if ([string]::IsNullOrEmpty($env:Database__MigrateOnStartup)) { $env:Database__MigrateOnStartup = 'true' }
    Start-Background 'api' $script:RootDir 'dotnet' @('run', '--project', 'backend/src/ContentReview.Api', '--no-build')
}

$result = Wait-ForHttp 'api' "http://localhost:$($script:ApiPort)/health" $ApiTimeoutSeconds
if ($result -eq 'ok') {
    Write-Ok "API is healthy (http://localhost:$($script:ApiPort)/health)"
}
else {
    if ($result -eq 'timeout') {
        Write-Warn "The API did not report Healthy within ${ApiTimeoutSeconds}s (last HTTP status: $($script:LastStatus))."
        Write-Warn 'A 503 usually means it cannot reach the database: check ConnectionStrings__Default in .env.'
        Write-Warn 'Log: .run\api.log'
    }
    exit 1
}

# 4. Portals -----------------------------------------------------------------------------------------------------
$npm = Get-Command npm.cmd -ErrorAction SilentlyContinue
if (-not $npm) { $npm = Get-Command npm }

foreach ($portal in @(@('admin', 'admin-portal', $script:AdminPort, 'Admin Portal'), @('author', 'author-portal', $script:AuthorPort, 'Author Portal'))) {
    $name = $portal[0]; $folder = $portal[1]; $port = $portal[2]; $label = $portal[3]

    if (Get-RunningComponent $name) {
        Write-Ok "$label is already running"
    }
    else {
        Write-Info "Starting the $label..."
        Start-Background $name (Join-Path $script:RootDir "frontend\$folder") $npm.Source @('run', 'dev')
    }

    $result = Wait-ForHttp $name "http://localhost:$port/" $PortalTimeoutSeconds
    if ($result -eq 'ok') {
        Write-Ok "$label is up (http://localhost:$port)"
    }
    else {
        if ($result -eq 'timeout') { Write-Warn "$label did not answer within ${PortalTimeoutSeconds}s. Log: .run\$name.log" }
        exit 1
    }
}

# 5. pgAdmin (slow to start; not fatal) ---------------------------------------------------------------------------
$pgAdminPort = $env:PGADMIN_PORT
if ([string]::IsNullOrEmpty($pgAdminPort)) { $pgAdminPort = '5050' }
$postgresPort = $env:POSTGRES_PORT
if ([string]::IsNullOrEmpty($postgresPort)) { $postgresPort = '5432' }
$pgAdminUrl = "http://localhost:$pgAdminPort"

$deadline = (Get-Date).AddSeconds(60)
while ((Get-HttpStatus "$pgAdminUrl/misc/ping") -ne 200 -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
if ((Get-HttpStatus "$pgAdminUrl/misc/ping") -eq 200) {
    Write-Ok 'pgAdmin is up'
}
else {
    Write-Warn 'pgAdmin is still starting; give it a little longer (docker compose logs pgadmin).'
}

Write-Host @"

Everything is running:

  Admin Portal   http://localhost:$($script:AdminPort)
  Author Portal  http://localhost:$($script:AuthorPort)
  API            http://localhost:$($script:ApiPort)
  Health         http://localhost:$($script:ApiPort)/health
  Swagger UI     http://localhost:$($script:ApiPort)/swagger   (added by a later story)
  PostgreSQL     localhost:$postgresPort
  pgAdmin        $pgAdminUrl

Logs: .run\*.log      Stop everything with: .\scripts\stop.ps1
"@
