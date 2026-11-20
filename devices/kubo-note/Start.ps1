$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$ProgressPreference = 'SilentlyContinue'
function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User') + ';' + $env:APPDATA + '\npm'
}
function Install-PackageIfMissing($id) {
    if (!(Get-Command winget -ErrorAction SilentlyContinue)) { throw 'Nainstaluj App Installer z Microsoft Store a znova otvor START.cmd.' }
    & winget install --exact --id $id --source winget
    if ($LASTEXITCODE -ne 0) { throw "Instalacia $id sa nedokoncila. Dokonci ju a znova otvor START.cmd." }
    Refresh-Path
}
Refresh-Path
if (Get-Command py -ErrorAction SilentlyContinue) { & py -3.12 --version; $pythonOK = ($LASTEXITCODE -eq 0) }
if (!$pythonOK) { Install-PackageIfMissing 'Python.Python.3.12' }
if (!(Get-Command git -ErrorAction SilentlyContinue)) { Install-PackageIfMissing 'Git.Git' }
if (!(Get-Command codex.cmd -ErrorAction SilentlyContinue) -and !(Get-Command codex.exe -ErrorAction SilentlyContinue)) {
    if (!(Get-Command npm.cmd -ErrorAction SilentlyContinue)) { Install-PackageIfMissing 'OpenJS.NodeJS.LTS' }
    & npm.cmd install -g '@openai/codex'
    if ($LASTEXITCODE -ne 0) { throw 'Instalacia Codex zlyhala.' }
}
$codex = (Get-Command codex.exe,codex.cmd -ErrorAction SilentlyContinue | Select-Object -First 1).Source
$ErrorActionPreference = 'Continue'
$login = & $codex login status 2>&1 | Out-String
$ErrorActionPreference = 'Stop'
if ($login -notmatch 'ChatGPT') {
    Write-Host 'Prihlas sa vlastnym uctom ChatGPT.'
    & $codex login
    if ($LASTEXITCODE -ne 0) { throw 'Prihlasenie sa nedokoncilo.' }
}
Write-Host 'Na prepis nechaj bezat lokalne API aplikacie Local Dictation.'
$net = Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1
if ($net) { $env:PALA_INITIAL_IP = ($net.IPv4Address | Select-Object -First 1).IPAddress }
$env:ANDROID_HOME = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
& "$PSScriptRoot\mac-bridge\Pala-Windows.cmd"
exit $LASTEXITCODE
