[CmdletBinding()]
param(
    [string]$Environment = 'C:\AI\build\Local-Dictation\server-env',
    [string]$SharedEnvironment = 'C:\AI\PalaNote-ASR\.venv'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$savedVirtualEnvironment = $env:VIRTUAL_ENV
try {
    if (-not (Test-Path -LiteralPath (Join-Path $SharedEnvironment 'pyvenv.cfg'))) {
        throw 'The tested shared ASR environment is missing. See the development guide for a fresh environment.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $Environment 'pyvenv.cfg'))) {
        $env:VIRTUAL_ENV = $SharedEnvironment
        py -m venv $Environment
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the candidate environment.' }
    }
    $packages = Join-Path $SharedEnvironment 'Lib\site-packages'
    $link = Join-Path $Environment 'Lib\site-packages\local-dictation-shared-runtime.pth'
    [System.IO.File]::WriteAllText($link, $packages + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    $env:VIRTUAL_ENV = $Environment
    py -B -m pip install -r (Join-Path $repoRoot 'server\requirements-api.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Candidate dependency installation failed.' }
    Write-Output 'Candidate environment prepared. Existing ASR packages were reused read-only.'
} finally {
    $env:VIRTUAL_ENV = $savedVirtualEnvironment
}
