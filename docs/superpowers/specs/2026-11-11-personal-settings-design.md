# Personal settings and recent dictations

Peter requested model choices, dark mode, a configurable shortcut, icon options,
and retention of the latest 20 transcripts with their original audio.
This supersedes the earlier pending-Review-only retention decision.

## Approach

Extend the existing WPF client and existing loopback session API.
A new web client would duplicate working native features; replacing the existing
batch backend would disrupt TypeWhisper. Neither is needed for these changes.

The session API uses port 48125. The current absence of model choices is caused
by that service being offline. Reconnect automatically while disconnected and
provide an explicit Start local API action using the existing task-local launcher.

## Behavior

- Appearance offers System, Light, and Dark, with immediate preview and persisted
  selection on Save. System follows Windows on launch; high contrast takes priority.
- Preserve Ctrl+Shift as the default shortcut. Accept Ctrl/Shift/Alt combinations
  with Space, letters, digits, or F1–F24. Validate before saving; ignore auto-repeat.
  The existing Escape cancellation and guarded text delivery remain in force.
- Keep up to 20 recent dictations containing received text and captured audio.
  Store transcript metadata and WAV audio protected with current-user DPAPI under
  C:\AI\LocalDictation\data\history. Preserve original model provenance and Review.
- The history UI supports playback, text copy, Review, explicit WAV export, and
  deletion. Export intentionally writes an ordinary WAV to the chosen file.
- Evict the oldest history transcript and audio together after saving a new item.
  Pending Review is separate and is not evicted by this limit.
- Record at most 10 minutes of 16 kHz mono PCM16, as before. The history copy is
  bounded by this same limit. No cloud uploads or model changes are involved.
- Peter approved the pink waveform icon on 2026-11-11. Use Assets/waveform.ico
  for the executable, app windows, taskbar, and tray. Preserve the SVG source
  and multi-resolution icon generation script.

## Verification

Test settings persistence, shortcut repeats/validation, history roundtrip and
21st-item eviction, encrypted data, audio content, and ID validation. Build WPF
and inspect the actual app. A build is not microphone or destination acceptance.

No commit or push is authorized by this request.
