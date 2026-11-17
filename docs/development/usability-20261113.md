# Models, unified History and compact playback validation

## Result

Models now has two cards: installed model management and adding. Test and Remove
use the selected installed row. The Add card supports missing catalog models or
a public Hugging Face URL, compatibility/size inspection and download/cancel.
Shared originals are retained; only verified app-managed files can be removed.

History combines recent recordings and protected unfinished text with status.
The duplicate Pending Review navigation/opener is removed. Pending-only rows
remain visible beyond audio eviction and disable unavailable audio actions.
Deletion closes matching editors before removing a dictation and its choices.

Actions sit above a roughly 108-DIP player. The faint waveform underlays the
timeline and uses RMS with fixed -54..-12 dBFS references, tested with synthetic
silence, low noise, normal speech-level signals and isolated high bursts. This
is an energy display, not a speech/noise classifier or hardware calibration.

## Evidence

- 443 Core tests passed, including merge/receipt retention, catalog URL refresh,
  model removal client response checks, waveform scale and playback lifecycle.
- 175 backend tests passed, including 26 removal/cache cases using temporary
  model files and real temporary junctions. No real installed model was removed.
- 68 established windowless checks passed. Actual Models, History and player
  content were rendered in Light/Dark at wide/compact sizes.
- Meaningful merge, waveform, stale audio, editor deletion, receipt precedence
  and catalog replacement regressions failed before their respective fixes.
- Focused model-removal review found no P1/P2 issue. The three UI/workflow review
  findings were fixed and the remediation readback found no remaining issue.

Core/server tests and rendering used no native window handles, desktop control,
audio output, microphone or real inference. OpenAI/Outlook insertion and actual
device playback remain manual application acceptance checks.

## Publication and verified deployment

Source: C:\Git\Local-Dictation\repo, feature/native-windows.
Implementation commit: ddd91749599f3ed4753d7dbdcf1dcb4fc8b7240a.
Installed version: 0.1.0-usability.20261113.1+f5910eba3bfe151c615aecfa1b66c91a5baba988.
The candidate was built before its implementation commit, so informational
metadata retains base f5910eb. The unique usability version and recorded payload
hashes identify the deployed candidate.

Client PID at verification: 57124.
Updated session API PID: 34080, ready on loopback port 48125.
Read-only discovery returned 4 model entries and
1 removable managed installations. OPTIONS confirmed both
saved-audio verification and model-removal POST routes without invoking either.

Peter confirmed normal client Exit before replacement. All 17 client payload
files and their backups were hash-verified, with current settings byte-identical
after startup. Only the verified idle session API was restarted. Production
listener state on 8123 was unchanged; no real model was removed or modified.

Candidate: C:\Git\Local-Dictation\usability-candidate-20261113.
Artifacts: C:\AI\build\Local-Dictation-usability.
External deployment-manifest.json records backup and payload hashes;
runtime-verification.json records PID/version/settings and capability checks.

No desktop control, recording, audio playback or real inference was used during
deployment. Native OpenAI/Outlook and audio-device acceptance remain unverified.
Peter explicitly authorized comment, commit and push on the existing branch;
the deployment record is published as a follow-up documentation commit.
