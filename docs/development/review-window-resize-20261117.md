# Compact Review resize validation

Date: 2026-11-17
Branch: codex/review-window-resize
Base: 57ef10de9f68a73e88795a676441e314df3ff911
Candidate: 0.1.0-reviewresize.20261117.1+working.57ef10d

## Problem and resulting behavior

Compact Review used an auto-sized StackPanel and fixed reader/editor limits.
Increasing the window height left the transcript viewport at 100 DIP and the
editor at 110 DIP. The window and panel now use bounded grids with an expanding
text row. Main actions remain below the content; secondary controls scroll when
space is short. The inline model picker has its own bounded scroll area.

No dictionary application, recording, model, delivery or storage policy changed.
Dictionary usage counters and explicit deletion are recorded as requested future
work; recognition hints and exact corrections remain disabled pending evaluation.

## Verification

- New regression first failed: reader 100 -> 100, editor 110 -> 110 DIP.
- Fixed layout at equal width and content heights 520 -> 900 DIP:
  reader 157 -> 537, editor 92.2 -> 466, in both Light and Dark.
- Independent review found a small-window model-picker overflow. A second red
  regression measured its Transcribe action at 256-304 DIP outside its 68-244
  body. After adding the picker viewport, its action is 144-192; in private mode
  96-144 inside a 68-196 body. Test client size is 400 x 380, allowing for the
  frame of a 420-DIP-tall native window.
- 10 dedicated detached resize/picker checks passed; full suite: 226 passed.
- Core: 640 passed. Synthetic backend: 185 passed.
- Release publish succeeded; 17 candidate files, Help identical to source.
- Help HTML: balanced tags, 26 unique IDs, 15 local anchors resolved.
- Offline Light reader and Dark editor renders inspected. No desktop control.
- Independent follow-up found no further actionable issue.

The first full-suite invocation was stopped by DictionaryDraftChecks because
its output was outside its required isolated personal-tools root. Re-running
under the required root passed; the safety condition was not changed.

## Candidate and acceptance boundaries

Candidate files:

    C:\AI\build\Local-Dictation-review-resize-20261117\publish-build\client-candidate

Final detached suite:

    C:\AI\build\Local-Dictation-personal-tools\review-resize-suite-final-20261117

No native window, microphone capture, playback, model inference or native
insertion was used for verification. Real drag/focus/target-application acceptance
remains distinct. Running APIs and installed client files are preserved while
preparing the candidate. Client replacement requires normal tray Exit first.
No commit, push or merge is part of this request.

## Installed client verification

After Peter confirmed normal tray Exit, process checks first found the original
client still running; no files were replaced. A later process check confirmed
zero clients, and the approved replacement proceeded without another approval.

- Installed version: 0.1.0-reviewresize.20261117.1+working.57ef10d.
- New process: PID 18556, correct installed path, responding, main-window title
  Local Dictation. All 17 installed files match the reviewed manifest.
- All 128 data-file hashes are unchanged before replacement and after startup.
- The owned Start with Windows registration is unchanged, including value kind.
- Both API PIDs and process start times are unchanged; neither API was restarted.
- No new client error-log bytes after startup. No microphone, playback or native
  insertion test was performed. Real resize/focus acceptance remains separate.
- Previous payload is retained at:

    C:\AI\build\Local-Dictation-review-resize-20261117\deployment-20261117-1\previous-app

The generated client-candidate directory was removed only after full installed
hash parity and startup preservation checks. Manifests, test results, renders and
rollback payload remain. Source changes are local on codex/review-window-resize;
no commit, push or merge was requested or performed.
