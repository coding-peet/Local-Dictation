[CmdletBinding()]
param(
    [string]$DotnetPath = 'C:\AI\tools\dotnet-10.0.403\dotnet.exe',
    [string]$BuildRoot = 'C:\AI\build\Local-Dictation',
    [string]$AppDirectory = 'C:\AI\LocalDictation\app'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $BuildRoot 'dotnet-home'
if (-not (Test-Path -LiteralPath $DotnetPath)) {
    throw "A .NET 10 SDK is required. Set -DotnetPath to an installed SDK host."
}
& $DotnetPath test (Join-Path $repoRoot 'tests\LocalDictation.Core.Tests\LocalDictation.Core.Tests.csproj') --nologo "-p:ArtifactsPath=$BuildRoot\dotnet"
if ($LASTEXITCODE -ne 0) { throw 'Native core tests failed; publish was not run.' }
& $DotnetPath publish (Join-Path $repoRoot 'src\LocalDictation.App\LocalDictation.App.csproj') -c Release --no-self-contained -o $AppDirectory --nologo "-p:ArtifactsPath=$BuildRoot\dotnet"
if ($LASTEXITCODE -ne 0) { throw 'Native publish failed.' }
Write-Output "Published: $AppDirectory\LocalDictation.exe"
Write-Output 'This command did not launch the application or change startup/TypeWhisper settings.'
