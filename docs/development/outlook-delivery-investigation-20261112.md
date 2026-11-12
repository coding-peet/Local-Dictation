# Outlook delivery investigation and recovery candidate

Date: 12 November 2026. This is a diagnostic/recovery update, not a claim that
Outlook insertion is fixed.

## Actual application evidence

A controlled physical shortcut/microphone test visibly inserted into Notepad
between unchanged prefix/suffix markers. Its protected History item has an
insertion receipt and complete 5.24775-second WAV. Two new test History entries
have durable receipts; the second alone does not identify its destination.

The subsequent Outlook recording has 30.34775 seconds of protected audio and a
154-character transcript, but no insertion receipt. The supplied screenshot
shows an unconfirmed paste and a held previous clipboard. Do not infer the
precise failing native guard from this generic recovery message.

The original diagnostic client was restarted during the manual test. The new
process did not retain its process-only logging flag, so no new delivery journal
was produced. The next diagnostic launch must verify PID and fresh marker time.

CUA could enumerate Outlook windows but returned unavailable accessibility and
an unrelated foreground screenshot. No coordinate/input action was taken against
that mismatched surface. User-performed Outlook acceptance remains necessary
until native window attribution is reliable.

## Implemented changes

- Idle X hides recovery without discarding protected Review or the clipboard
  backup. Old queued events cannot undo dismissal. New recovery events and
  explicit Start/Exit/recovery requests can resurface it.
- The idle banner exposes Restore clipboard with the existing inspection
  confirmation. Recording Finish has priority; active/inserting operations
  block restoration. Eligibility is rechecked after the modal dialog.
- Compact Review recovery keeps one transcript surface, including long text.
- Missing-device warnings clear after valid recovery. Fresh enumeration failure
  persists through selection and rejected recording until successful refresh.
  Unrelated footer statuses and a missing saved device ID are preserved.
- Opt-in capture/delivery journals identify process, fixed guard stage and timing.
  Interruption causes distinguish invalidation, input generation, foreground,
  focused field and delivery keys. Native checks, retry policy and exact ownership
  requirements are unchanged.

Journals contain no field text/names, audio, clipboard values or paths. Capture
logging performs no extra UIA queries. Synchronous diagnostic IO adds measurement
overhead within the existing capture deadline; timings are instrumented timings.

## Verification

Current-source checks passed:

- 84 Core tests and Release publish.
- SettingsProbe: 23/23, with failing-before/passing-after microphone regressions.
- CompactFlowProbe: 30/30, with failing-before/passing-after close/event/action and
  long compact Review regressions. Its delivery/leases are synthetic.
- Diagnostic-only checks: 7/7, including opt-out, startup PID/build, fixed-stage
  privacy, interruption metadata, bounded rotation and nonfatal IO failure.

Independent review found two Important issues: clearing fresh enumeration failure
through Record, and duplicate full text during compact recovery. Both received
regressions and fixes. A minor stale-PID marker issue was fixed by also checking
marker time against this launch.

The current native DeliveryProbe was built and opened with an isolated data root.
Before the coordinator pressed Run checks, CUA returned:

> Computer Use was stopped by the user with the physical Escape key.

No native results report existed at that check. No further CUA or alternate GUI
input route was used. The helper was not force-terminated because a clipboard
owner must never be discarded on an assumption. Its waiting window can be closed
normally. Earlier 18/18 native WPF results remain historical; they are not a fresh
acceptance run of this candidate.

## Reproduction and remaining work

After safely exiting the client and closing completed fixtures, run:

~~~powershell
.\scripts\Start-DiagnosticClient.ps1
~~~

The script uses child-only diagnostic environment values, leaves normal hotkeys,
refuses a running client/fixture, and verifies a startup marker with the new PID
and a time at or after launch. It does not kill processes or change persistent
settings/environment. An ordinary later restart loses this process-only opt-in.

Use a new empty Outlook draft with no recipients. Verify that capture completes,
then inspect capture-metadata.jsonl and delivery-metadata.jsonl for that process.
Identify the exact failed invariant before changing transport or validation.
Do not retry an uncertain paste until its destination and recovery are resolved.

Actual Outlook insertion, latest native WPF acceptance, Review-approved return,
focus-switch resumption and visible F1 acceptance remain pending. No claim of
arbitrary application compatibility follows from a build or a Notepad receipt.

## Private evidence

~~~text
C:\AI\LocalDictation\diagnostics\20261112-delivery\history-audit.json
C:\Git\Local-Dictation\microphone-warning-probe\green\results.json
C:\AI\build\Local-Dictation-clipboard-banner\review-green\compact-flow-review-green.json
C:\AI\build\Local-Dictation-capture-probe\interruption-green.json
C:\AI\build\Local-Dictation-outlook-native-probe
~~~

Nine additional provisional quality recordings were prepared privately, without
new inference. Their exact WAVs and model-prefilled drafts are in the expanded
intake pack, with scoring disabled. Together with the original three, they can
form a 12-recording set after human correction/language checks. All 40 protected
source file hashes remained unchanged during export. No recording, transcript or
reference text is committed here.
