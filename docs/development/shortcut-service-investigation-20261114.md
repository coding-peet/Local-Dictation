# Shortcut settings and offline service investigation

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

Date: 2026-11-14

## Reported behavior

The recording shortcut did not open the banner. Peter also could not change
its value by pressing the desired key combination in Settings.

## Evidence

- The running client was the restored `0.1.0-insert.20261114.1` build, PID 23508.
  Its command line did not contain `--no-hotkey`.
- The supplied screenshot showed the session service offline and the recording
  button disabled. A health request to port 48125 failed with connection refused.
- Port 8123 still had its existing Python listener, PID 22352. It was preserved.
- The shortcut field in the screenshot was empty. Validation retained the saved
  `Ctrl+Shift` configuration; the displayed Not saved error concerned that draft.
- The current shortcut control is a text field. A physical chord does not enter
  a value. Help now gives explicit text-entry and offline-service instructions.
- Recheck shortcut currently recalculates configuration and the TypeWhisper
  collision policy. It does not prove that Windows is still delivering hook
  callbacks. No hook recovery change was made without evidence of hook loss.

## Validation

A windowless check exercised the real Settings text-change and commit path with
synthetic settings. It verified persistence, effective InputCommands selection,
start/stop command production, last-valid-value preservation and error recovery.
It did not create a native window, register hooks, capture audio or inject input.

Release diagnostic build: 0 warnings, 0 errors.
Windowless WPF/controller checks: 138 passed.

## Service recovery

The existing `scripts/Start-CandidateServer.ps1` launcher started only the absent
session API using its existing environment, models and data. The client and the
port 8123 listener were not restarted. Runtime readiness must be checked after
warm-up; process startup alone is insufficient.

Physical shortcut activation and microphone transcription remain manual checks.

Final health check: session API PID 4364 reported ready=true, busy=false,
max_seconds=600 and CUDA runtime ready. Client PID 23508 and the existing
port 8123 process PID 22352 remained running.
