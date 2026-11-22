# Shared helpers, dot-sourced by install.ps1 and update.ps1. Not run on its own.
Set-StrictMode -Version Latest

$taskName  = 'windows-mcp'
$progDir   = Join-Path $env:LOCALAPPDATA 'Programs\windows-mcp'
$dest      = Join-Path $progDir 'windows-mcp.exe'
$dataDir   = Join-Path $env:LOCALAPPDATA 'windows-mcp'
$tokenFile = Join-Path $dataDir 'token'
$logFile   = Join-Path $dataDir 'windows-mcp.log'
# Printed when the server does not come up in time: the logon task never starts without a desktop session.
$sessionHint = "Are you logged on at the VM's desktop? The task only runs in your interactive session."

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

function Stop-McpProcess {
    $procs = @(Get-McpProcess)
    foreach ($p in $procs) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    foreach ($p in $procs) { $null = $p.WaitForExit(5000) }
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

# Succeeds only when OUR process is running and the port accepts connections.
# On timeout prints the log tail and throws.
function Wait-McpReady([int]$Port, [int]$Seconds = 15) {
    Write-Host "Waiting for windows-mcp to listen on 127.0.0.1:$Port..."
    $deadline = (Get-Date).AddSeconds($Seconds)
    $ready = $false
    while (-not $ready -and (Get-Date) -lt $deadline) {
        $ready = (@(Get-McpProcess).Count -gt 0) -and (Test-Listening $Port)
        if (-not $ready) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) {
        Write-Warning "windows-mcp is not running and listening on port $Port after $Seconds seconds."
        Write-Host $sessionHint
        if (Test-Path -LiteralPath $logFile) {
            Write-Host "Last lines of ${logFile}:"
            Get-Content -LiteralPath $logFile -Tail 15
        }
        throw 'windows-mcp did not come up. See the log lines above.'
    }
}
