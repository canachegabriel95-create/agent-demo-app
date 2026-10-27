# Shared helpers for start.ps1 and stop.ps1. Dot-source this file; do not run it directly.
# Works in Windows PowerShell 5.1 and PowerShell 7.

$script:RootDir = Split-Path -Parent $PSScriptRoot
$script:RunDir = Join-Path $script:RootDir '.run'
$script:ApiPort = 5080
$script:AdminPort = 5173
$script:AuthorPort = 5174
# $IsWindows does not exist in Windows PowerShell 5.1, which only runs on Windows.
$script:OnWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or [bool]$IsWindows

function Write-Info([string]$Message) { Write-Host $Message }
function Write-Ok([string]$Message) { Write-Host "  [ok] $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "  [!!] $Message" -ForegroundColor Yellow }

# Runs a native command with its output discarded and returns $true when it exits with code 0.
function Test-NativeSuccess([scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command *> $null
        return ($LASTEXITCODE -eq 0)
    }
    catch { return $false }
    finally { $ErrorActionPreference = $previous }
}

# Runs a native command, returns its combined output as text and sets $script:NativeExitCode.
function Invoke-NativeCaptured([scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Command 2>&1 | Out-String
        $script:NativeExitCode = $LASTEXITCODE
        return $output
    }
    finally { $ErrorActionPreference = $previous }
}

# True when something accepts connections on localhost:$Port.
function Test-PortInUse([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $pending = $client.BeginConnect('localhost', $Port, $null, $null)
        return ($pending.AsyncWaitHandle.WaitOne(500) -and $client.Connected)
    }
    catch { return $false }
    finally { $client.Close() }
}

# Returns the HTTP status code of a URL, or 0 when nothing answers.
function Get-HttpStatus([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return [int]$response.StatusCode
    }
    catch {
        $response = $_.Exception.Response
        if ($response) { return [int]$response.StatusCode }
        return 0
    }
}

function Get-PidFile([string]$Name) { Join-Path $script:RunDir "$Name.pid" }
function Get-LogFile([string]$Name) { Join-Path $script:RunDir "$Name.log" }

# Returns the process recorded for a component if it is still running (and is one of the tools we launch,
# which guards against a recycled PID), otherwise $null. Node renames its process (e.g. to 'npm run dev'),
# so only the start of the name is compared.
function Get-RunningComponent([string]$Name) {
    $file = Get-PidFile $Name
    if (-not (Test-Path $file)) { return $null }

    $recorded = (Get-Content $file -ErrorAction SilentlyContinue | Select-Object -First 1)
    $processId = 0
    if (-not [int]::TryParse([string]$recorded, [ref]$processId)) { return $null }

    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($process -and $process.ProcessName -match '^(dotnet|node|npm|cmd|ContentReview)') { return $process }
    return $null
}

# Loads KEY=VALUE lines from a .env file into the environment. Values that are already set win
# (same precedence as docker compose). The file is parsed, not executed.
function Import-DotEnv([string]$Path) {
    foreach ($line in Get-Content $Path) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            $key = $Matches[1]
            $value = $Matches[2].TrimEnd("`r")
            if ($value -match '^"(.*)"$' -or $value -match "^'(.*)'$") { $value = $Matches[1] }
            if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($key))) {
                [Environment]::SetEnvironmentVariable($key, $value)
            }
        }
    }
}

function Get-ChildProcessIds([int]$ParentId) {
    if ($script:OnWindows) {
        Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentId" -ErrorAction SilentlyContinue |
            ForEach-Object { $_.ProcessId }
    }
    else {
        & pgrep -P $ParentId 2>$null
    }
}

# Stops a process and everything it started (dotnet run and npm start child processes).
function Stop-ProcessTree([int]$ProcessId) {
    if ($script:OnWindows) {
        & taskkill /PID $ProcessId /T /F *> $null
        return
    }

    foreach ($child in @(Get-ChildProcessIds $ProcessId)) {
        Stop-ProcessTree ([int]$child)
    }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Stop-Component([string]$Name) {
    $process = Get-RunningComponent $Name
    if (-not $process) {
        Remove-Item (Get-PidFile $Name) -ErrorAction SilentlyContinue
        return $false
    }

    Stop-ProcessTree $process.Id
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Process -Id $process.Id -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) {
        Start-Sleep -Milliseconds 500
    }
    Remove-Item (Get-PidFile $Name) -ErrorAction SilentlyContinue
    return $true
}
