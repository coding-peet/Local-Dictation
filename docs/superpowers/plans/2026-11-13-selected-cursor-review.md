# Selected-cursor Review insertion, second transcript and History player

## User input

Peter records commentary while reviewing other windows. A 158.6-second dictation
was saved but neither automatic completion nor repeated Insert placed it into
the OpenAI app. Setting a cursor and clicking Insert must place the retained
text there. Review should offer a second-model transcript. History needs
Play/Pause/Stop icons, a seekable position/time display and an amplitude timeline.

## Evidence and design

The installed client was 0.1.0-banner-model.20261113.1 (PID 57004), before the final
source review fixes in acc3bcee. Technical delivery metadata shows capture at
12:15:22Z, expected document length 60/caret 59, then rejection at 12:18:55.919Z
in validation before any queued input. The saved snapshot no longer matched;
the evidence does not establish that Peter edited the field. The invalid lease
was reused by every later Insert. No clipboard write or recovery caused this case.

Separate explicit selected-caret placement from automatic completion. Keep
automatic original-field/Finish guards unchanged. Explicit Insert reserves one
operation before capture, preserves external focus at the button, captures a
fresh editable external caret and validates its epoch before dispatch. Use an
exact same-field/document/owned-end proof to adopt a live draft for replacement;
otherwise insert at the chosen cursor and leave earlier text elsewhere intact.
Block unsettled transport/clipboard operations and drain final receipts so a
double click or delayed acknowledgment cannot duplicate an insertion.

Compact Review floats above the target so Insert stays reachable. Only its
enabled inactive-window Insert hit avoids activation; editing and choices use
ordinary focus. Already inserted History items remain readable and can be
verified again, without enabling another insertion of that same Review.

Post-hoc verification transcribes only the original audio with the selected
second model. It preserves source text/provenance/manual edits in History and
creates a separate comparison. Agreement still waits for explicit placement;
differences are reviewed. The complete second transcript is visible read-only.

History playback owns and clears decrypted audio, computes its waveform without
opening output hardware, and creates a device only after explicit Play. The
playback output is fakeable for deterministic seek/pause/stop/lifecycle checks.

## Validation and deployment

Use Core/server synthetic tests and actual-controller windowless checks. Render
Review, model selector and player in Light/Dark. Do not record, play sound, drive
the desktop, or run/rehost the blocked DeliveryProbe diagnostic. Native OpenAI
insertion still requires Peter's manual acceptance after deployment.

Build a separate candidate. Normal client Exit is the only technical prerequisite
for replacing its loaded DLLs. Preserve current settings/data/models and verify
17 payload/backup hashes. Restart only the verified idle candidate API on 48125
to load the new endpoint; preserve production TypeWhisper API 8123.

Canonical checkout: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows. Starting HEAD: acc3bcee00fe494bf30c9e4f8c556d47c744f058.
Peter subsequently authorized comment, commit and push on this existing branch.
Peter subsequently identified the complete History workspace for main-window
integration. See the integrated-history follow-up plan and validation record.
