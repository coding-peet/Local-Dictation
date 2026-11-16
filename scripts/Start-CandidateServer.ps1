[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 48125,
    [string]$Environment = 'C:\AI\build\Local-Dictation\server-env',
    [string]$ModelRoot = 'C:\AI\PalaNote-ASR\models',
    [string]$DataRoot = 'C:\AI\LocalDictation\server-data'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if ($Port -eq 8123) { throw 'Port 8123 is reserved for the existing deployed batch server.' }
if (-not (Test-Path -LiteralPath (Join-Path $Environment 'pyvenv.cfg'))) {
    throw 'Run Setup-CandidateServer.ps1 first.'
}
# A blocked TCP handshake does not mean the port is free. Inspect listeners
# without opening a connection, and fail closed if enumeration fails.
$listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if ($listeners | Where-Object { $_.Port -eq $Port }) {
    throw "Port $Port is already in use. No process was changed."
}
$logDirectory = Join-Path (Split-Path $DataRoot -Parent) 'logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$env:VIRTUAL_ENV = $Environment
$env:LOCAL_DICTATION_MODEL_ROOT = $ModelRoot
$env:LOCAL_DICTATION_DATA_ROOT = $DataRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$script = Join-Path $repoRoot 'server\api_main.py'
$process = Start-Process py -ArgumentList @('-B', ('"' + $script + '"'), '--port', $Port) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory "api-$stamp.stdout.log") -RedirectStandardError (Join-Path $logDirectory "api-$stamp.stderr.log")
Write-Output "Candidate launcher PID: $($process.Id); loopback port: $Port"
Write-Output 'Warm-up can take one to two minutes. Check /health ready before recording. Production was not restarted.'
