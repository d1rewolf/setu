<#
.SYNOPSIS
  Installs setu for the current user and starts it. No admin rights needed.
.PARAMETER Exe
  Path to the setu.exe to install.
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

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

# The account name from the logon token. Over SSH, $env:USERDOMAIN can be WORKGROUP,
# which Task Scheduler cannot map to an account.
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
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
    Write-Host $sessionHint
    if (Test-Path -LiteralPath $logFile) {
        Write-Host "Last lines of ${logFile}:"
        Get-Content -LiteralPath $logFile -Tail 15
    }
    throw 'setu did not start. See the log lines above.'
}

$port = Get-McpPort
Wait-McpReady $port
Write-Host ''
Write-Host 'setu is installed and running.'
Write-Host "Port: $port"
Write-Host 'Read the token from your Linux machine with:'
Write-Host "  ssh user@vm 'type %LOCALAPPDATA%\setu\token'"
Write-Host 'Or in this window:'
Write-Host "  type $tokenFile"

$sshd = Get-Service -Name sshd -ErrorAction SilentlyContinue
if ((-not $sshd) -or ($sshd.Status -ne 'Running')) {
    Write-Host ''
    Write-Warning 'OpenSSH Server (sshd) is missing or not running, so you cannot reach setu from Linux yet.'
    Write-Host 'In an elevated (Administrator) PowerShell, run:'
    Write-Host '  Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0'
    Write-Host '  Set-Service sshd -StartupType Automatic'
    Write-Host '  Start-Service sshd'
    Write-Host '(Skip the first line if OpenSSH Server is already installed.) Nothing was changed for you.'
}
