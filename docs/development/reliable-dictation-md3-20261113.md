# Reliable dictation and Material Design 3 validation

Date: 2026-11-13

## Source and build

- Canonical checkout: C:\Git\Local-Dictation\repo.
- Branch: feature/native-windows.
- Baseline commit: b494dca1845394af04d0d1e18eec06568ce1292a.
- Existing dirty/untracked work was preserved. No commits or pushes were made.
- Candidate: C:\Git\Local-Dictation\md3-candidate-20261113.
- Published product: C:\Git\Local-Dictation\app.
- Informational version input: 0.1.0-md3-cursor.20261113.
- Dependency: MaterialDesignThemes 5.4.0 and its pinned restore dependencies.

## Later recovery update

The subsequent recovery/receipt and capsule/cloud build supersedes this record's
deployed version and test count. See [the follow-up validation record](clipboard-recovery-and-shapes-20261113.md).
The evidence below describes the earlier deployment.

## Implemented behavior

The recording banner is a small movable, permanently nonactivating capsule.
It contains phase, duration/waveform and small actions, with no transcript editor
or normal clipboard-restoration button. Optional Review has its own compact window.
Settings, Models, Pending Review, History and both Review views use MD3 toolkit
components, semantic theme roles and Roboto.

First-paste acknowledgement can atomically rebase a Chromium/ProseMirror accessibility
projection only after an unchanged exact collapsed edge-caret snapshot, no selection,
first-ever insertion, a complete four-event native paste, complete payload-only readback
and its exact ending caret. Raw framework/class/control-type proof is independent of
the host executable name, so it supports a matching Codex or Edge host as well as Chrome.
The same acceptance path handles normal and late readback. It never retries input.

Finish latches exact original-field presence and the input epoch. Waiting live work is
revoked after semaphore acquisition and again at the UIA worker. Automatic final insertion
does not restore focus. Explicit Insert may restore the validated original destination.
Final acknowledgement has a separate DeliveryIntent from focus restoration, preserving
the existing Outlook paragraph projection for automatic final insertion.

Terminal live transport failures are remembered for later sessions by executable name;
focus pauses and user edits are excluded. Settings can reset those remembered choices.
The older initial empty-Value projection is permanently retired after any acknowledged
write, so retained dictated text identical to a placeholder cannot falsely acknowledge
withdrawal or lead to a duplicate final paste.

## Automated and code review evidence

- Final Core run: 343 passed, 0 failed, 0 skipped.
- Existing backend suite: 116 passed. Expected negative-test logging includes one
  deliberately malformed transcription result; the unittest run completed with OK.
- Integrated Release build and publish succeeded with 0 warnings/errors.
- git diff --check passed.
- Fresh whole-change review approved after all Important/Minor findings were fixed.

Final Core result:

    C:\AI\build\Local-Dictation-md3-final\results\core-final.trx

Detailed implementation/review reports:

    Pala Note\dictation-md3-work\delivery-report.md
    Pala Note\dictation-md3-work\ui-report.md
    Pala Note\dictation-md3-work\whole-review.md

## Visual verification

Real WPF resource/Loaded rendering was inspected in Light and Dark for all Settings
pages, History, full/compact Review and the banner. Multiline History text is top-aligned.
Selected navigation inheritance and opaque Highlight/HighlightText pairing were checked
against the actual toolkit template in an isolated synthetic rendering scenario.
Windows high-contrast mode itself was not changed or verified as an OS session.

Initial renderer setup accidentally allowed the product App.OnStartup to run. It was
corrected to a plain WPF Application loading the same resources, without product startup.
No microphone capture or insertion occurred during rendering. Production settings were
checked unchanged (SHA256 A3B42391F3D3F15516984C121048DC807AE9C0844033DA297BB42D14106A1B3C)
before deployment. The corrected renderer is documented in the UI report.

## Deployment

All 14 old product files were backed up and hash-verified. All 17 candidate files were
copied and hash-verified. No recursive deletion or removal of user data was performed.
Models, recordings and history were outside the replacement.

    Backup: C:\AI\build\Local-Dictation-md3-final\previous-app-before-md3
    Manifest: C:\AI\build\Local-Dictation-md3-final\deployment-manifest.json
    Settings snapshot: C:\AI\build\Local-Dictation-md3-final\settings-before-start.json

The installed client was started (PID 24496) and its actual MD3 window inspected.
Peter closed TypeWhisper, removing the Ctrl+Shift collision. Its configuration was not
changed and its process was not force-terminated by the agent.

At startup, the saved microphone was disconnected and the candidate API was offline.
The microphone was changed through the app to System default. The selected KInIT Turbo
Slovak model, Live=true, InsertAtCursor=true, VerifyAfterRecording=false and Ctrl+Shift
were retained. The existing candidate server was started from the canonical checkout
on 127.0.0.1:48125. The production TypeWhisper backend on 8123 was not changed/restarted.

Final runtime check: ready=true, busy=false, CUDA initialized, FP16, primary
KInIT model cached in CPU memory; warm-up 30.573 seconds. API process PID 60388.
The actual installed Settings window showed Ready - Local transcription and
Ctrl+Shift starts and stops dictation. No recording was started for this check.
Installed ProductVersion:

    0.1.0-md3-cursor.20261113+b494dca1845394af04d0d1e18eec06568ce1292a

Post-start settings comparison confirmed only the unavailable microphone selection
changed to System default; endpoint, model selections, verification/live/insertion,
shortcut and theme were retained. The pre-start settings snapshot is preserved.

## Acceptance limits

The previously blocked DeliveryProbe executable was not executed, renamed, rebuilt in
another location or loaded through another host. No security settings were changed.
Native Codex pause/continue/earlier-word correction and Outlook insertion still need
application acceptance. Unit tests and WPF rendering do not establish those results.
Native banner dragging across monitors/DPI, physical focus/Finish timing and complete
clipboard restoration under every target application were not claimed as proven.
