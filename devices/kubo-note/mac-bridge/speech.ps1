param([string]$OutputFile)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$text = [Console]::In.ReadToEnd()
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
