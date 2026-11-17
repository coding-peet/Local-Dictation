[CmdletBinding()]
param(
    [string]$DotnetPath = 'C:\AI\tools\dotnet-10.0.403\dotnet.exe',
    [string]$BuildRoot = 'C:\AI\build\Local-Dictation',
    [string]$AppDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

function Get-NormalizedDirectory([string]$Path) {
    $absolute = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    return [IO.Path]::GetFullPath($absolute).TrimEnd('\', '/').Replace('/', '\')
}

function Test-DirectoryOverlap([string]$Left, [string]$Right) {
    return $Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase) -or
        $Left.StartsWith($Right + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $Right.StartsWith($Left + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparseDirectory([string]$Path) {
    # Reject junction/symlink aliases, including ancestors of a new output.
    $current = [IO.DirectoryInfo]::new($Path)
    while ($null -ne $current) {
        $item = Get-Item -LiteralPath $current.FullName -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Build output must not use a junction or symbolic link.'
        }
        $current = $current.Parent
    }
}

$BuildRoot = Get-NormalizedDirectory $BuildRoot
if ([string]::IsNullOrWhiteSpace($AppDirectory)) {
    $AppDirectory = Join-Path $BuildRoot 'client-candidate'
}
$AppDirectory = Get-NormalizedDirectory $AppDirectory
$protectedDirectories = @(
    @{ Path = 'C:\Git\Local-Dictation\app'; Reason = 'the installed app' },
    @{ Path = $repoRoot; Reason = 'the source checkout' },
    @{ Path = 'C:\AI\LocalDictation'; Reason = 'private data and runtime files' },
    @{ Path = 'C:\AI\PalaNote-ASR'; Reason = 'the existing backend and models' }
)
foreach ($directory in @($BuildRoot, $AppDirectory)) {
    foreach ($protected in $protectedDirectories) {
        if (Test-DirectoryOverlap $directory (Get-NormalizedDirectory $protected.Path)) {
            throw "Build output overlaps $($protected.Reason). Use a separate candidate directory; deployment is a separate step."
        }
    }
    Assert-NoReparseDirectory $directory
}

function Assert-NoRunningClientOverlap {
    try {
        # Read executable paths only for Local Dictation; never stop a process.
        $clients = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -eq 'LocalDictation' })
        foreach ($client in $clients) {
            $executable = $client.Path
            if ([string]::IsNullOrWhiteSpace($executable)) { throw 'Unknown client path' }
            $runningDirectory = Get-NormalizedDirectory (Split-Path $executable -Parent)
            foreach ($directory in @($BuildRoot, $AppDirectory)) {
                if (Test-DirectoryOverlap $directory $runningDirectory) {
                    throw 'Build output overlaps a running Local Dictation client. Exit it normally through tray -> Exit, or use a separate candidate directory.'
                }
            }
        }
    } catch {
        throw "Cannot safely publish while checking running clients: $($_.Exception.Message)"
    }
}

Assert-NoRunningClientOverlap
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
    throw 'A .NET 10 SDK is required. Set -DotnetPath to an installed SDK host.'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $BuildRoot 'dotnet-home'
& $DotnetPath test (Join-Path $repoRoot 'tests\LocalDictation.Core.Tests\LocalDictation.Core.Tests.csproj') --nologo --artifacts-path (Join-Path $BuildRoot 'dotnet')
if ($LASTEXITCODE -ne 0) { throw 'Native core tests failed; publish was not run.' }
# A client may have been started while tests ran. Recheck before writing payload.
Assert-NoRunningClientOverlap
& $DotnetPath publish (Join-Path $repoRoot 'src\LocalDictation.App\LocalDictation.App.csproj') -c Release --no-self-contained -o $AppDirectory --nologo --artifacts-path (Join-Path $BuildRoot 'dotnet')
if ($LASTEXITCODE -ne 0) { throw 'Native publish failed.' }
Write-Output "Candidate published: $AppDirectory\LocalDictation.exe"
Write-Output 'Deployment is separate. This command did not launch the client, restart APIs, or change startup/TypeWhisper settings.'
