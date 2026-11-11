# Compact Review implementation plan

**Goal:** Resolve alternate wordings inside the small banner and automatically
return the completed text to the original cursor without manual copying.

**Spec:** ../specs/2026-11-11-compact-review-design.md

**Architecture:** Retain existing guarded target leases and insertion receipts.
Add tested phrase-progress and completion policies, a compact WPF review panel,
and controller state that schedules insertion once on successful completion or
the last explicit choice. No model/backend changes.

- [x] Add next-difference and successful-completion policy tests and helpers.
- [x] Implement the compact two-wording panel without model labels.
- [x] Capture preview-only targets, gate live delivery, and route completion
  through compact Review with cancellation/failure protection.
- [x] Verify state transitions, native rendering, and insertion recovery.
- [x] Update the bundled Help, publish, and relaunch the idle client.

No commit or push is authorized. Keep user audio/transcripts outside the repo.

## Evidence

- 80 Core tests pass. The 18 new progress/completion cases were observed failing
  before their implementations.
- CompactFlowProbe passed 11 controller integration cases with fake delivery,
  synthetic data and no microphone/API/external typing. It covers preview-only
  gating, final placement, automatic next choice, agreement, cancellation,
  verification failure, retry/idempotence, receipt-save failure, notification
  exceptions, stale-run cleanup and final approved text visibility.
- The probe initially wrote passing results but stalled in WPF shutdown. Its
  test-host shutdown was corrected; the final run exits 0 after bounded cleanup.
- The actual compact WPF banner was rendered and visually inspected with two
  synthetic differences; no model/source names appear on the word choices.
- Custom Enter commits on key-up; native delivery also waits for Enter/Space
  and modifiers to be released. The app never synthesizes a submission key.
- Already inserted historical items remain read-only in the compact panel.
- After native UI confirmed no active recording and API idle, the client was
  updated with file-hash readback. Saved settings were unchanged. PID40776
  runs normally and the native Settings UI reports Ready with the new labels.
- Actual Codex-field acceptance remains Peter's manual test. A missing or
  changed original destination requires explicit Choose destination.
