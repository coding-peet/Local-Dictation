# Synthetic import transport fixture

This fixture runs a separate loopback API with tiny fake model metadata and a
synthetic engine. It loads no actual models, uses no GPU, and reads no user audio,
settings, history or clipboard. Do not point it at production data.

## 1. Start a fresh fixture

From C:\Git\Local-Dictation\repo:

```powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B diagnostics/ImportApiFixture/server.py `
    --root C:\AI\build\Local-Dictation-personal-tools\fixture-server-NEW `
    --port 48226
```

Use a new output directory; the script refuses an existing root and ports 48125
and 8123. It creates only synthetic test metadata beneath the approved build root.
It does not stop or restart either normal API.

## 2. Run the integration check from another shell

```powershell
$env:LOCAL_DICTATION_SYNTHETIC_IMPORT_ENDPOINT = 'http://127.0.0.1:48226'
& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
    C:\AI\build\Local-Dictation-personal-tools\ui\bin\VerificationBannerRender\release\VerificationBannerRender.dll `
    --output C:\AI\build\Local-Dictation-personal-tools\integration-NEW `
    --only ImportIntegrationChecks
```

The check refuses any other endpoint and requires the fixture's explicit health
marker. It tests real HTTP normalization and WebSocket transcription/verification,
per-file failure, protected History, cancellation/worker drain, unchanged originals
and zero cursor destinations. Every input and transcript is synthetic. It is not
ASR quality, microphone, playback or native application acceptance.

## 3. Stop only the synthetic fixture

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:48226/__fixture/stop
```

This endpoint exists only in this test fixture. It requests ordinary server
shutdown; it does not terminate another process or delete test artifacts.
