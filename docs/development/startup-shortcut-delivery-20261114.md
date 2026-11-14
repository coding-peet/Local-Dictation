# Automatic service startup and pressed shortcut delivery

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

Date: 2026-11-14

## Delivered behavior

The application now ensures its session API is running at startup. It probes the
configured loopback listener first. An occupied port is preserved, including a
service still preparing or an incompatible listener. The existing launcher starts
only when the listener is absent; concurrent requests share a serialized gate and
a two-minute grace period before another launch attempt. Launch failures can be
retried with Start local API. Port 8123 remains reserved for the existing API.
Exiting the client leaves the backend running.

The shortcut field accepts physical key combinations. Focus it, press a supported
chord and release every key to save it. Escape cancels; Tab or focus loss abandons
an incomplete gesture. Unsupported combinations preserve the saved shortcut.
While the field captures keys, global toggle/cancel commands are suspended and
search/help handling yields to the recorder. Leave the field to use the shortcut.

## Verification

- Test-first Core scaffolds failed 19 of 20 initial checks before implementation.
  The Settings check initially failed because the field still required text.
- Final full Core suite: 490 passed, 0 failed.
- Final windowless WPF/controller suite: 138 passed, 0 failed.
- Release build: 0 warnings, 0 errors.
- The real Settings field received detached PreviewKeyDown/Up events, including
  modifier-only/default chords, alternate chords, Escape, Tab and Ctrl+F. Checks
  verified persistence, effective input configuration, command isolation, invalid
  input, cancellation and loss of focus.
- The launcher adapter's probe used a temporary loopback fixture listener and
  confirmed that an existing listener does not invoke its launch action.
- Narrow 640 and wide 1040 DIP renders were inspected. No redesign was introduced.
- Help HTML parse/content checks and git diff --check passed.
- Independent reviewer found no actionable product defects. Its event-routing
  verification gap was addressed before deployment.

No desktop control, native input injection, microphone capture or playback was
used. Physical activation, native Alt/SystemKey event delivery and actual audio
transcription remain manual checks. The absent-service launch decision is tested
with synthetic dependencies; the production service was preserved during rollout.

## Deployment

Version: 0.1.0-startup.20261114.1+07a9fe6f843db9b2bf2d62ef409cec01bd113528
Client: C:\Git\Local-Dictation\app\LocalDictation.exe
New client PID: 40044

The previous client exited normally after Peter's confirmation. All 17 candidate
files and all 17 backup files were verified by SHA-256. All 117 protected data
files, including settings, remained byte-identical through replacement/startup.

The session API retained PID 4364 on port 48125, ready=true and busy=false.
The existing port 8123 API retained PID 22352. Neither API was restarted.

Candidate: C:\Git\Local-Dictation\startup-shortcut-candidate-20261114
Artifacts: C:\AI\build\Local-Dictation-startup-shortcut
Backup: C:\AI\build\Local-Dictation-startup-shortcut\previous-app-20261114-180952Z

candidate-manifest.json and deployment.json in the artifact directory record the
verified files, versions, process IDs and preservation results.

The candidate includes the prior Review insertion error/metadata patch that was
already deployed in the restored .1 build. The rejected focus interception remains
absent. No commit or push was made for this new request.
