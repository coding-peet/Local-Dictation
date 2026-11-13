# Silent pause continuation recovery

Date: 13 November 2026

## Confirmed defect and change

The cursor-check worker previously set a permanent unavailable flag when any
operation exceeded its deadline. Even after that provider call actually ended,
every subsequent live update was refused for the lifetime of the client. A
temporary stall could therefore leave recognition working while cursor updates
never resumed.

AutomationWorker now retains the timed-out operation's completion task. Fresh
work is refused while that task is incomplete. After it actually completes,
the same background MTA thread can accept fresh work. The queue remains bounded
and serialized. Expired queued callbacks are skipped, cancelled callbacks retain
their cancellation token, and the expired action is never replayed.

Native field identity, surrounding-text ownership, caret, input-generation and
clipboard checks were not relaxed. An uncertain paste still requires its existing
confirmation/recovery path; worker recovery alone cannot authorize another paste.
The controller now journals live-check exception type/code/stack using the existing
metadata-only error logger, which excludes transcript and clipboard contents.

## Evidence boundary

This is a reproduced code defect. Four regressions fail against the preserved
original worker and pass with the fix. Historical native logs separately show
placeholder/range failures, successful live updates, and an input-generation
interruption; those records do not prove a timeout caused every reported pause
incident. This update does not relabel those incidents or claim new native acceptance.

## Silent tests

The Core test project compiles the actual production windowless worker source.
Synthetic callbacks exercise a held provider, bounded timeout, refusal while
still blocked, fresh work after actual return, the original thread/apartment,
skipped expired queue entries, repeated recovery without replay, and cancellation
before a simulated delivery step. No App startup or native UIA/input call occurs.

A review finding about a 100 ms barrier-admission race was fixed in the tests.
They now use explicit provider-exit signals and bounded read-only availability
probes. The final four cases were rerun against the old source to verify RED again.

Two backend tests drive the actual AudioSession with synthetic PCM bytes entirely
in memory and a scripted recognizer. A three-second quiet gap preserves the first
draft and permits an earlier-word correction on continuation. A 23-second gap
crosses a quiet commit, discards only its live quiet buffer, then emits the complete
accumulated transcript on resumed speech. Final refinement receives all original
bytes and can correct earlier wording. Revisions remain ordered and nonempty text
is preserved. These are protocol tests, not recognition-quality measurements.

Final results:

- Original worker: 4 expected regression failures, 0 passed.
- Fixed source: 366 Core/worker tests passed, 0 failed/skipped.
- Backend: 118 tests passed. Expected negative-test logging includes the deliberately
  malformed transcription result; unittest completed with OK.
- Integrated Release publish: 0 warnings, 0 errors.
- Focused independent review: no open findings after the test timing fix.

Evidence:

    C:\AI\build\Local-Dictation-pause-silent\results\worker-final-red.trx
    C:\AI\build\Local-Dictation-pause-silent\results\core-final-pause.trx
    C:\AI\build\Local-Dictation-pause-silent\before\AutomationWorker.cs

No PC control, GUI automation, windows created by a test, microphone capture,
audio playback, clipboard access, model inference or running API restart was
used. The blocked DeliveryProbe was not executed, renamed or rehosted.

## Build and deployment state

Candidate:

    C:\Git\Local-Dictation\pause-candidate-20261113

ProductVersion:

    0.1.0-pause.20261113.1+b494dca1845394af04d0d1e18eec06568ce1292a

Peter confirmed a normal Exit, and the old process was independently verified
absent. All 17 old payload files were backed up and hash-verified before replacement.
All 17 installed payload hashes match the candidate; a later readback found zero
mismatches. Models, recordings, protected History and Pending Review were outside
the replacement. The saved settings remain byte-identical to the pre-start snapshot.

The installed client is running as PID 12960. Its delivery-session.json
independently identifies the same PID and exact build. Startup timestamp (UTC):
2026-11-13T10:01:48.1664893+00:00. Candidate API PID 60388 reports ready=true and busy=false.
No client error-log update occurred after startup. No PC control, recording or
playback was used during deployment/readback. No running API was restarted.

Deployment evidence:

    C:\AI\build\Local-Dictation-pause-silent\previous-app-before-pause-fix
    C:\AI\build\Local-Dictation-pause-silent\deployment-manifest.json
    C:\AI\build\Local-Dictation-pause-silent\settings-before-start.json
    C:\AI\build\Local-Dictation-pause-silent\startup.json

Bounded metadata-only delivery diagnostics remain enabled for this client process;
they do not include transcript, audio or clipboard values.

Canonical source: C:\Git\Local-Dictation\repo

Branch: feature/native-windows

Base HEAD: b494dca1845394af04d0d1e18eec06568ce1292a

Existing dirty/untracked work was preserved. No commit, push or worktree was made.

Native pause/continue behavior in a real target remains an application acceptance
limit. The passing silent tests establish the corrected worker and session code
paths, rather than microphone, provider timing or field-rendering behavior.
