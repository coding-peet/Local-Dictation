# Import errors and private selection follow-up

Date: 2026-11-17
Baseline: 586ed67d4e0e1947c70c01d55c708b456e42c098, feature/native-windows.
Status: implemented, verified and deployed to the existing client location.

## Authorized scope

Peter requested better per-file import errors and selection among private
transcripts, without recording or voice tests and without changes to running
APIs. The implementation plan is
../superpowers/plans/2026-11-17-import-errors-private-selector.md.

## Behavioral decisions

Import failures use curated English messages. Raw filesystem exception text,
absolute source paths, protocol response bodies and transcript content are not
shown as diagnostic text. Original inputs and previously completed results are
preserved. Saving failure must remain visible even after a transcription error.

The private picker covers the existing pending set: private, nonempty, not
already inserted. This preserves insertion receipts and does not reopen inserted
text for duplicate delivery. The choices expose an immutable ID/date/preview
snapshot. Selection requires an explicit Open action; it does not persist or
claim a destination. No private transcript is added to History by this feature.

## Verification and deployment

All test inputs were synthetic files/text and in-process HTTP handlers. No
recording, playback, speech inference, real API requests or native input tests
were used. Builds and fixtures are outside Git.

- Core: 601/601 tests passed, including 47 focused import error tests.
- Detached WPF: 201/201 state/layout checks passed; no native window handle,
  input or audio. Light/Dark renders cover narrow/wide layouts.
- Release publish completed with no warnings or errors.
- Independent review found no critical or important issues. Its minor detailed
  Review applying-state finding was reproduced, then fixed with one shared
  availability predicate. The selector and controller both reject opening while
  detailed or compact Review is applying; the full WPF suite passed again.
- Private fixtures verified unchanged protected History/Pending and Settings
  bytes. Selection, edits, removal fallback, stale IDs and busy/apply guards were
  checked without opening native Review.
- Import tests cover missing/empty/oversize/changed/unreadable files, safe
  protocol/status errors, bounded response parsing, stalled response bodies,
  cancellation, partial text and combined transcription/saving failures.

Evidence locations:

    C:\AI\build\Local-Dictation-personal-tools\import-private-core-results
    C:\AI\build\Local-Dictation-personal-tools\import-private-final-20261117
    C:\AI\build\Local-Dictation-personal-tools\private-details-apply-red-20261117
    C:\AI\build\Local-Dictation-personal-tools\private-details-apply-green-20261117

Native recording, playback, speech quality, cursor insertion and real API
inference remain outside this verification. The acoustic evaluation and
personal dictionary gate remain deferred.

## Client package

Candidate:

    C:\Git\Local-Dictation\import-private-candidate-20261117

Informational version:

    0.1.0-import-private.20261117.1+working.586ed67.586ed67d4e0e1947c70c01d55c708b456e42c098

The candidate had 17 payload files. Peter confirmed normal tray Exit; process
absence was verified before replacement. The previous payload was backed up and
hash-verified. All 17 installed files matched the candidate. All 107 protected
data files matched their pre-deployment hashes before and after client launch.
The redundant candidate directory was then removed after a final hash check.

Installed location:

    C:\Git\Local-Dictation\app

Deployment evidence and previous-payload backup:

    C:\AI\build\Local-Dictation-personal-tools\import-private-deployment-20261117

Client PID 44376 started from the installed location and responded normally.
The error log grew by zero bytes. Verification used process/log checks; no
native desktop control, microphone capture, playback or insertion was used.
Both API listeners retained their original process IDs after launch. No API
process was restarted, stopped or reconfigured. Normal client startup can query
its existing API connection; no test transcription or model inference ran.

The passive listener baseline is session API 48125 / PID 54728 and batch API
8123 / PID 51740. Both identities were unchanged after testing and packaging.
No server source, API runtime directory or model weights were changed.


## Git publication authorization

On 2026-11-17, Peter requested commit and push of all reviewed changes to
feature/native-windows and asked to include current Help. This report belongs
to that publication change set. The Help troubleshooting paths and tray-menu
names were checked against the current General/Models/Dictation/History UI.
No runtime data, recordings, models, logs or generated binaries belong in Git.
