# Owned draft range repair - 12 November 2026

## Status

The current candidate passes 125 Core checks, nine pure diagnostic checks,
35 synthetic controller/Review checks, and all 23 controlled native delivery
checks. Release compilation succeeds. Both native clipboard pending/unsettled
flags are false. Actual Outlook acceptance remains a separate deployment check;
passing fixture fields does not establish compatibility with every editor.

All source work uses C:\Git\Local-Dictation\repo on feature/native-windows.
The implementation includes the previously committed Settings simplification.
Models, history/audio, Pending Review, environments, and SDKs remain in C:\AI.

## New observed failure

The latest supplied Outlook screenshot reports:

    The owned draft range is no longer available.

That message has one source in WindowsTextDelivery: a nonempty owned suffix
could not be found after full-text identity and the collapsed caret passed.
OwnedText is populated only after successful acknowledgment. This is therefore
a later correction failure after an initial acknowledged paste, distinct from
the earlier one-character first-paste mismatch.

The current installed process was PID 50816, launched normally at 15:22 CET.
The diagnostic marker still referred to earlier PID 21996. No new per-process
capture/delivery journal exists for 50816, so it cannot identify the precise
FindText predicate or deleted character. The client was subsequently exited
normally and absence of its process was verified. The detached test draft was
visually empty above its preserved signature; no message was sent.

## Candidate design

- Replace dependence on FindText for the mutable owned suffix with construction
  from the current validated caret. Every candidate must prove exact suffix,
  full prefix, caret endpoint, and document boundaries before clipboard/input.
- Compare common prefixes on complete ordinal .NET text elements in both old
  and new drafts. Never split a surrogate, combining sequence, or ZWJ grapheme.
- Character units are provider-defined, not UTF-16 offsets. Bound a search by
  text length but accept only the exact resulting range.
- Preserve the existing two-second worker deadline and exact post-paste ACK.
- For whole replacements and short shared prefixes, try an optional anchor one
  Character before the original insertion point. Its start lies in unchanged
  prefix rather than directly on the insertion boundary. Revalidate it fully;
  stale provider/argument errors become cache misses, never new destinations.
- At document start, use the fresh document-start endpoint. Keep cancellation,
  focus, and input-generation checks outside optional-cache exception handling.
- Capture reports the fixed caret-anchor stage without logging field text.

Microsoft documents provider-defined units and endpoint crossing:
[MoveEndpointByUnit](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-moveendpointbyunit),
[text units](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationtextunits).
The [WPF implementation](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/TextRangeAdaptor.cs)
walks insertion positions inside a Character movement request; a small number
of RPCs is not a wall-clock bound for large movement counts.

## Evidence so far

- Prefix helper RED: 13/22 pass, nine fail with the previous UTF-16 loop.
- Prefix helper GREEN: 22/22; complete Core suite 106/106.
- Native baseline RED: 19/22; both FindText-suppressed correction cases reproduce
  the exact reported failure with queued input zero. The invalid-movement test
  had not reached that capability yet. Clipboard pending/unsettled both false.
- First caret-only candidate: 21/22. Short correction passes; long Unicode full
  withdrawal exceeds the deadline. No clipboard/input was queued by that failure.
- Exact captured-caret cache candidate: still 21/22 with the same long-withdrawal
  timeout. A range at the insertion boundary is not a reliable left anchor.
- Current neighbor-anchor revision: GREEN 23/23 at 16:42:28–16:42:44 CET.
  Both clipboard pending/unsettled flags are false. It covers stale cached-range
  fallback, combining/ZWJ neighbors, repeated surroundings and full Unicode
  correction/withdrawal. The long Unicode case completes all its operations in
  2.883 seconds total; each operation retains its existing worker deadline.

Do not increase the deadline or weaken ownership to make the fixture pass.
Long nonzero-common-prefix behavior remains limited to measured cases.

## Outlook completion-only delivery

Peter explicitly selected a practical Outlook workflow: keep all live wording
in the banner and insert the completed result once, after Finish and any word
choices. Desktop new/classic Outlook are identified by the exact process names
olk and OUTLOOK from the already captured top-level window. No window title or
message content is read for this decision.

The per-session flag suppresses partial, final-event and resume writes without
changing saved live-insertion settings or live recognition. CompleteRun retains
the validated original destination for final insertion. Verification differences
still wait for choices; cancellation/failure blocks automatic insertion. The
banner explicitly says Outlook inserts after Finish, while blocking errors take
precedence over that policy hint.

Pure policy tests bring Core coverage to 125. CompactFlowProbe passes all 35
cases, including Outlook partial/resume suppression, one final insertion,
verification choices, cancellation, unchanged settings/other-app behavior, and
active verification failure/cancellation message precedence. Actual Outlook
paste acceptance remains separate from these synthetic controller tests.

Independent reviews found no remaining important range or controller issues
after correcting the reflected BrowserDeliveryProbe constructor call and the
Outlook hint's precedence over blocked verification messages.

## Outputs and next steps

    Client: C:\Git\Local-Dictation\candidate-outlook-final-only
    Probe: C:\Git\Local-Dictation\owned-suffix-probe\final-policy
    Evidence: C:\AI\build\Local-Dictation-owned-suffix-native
    Core evidence: C:\AI\build\Local-Dictation-owned-suffix-core

The successful native report is final-policy.json. The completed synthetic host
was released only after checking its exact executable path, report timestamps,
23 passing results and both clipboard flags false. No real client was forced
closed. Source and deployment verification must keep these reports distinct
from actual Outlook acceptance.

Build the reviewed commit from the canonical folder, preserve the previous app
files, and deploy only with the real client normally exited. Verify all published
hashes, embedded source root, Help, and the new startup PID/build. Keep production
API8123 and the separate candidate listener unchanged.

Launch the installed client through Start-DiagnosticClient.ps1 -ExposeTestBanner
for an attributed Outlook retest. A normal subsequent restart loses that
process-only logging flag. Verify PID/build before reading delivery evidence.
Actual full Outlook insertion remains unaccepted until that test succeeds.
