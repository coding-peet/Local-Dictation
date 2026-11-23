[CmdletBinding()]
param(
    [string]$CandidateRoot = 'C:\Git\Local-Dictation\build\local-speech-read-aloud',
    [string]$ApiKeyDirectory = 'C:\Git\Local-Dictation\build\local-speech-candidate-20261122\app-keys',
    [ValidateRange(1, 65535)] [int]$Port = 48126
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $CandidateRoot).Path
$exe = (Get-Item -LiteralPath (Join-Path $root 'client\LocalDictation.exe')).FullName
$key = (Get-Item -LiteralPath (Join-Path $ApiKeyDirectory 'local-dictation.key')).FullName
$endpoint = "http://127.0.0.1:$Port"
$health = Invoke-RestMethod -Uri "$endpoint/health" -NoProxy -TimeoutSec 5
if (-not $health.ready -or -not $health.capabilities.speech -or -not $health.capabilities.application_keys) {
    throw 'Start the keyed local speech API before launching this candidate.'
}
if (Get-Process -Name LocalDictation -ErrorAction SilentlyContinue) {
    throw 'Exit the running Local Dictation through its tray menu to open the isolated candidate.'
}

$profile = Join-Path $root 'client-profile'
$data = Join-Path $profile 'data'
$file = Join-Path $data 'settings.json'
if (-not (Test-Path -LiteralPath $file)) {
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    @{ Endpoint = $endpoint; PalaIntegrationEnabled = $false; PalaBridgeAutoStart = $false } |
        ConvertTo-Json | Set-Content -LiteralPath $file -Encoding UTF8
} else {
    $settings = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($settings.Endpoint -ne $endpoint) { throw 'The candidate profile uses a different local API address.' }
}

$env:LOCAL_DICTATION_HOME = $profile
$env:LOCAL_DICTATION_API_KEY_ENDPOINT = $endpoint
$env:LOCAL_DICTATION_API_KEY_FILE = $key
Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ArgumentList '--startup' -WindowStyle Hidden
Write-Output 'Read-aloud candidate started in the system tray with its isolated profile.'
