# Completion follow-up — 2026-11-10

## Implemented

Review now stores the admitted model IDs, actual pinned revisions where known,
UTC session start, and an identity for the exact comparison snapshot. New settings
or registry changes do not rewrite older Review provenance. Older items keep
unknown revisions rather than borrowing current metadata.

The native Review window has Reviewed text, Dictation original and Verification
original tabs. Original Copy actions preserve phrase choices and manual drafts.
A completed legacy comparison remains accessible; an interrupted legacy checkpoint
is not presented as a completed verifier output.

Expected clipboard errors are handled inside the copy operation. They report a
retry instruction without reaching the global emergency handler or stopping an
unrelated recording.

The non-activating banner uses the destination monitor's physical work area and
the banner's own per-monitor DPI. Geometry tests cover negative desktop coordinates,
taskbar bounds and scaled placement. Actual multi-monitor behavior still needs
native acceptance.

Missing catalog models can be inspected and downloaded from Settings / Models.
Inspection uses their pinned revision. Healthy installed models cannot be replaced
through this repair operation. Managed installations and same-revision repair
persist across restart. Invalid or missing managed manifests invalidate runtime/
quality evidence and make repair available.

Candidate preparation can finish without the default model's weights. This lets
the GUI install a missing initial model. Original model files remain in their
existing read-only location; new installations use managed storage.

## Verified

- 86 Python tests passed.
- 39 native core tests passed.
- WPF build completed with zero errors and warnings.
- The follow-up received an independent fresh-context review.
- Its three Important findings have RED-to-GREEN regression coverage:
  legacy originals, managed manifest identity, and clipboard contention.

A real isolated candidate was prepared with neither default model installed,
reusing the existing original Tiny files read-only. It became ready with an empty
default CPU-cache list. An actual Tiny transcription then completed, and health
reported idle with no loaded GPU model.

The public catalog inspection resolved KInIT Turbo to:

~~~text
eb2c35bf0971f836fbf045ebe6e897826172d5d7
3241218371 inspected download bytes
~~~

No duplicate multi-gigabyte model was downloaded for this check. The missing-model
install, same-revision repair, rollback and restoration tests use synthetic
original-format model fixtures.

## Native acceptance

Successful native EXE execution, rendered layout, actual mixed-DPI behavior,
microphone, keyboard hook and cursor insertion remain unverified. Peter's manual
launch of the published app-folder EXE also triggered the same Defender rule.
Read-only policy inspection now confirms action 6 (Warn), not an unconditional
Block setting. The built-in Unblock flow is available with administrator approval
on this Defender version. Peter must complete that Windows prompt personally and
retry the EXE. See [the execution check](native-windows.md#native-acceptance-and-execution-check).

The possible effect of the executable location has not been independently isolated;
Program Files is not an established remedy for the observed executable trust rule.

The client remains at:

~~~text
C:\AI\LocalDictation\app\LocalDictation.exe
~~~

No security setting, policy, exclusion or executable trust location was changed.
Running from an administrator account was not treated as proof that the Defender
execution block had been resolved.

The recording/verification API is separate from the native client. Initial manual
testing should open Settings and use Record to Review before testing direct
insertion. Select the actual microphone. Quit TypeWhisper before enabling the new
Ctrl+Shift shortcut.

## Remaining limits

Live quality remains experimental; the earlier 81-second measurement and its
accuracy limitations are unchanged. This follow-up did not tune recognition.

Installation transactions roll back ordinary exceptions within a running process.
There is no durable transaction journal/startup reconciliation yet. Process
termination during publication can leave staging, recovery or unregistered model
directories. Preserve them for diagnosis; do not describe this as crash-atomic
installation.

Native acceptance, manual Codex insertion and production handover remain separate
from compiler/core/API verification.
