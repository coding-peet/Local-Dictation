$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$jdkDir = Join-Path $env:ProgramFiles 'Microsoft'
function Find-Java {
    Get-ChildItem $jdkDir -Filter 'jdk-17*' -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}
$jdk = Find-Java
if (!$jdk) {
    & winget install --exact --id Microsoft.OpenJDK.17 --source winget
    $jdk = Find-Java
    if (!$jdk) { throw 'Nainstaluj Microsoft OpenJDK 17 a skus znova.' }
}
$env:JAVA_HOME = $jdk.FullName
$sdk = $env:ANDROID_HOME
$manager = Join-Path $sdk 'cmdline-tools\latest\bin\sdkmanager.bat'
if (!(Test-Path $manager)) {
    $tempDir = Join-Path $env:TEMP ('pala-sdk-' + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    $zip = Join-Path $tempDir 'tools.zip'
    Invoke-WebRequest -UseBasicParsing 'https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip' -OutFile $zip
    Expand-Archive $zip $tempDir
    $parent = Join-Path $sdk 'cmdline-tools'
    New-Item -ItemType Directory -Force $parent | Out-Null
    Move-Item (Join-Path $tempDir 'cmdline-tools') (Join-Path $parent 'latest')
}
Write-Host 'Precitaj si licencie Android SDK. Ak suhlasis, potvrd y.'
& $manager "--sdk_root=$sdk" --licenses
if ($LASTEXITCODE -ne 0) { throw 'Licencie Android SDK sa nepodarilo nastavit.' }
& $manager "--sdk_root=$sdk" 'platforms;android-35' 'build-tools;35.0.0' 'platform-tools'
if ($LASTEXITCODE -ne 0) { throw 'Android SDK sa nepodarilo pripravit.' }
Set-Location (Join-Path $PSScriptRoot 'samsung')
& "$env:JAVA_HOME\bin\java.exe" -classpath gradle/wrapper/gradle-wrapper.jar org.gradle.wrapper.GradleWrapperMain assembleDebug
if ($LASTEXITCODE -ne 0) { throw 'Vytvorenie APK zlyhalo.' }
$dest = Join-Path $env:LOCALAPPDATA 'PalaBridge'
Copy-Item app/build/outputs/apk/debug/app-debug.apk (Join-Path $dest 'Pala-Nakup.apk') -Force
Start-Process explorer.exe -ArgumentList ('"' + $dest + '"')
