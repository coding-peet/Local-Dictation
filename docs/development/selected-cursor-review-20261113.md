# Selected-cursor insertion and Review workflow validation

## Problem and evidence

A 158.6-second recording was preserved in History but did not enter the intended
OpenAI editor. Delivery metadata recorded a complete-document validation
conflict before input was queued. The old client then retried the same invalid
destination lease on every Insert. The log does not establish whether the
document really changed or its accessibility projection changed. It records no
actual document text at this boundary. This was not a clipboard-recovery failure
or the unrelated API-admission replay fix.

The installed client at investigation was 0.1.0-banner-model.20261113.1,
PID 57004. It also predated the final acc3bcee source review corrections.

## Resulting behavior

- Explicit Insert captures the selected external caret after reserving one
  insertion. Compact Review floats above the destination; only its Insert mouse
  click preserves external focus. Stale original leases no longer block this
  deliberate placement. Exact owned-end proof permits replacing the same live
  draft; another position receives a new insertion without deleting earlier text.
- Automatic Finish and final phrase acceptance retain original-field guards.
  No arbitrary-focus automatic insertion was added. Unsettled transport and
  clipboard operations still block fresh input; receipts prevent duplicate clicks.
- Second transcript in Review and Details uses the protected original WAV and
  one eligible different verifier. It preserves the source text, edits and
  provenance and creates a separate comparison, with the entire second output
  readable in Review. Post-hoc agreement waits for explicit placement.
- History has Play/Pause/Stop icons, elapsed/total time, seekable round-thumb
  timeline and real PCM amplitude waveform. Loading and rendering are silent.
  Sequential audio generations retain a failed-retirement barrier across replay
  and selection changes; buffers in use are cleared only after safe retirement.

## Verification

On 13 November 2026, from C:\Git\Local-Dictation\repo:

- 421 Core tests passed, including selected-caret ownership, duplicate-click
  policy, saved comparison/provenance, multipart client and composed output
  retirement cases. The retirement regressions failed on the previous behavior.
- 149 backend tests passed with synthetic engines/audio. The legacy malformed
  result test intentionally logs a duration KeyError; the suite finishes OK.
- 50 established windowless WPF/controller checks passed. They cover fresh
  placement after a stale lease, input changes during capture, unsupported
  targets, double click, unchanged automatic targeting and uncertain transport.
- Release renderer build and client publish succeeded with zero build warnings
  or errors. Actual Review, second-model picker and player were rendered in
  Light/Dark; the timeline has a centered round thumb and thin track.
- Focused workflow review confirmed language-retry and cancelled-provenance
  remediations. Player review covered the retirement barrier correction.

No desktop control, microphone capture, real audio playback or model inference
was used. Tests and renders do not prove native OpenAI/Outlook acceptance or
actual output-device behavior. Those remain manual acceptance checks. The
Defender-blocked DeliveryProbe was not run, rebuilt, renamed or rehosted.

## Candidate and deployment

Prepared version: 0.1.0-review.20261113.1.

Candidate: C:\Git\Local-Dictation\review-workflow-candidate-20261113.

Validation artifacts: C:\AI\build\Local-Dictation-review-workflow.

The candidate is separate from the installed app. Deployment requires normal
client Exit, backup and verification of all 17 payload files, and preservation
of the current settings bytes. Only the idle candidate API on 48125 needs a
controlled restart for the new endpoint; production TypeWhisper on 8123 is
outside that handover. Runtime verification will be recorded separately.

Source branch: feature/native-windows. Base HEAD: acc3bcee00fe494bf30c9e4f8c556d47c744f058.
Peter requested comment, commit and push after reviewing this candidate. Publish
the reviewed source on the existing branch without changing the installed app.

## Open UI follow-up

Peter subsequently requested integrating a popup into the main window. The
target is not yet identified: second-model selection, Review, or History. Keep
this request open rather than implementing a guessed relocation. The prepared
candidate retains the documented separate views. This publication does not
resolve that UI follow-up or claim native application acceptance.
