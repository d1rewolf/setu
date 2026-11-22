<#
.SYNOPSIS
  Replaces the installed setu.exe with a new build and restarts it.
.PARAMETER Exe
  Path to the new setu.exe.
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $Exe)) { throw "Exe not found: $Exe" }
if (-not (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
    throw "The '$taskName' scheduled task does not exist. Run install.ps1 first."
}
$src = (Resolve-Path -LiteralPath $Exe).Path

Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

Write-Host 'Waiting for setu to exit...'
$deadline = (Get-Date).AddSeconds(10)
while (@(Get-McpProcess).Count -gt 0 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
$left = @(Get-McpProcess)
if ($left.Count -gt 0) {
    Write-Warning 'It is still running after 10 seconds; stopping it.'
    foreach ($p in $left) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    foreach ($p in $left) { $null = $p.WaitForExit(5000) }
}

Copy-Item -LiteralPath $src -Destination $dest -Force
Write-Host "Copied exe to $dest"

Start-ScheduledTask -TaskName $taskName

$port = Get-McpPort
Wait-McpReady $port
Write-Host "setu is updated and listening on 127.0.0.1:$port."
