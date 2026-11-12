# Outlook delivery implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans task by task.

**Goal:** Diagnose the actual Outlook failure and remove recovery UI traps while
preserving destination ownership and the original clipboard.

**Architecture:** Keep the existing guarded WindowsTextDelivery adapter. Improve
recovery visibility and opt-in technical evidence independently of transport.

**Tech Stack:** C#, WPF, .NET 10, existing native/controller diagnostics.

**Spec:** ../specs/2026-11-12-outlook-delivery-design.md

## Global constraints

- Use py on this Windows machine.
- Preserve models, protected data and the separate deployed batch API on 8123.
- Never send a message or synthesize a send shortcut.
- Do not terminate a client that retains a clipboard backup or active transaction.
- Keep diagnostic text/audio/field names out of journals; preserve process opt-in.

## Review focus

- Fresh microphone enumeration failure must survive choosing the default row.
- Recovery during compact Review must not duplicate its full transcript.
- A queued old notification must not reopen a deliberately hidden banner.
- The confirmation dialog may pump events; recheck restoration eligibility.
- Diagnostic timings include instrumentation overhead, not microphone latency.

## Task 1: Microphone status

Files: Views/SettingsWindow.cs; diagnostics/SettingsProbe/Program.cs and README.

- [x] Reproduce missing-to-default/active stale-warning failures.
- [x] Add ApplyMicrophoneDevices for deterministic enumeration outcomes.
- [x] Clear only owned stale warnings; retain saved missing IDs and unrelated text.
- [x] Cover selection and rejected Record after failed discovery; SettingsProbe passes 23 cases.

## Task 2: Recovery banner

Files: AppController.cs; Views/BannerWindow.cs; CompactFlowProbe source/README.

Interfaces: CanRestoreClipboard and RestoreClipboardAfterInspection().

- [x] Reproduce idle Close, stale event, new event and action-routing failures.
- [x] Version recovery visibility and expose the existing confirmed restore action.
- [x] Cover long compact Review recovery with one visible transcript surface.
- [x] CompactFlowProbe passes 30 cases; Exit/paste/receipt guards remain.

## Task 3: Attributable capture/delivery evidence

Files: App.xaml.cs; CaptureDiagnostic.cs; DiagnosticJournal.cs;
DeliveryDiagnostic.cs; WindowsTextDelivery.Capture; DeliveryProbe diagnostic checks.

Interfaces: CaptureDiagnostic.Begin/Stage/Complete/Fail and
DeliveryDiagnostic.WriteSessionIfEnabled().

- [x] Observe failing diagnostic-only checks before implementation.
- [x] Add fixed-stage, opt-in, bounded journals with no additional native reads.
- [x] Pass seven pure file-IO checks without UI, microphone, clipboard or input.
- [x] Independently review privacy, exception preservation and native-call order.

## Task 4: Candidate and actual Outlook failure

- [x] Core tests pass 84 cases; separate Release candidate published.
- [ ] Complete native delivery checks after safe client exit and clipboard recovery.
- [x] Update Help for hide/restore behavior; static links and candidate files checked.
- [ ] Launch with diagnostics and verify startup PID/build against the real process.
- [ ] Reproduce Outlook once and inspect the exact failing stage.
- [ ] Make a minimal evidence-driven correction with its failing regression.
- [ ] Confirm intended-field text, receipt and recovery with/without Review.
- [ ] Record results/limits, review intended paths, commit and push without force.


Native acceptance is still pending. The 12 November native fixture was launched,
but CUA returned an enforced physical-Escape stop before Run checks. No further
CUA or alternate GUI automation was attempted. Existing 11 November native results
are historical and must not be presented as a fresh run of this candidate.
