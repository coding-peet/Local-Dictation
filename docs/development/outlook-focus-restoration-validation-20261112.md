# Outlook focus restoration validation - 12 November 2026

Status: the 30-case native clipboard/focus baseline passed. The keyboard
experiment failed its delivery comparison and is not approved for rollout.
Clipboard remains the default. The experimental keyboard path is diagnostic-only
and has not been enabled in the installed client. Actual Outlook cause and
acceptance remain unproven.

## Actual application evidence

In the user's later manual test, client PID 8716 repeatedly refused final
insertion before queuing any native input. The journal recorded focused-field
interruptions and one foreground-window interruption during clipboard backup.
Final delivery attempts had QueuedInputEvents=0. No previous clipboard backup
remained held by these attempts.

This is a pre-insertion focus-validation failure. It is distinct from the earlier
successful visible paste whose readback differed by one boundary LF, documented
in outlook-paragraph-ack-validation-20261112.md. The paragraph acknowledgment rule
cannot repair an attempt that stops before a paste is queued.

The original records do not identify the expected and observed UIA process/field
identity at the failure. They do not establish asynchronous restoration,
disturbance by redundant SetFocus, or capture of an unintended field as the cause.
A future attributable trial must associate capture and delivery metadata before
drawing that conclusion. No real transcript, signature, UI name or message
content is stored here.

## Diagnostic evidence

The opt-in focus mismatch record uses only foreground/identity values already
read by the existing guard. It serializes expected/observed numeric handles,
process IDs parsed from those identities, and stable 16-hex identity hashes.
A foreground-only failure has no observed focused identity. Raw runtime
identities are not serialized, and opt-out retains no payload or journal writes.

Capture now records a fixed window-scope-proof event after the scope resolver
returns and before its matched gate. Its nine fields are Time, ProcessId, Stage,
Milliseconds, Status, ExpectedWindow, NativeHost, Root and Matched. Status is one
of matched/mismatched/unresolved; handles are numeric. Both initial and current
diagnostic opt-in are required. This event reuses the resolved scope and adds no
UIA queries, names or field text.

Metadata generation is best effort. The instrumentation itself adds no native
input or UIA queries. Ownership resolution and the bounded readiness loop below
are separate validation operations.

## Exact window association and focus restoration

Before delivery, the captured original field must resolve through its nearest
native host to the captured top-level window. An unresolved or foreign root
pauses delivery without adopting another field. The current host/window
association is rechecked before focus restoration and input.

Final delivery releases acceptance keys, validates the captured original window
and identity, then activates that window:

1. Allow up to 100 ms for activation to restore the exact captured field itself.
   If it is already ready, do not call provider SetFocus redundantly.
2. Otherwise request SetFocus once on that captured field and allow up to 500 ms
   for its exact identity. No input or clipboard replacement occurs while waiting.
3. Continue through the existing full-document ownership, exact-caret, key and
   paste-acknowledgment checks. Never promote the currently focused field to the
   destination.

Initial foreground acquisition may complete asynchronously within this bounded
wait. Once the original window has arrived, later foreground loss aborts.
Cancellation, invalidation, input-generation changes and a lost window-scope
association still stop the operation. A provider that never restores the original
focus leaves insertion paused. These changes address controlled focus mechanisms;
they do not establish the cause or correction of the user's Outlook failure.

## Clipboard/focus baseline and pure checks

All source work used C:\Git\Local-Dictation\repo on feature/native-windows.
Generated output stays outside the checkout; no new worktree was created.

- Fresh final-source Core rerun: 281/281 passed, exit code zero. The pure Unicode
  planner preserves literal UTF-16 units and rejects controls, unpaired
  surrogates, U+2028 and U+2029.
- Fresh final-source pure diagnostics: 11/11 passed. The Checks array confirms
  every case passed, including focus identity privacy and fixed-schema scope
  proof with opt-out/no-file checks.
- Focus RED before the correction passed 27/29. Exactly two new cases failed:
  redundant SetFocus disturbed an already focused target, and deferred SetFocus
  returned before the original identity was ready. The never-restored-focus
  refusal case already passed. Both final clipboard flags were false.
- The first post-change native attempt stopped after seven results, six passed.
  Review reinsertion lost foreground on its first readiness check, before
  provider SetFocus, clipboard backup or input. QueuedInputEvents=0 and both
  final clipboard flags were false. This is a separate interrupted attempt.
- The later exclusive run passed all 30 clipboard/focus baseline checks,
  including redundant/deferred/never-restored focus and the handle-less field's
  original-window association. The deferred case used one provider focus request
  and one paste after exact focus returned; the never case issued no paste.
  A matching-text foreign window was refused with no focus request or paste.

The exclusive report is 32/35 overall because five keyboard comparison cases
follow the 30 passing baseline cases. It is not a full 35-case pass. Both final
clipboard recovery/unsettled flags were false.

## Final-source baseline repeat

The final source was published from the canonical checkout. A fresh explicit
--baseline-only run passed 30/30 native checks with ExpectedChecks=30 and
SelectedSuite=baseline-only. ClipboardRecoveryPending and
ClipboardTransactionUnsettled were both false. The completed fixture closed
normally. This repeats the production clipboard/focus contract; it neither skips
nor reclassifies the failed keyboard experiments as passing tests.

The separate candidate client also published successfully. Core passed 281/281
and the opt-in metadata checks passed 11/11 in this continuation. Independent
source review found no Important or Critical blocker for the default clipboard
candidate. Actual Outlook acceptance remains separate.

## Keyboard transport experiment and rollout decision

The requested alternative was evaluated as a private diagnostic transport
selector. It emits Unicode UTF-16 down/up pairs without using the clipboard,
modifier shortcuts or paste. The production selector still defaults to Clipboard.
Neither experiment was enabled in the installed client.

The first implementation grouped up to 256 UTF-16 units, preserving surrogate
pairs, with exact progressive acknowledgment between groups. In exclusive.json:

- Short owned revision, append and withdrawal passed with both transports.
- The 3360-unit clipboard operation passed in about 129 ms. Keyboard delivery
  failed after its first 512 input events; complete text/caret were unconfirmed.
- The rich-text comparison failed an existing-formatting check during its
  clipboard half, before it measured keyboard insertion.
- Missing original focus correctly queued zero keyboard events.
- Diverting focus after four typed units let remaining queued keyboard events
  reach the synthetic decoy. This failed the required no-wrong-field-write check.

The revised implementation acknowledges one complete Unicode scalar at a time.
In scalar.json, two of five checks passed:

- The never-restored-focus case queued zero events and changed no destination.
- Diversion after four ASCII units stopped after exactly eight events/four
  acknowledged chunks. The decoy stayed unchanged, and retry sent no replay.
- The 3360-unit keyboard attempt remained incomplete after 30.020 seconds,
  despite a diagnostic-only 30-second worker budget. It queued 1970 events in
  961 scalar chunks and failed exact full-text/caret confirmation. Clipboard
  completed the same controlled payload in about 109 ms.
- The short comparison stopped on a clipboard text/caret mismatch before its
  keyboard half. Untagged keyboard input was observed before capture, so this
  short transport comparison is inconclusive. The fixture is being hardened
  with a strict precondition; this is not a demonstrated keyboard transport
  failure.
- Rich-text insertion had exact text/caret measurements for both transports,
  but the subsequent original italic-suffix formatting check failed. The fixture
  is under investigation; this does not yet establish product formatting damage.
  The case did not pass rich-text acceptance.

Both scalar-run clipboard flags were false at completion. The keyboard cases
used no paste and left the clipboard unchanged. The two passing focus-refusal
checks do not establish successful long or rich-text delivery.

Reject enabling this keyboard path as a default or universal fallback. The
256-unit version allowed a wrong-field write; scalar acknowledgment stopped that
specific tested diversion but could not complete 3360 units even in 30 seconds.
That does not meet the existing five-second final-insertion budget. Keep the
clipboard path as default, with no automatic paste fallback or replay after an
unconfirmed keyboard attempt. Further experiments must preserve these limits;
the result is not support for replacing clipboard delivery across Windows apps.

## Documentation checks and evidence

The earlier static Help checks passed: 20 unique IDs, 12 valid internal anchors,
no scripts and a restrictive content security policy. Updated Help passed the
same static checks again. F1 routing and its visual-check limit are recorded below.
This report update changes no product or Help files.

    C:\AI\build\Local-Dictation-focus-identity\red.json
    C:\AI\build\Local-Dictation-focus-identity\native-red.json
    C:\AI\build\Local-Dictation-focus-identity\native-green.json
    C:\AI\build\Local-Dictation-scope-proof\red.json
    C:\AI\build\Local-Dictation-scope-proof\green.json
    C:\AI\build\Local-Dictation-unicode-plan\test-results\unicode-separators-green.trx
    C:\AI\build\Local-Dictation-focus-final\test-results\focus-final-core.trx
    C:\AI\build\Local-Dictation-focus-final\diagnostic11.json
    C:\AI\build\Local-Dictation-focus-final\baseline30.json
    C:\AI\build\Local-Dictation-keyboard-spike\diagnostic11.json
    C:\AI\build\Local-Dictation-keyboard-spike\exclusive.json
    C:\AI\build\Local-Dictation-keyboard-spike\scalar.json

## Installed clipboard/focus candidate

After the user exited the old client normally, source commit
5eb991597f0bdaa84c719f22a29927f0b5ed44d4 was republished from the canonical
checkout into C:\Git\Local-Dictation\focus-candidate. Assembly metadata verified
that commit and SourceRoot=C:\Git\Local-Dictation\repo.

All 14 original application files were backed up before replacement. All 14
installed hashes then matched the candidate. The 761 protected user-data files
present at the time retained their hashes across deployment. Models, runtime
environments and API configuration were not part of the copy.

    C:\AI\build\Local-Dictation-focus-final\deployment-backup

The diagnostic launcher started installed PID 52496 with build
0.1.0+5eb991597f0bdaa84c719f22a29927f0b5ed44d4. Its session marker verified the
actual PID; normal Ctrl+Shift hotkeys were enabled, without --no-hotkey. Settings
showed Ready and the candidate API reported ready=true, busy=false.

F1 opened a new Chrome tab for the installed app/Help.html. The browser protocol
policy rejected observing local file: page content, so visual page verification
was not performed or bypassed. Static Help checks passed: 20 unique IDs, all 12
internal anchors valid, zero scripts and a restrictive content security policy.

The production selector remains ClipboardPaste. The private Unicode prototype
has no settings, default or fallback activation path; shipping this source does
not enable keyboard delivery.

## Remaining acceptance

The clipboard/focus candidate is installed. Run a fresh manual
Outlook trial with an attributable build/PID and capture-to-delivery association.
Confirm visible complete text, preserved surroundings, durable insertion status,
restored clipboard and no replay. Published Help still needs a visual check in
an environment where local file observation is permitted.

The installed Outlook cause and correction remain unproven. Synthetic native
baseline success and pure checks are not actual Outlook acceptance. Keyboard
transport remains a disabled diagnostic experiment.
