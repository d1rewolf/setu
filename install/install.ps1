<#
.SYNOPSIS
  Installs windows-mcp for the current user and starts it. No admin rights needed.
.PARAMETER Exe
  Path to the windows-mcp.exe to install.
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$taskName = 'windows-mcp'
$progDir  = Join-Path $env:LOCALAPPDATA 'Programs\windows-mcp'
$dest     = Join-Path $progDir 'windows-mcp.exe'
$dataDir  = Join-Path $env:LOCALAPPDATA 'windows-mcp'
$tokenFile = Join-Path $dataDir 'token'
$logFile   = Join-Path $dataDir 'windows-mcp.log'

function Get-McpPort {
    $port = 47631
    $cfg = Join-Path $dataDir 'config.json'
    if (Test-Path -LiteralPath $cfg) {
        try {
            $json = Get-Content -LiteralPath $cfg -Raw | ConvertFrom-Json
            if ($json -and ($json.PSObject.Properties.Name -contains 'port')) { $port = [int]$json.port }
        } catch {
            Write-Warning "Could not read port from $cfg; using $port."
        }
    }
    return $port
}

function Stop-McpProcess {
    $procs = @(Get-Process -Name 'windows-mcp' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $dest })
    foreach ($p in $procs) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    foreach ($p in $procs) { $null = $p.WaitForExit(5000) }
}

if (-not (Test-Path -LiteralPath $Exe)) { throw "Exe not found: $Exe" }
$src = (Resolve-Path -LiteralPath $Exe).Path

# Re-running install is safe: stop and remove any earlier task and process first.
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
Stop-McpProcess

New-Item -ItemType Directory -Force -Path $progDir | Out-Null
Copy-Item -LiteralPath $src -Destination $dest -Force
Write-Host "Copied exe to $dest"

$user = "$env:USERDOMAIN\$env:USERNAME"
$action    = New-ScheduledTaskAction -Execute $dest
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
# ExecutionTimeLimit zero is required: the default of 72 hours would kill the server.
$settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings | Out-Null
Write-Host "Registered scheduled task '$taskName' (runs at logon as $user)."

Start-ScheduledTask -TaskName $taskName

Write-Host 'Waiting for the token file...'
$deadline = (Get-Date).AddSeconds(15)
while (-not (Test-Path -LiteralPath $tokenFile) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}
if (-not (Test-Path -LiteralPath $tokenFile)) {
    Write-Warning "The token file did not appear within 15 seconds: $tokenFile"
    if (Test-Path -LiteralPath $logFile) {
        Write-Host "Last lines of ${logFile}:"
        Get-Content -LiteralPath $logFile -Tail 15
    }
    throw 'windows-mcp did not start. See the log lines above.'
}

$port = Get-McpPort
Write-Host ''
Write-Host 'windows-mcp is installed and running.'
Write-Host "Port: $port"
Write-Host 'Read the token from your Linux machine with:'
Write-Host '  ssh user@vm type %LOCALAPPDATA%\windows-mcp\token'
Write-Host 'Or in this window:'
Write-Host "  type $tokenFile"

$sshd = Get-Service -Name sshd -ErrorAction SilentlyContinue
if ((-not $sshd) -or ($sshd.Status -ne 'Running')) {
    Write-Host ''
    Write-Warning 'OpenSSH Server (sshd) is missing or not running, so you cannot reach windows-mcp from Linux yet.'
    Write-Host 'In an elevated (Administrator) PowerShell, run:'
    Write-Host '  Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0'
    Write-Host '  Set-Service sshd -StartupType Automatic'
    Write-Host '  Start-Service sshd'
    Write-Host '(Skip the first line if OpenSSH Server is already installed.) Nothing was changed for you.'
}
