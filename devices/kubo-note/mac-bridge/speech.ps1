param([string]$OutputFile)
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$text = [Console]::In.ReadToEnd()
$bridgeProfile = Join-Path $env:LOCALAPPDATA 'PalaBridge'
$voiceSettings = Join-Path $bridgeProfile 'speech-voice.json'
if (Test-Path -LiteralPath $voiceSettings -PathType Leaf) {
    $settings = Get-Content -LiteralPath $voiceSettings -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $settings.python -or -not $settings.model -or
        -not [IO.Path]::IsPathRooted($settings.python) -or -not [IO.Path]::IsPathRooted($settings.model) -or
        -not (Test-Path -LiteralPath $settings.python -PathType Leaf) -or
        -not (Test-Path -LiteralPath $settings.model -PathType Leaf)) {
        throw 'The local Piper runtime or configured voice model is missing.'
    }
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    $text | & $settings.python -I -B (Join-Path $PSScriptRoot 'speech_piper.py') $settings.model $OutputFile
    if ($LASTEXITCODE -ne 0) { throw 'The local Piper voice failed to synthesize speech.' }
    return
}

Add-Type -AssemblyName System.Speech
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $slovak = $voice.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'sk-SK' } | Select-Object -First 1
    if ($slovak) { $voice.SelectVoice($slovak.VoiceInfo.Name) }
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(24000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $voice.SetOutputToWaveFile($OutputFile, $format)
    $voice.Speak($text)
} finally {
    $voice.Dispose()
}
