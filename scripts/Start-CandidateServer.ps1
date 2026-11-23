[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 48125,
    [string]$Environment = 'C:\AI\build\Local-Dictation\server-env',
    [string]$ModelRoot = 'C:\AI\PalaNote-ASR\models',
    [string]$DataRoot = 'C:\AI\LocalDictation\server-data',
    [string]$ApplicationKeyDirectory,
    [string]$PiperProfile
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
# The installed client uses the ordinary port and data folder on auto-start.
# Explicit candidate arguments continue to select their own files.
if ($Port -eq 48125 -and -not $ApplicationKeyDirectory -and -not $PiperProfile -and
    [IO.Path]::GetFullPath($DataRoot) -eq 'C:\AI\LocalDictation\server-data') {
    $installedKeys = 'C:\AI\LocalDictation\api-keys'
    if (Test-Path -LiteralPath $installedKeys) {
        $ApplicationKeyDirectory = $installedKeys
        $PiperProfile = Join-Path $env:LOCALAPPDATA 'PalaBridge\speech-voice.json'
    }
}
if ($PiperProfile -and -not $ApplicationKeyDirectory) { throw 'Piper synthesis requires an application key directory.' }
$launchArgs = @('-B', ('"' + $script + '"'), '--port', $Port)
if ($ApplicationKeyDirectory) {
    $keyDirectory = [IO.Path]::GetFullPath($ApplicationKeyDirectory)
    $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if (-not (Test-Path -LiteralPath $keyDirectory)) {
        New-Item -ItemType Directory -Path $keyDirectory | Out-Null
        $acl = Get-Acl -LiteralPath $keyDirectory
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $owner, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        Set-Acl -LiteralPath $keyDirectory -AclObject $acl
        py -B (Join-Path $repoRoot 'server\provision_candidate_keys.py') $keyDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Candidate application key provisioning failed.' }
    }
    $registry = Join-Path $keyDirectory 'registry.json'
    foreach ($path in @($keyDirectory, $registry, (Join-Path $keyDirectory 'local-dictation.key'), (Join-Path $keyDirectory 'bridge.key'))) {
        $item = Get-Item -LiteralPath $path
        $acl = Get-Acl -LiteralPath $item.FullName
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference -ne $owner }).Count) {
            throw 'Candidate application key files must be private to the current Windows user.'
        }
    }
    $launchArgs += @('--application-key-registry', ('"' + $registry + '"'))
}
if ($PiperProfile) {
    $profile = (Get-Item -LiteralPath $PiperProfile).FullName
    $launchArgs += @('--piper-profile', ('"' + $profile + '"'))
}
$process = Start-Process py -ArgumentList $launchArgs -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory "api-$stamp.stdout.log") -RedirectStandardError (Join-Path $logDirectory "api-$stamp.stderr.log")
Write-Output "Candidate launcher PID: $($process.Id); loopback port: $Port"
Write-Output 'Warm-up can take one to two minutes. Check /health ready before recording. Production was not restarted.'
