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

The original diagnostic client was restarted during the earlier manual test. That
process did not retain its process-only logging flag, so no new delivery journal
was produced. On the resumed test, Start-DiagnosticClient.ps1 verified the fresh
startup marker for PID 54912 and build
0.1.0+67490cce1e25fe1edcfc019808efe109a89c5463. Normal hotkeys remain enabled.

Earlier CUA observations returned unavailable Outlook accessibility and an
unrelated foreground screenshot. No coordinate/input action was taken against
that mismatched surface. The resumed test subsequently reached a detached empty
New mail draft: its screenshot was attributable, recipients stayed empty, and
synthetic direct text entry appeared above the preserved signature. Its AX tree
remained unavailable. CUA also did not consistently expose the recording banner
as a separate targetable window; this limits automation, not the evidence from
the application's own diagnostic journal.

Two capture attempts in PID 54912 failed at text-pattern before any paste was
queued; that process has no capture-complete event. A later baseline STA process,
PID 31264, completed capture in the same actual draft after Right navigation
(124.6156 ms), and completed another capture in 35.3417 ms. A failed capture is
therefore not proof that this Outlook editor never exposes TextPattern.

At 11:46:07 CET, PID 31264 reached paste confirmation with four queued input
events. Readback failed 33 times at selection-count with InvalidOperationException;
no observed text length or caret was obtained. The operation became conflicted
and retained clipboard recovery. This localizes the observed failure to post-paste
selection readback; it does not yet explain why the selection was unavailable or
prove whether the editor consumed the paste. The process must retain its held
clipboard backup until the destination is inspected and recovery is deliberate.
The corresponding protected History item saved at 11:46:14 CET has no confirmed
insertion receipt. No private transcript or signature content is recorded here.

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

Journals contain no field names/values/text, audio, clipboard contents or paths.
Ordinary operation and fixed-stage journaling add no UIA queries. With diagnostics
explicitly enabled, a capture failure at text-pattern can additionally make a
best-effort read of the focused provider's technical shape: framework/class,
numeric control type, PID, focus flags and supported pattern IDs. String metadata
is sanitized and limited to 80 characters; at most 32 pattern IDs are retained.
It reads no Name, Value, text range, AutomationId, sibling or ancestor. Disabling
diagnostics before this failure path also suppresses its metadata factory.
Diagnostic failure does not replace the original capture exception. These
opt-in extra reads and synchronous journal IO add measurement overhead within
the existing capture deadline; timings are instrumented timings.

AutomationWorker now uses a dedicated, windowless MTA thread, following
[Microsoft's UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading).
The separate ClipboardOwner STA thread is unchanged. This is a threading
compliance correction, not a proven cause or fix for the Outlook failure: the
successful actual draft capture above still used the baseline STA worker.
The candidate now passes the 19-case native fixture below; actual Outlook
acceptance remains pending at this checkpoint.

## Collapsed-selection regression and candidate correction

The paste path selected its UIA range immediately before every paste, even when
that range was already collapsed at the validated caret. The candidate now calls
selected.Select() only when oldTail.Length is nonzero and a real owned suffix
must be replaced. Explicit caret restoration remains when the captured caret
needs to be restored. Ownership, focus, full-text and exact-caret acknowledgment
requirements remain unchanged; no extra paste is issued.

A native adversarial provider in DeliveryProbe models an editor that loses its
active selection when an already collapsed UIA range is selected. The baseline
passed 18/19 checks and failed only this new case: one collapsed provider Select,
one synchronously suppressed Ctrl+V and 52 selection-count readback retries.
The fixture inspected its own known destination, confirmed that its synchronous
interceptor had consumed the queued paste, and restored the clipboard safely.
With the narrow selection change, all 19/19 checks pass, including initial append,
continued append and owned-suffix replacement. Both final pending/unsettled
clipboard flags are false. The coordinator observed this native run through CUA.

This is a controlled provider regression, not evidence that every Chromium editor
rejects collapsed selection. Microsoft's
[ITextRangeProvider::Select contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-select)
permits degenerate ranges. Current
[Chromium range-provider source](https://raw.githubusercontent.com/chromium/chromium/main/ui/accessibility/platform/ax_platform_node_textrangeprovider_win.cc)
contains focus-adjustment and blur behavior around selection. This is a plausible
mechanism, not proof of the installed Outlook/WebView version or its failure.
The actual manual candidate result is recorded below; successful full Outlook
acceptance is still required after any further correction.

## Verification

The recovery candidate checks passed:

- 84 Core tests and Release publish.
- SettingsProbe: 23/23, with failing-before/passing-after microphone regressions.
- CompactFlowProbe: 30/30, with failing-before/passing-after close/event/action and
  long compact Review regressions. Its delivery/leases are synthetic.

Additional current-source checks passed:

- Diagnostic-only checks: 9/9, including opt-out, startup PID/build, fixed-stage
  privacy, interruption metadata, bounded rotation, nonfatal IO failure, the
  opt-in failed-provider shape and structural document-mismatch metadata. The
  failed-provider case failed before implementation and passed afterward,
  including a flag change to opt-out before the read factory.
- A fresh Core test rerun passes all 84 cases. The required synthetic server
  suite passes 116/116 in 17.071 seconds without restarting an API or loading
  model weights. These checks do not establish actual Outlook compatibility.
- AutomationWorkerProbe: 6/6 without UIA calls, windows, clipboard, microphone or
  API access. The initial STA baseline passed 5/6 and failed the MTA requirement;
  the one-line apartment change passes all six. Serialization, exception identity,
  deadline handling and cancellation remain covered.
- Updated native DeliveryProbe: RED 18/19, GREEN 19/19 for the collapsed-selection
  regression above. Final reports confirm no pending or unsettled clipboard
  transaction in either run; the RED case's held lease was resolved by its owner.

Independent review found two Important issues: clearing fresh enumeration failure
through Record, and duplicate full text during compact recovery. Both received
regressions and fixes. A minor stale-PID marker issue was fixed by also checking
marker time against this launch.

The first attempt to run the current native DeliveryProbe used an isolated data
root. Before the coordinator pressed Run checks, CUA returned:

> Computer Use was stopped by the user with the physical Escape key.

No native results report existed at that earlier check, and no further CUA or
alternate GUI input route was used in that turn. The helper was left running to
avoid discarding a possible clipboard owner without evidence.

After the user released the PC again, the native fixture ran from 09:53:14 to
09:53:24 CET on 12 November. All 18/18 checks passed. The report confirms both
ClipboardRecoveryPending=false and ClipboardTransactionUnsettled=false. This
fresh baseline STA run, before the MTA change, covers native WPF insertion, Review
return, focus-switch resumption, rich-text preservation and delayed-paste recovery in synthetic fixture fields.
The helper was closed normally through CUA and disappeared from the window
inventory. These results do not establish Outlook editor compatibility.

## Reproduction and remaining work

After safely exiting the client and closing completed fixtures, run:

~~~powershell
.\scripts\Start-DiagnosticClient.ps1
~~~

The script uses child-only diagnostic environment values, leaves normal hotkeys,
refuses a running client/fixture, and verifies a startup marker with the new PID
and a time at or after launch. It does not kill processes or change persistent
settings/environment. An ordinary later restart loses this process-only opt-in.

For native test targeting only, -ExposeTestBanner sets the child-only
LOCAL_DICTATION_TEST_BANNER_WINDOW flag. This exposes the recording banner's
taskbar entry while retaining its existing ShowActivated=false and insertion
guards. Omitting the switch explicitly clears an inherited test opt-in for that
child. CUA inventory visibility with this option has not yet been verified.

Use a new empty Outlook draft with no recipients. Verify that capture completes,
then inspect capture-metadata.jsonl and delivery-metadata.jsonl for that process.
Identify the exact failed invariant before changing transport or validation.
Do not retry an uncertain paste until its destination and recovery are resolved.

The actual candidate test below now has working selection readback but fails
exact document acknowledgment. Determine the one-character mismatch before
attributing the complete Outlook failure to collapsed range selection. Successful
full Outlook insertion, its Review-approved return and focus-switch resumption,
and visible F1 acceptance remain pending.
Native acceptance is complete for the 19-case synthetic fixture. No claim of
arbitrary application compatibility follows from a build, a Notepad receipt or
those native fixture checks.

## Latest manual candidate retest and remaining blocker

The user prepared a new detached empty draft for the corrected candidate. The
launcher started C:\Git\Local-Dictation\candidate-outlook-caret-fix with
-ExposeTestBanner and verified the fresh marker for PID 7216 at 12:05:39 CET.
At that launch its assembly version identified base commit 67490cc, with the
reviewed caret correction present as local changes when the candidate was built.

The first subsequent CUA listWindows call reported an enforced physical-Escape
stop. The required completion summary was emitted, and no further CUA input or
alternate GUI route was used in that turn. At that checkpoint, actual candidate
retesting had not yet occurred and diagnostic-banner inventory visibility was
unverified.

The user then performed a manual candidate test in PID 7216. At 12:10:06 CET,
confirmation recorded four queued events, BeforeLength=241, RequestedLength=22,
ExpectedLength=263 and ObservedLength=262. ObservedCaret=22 and ExactCaret=true;
ExactText=false. ReadbackRetries=0 and all three existing normalization flags
were false. Selection readback now works in this actual candidate attempt; the
earlier selection-count failure did not recur. The current unresolved boundary
is the one-character whole-document mismatch, whose type and location have not
yet been established. Do not infer them from the length difference.

The supplied screenshot shows the first partial text visible and an Office
Transform with Copilot popup. This is not evidence that the popup caused the
mismatch. The next diagnostic candidate is now prepared at
C:\Git\Local-Dictation\candidate-outlook-boundary-diagnostics. Its optional
TextMismatch metadata compares the already-held expected and observed strings
in memory, without extra UIA reads. It records FirstMismatchOffset,
SingleDeletedPosition, a fixed SingleDeletedKind enum and
MissingAtOwnedBoundary. Single deletion is reported only when removing that
position explains the entire observed string; ambiguous equal-character runs
are checked independently at the owned boundary. Arbitrary character values,
text and string fragments are never serialized. Unsupported differences retain
null deletion fields rather than guessing a cause.

The pure diagnostic report passes 9/9. The actual missing character remains
unidentified until a new attributable attempt produces this metadata. The
production paste transport remains Ctrl+V; no Shift+Insert change was made.
Exact acknowledgment has not been relaxed.

Clipboard recovery remains pending for this attempt. The requested handoff is to
inspect/close the known draft, deliberately restore the clipboard and exit the
client normally; completion has not yet been verified. A later authorized CUA
resume immediately reported another physical-Escape stop before inventory, and
no alternate GUI route was used. Diagnostic-banner inventory visibility remains
unverified. Authorization is already given; the stopped tool session blocks the
GUI route. Successful full Outlook insertion is still not established.

## Private evidence

~~~text
C:\AI\LocalDictation\diagnostics\20261112-delivery\history-audit.json
C:\Git\Local-Dictation\microphone-warning-probe\green\results.json
C:\AI\build\Local-Dictation-clipboard-banner\review-green\compact-flow-review-green.json
C:\AI\build\Local-Dictation-focus-capture-metadata\green.json
C:\AI\build\Local-Dictation-worker-threading\green.json
C:\AI\build\Local-Dictation-collapsed-selection\red.json
C:\AI\build\Local-Dictation-collapsed-selection\green.json
C:\AI\build\Local-Dictation-outlook-boundary-diagnostics\green.json
C:\AI\LocalDictation\logs\capture-metadata.jsonl
C:\AI\LocalDictation\logs\delivery-metadata.jsonl
C:\AI\build\Local-Dictation-outlook-native-probe
~~~

Nine additional provisional quality recordings were prepared privately, without
new inference. Their exact WAVs and model-prefilled drafts are in the expanded
intake pack, with scoring disabled. Together with the original three, they can
form a 12-recording set after human correction/language checks. All 40 protected
source file hashes remained unchanged during export. No recording, transcript or
reference text is committed here.
