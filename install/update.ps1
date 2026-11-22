<#
.SYNOPSIS
  Replaces the installed windows-mcp.exe with a new build and restarts it.
.PARAMETER Exe
  Path to the new windows-mcp.exe.
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$taskName = 'windows-mcp'
$dest     = Join-Path $env:LOCALAPPDATA 'Programs\windows-mcp\windows-mcp.exe'
$dataDir  = Join-Path $env:LOCALAPPDATA 'windows-mcp'
$logFile  = Join-Path $dataDir 'windows-mcp.log'

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

function Get-McpProcess {
    return @(Get-Process -Name 'windows-mcp' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $dest })
}

function Test-Listening([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        if ($iar.AsyncWaitHandle.WaitOne(500) -and $client.Connected) {
            $client.EndConnect($iar)
            return $true
        }
        return $false
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

if (-not (Test-Path -LiteralPath $Exe)) { throw "Exe not found: $Exe" }
if (-not (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
    throw "The '$taskName' scheduled task does not exist. Run install.ps1 first."
}
$src = (Resolve-Path -LiteralPath $Exe).Path

Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

Write-Host 'Waiting for windows-mcp to exit...'
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
Write-Host "Waiting for windows-mcp to listen on 127.0.0.1:$port..."
$deadline = (Get-Date).AddSeconds(15)
$up = $false
while (-not $up -and (Get-Date) -lt $deadline) {
    $up = Test-Listening $port
    if (-not $up) { Start-Sleep -Milliseconds 500 }
}
if (-not $up) {
    Write-Warning "windows-mcp is not listening on port $port after 15 seconds."
    if (Test-Path -LiteralPath $logFile) {
        Write-Host "Last lines of ${logFile}:"
        Get-Content -LiteralPath $logFile -Tail 15
    }
    throw 'Update finished but the server did not come up. See the log lines above.'
}
Write-Host "windows-mcp is updated and listening on 127.0.0.1:$port."
