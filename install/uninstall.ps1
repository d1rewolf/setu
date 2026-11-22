<#
.SYNOPSIS
  Stops and removes setu. Keeps the token, config and log unless -RemoveData is given.
#>
param(
    [switch]$RemoveData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$taskName = 'setu'
$progDir  = Join-Path $env:LOCALAPPDATA 'Programs\setu'
$dest     = Join-Path $progDir 'setu.exe'
$dataDir  = Join-Path $env:LOCALAPPDATA 'setu'

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed scheduled task '$taskName'."
} else {
    Write-Host "No scheduled task '$taskName' found."
}

$procs = @(Get-Process -Name 'setu' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $dest })
foreach ($p in $procs) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
foreach ($p in $procs) { $null = $p.WaitForExit(5000) }

if (Test-Path -LiteralPath $progDir) {
    Remove-Item -LiteralPath $progDir -Recurse -Force
    Write-Host "Removed $progDir"
}

if ($RemoveData) {
    if (Test-Path -LiteralPath $dataDir) {
        Remove-Item -LiteralPath $dataDir -Recurse -Force
        Write-Host "Removed $dataDir"
    }
} else {
    Write-Host "Kept $dataDir (token, config, log). Pass -RemoveData to delete it too."
}
