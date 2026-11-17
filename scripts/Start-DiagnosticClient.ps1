[CmdletBinding()]
param(
    [string]$AppDirectory = 'C:\Git\Local-Dictation\app',
    [string]$DataRoot = 'C:\AI\LocalDictation',
    [switch]$ExposeTestBanner
)
$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath($AppDirectory)
$dataDirectory = [IO.Path]::GetFullPath($DataRoot)
$executable = Join-Path $appRoot 'LocalDictation.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Build/publish LocalDictation.exe into AppDirectory first.'
}
if (Get-Process -Name LocalDictation -ErrorAction SilentlyContinue) {
    throw 'Local Dictation is already running. Exit it normally after resolving clipboard recovery; this script never terminates it.'
}
if (Get-Process -Name DeliveryProbe,BrowserDeliveryProbe,CompactFlowProbe,InlineReviewProbe,SettingsProbe -ErrorAction SilentlyContinue) {
    throw 'A diagnostic fixture is still running. Finish it and resolve its clipboard recovery before starting the client.'
}

# Direct process launch preserves these child-only variables. No user or machine
# environment, settings, API process, microphone or clipboard is changed here.
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.WorkingDirectory = $appRoot
$start.Environment['LOCAL_DICTATION_HOME'] = $dataDirectory
$start.Environment['LOCAL_DICTATION_DELIVERY_DIAGNOSTICS'] = '1'
# Explicit per-child test mode; do not inherit an earlier shell's opt-in.
$start.Environment['LOCAL_DICTATION_TEST_BANNER_WINDOW'] = if ($ExposeTestBanner) { '1' } else { '0' }
$launchUtc = [DateTimeOffset]::UtcNow
$client = [Diagnostics.Process]::Start($start)
$marker = Join-Path $dataDirectory 'logs\delivery-session.json'
$session = $null
for ($attempt = 0; $attempt -lt 100; $attempt++) {
    if ($client.HasExited) { throw 'The diagnostic client exited before its startup marker was verified.' }
    if (Test-Path -LiteralPath $marker -PathType Leaf) {
        try {
            $candidate = Get-Content -LiteralPath $marker -Raw -Encoding utf8 | ConvertFrom-Json
            if ($candidate.ProcessId -eq $client.Id -and ([DateTimeOffset]$candidate.Time) -ge $launchUtc) { $session = $candidate; break }
        } catch { } # A concurrent marker write may not yet be complete.
    }
    Start-Sleep -Milliseconds 100
}
if ($null -eq $session) {
    throw 'Startup diagnostic PID was not verified. The client was left running; do not force-stop a possible clipboard backup.'
}
[pscustomobject]@{
    ProcessId = $client.Id
    BuildVersion = $session.BuildVersion
    DiagnosticSessionVerified = $true
    TemporaryTaskbarButton = [bool]$ExposeTestBanner
    Marker = $marker
    HotkeyLaunch = 'Normal; --no-hotkey was not used'
}
