# Review return implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline; independent
> banner and Large-v3 tasks are delegated in this session.

**Goal:** Pause/resume original cursor delivery and accept verified text back
into its original field without manual copy/paste or multiple Review windows.

**Architecture:** Existing guarded owned-range replacement with transient
suspension, retained per-Review target leases, and durable insertion receipts.

**Tech Stack:** .NET 10/WPF, existing UI Automation/Win32 and xUnit; existing
Python FP16 engine for isolated live qualification.

**Spec:** ../specs/2026-11-11-review-return-design.md

## Tasks

- [x] Test guard resume at an arbitrary caret, conflict after external edits,
  owned-only removal/restoration, and persisted successful insertion receipts.
- [x] Implement resumable native delivery and explicit focus restoration.
- [x] Retain target leases, save before removal, and always Review verification.
- [x] Add Accept and insert, explicit destination selection and single-window Review.
- [x] Integrate movable banner and measured Large-v3 live findings.
- [x] Build, run appropriate tests and native verification, review safety,
  then update the client after preserving/stopping its old recording.

## Evidence and completion

- 62 Core tests pass, including five resume/receipt cases and two same-ID
  pending-object replacement cases. New regression tests were observed failing
  before their implementations.
- Seven native DeliveryProbe checks passed using actual UIA, GlobalInput and
  WindowsTextDelivery against synthetic fixture-owned windows. Peter clicked
  Run checks after the CUA tool's new-app approval timed out.
- The safety review found final-delivery serialization, stale destination
  arming, receipt upsert and final-cleanup reentrancy defects; these were fixed.
  Final cleanup rechecks run ownership and blocks new recording during closing.
- Native WPF banner and dark Review renders were visually inspected. Review
  resource resolution was made assembly-qualified for standalone diagnostics.
- Large-v3 real live test accepted all 91.8 seconds but returned WER26.74%
  with 5.347/7.790-second first partials. Normal Large remains after-recording;
  the production listeners and model files were not changed.
- After Peter authorized stopping/updating, the old client was already gone.
  Published files were copied to C:\Git\Local-Dictation\app with hash readback.
  Client PID27184 was launched without --no-hotkey and the native UI reported
  Ready with the updated workflow copy.
- Codex destination acceptance remains Peter's manual test. Automatic insertion
  of manually added line breaks/control characters is not supported; validation
  fails before changing focus/text and leaves the Review recoverable.

## Review focus

Never duplicate text after a failed pending cleanup or double click. Never
overwrite an externally edited document or a different field. Save before
removal, preserve pending data after any failure, and do not close on merely
attempted insertion. Do not start GPU inference while the isolated test runs.
