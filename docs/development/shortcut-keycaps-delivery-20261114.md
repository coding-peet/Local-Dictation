# Explicit shortcut keycap recorder

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

Date: 2026-11-14

## User-visible change

The previous read-only text field did not clearly communicate shortcut assignment.
The setting now has a persistent Start / stop recording label and an outlined
Change button showing one keycap per saved key.

Activating Change acquires keyboard focus and enters Listening. Pressed keys
appear as individual keycaps. A supported gesture saves when all keys are released;
then Listening ends and recording commands resume immediately. Focus alone never
starts capture. Escape, Tab, leaving the recorder, switching windows or clicking
it again cancels and restores the saved keycaps.

An invalid chord keeps the prior setting and permits another attempt. A failed
save does not change the effective shortcut. It remains visibly unsaved and can
be retried or cancelled. Backend startup behavior is unchanged.

The interaction draws on keyboard-based assignment in Microsoft PowerToys:
https://learn.microsoft.com/en-us/windows/powertoys/keyboard-manager

The recorder remains inline in Settings; it creates no assignment dialog.

## Verification

- Test-first windowless check failed against the old text field.
- Independent review found that accessibility Invoke may not transfer keyboard
  focus. A new denied-focus test failed before the fix. Listening now requires
  successful focus acquisition before global commands are suspended.
- Reviewer rechecked the fix and cancellation/save-error lifecycle: no further
  actionable findings.
- Core suite: 490 passed, 0 failed.
- WPF/controller checks: 139 passed, 0 failed.
- Release diagnostic build: 0 warnings, 0 errors.
- Idle, Listening, held-key preview and saved states rendered at 640/1040 DIPs in
  Light and Dark. Keycap bounds checked against the actual recorder. Rendered
  states inspected, including four-key wrapping at narrow width.
- Real button Click and detached PreviewKeyDown/Up events covered activation,
  live keycaps, save/exit, invalid input, Escape, Tab, blur, second-click cancel,
  denied focus and synthetic file-system save failure followed by recovery.
- Help HTML/content and git diff --check passed.

These checks created no native window or keyboard input and used no microphone
or playback. Physical activation, native Alt/SystemKey routing and transcription
remain manual checks.

## Candidate

Version: 0.1.0-shortcut.20261114.1+07a9fe6f843db9b2bf2d62ef409cec01bd113528
Candidate: C:\Git\Local-Dictation\shortcut-keycaps-candidate-20261114
Artifacts: C:\AI\build\Local-Dictation-shortcut-keycaps

## Deployment

Peter confirmed normal client Exit. The prepared candidate was installed and
started as PID 26100. All 17 installed files and 17 backup files were verified by
SHA-256. All 117 protected data files remained byte-identical through deployment.

Both API listeners retained their original identities: PID 4364 on port 48125 and
PID 22352 on port 8123. Session health reported ready=true and busy=false.

Backup: C:\AI\build\Local-Dictation-shortcut-keycaps\previous-app-20261114-183515Z

The artifact directory contains candidate-manifest.json, previous-app-manifest.json
and deployment.json with the preservation results. No commit or push was made for
this request.
