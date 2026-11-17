# Outlook paragraph acknowledgment validation - 12 November 2026

Status: candidate correction and controlled native regression passed. Installed
Outlook acceptance remains pending.

## Actual Outlook evidence before this correction

The user confirmed that the final-only attempt inserted the complete result in
one paste and left the existing signature unchanged. The attributable client
was PID 48968, build ae963cb. It still entered unconfirmed-paste recovery.

The same attempt recorded these content-free measurements:

- BeforeLength=241; RequestedLength=48.
- ExpectedLength=289; ObservedLength=288.
- ObservedCaret=48; ExactCaret=true; ExactText=false.
- The full comparison identified one missing LF at owned boundary 48. The first
  mismatch was offset 49 because either of two identical initial LFs explains
  the same observed string. Every other character matched exactly.

This localizes the acknowledgment failure to the accessible document readback.
It does not establish Outlook's internal document/paragraph implementation. No
real transcript, signature text, recipient or message content is stored here.

## Narrow acknowledgment rule

Desktop Outlook still keeps live wording in the banner and inserts the complete
final result once after Finish and any required verification choices.

The new rule accepts a readback projection only when all of these hold:

1. The captured process name is exactly `olk` or `OUTLOOK`, case-insensitively.
2. This is final delivery, with no previously owned text and insertion at
   document offset zero.
3. The payload is nonempty and contains no `char.IsControl` characters.
4. The original document starts with exactly two LF characters, followed by a
   character other than CR or LF.
5. The observed caret equals the complete payload length.
6. The full observed document equals the exact payload followed by the original
   document with only its first LF removed, using ordinal equality.

There is no whitespace normalization or payload alteration. Missing payload
characters, signature changes, CRLF/NBSP substitutions, unknown applications,
live updates, existing owned text and other insertion offsets remain rejected.
Later signature formatting is allowed only when it remains exactly unchanged.

Raw `ExactText` remains false for this case. The separate
`OutlookParagraphProjection` flag identifies the accepted projection. Delivery
adopts the actual observed document as its ownership baseline, acknowledges the
existing paste and restores the previous clipboard. It never pastes again to
obtain acknowledgment; repeating the same accepted final request is a no-op.

## Verification

All source work used C:\Git\Local-Dictation\repo on feature/native-windows,
with generated output outside the checkout. No new worktree was created.

- Core: 201/201 passed, including 76 new pure predicate cases. The test-first
  run produced nine expected positive-case failures against a false stub;
  implementation then passed the full suite. The fixed synthetic fixture
  matches the 241/48/289/288 structural lengths above.
- Pure diagnostic checks: 9/9 passed. These perform synthetic/file-IO checks;
  they do not exercise an external destination.
- Final native DeliveryProbe: 26/26 passed on 12 November, 18:47:58-18:48:15 CET.
  ClipboardRecoveryPending=false and ClipboardTransactionUnsettled=false.
- All three paragraph cases passed in the final run. The known Outlook
  projection was acknowledged after one complete native paste, preserving the
  exact payload, caret and signature, adopting the observed baseline and
  restoring the clipboard. Repeating the accepted request did not paste again.
  An ordinary process and an additional signature edit remained unconfirmed,
  retained recovery until deliberate fixture cleanup, and refused replay.
- The final run also passed the existing invalid endpoint and failed clipboard
  restoration checks that did not complete in the earlier attempts below.
- Static Help checks passed: 20 unique IDs, 12 valid internal links, no scripts,
  and the existing restrictive content security policy. All three edited
  documents passed UTF-8/content/trailing-whitespace checks; the tracked Help
  and requirements changes passed `git diff --check`. Help was not opened or
  rendered at that earlier static-only checkpoint; the installed F1 check is recorded below.

The native paragraph provider first performs an ordinary synchronous native
paste, then models the single-LF projection. It is a controlled synthetic
provider, not installed-Outlook acceptance.

## Earlier native attempts and bitmap representation correction

The two earlier native runs failed at different stages:

1. `green.json` completed 25/26 checks, including all three new paragraph cases.
   "Invalid provider endpoint movement cannot queue replacement input" was
   interrupted during its initial draft by the input-generation guard in
   clipboard backup. QueuedInputEvents=0, ClipboardRestored=true, and both final
   clipboard recovery/unsettled flags were false. It never reached the invalid
   endpoint assertion. This was an interrupted initial insertion, not evidence
   that replacement input bypassed the endpoint guard.
2. `green-repeat.json` stopped after two recorded checks. "Failed native
   clipboard restoration retains every original backup format" failed while
   `Fingerprint` called `BitmapBytes`, reporting "Bitmap data is unavailable".
   This occurred before a transaction started and before any destination
   observation; Diagnostic=null. Both final recovery/unsettled flags were false.
   This is separate from the first run's input-generation interruption.

A strict synthetic bitmap regression then exposed a representation defect in
`ClipboardTransaction.Clone`: `CopyImage` used `LR_CREATEDIBSECTION`. For
CF_BITMAP, the RED run received a 104-byte DIBSECTION description where the
32-byte BITMAP representation was required on this x64 machine.

Microsoft documents that a DIB section cannot be pasted between applications.
[CreateDIBSection documentation](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createdibsection)

The correction uses `CopyImage` with flags zero. Omitting
`LR_CREATEDIBSECTION` creates a device-dependent bitmap; omitting
`LR_COPYRETURNORG` requires a new object rather than returning the source.
[CopyImage documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-copyimage)

The GREEN synthetic check passed for CF_BITMAP and CF_DSPBITMAP: independent
handles, BITMAP representation and preserved pixels after source deletion.
It published no synthetic data to the clipboard and reported
UserClipboardUnchanged=true. This proves the corrected cloning representation;
it does not independently prove the cause of the earlier fingerprint failure.
The final 26/26 native run passed afterward with strict format checks retained.

## Evidence

    C:\AI\build\Local-Dictation-outlook-lf-ack\dotnet
    C:\AI\build\Local-Dictation-outlook-lf-native\diagnostic-final.json
    C:\AI\build\Local-Dictation-outlook-lf-native\green.json
    C:\AI\build\Local-Dictation-outlook-lf-native\green-repeat.json
    C:\Git\Local-Dictation\outlook-lf-native-probe\bitmap-red.json
    C:\AI\build\Local-Dictation-outlook-lf-native\bitmap-green.json
    C:\AI\build\Local-Dictation-outlook-lf-native\green-final.json

## Deployment and Help verification

The code commit is 05447e4927c5d5d347cddb7e49aa4200edb76e1e. The release
build passed 201/201 Core tests and was published from the canonical checkout.
The published assembly records LocalDictation.SourceRoot as
C:\Git\Local-Dictation\repo and its informational version as
0.1.0+05447e4927c5d5d347cddb7e49aa4200edb76e1e.

After verifying no client or fixture was running, all 14 client files were
backed up, replaced and hash-checked. All 78 protected data/settings files were
unchanged during that file copy. Models and user data remain under C:\AI.
The local backup and deployment manifest are at:

    C:\AI\build\Local-Dictation-deploy-20261112T175158Z

The candidate service was found offline after the user's pause. It was started from
the canonical checkout with Start-CandidateServer.ps1; port 48125 then reported
ready=true and busy=false. No production service was restarted.

The installed client launched normally with hotkeys and child-only delivery
diagnostics. Startup marker and actual process path matched PID 38892 and the
version above at 18:52:01 CET. This identifies the next Outlook trial; it does
not by itself establish insertion acceptance.

F1 in the installed Settings window opened C:\Git\Local-Dictation\app\Help.html
in the default browser. The updated Outlook troubleshooting disclosure opened
and displayed its complete new paragraph without clipping. The published Help
and application-level F1 check passed; no user settings were changed for this
inspection.

## Follow-up trial and shortcut collision

The first physical trial after deployment was blocked because TypeWhisper was
running with the same Ctrl+Shift shortcut. The client deliberately disabled
that colliding chord. Peter chose to quit TypeWhisper and retain Ctrl+Shift,
then confirmed that he had also exited Local Dictation himself. This is not
evidence of a spontaneous client crash or a successful Outlook insertion.

The client was restarted normally with diagnostics at 19:05:48 CET. The startup
marker and process command line identify PID 8716, the same code version above,
and exactly C:\Git\Local-Dictation\app\LocalDictation.exe without arguments.
TypeWhisper was absent. Saved shortcut settings were EnableShortcut=true and
Shortcut=Ctrl+Shift. In the running Settings window, Recheck shortcut displayed
"Ctrl+Shift starts and stops dictation." The local API was ready and idle.

Settings was closed while leaving the client running, and the empty test draft
was left open. A fresh physical shortcut/microphone trial was requested. The UI
status proves configuration and collision checks, not actual delivery of a
physical keyboard hook event or successful Outlook insertion. That acceptance
remains pending.

## Remaining acceptance

Manual Outlook acceptance remains pending. Use the attributable installed
client identified above for the user's actual Outlook test, confirming the
complete visible result, preserved signature, durable insertion status, restored
clipboard and absence of replay. Review-approved return and focus-switch
resumption remain distinct acceptance checks.

The corrected installed Outlook flow has not yet passed a manual test. A build,
Core tests, diagnostic checks or synthetic native field does not establish that
result. The published Help and F1/application-level check passed as recorded above.
