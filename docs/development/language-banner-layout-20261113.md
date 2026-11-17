# Language banner layout follow-up: 13 November 2026

## Requested changes

The language indicator was too narrow. Widen the banner, make removal marks
less prominent, and replace the language-tag helper sentence with one clear heading.

## Implemented and deployed

- Normal banner: 392 x 88 DIP, previously 328 x 88.
- Verify banner: 456 x 176 DIP, previously 440 x 176.
- The status/language button stretches across its allocated cell. Elapsed time,
  detected language and a queued correction fit without ellipsis.
- Language codes remain 14 DIP. Removal marks are separate muted 10-DIP text;
  their whole-chip 48-DIP interaction target and accessible name are retained.
- Heading: "Preferred Auto languages (supported by installed models)".
  The helper sentence under the tags is removed. The + add control remains.
- No transcription, native insertion, settings schema or API behavior changed.

Version: 0.1.0-language-ui.20261113.1+b494dca1845394af04d0d1e18eec06568ce1292a.
Verified client PID: 18528.
All 17 installed files and their backups were hash-verified. Settings are
byte-identical. Both existing APIs remain running; neither was restarted.
Evidence: C:\AI\build\Local-Dictation-language-ui\runtime-verification.json.

## Validation

The extended existing windowless renderer reproduced clipping before the fix:
"Banner truncates the elapsed time and language: 09:59 · Auto · EN → AUTO".
It measures real rendered text against available width rather than checking
only the underlying string. After the fix, all 28 existing/extended checks pass.
Light/Dark renders cover short/long Verify text, normal recording, detected and
fixed language, pending correction, tags and picker. No native HWND is created.
Release build and publish passed with zero warnings/errors. The UI-only change
does not warrant repeating unchanged Core/backend inference tests.

No desktop control, recording, playback or native paste test was used.
The installed process, build marker and API readiness were checked through CLI.

Source: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows.
Base HEAD: b494dca1845394af04d0d1e18eec06568ce1292a.
All existing dirty/untracked work is preserved; no commit or push was requested.
