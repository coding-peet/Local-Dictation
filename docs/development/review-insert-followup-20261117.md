# Review Insert follow-up

Date: 2026-11-17
Branch: codex/review-window-resize
Status: targeted client fix deployed; native paste acceptance remains open.

## Investigation baseline

Four current selected-cursor attempts failed with Windows error 5 at process
integrity inspection. Process lookup identifies the attempted target as csrss,
not an editable destination. The check correctly refused it before native paste.
At the start of the investigation, the installed client was
reviewresize.20261117.1. No API was restarted.

A historical gesture interceptor was rejected after the user reported a recording
regression. Its archived source was read, but not reapplied. Current investigation
must separate an inactive Review click from direct focus on the target.

## Diagnostic limitation

The current Windows computer-use API activates a selected window before input.
That changes the inactive-window condition. The attempted automated route was
ended without claiming valid capture evidence. No user-app content was typed or
pasted. Its test windows closed at their timer.

## Manual capture evidence

The user confirmed that Insert fails even after selecting an editable field in
another application. The earlier answer "nowhere" does not mean that no field
was selected. Do not ask this same clarification again.

The same-process manual probe captured the expected prepared Edit field once.
That only validates the narrow same-process fixture. It does not validate native
insertion or explain the user's external-process failure.

The separate-process probe reproduced a lost foreground: GetForegroundWindow
returned zero already at Insert preview mouse-down and remained zero through
capture, before controller refresh could account for the failure. The capture
refused the unexpected destination. No paste or clipboard access occurred.
This excludes refresh as the cause of that particular observed loss, but does
not yet identify which native/WPF activation step causes it.

A diagnostic-only WS_EX_NOACTIVATE variant preserved the external Edit field.
Real production Capture also succeeded against that exact synthetic field.
The source fix now applies only the native no-activate bit at Insert hit-testing,
before mouse activation, and restores it outside Insert or when unavailable.
It uses the native foreground handle instead of stale WPF Window.IsActive.
No preview gesture interceptor, recording hook or banner behavior changed.

The targeted dynamic fix passed the same external-process manual capture trial.
The target remained foreground through mouse-down, mouse-up, Refresh and real
Capture. Target focus loss appeared only when the probe closed. Actual paste
and normal editing/repeated insertion remain separate acceptance checks.
The native capture path is permitted only after foreground, focused PID and Edit control type
match the exact test-owned target; its Apply method is never called.

Private reports are stored outside Git under
C:\AI\build\Local-Dictation-review-insert-20261117. Current probe limitations
include synthetic controller state and an inert input-epoch fixture. No voice
recording, model inference, API restart or deployed-client replacement occurred
during these diagnostic trials. The later client deployment is recorded below.

## Independent size request

Default compact Review size is changed in source from 560 x 600 to 620 x 680 DIP.
The ten detached resize/picker checks pass. All 640 Core, 226 detached WPF
and eight owned-hidden-window native activation checks pass. The native
activation test failed before the fix and passed after it; it covers unrelated
controls, unavailable Insert and preservation of existing style bits.
Initial-size renders were inspected offline. This size is included in the follow-up client deployment below.

## Client deployment

The installed client was no longer running. No LocalDictation process remained
before file exchange. A verified backup of all 17 previous payload files was
created before copying the 17 reviewed candidate files.

Client build: 0.1.0-reviewinsert.20261117.2 (working 57ef10d).
Started client PID: 33340; responsive, title Local Dictation; no new client errors.
All 142 data files were hash-identical after restart. Start with Windows was
preserved. API PIDs 55220 (8123) and 56912 (48125) retained their exact original
start-time ticks; no API restart occurred. Compare parsed timestamps as UTC ticks,
not a DateTime object against its original JSON string.

Private backup, hashes and before/after state:
C:\AI\build\Local-Dictation-review-insert-20261117\deployment-2

## Publication scope

The user subsequently requested comment, commit and push on the current branch.
Publish the resize/focus repairs, synthetic diagnostics, Help and this evidence.
History Insert remains a separate requested extension: its saved design explicitly
awaits workflow approval, and no History insertion behavior is implemented here.
No merge is included.


## Pre-publication verification

Fresh checks before the requested commit/push:

- 640 Core tests.
- 226 detached WPF state/layout checks.
- 8 hidden-window native activation checks, without foreground change or input.
- 185 backend unit tests with synthetic inputs in the existing candidate environment.
- 18 build/MSI contract tests; these do not qualify the obsolete MSI pilot.
- Diagnostic/client build: zero warnings and zero errors.

No voice test, model inference or API restart was used for these checks. Native
paste acceptance and the ordinary-edit/return-to-target sequence remain open.
The earlier capture-only manual result is not a native-paste acceptance result.
