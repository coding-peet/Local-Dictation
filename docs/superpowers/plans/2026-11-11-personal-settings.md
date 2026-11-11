# Personal Settings Implementation Plan

> Implement inline in the existing native-windows worktree. Preserve prior edits.

Goal: make model choices available and add appearance, shortcut, recent audio
history, and icon choices to the existing native Windows application.

Architecture: keep the existing session API and WPF application. Add a protected
history store in Core, playback in Views, and settings-backed theme and shortcut
services. Keep model files and production runtime in C:\AI.

Spec: ../specs/2026-11-11-personal-settings-design.md

## Tasks

- [x] Diagnose offline session API; start only port 48125 and verify /v1/models.
- [x] Add tests for shortcut validation/repeats and settings roundtrip.
- [x] Add protected HistoryStore; verify 21st-item eviction and original WAV data.
- [x] Wire capture, finalization, history UI, copy/export, and playback cleanup.
- [x] Add theme resources and configurable shortcut settings, keeping --no-hotkey.
- [x] Create icon choices and integrate the provisional native icon.
- [x] Review changes, run Core tests and WPF build, then publish to a sibling
      versioned app folder for verification before replacing the running version.
- [x] Update current instructions and record observed acceptance limits.

Result: 45 Core tests passed, WPF publish passed without compiler warnings.
The native model dropdown, light/dark appearance and empty History window were
visually inspected. Real microphone recording, audio-device playback and
insertion acceptance remain for manual testing. Source changes are uncommitted.

Review focus: repeated hotkeys, disabled hotkeys, capture cancellation, failed
protected saves, audio pruning, stale playback callbacks, offline settings,
and editable Settings controls during reconnect.
