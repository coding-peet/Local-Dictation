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
- [x] Add fixed-stage, opt-in, bounded journals without extra native reads on the ordinary journal path.
- [x] Add best-effort focused technical provider shape only after an opted-in text-pattern capture failure.
- [x] Pass nine pure file-IO checks, including bounded provider metadata, opt-out factory suppression and structural mismatch privacy; no UI, microphone, clipboard or input.
- [x] Independently review privacy, exception preservation and native-call order.

## Task 3a: UI Automation worker compliance

Files: Services/AutomationWorker.cs; diagnostics/AutomationWorkerProbe.

- [x] Reproduce the threading requirement failure: STA baseline passes 5/6.
- [x] Use a dedicated windowless MTA thread, per Microsoft's UI Automation guidance.
- [x] Pass all six pure worker checks; keep the separate ClipboardOwner STA unchanged.
- [x] Run the current MTA candidate through the updated native fixture: 19/19 pass.
- [ ] Complete actual Outlook acceptance of the candidate.

This correction is not a demonstrated Outlook root cause. Baseline PID 31264
completed actual draft capture on STA; that outcome does not establish the effect
of the MTA change.

## Task 3b: Avoid redundant collapsed range selection

Files: Services/WindowsTextDelivery.cs; diagnostics/DeliveryProbe/Program.cs and
CollapsedSelectionTextBox.cs.

- [x] Reproduce the new native adversarial-provider failure: RED 18/19; exactly one collapsed Select, one suppressed paste, 52 selection-count retries.
- [x] Select the owned replacement range only when oldTail is nonempty; retain explicit caret restoration and every exact acknowledgment guard.
- [x] Pass GREEN 19/19; verify no final pending/unsettled clipboard transaction.
- [x] Add child-only -ExposeTestBanner to permit controlled native targeting without changing activation or insertion guards.
- [ ] Verify the diagnostic banner appears in CUA inventory.
- [x] Perform a manual candidate retest in an actual new detached empty draft; selection readback works but exact whole-document acknowledgment fails by one character.
- [ ] Identify the mismatch structurally and repeat full actual acceptance; keep exact acknowledgment guards.

Collapsed Select is valid under the UIA contract. The fixture models one
adversarial behavior; it does not establish a universal Chromium restriction.

## Task 4: Candidate and actual Outlook failure

- [x] Core tests pass 84 cases; separate Release candidate published.
- [x] Complete native delivery checks: fresh baseline STA run passes 18/18; no pending/unsettled clipboard transaction.
- [x] Update Help for hide/restore behavior; static links and candidate files checked.
- [x] Launch with diagnostics; verify fresh marker for PID 54912 and installed build 67490cc.
- [x] Launch the corrected candidate with -ExposeTestBanner; verify fresh marker for PID 7216.
- [x] Attribute actual Outlook capture failures and later successful baseline capture by PID.
- [x] Reproduce queued paste with failed readback: selection-count, 33 retries, no text/caret observation.
- [x] Reproduce a matching unavailable-selection boundary in a controlled provider and remove the unnecessary collapsed selection, with RED/GREEN evidence.
- [x] Verify candidate selection readback in actual Outlook: PID 7216, zero retries and exact caret.
- [x] Add opt-in in-memory TextMismatch metadata: offsets, fixed missing-character class and owned-boundary flag; no extra UIA queries or arbitrary characters/text.
- [x] Publish candidate-outlook-boundary-diagnostics and pass its 9/9 pure diagnostic checks.
- [x] Repeat the Core test suite: 84/84 pass.
- [x] Complete the required synthetic server suite: 116/116 pass in 17.071 seconds, without API restart or model-weight loading.
- [ ] Identify the remaining one-character whole-document mismatch without inferring its character type or relaxing acknowledgment.
- [ ] Confirm intended-field text, receipt and recovery with/without Review.
- [ ] Record results/limits, review intended paths, commit and push without force.


The earlier 12 November attempt stopped before Run checks after CUA reported
physical Escape. No further CUA or alternate GUI automation was attempted in
that turn. After the user released the PC again, the fresh baseline STA native
WPF run passed 18/18 from 09:53:14 to 09:53:24 CET. The helper reported no pending or unsettled
clipboard transaction and was closed normally.

Actual Outlook acceptance remains pending. A detached empty New mail draft was
observed through an attributable screenshot; synthetic direct entry appeared
above its preserved signature with no recipients. Its AX tree and the recording
banner are not consistently targetable through CUA.

Baseline PID 54912 failed twice at text-pattern before paste. Baseline STA PID
31264 later completed capture, then a real paste attempt reached four queued
input events and failed post-paste selection-count readback 33 times at 11:46:07
CET. Clipboard recovery remained pending; do not stop its owner or retry until
the destination and held backup are resolved. No successful actual Outlook
insertion or proven MTA/Outlook fix was established at that baseline checkpoint.
The collapsed-selection candidate passed its native regression; the later manual
candidate result is recorded below.


## Latest actual-retest checkpoint

The user prepared a new detached empty draft. The corrected candidate in
C:\Git\Local-Dictation\candidate-outlook-caret-fix started with -ExposeTestBanner;
its fresh startup marker verified PID 7216 at 12:05:39 CET. The first subsequent
CUA listWindows call reported an enforced physical-Escape stop. The completion
summary was emitted, and no further CUA or alternate GUI route was used.

That stop prevented CUA inventory verification; the user subsequently performed
a manual actual candidate test. At 12:10:06 CET, PID 7216 queued four events and
read back ObservedLength=262 versus ExpectedLength=263 (BeforeLength=241,
RequestedLength=22). ExactCaret=true at 22, ExactText=false, ReadbackRetries=0;
all existing normalization flags were false. The selection-count failure did not
recur in this attempt. The one-character whole-document mismatch remains
unexplained; do not speculate about the character type or location.

The supplied screenshot shows partial text and an Office Transform with Copilot
popup, without establishing that the popup caused the mismatch. Structural
mismatch diagnostics are prepared in
C:\Git\Local-Dictation\candidate-outlook-boundary-diagnostics. The optional
TextMismatch fields are FirstMismatchOffset, SingleDeletedPosition,
SingleDeletedKind (a fixed enum) and MissingAtOwnedBoundary. They compare the
existing in-memory expected/observed strings only, add no UIA queries, and record
no arbitrary characters, text or fragments. The report at
C:\AI\build\Local-Dictation-outlook-boundary-diagnostics\green.json passes 9/9.
The actual one-character mismatch remains unresolved. Keep production Ctrl+V
and exact acknowledgment unchanged; no Shift+Insert transport change was made.

Clipboard recovery remained pending. The user was asked to inspect/close the
known draft, deliberately restore the clipboard and exit normally; completion
is not yet verified. A later explicitly authorized CUA resume again reported
physical Escape before inventory. No alternate GUI route was used.

Diagnostic-banner inventory visibility and successful full Outlook insertion
remain unverified. Authorization is already given; the stopped tool session
blocks GUI work. Keep the actual insertion/receipt acceptance tasks open and do
not describe the Outlook failure as fixed.


Continue after normal close of the known draft, deliberate clipboard restoration
and normal client exit are confirmed. Launch the boundary-diagnostics candidate
with process-attributed logging and repeat one controlled actual Outlook test.
Inspect its structural mismatch metadata before changing document equivalence
or paste transport. Full insertion, durable receipt and clipboard recovery must
still be verified separately.
