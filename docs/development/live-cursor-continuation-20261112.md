> Superseded candidate record: the first deployment below FAILED Peter's real
> acceptance test. Its provider-specific LF exception is removed from the current
> implementation. Do not treat its passing synthetic checks as application success.
> The current repair uses independent empty-value proof and exact suffix lookup.

# Live cursor continuation validation — 2026-11-12

## Result and cause

Fixed a demonstrated initial-paste acknowledgement failure for the Chrome
ProseMirror Edit provider. The support journal recorded a document containing
one LF before insertion, a 24-unit complete payload afterwards and the exact
end caret. The expected document still included the empty-paragraph LF, so
the client invalidated ownership and stopped all later updates.

The repair accepts only two exact captured provider strings:

    Chrome/ProseMirror/50004
    Chrome/ProseMirrorProseMirror-focused/50004

It also requires an original full document equal to one LF, start zero, no
owned text, a nonempty control-free payload, exact complete ordinal readback
equal to the payload and its exact end caret. It adopts the observed baseline
once and acknowledges the original paste. Normal and late confirmation use
the same rule. Raw ExactText remains false; EmptyParagraphProjection is true.

Window, field identity, input-generation and clipboard checks remain in force.
Other text or placeholder words are not discarded. This is not arbitrary
whitespace normalization or a retry of an unconfirmed paste.

## Fresh verification

- Core: 298 passed, zero failed or skipped, including positive and negative
  projection cases and subsequent ownership/replacement checks.
- Backend: 116 synthetic tests passed. No model weights were loaded and no
  running service was restarted.
- Native RED: 30/31 passed against unchanged production delivery code. Only
  the new single-LF regression failed: before 1, requested 10, expected 11,
  observed 10, exact caret 10, one four-event paste, conflict and retained lease.
  The fixture closed its synchronous paste target and restored the clipboard.
- Native GREEN: 31/31 passed. A first attempt was interrupted by mouse input
  during clipboard backup and stopped before queuing input; the clean retry
  passed all 31. A later final-path attempt lost its fixture window and was
  rejected by capture/identity guards. After the real client and old fixture
  exited normally, the final clean run passed 31/31 with no pending or
  unsettled clipboard transaction.
- The new native case exercises real production paste/confirmation, then a
  quiet pause and identical no-op, continuation, earlier-word correction,
  withdrawal and final insertion with focus restoration. It verifies complete
  text, exact caret, no duplicate paste and original clipboard restoration.
- Existing native checks also cover surrounding text, external edits, focus
  loss, real controller completion/verification agreement, delayed paste,
  owned Unicode ranges, Outlook projection and original-window association.
- Release client/probe publish succeeded. Git diff whitespace check passed.
- Independent read-only review found no confirmed product defect. Its final
  path test gap was corrected before the final native run.

Private synthetic reports are under:

    C:\AI\build\Local-Dictation-continuation-red\native-red.json
    C:\AI\build\Local-Dictation-continuation-green\native-green.json
    C:\AI\build\Local-Dictation-continuation-green\native-final.json
    C:\AI\build\Local-Dictation-continuation-green\native-final-clean.json

## Evidence limits and remaining acceptance

The native fixture assigns ProseMirror metadata only to its own WPF adapter.
It models the recorded first-LF disappearance, not the complete real provider.
In particular it does not establish what actual ProseMirror returns after
withdrawing all text. Its synthetic withdrawal result is not real-provider
acceptance. Core tests likewise cannot establish application compatibility.

Other support events showed disappearing multi-character placeholder-like
baselines and Outlook pastes that received no acknowledgement. These are
separate unresolved observations; this repair does not accept those shapes.

Peter's real microphone pause/continue test in Notepad and his normal editor,
followed by Outlook, remains required. Existing Outlook final-only behavior
is unchanged. Do not claim that all applications are fixed from these results.

## Deployment

Candidate: C:\Git\Local-Dictation\continuation-candidate.
Build identity: 0.1.0+b494dca.cursor-continuation-20261112.

Deployment status is recorded after normal client shutdown and file-hash/PID
verification. Never replace files beneath a running client or force-stop it
while it may retain a clipboard backup. Models, settings, history and both
backend listeners are outside the deployment scope.

## Verified installed client

The candidate was copied to C:\Git\Local-Dictation\app after Peter confirmed
normal Exit of the previous client. All 14 installed files matched the candidate
SHA-256 manifest. The previous 14 files were independently copied and hash-checked
under C:\AI\build\Local-Dictation-continuation-green\previous-app-b494dca.

The installed app restarted normally with hotkeys. Its diagnostic startup marker
matched process 37472 and build:

    0.1.0+b494dca.cursor-continuation-20261112.b494dca1845394af04d0d1e18eec06568ce1292a

The application-control view showed Ready and Saved automatically. No backend,
model, settings or history migration was performed. Real pause/continue acceptance
is pending Peter's result. Changes remain uncommitted on feature/native-windows,
starting from b494dca in C:\Git\Local-Dictation\repo.
