param(
    [Parameter(Mandatory)] [string] $OutputDir,
    [string] $ArduinoJsonInclude,
    [string] $VsEnvironment = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
)

$ErrorActionPreference = 'Stop'
$note = (Get-Item -LiteralPath (Join-Path $PSScriptRoot '..')).FullName
$vcvars = (Get-Item -LiteralPath $VsEnvironment).FullName
$stage3Output = (New-Item -ItemType Directory -Path $OutputDir -Force).FullName
$include = Join-Path $stage3Output 'stage3_archive_handlers.inc'

py -B (Join-Path $PSScriptRoot 'prepare_stage3_archive_host.py') $include --recording-screen (Join-Path $stage3Output 'recording_screen_host.inc')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$viewTests = ''
if ($ArduinoJsonInclude) {
    $jsonHeaders = (Get-Item -LiteralPath $ArduinoJsonInclude).FullName
    $viewTests = @"
cl /nologo /EHsc /std:c++17 /utf-8 /I"$jsonHeaders" "$note\tests\test_stage3_workspace_view.cpp" /I"$note\pala_note\src\app" /Fe:test_stage3_workspace_view.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
test_stage3_workspace_view.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
echo Stage 3 Workspace view cases passed.
"@
}
$captureFixture = Join-Path $stage3Output ("long-recording-" + [Guid]::NewGuid().ToString("N"))
[void][IO.Directory]::CreateDirectory($captureFixture)
$launcher = Join-Path $stage3Output 'stage3-host-tests.cmd'
@"
@echo off
set "PATH=%SystemRoot%\System32;%SystemRoot%;%SystemRoot%\System32\Wbem;%SystemRoot%\System32\WindowsPowerShell\v1.0"
call "$vcvars" >nul
if not "%errorlevel%"=="0" exit /b %errorlevel%
set "NOTE=$note"
cl /nologo /EHsc /std:c++20 /utf-8 /DPALA_SPEECH_STREAM_HOST_TEST /I"%NOTE%\tests" /I"%NOTE%\pala_note\src\app" "%NOTE%\tests\test_stage3_speech_stream.cpp" "%NOTE%\pala_note\src\app\speech_stream.cpp" /Fe:test_stage3_speech_stream.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
test_stage3_speech_stream.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
echo Stage 3 speech stream cases passed.
cl /nologo /EHsc /std:c++20 /utf-8 /I. /I"%NOTE%\pala_note\src\app" "%NOTE%\tests\test_stage3_archive_host.cpp" /Fe:test_stage3_archive_host.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
test_stage3_archive_host.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
echo Stage 3 archive cases passed.
cl /nologo /EHsc /O2 /std:c++17 /utf-8 /I. /DPALA_TEST_RECORDING_SCREEN "%NOTE%\tests\test_long_recording.cpp" /Fe:test_long_recording.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
test_long_recording.exe "$captureFixture"
if not "%errorlevel%"=="0" exit /b %errorlevel%
cl /nologo /EHsc /std:c++20 /utf-8 /I"%NOTE%\pala_note\src\app" "%NOTE%\tests\test_stage3_archive_month_job.cpp" /Fe:test_stage3_archive_month_job.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
test_stage3_archive_month_job.exe "%CD%\archive-month-fixture.job"
if not "%errorlevel%"=="0" exit /b %errorlevel%
echo Stage 3 archive deletion job cases passed.
$viewTests
cl /nologo /EHsc /std:c++17 /utf-8 "%NOTE%\tests\stage3_mcp_note_helper.cpp" /Fe:stage3_mcp_note_helper.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
cl /nologo /EHsc /std:c++17 /utf-8 "%NOTE%\tests\test_recording_owner.cpp" /Fe:test_recording_owner.exe
if not "%errorlevel%"=="0" exit /b %errorlevel%
"@ | Set-Content -LiteralPath $launcher -Encoding ascii

Push-Location -LiteralPath $stage3Output
try { & $env:ComSpec /d /c stage3-host-tests.cmd } finally { Pop-Location }
exit $LASTEXITCODE
