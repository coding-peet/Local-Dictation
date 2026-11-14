# Automatic API startup and pressed shortcut capture

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task.

**Goal:** Start the existing local session service with the client and set the
recording shortcut by pressing its keys in Settings.

**Architecture:** Reuse the existing launcher after a bounded loopback listener
probe. Serialize start requests and allow runtime warm-up without launching a
second process. Use a pure shortcut capture state machine and a read-only WPF
field; suspend recording commands while that field owns keyboard focus.

**Tech Stack:** Existing .NET 10 WPF client, Core library, PowerShell launcher.

**Spec:** Peter's explicit request on 2026-11-14 and docs/PRODUCT_REQUIREMENTS.md.

## Global constraints

- Work only in C:\Git\Local-Dictation\repo; preserve existing dirty changes.
- Preserve running APIs, data and models. Never start the session API on 8123.
- Use windowless tests; no microphone, playback, native input or desktop control.
- Keep Ctrl+Shift as default and retain existing shortcut validation.
- Keep the existing service running when the client exits.

## Review focus

- Concurrent startup requests while the child has not bound its listener.
- An existing listener that is preparing, incompatible or unresponsive.
- Captured Ctrl+Shift accidentally starting dictation or Escape cancelling it.
- Repeat events, modifier release order, invalid chords and lost focus.
- Keyboard search/help shortcuts intercepting the shortcut recorder.

## Task 1: Idempotent service startup

- [x] Add Core coordinator tests: existing listener, missing listener, concurrent
  starts, launch failure, startup grace period and reserved/invalid endpoint.
- [x] Implement coordinator and existing-launcher adapter; wire app startup and
  manual Start local API through the same path. Keep health/readiness reporting.
- [x] Run Core checks and verify the adapter's existing-listener path without
  stopping or launching a real service.

## Task 2: Shortcut capture

- [x] Add failing Core tests for capture, repeats, releases, cancel, invalid keys
  and recording-command suspension.
- [x] Implement capture state and focused read-only Settings interaction. Save a
  completed valid gesture; preserve the old shortcut on cancellation or failure.
- [x] Extend real windowless Settings checks for capture, persistence, loss of
  focus, cancellation and global command isolation. Render narrow/wide states.

## Task 3: Documentation and candidate

- [x] Update requirements and Help for automatic startup and pressed capture.
- [x] Run Release Core and windowless WPF checks, inspect changed UI renders,
  review the final diff and build a versioned candidate outside the checkout.
- [x] Deploy only after the currently running client exits normally. Preserve
  both API listeners and byte-identical settings/history throughout deployment.

## Decisions

- A two-minute grace period prevents duplicate starts before the listener binds.
  A failed launcher can be retried immediately; a later manual attempt can retry
  a start whose grace period expired.
- Focus the field, press a chord and release all its keys to save it. Escape
  cancels; Tab leaves the field. Invalid chords preserve the current setting.
- No new service installation, Windows startup entry or automatic service stop.

## Completion evidence

- Core: 490 passed. WPF: 138 passed; build 0 warnings/errors.
- Fresh independent review: no actionable product findings. Its routed-event
  coverage gap was addressed with detached PreviewKeyDown/Up checks.
- Deployed 0.1.0-startup.20261114.1, PID 40044. All 17 application and
  backup files verified; 117 protected files and both API PIDs unchanged.
- Physical keyboard activation, native Alt/SystemKey dispatch and microphone
  transcription remain manual checks; no native input or audio was used.

## Revision: explicit keycap recorder

Peter's screenshot/feedback on 2026-11-14 requests a recognizable shortcut
assignment control with explicit activation and visible pressed keys.

- Replace the read-only text field with an outlined button showing individual
  keycaps and Change. Focus alone must not start capture; clicking or keyboard
  activation enters Listening. Keep this inside the existing Settings card.
- Show captured keys as keycaps while held. Escape, Tab, loss of focus or a second
  click cancels. A successful save returns to idle and resumes global commands.
- Retain a visible action label, persistent setting label and accessible names.
  Keep the last valid shortcut on invalid input or save failure.
- Reuse the existing core capture and command-isolation logic. Keep backend startup
  unchanged. Cover button/routed-event behavior and idle/listening/preview/saved
  narrow/wide renders before preparing another candidate.

Revision status: implementation, review and 490 Core / 139 WPF checks complete;
deployed as 0.1.0-shortcut.20261114.1, PID 26100. All 17 app/backup files
verified; 117 protected files and both API identities preserved.
