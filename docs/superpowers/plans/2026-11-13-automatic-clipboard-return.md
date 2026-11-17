# Automatic clipboard return implementation plan

**Goal:** Return the user's clipboard after every delivery attempt and keep an
unconfirmed dictation in the banner until explicit insertion succeeds or X hides it.

**Authority:** Peter's explicit 13 November clarification supersedes the older
clipboard-recovery requirements. No additional design or execution approval is
needed. Work inline in the canonical checkout; do not commit or push unrequested.

**Architecture:** Restore when the attempt completes, with a one-second return
deadline after dispatch that runs independently of UIA readback.
Keep insertion confirmation and retained receipts independent of clipboard
ownership. Retain only failed clipboard restoration for automatic retry; never
open a recovery window. A fresh explicit Insert may retry an uncertain delivery.

**Constraints:** Preserve target ownership checks, input epochs, protected
Review/history, duplicate-click reservations, backend processes and private data.
No desktop control, microphone, playback or native input testing. An asynchronous
paste may consume restored clipboard contents; this limitation was explained and
Peter explicitly chose unconditional automatic return. Do not enable the rejected
Unicode transport prototype.

## Review focus

- Exceptions or stalled readback must not retain the temporary paste clipboard.
- User copies made during delivery take precedence over the saved clipboard.
- Explicit retries must work after an uncertain result; automatic replay stays off.
- A retained confirmed receipt must prevent another insertion after worker timeout.
- Windows clipboard contention must retry restoration without a dialog or data loss.

## Task 1: Clipboard lifetime and read-only receipts

- [x] Add a windowless regression: an unconfirmed synthetic dispatched lease is
  released by Dispose and permits the next attempt. Observe failure first.
- [x] Restore every transaction regardless of acknowledgement in finally, with
  an independent one-second return deadline. Preserve newer clipboard owners.
- [x] Decouple late exact-text/caret confirmation from an active clipboard lease.
- [x] Automatically retry actual restoration failures independently of readback.

## Task 2: Banner and explicit retry

- [x] Change the existing uncertain-delivery regression to require one fresh
  explicit insertion; observe failure before modifying the controller.
- [x] Remove recovery window/routes/actions from the banner, Settings and tray.
- [x] Preserve banner text and an actionable Insert after failed attempts; X hides
  without deleting protected text. Confirmed receipts still prevent duplicates.
- [x] Render and inspect the actual banner content without a native window.

## Task 3: Documentation and verification

- [x] Update current requirements and offline Help; identify historical native
  diagnostic checks whose old recovery expectations are superseded.
- [x] Run all Core tests, Release build and the complete windowless renderer.
- [x] Review the complete patch with one fresh reviewer and resolve findings.
- [x] Publish a separate candidate and record manifests/check results; never
  overwrite loaded binaries. Native insertion remains a manual acceptance check.

## Progress

- Initial state: feature/native-windows, clean HEAD 1236dd8.
- Original chat renamed to live local transcript - old with explicit consent.
- Ruling: retain the payload during the ordinary one-second acknowledgement
  window; restoring immediately after SendInput could systematically outrun the
  receiver. An independent timer bounds clipboard lifetime even if UIA stalls.
- Review finding P1: pre-dispatch UIA can stall after replacement. Arm the return
  timer at replacement too, reject expired dispatch under the same lease lock,
  and renew the complete acknowledgement window after actual dispatch.
