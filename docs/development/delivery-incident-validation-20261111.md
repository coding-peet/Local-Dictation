# Delivery and repetition incident validation — 11 November 2026

Scope: fixes after 66d0260 on feature/native-windows. Current expected behavior is
in docs/PRODUCT_REQUIREMENTS.md. Real recordings, transcripts, logs and generated
diagnostic output remain outside Git.

## Reproduced causes

The protected incident contained 16.388 seconds of audio, with Large-v3 as the
primary model and no verification model. Replaying the whole recording in one
inference reproduced the 672-character output with a consecutive identical-word
run of 102. The repetition therefore originated in recognition, rather than
Review assembly or insertion. The existing session draft replacement and PCM
segmentation also passed explicit characterization checks.

The original transport sent individual Unicode events in chunks under a
two-second worker deadline. A native synthetic test reproduced a timeout after
only 432 characters of approximately 3,000. It could mark the destination as
conflicted even when Windows accepted no input. Completion also created an
interactive Review before attempting an otherwise automatic final insertion.

## Changes

- Apply Whisper's native temperature/compression fallback to original non-fast
  profiles. This re-decodes audio; it never deletes repeated transcript words.
  Log-probability and no-speech score thresholds remain unset because retaining
  beam vocabulary scores exceeded this 4 GiB GPU's memory during the experiment.
- Capture the original cursor before showing recording UI. Keep recording audio
  immediately, and block stale Review actions during a new destination check.
- Normal completion and verification agreement insert before showing interactive
  Review. Differences, lost destinations and actionable failures retain Review.
- Use one guarded paste for each nonempty live/final suffix replacement. Retain
  original destination identity, surrounding text and owned-range checks. Exact
  text and caret acknowledgement are required; equivalence diagnostics do not
  relax validation. No Enter or send shortcut is synthesized.
- Preserve all supported clipboard formats before replacement; refuse unsupported
  formats beforehand. Exclude temporary dictation from Windows clipboard history
  and cloud synchronization. An uncertain queued paste retains its payload and
  previous clipboard backup until deliberate recovery. A later user copy wins.
- Retain complete saved format handles across a partial restoration failure.
  Provide a tray recovery action and refuse normal Exit while recovery is pending.
  Successful insertion still records its receipt if clipboard restoration warns.
  Any unresolved transaction also blocks Exit before the worker reaches cleanup;
  a stalled accessibility provider cannot hide its retained clipboard backup.
  Explicit restoration remains disabled while the worker still owns the operation.
- Allow five seconds for final automation and 120 seconds for live finalization;
  the live automation deadline remains two seconds. Progress watchdogs, Escape
  cancellation and the ten-minute recording limit remain active.
- Add opt-in local delivery diagnostics containing technical metadata and counts,
  without transcript, audio, clipboard contents, UI names or document paths.

## Recording replay

Same original FP16 Large-v3 weights and audio, sequential inference:

| Policy | Inference | Total including load | Output length | Longest repeated-word run |
| --- | --- | --- | --- | --- |
| Previous generation policy | 39.497 s | 46.865 s | 672 | 102 |
| Native compression fallback | 40.949 s | 47.727 s | 91 | 1 |

This confirms removal of the reproduced loop, not a WER or semantic accuracy
claim: no reference transcription or listening assessment was used for this clip.
The corrected first word also differed. Models and precision were not changed.

## Final checks

- Release publish and Core tests: 84/84; no skipped tests or compiler warnings.
- Backend suite: 91/91. A deprecation warning and expected error-path logging do
  not change the successful result; dependencies were not updated for this fix.
- CompactFlowProbe: 24/24, including an active reservation before recovery is
  advertised. This case reproduced RED before the final Exit guard.
- ReviewInputProbe: 8/8.
- SettingsProbe: 15/15.
- InlineReviewProbe: 15/15.
- Native DeliveryProbe: 15/15, visibly observed through Windows app control.
  Approximately 3,000-character live delivery took 339 ms, final delivery 462 ms.
  Native RichTextBox preserved surrounding text and formatting. A 1.6-second
  deferred paste retained its own payload until explicit clipboard recovery.
  Forced partial clipboard restoration retained every original for retry.

The native fixture also checked actual controller final/completed event chains
without verification and with verification agreement. It used only its own
synthetic fields, never an email or external app. Native reports are under
C:\Git\Local-Dictation\delivery-fix-probe; controller/WPF reports are under
C:\Git\Local-Dictation\incident-final-probes, with compiler output under
C:\AI\build\Local-Dictation-incident-final-probes. The app candidate is under
C:\Git\Local-Dictation\candidate-delivery-fix.

The three unchanged input/settings/inline probes ran before the final narrow Exit
guard; the 24-case controller probe ran after it. Native transport was unchanged
by that lifecycle guard. The final client was rebuilt and all 84 Core tests rerun.
Offline Help passed 20 unique IDs, 12 internal links and self-contained resource/
CSP checks. Native Help rendering was not repeated for these wording changes.

## Deployment

Installed the candidate's complete 14-file payload into
C:\Git\Local-Dictation\app. Every installed file's SHA-256 matched the candidate;
settings.json's hash was unchanged. The client runs normally without --no-hotkey.
Its actual Settings window displayed Ready · Local transcription and Saved
automatically, with the saved Large-v3 selection and verification disabled.

Restarted only the idle candidate on 48125 from this updated checkout. It reports
ready=true; the production 8123 listener retained the same process identity.
For the manual Outlook test, this client process has text-free local delivery
diagnostics enabled. This process-only flag does not change saved settings.
Deployment evidence is outside Git in
C:\AI\build\Local-Dictation-incident-final\deployment.json.

An earlier un-deployed candidate incorrectly treated a clipboard sequence change
during Windows format synthesis as supersession. The previous clipboard failed
to restore in that one native test. This was disclosed to Peter. Ownership now
uses the owner, unique marker and exact payload; the sequence alone cannot
discard the backup. Files, recordings and protected transcripts were unaffected.

## Application acceptance and limits

Peter chose to test Outlook insertion manually after updating. Automated Outlook
control permission timed out; no alternate external-window inspection or input
route was used. Outlook/WebView and Codex compatibility remain unverified by
these native WPF checks. Unsupported, elevated, protected, manually edited or
unavailable destinations require an explicit destination decision.

Clipboard recovery is in memory. A crash or forced termination cannot preserve
that backup. There is no generic Windows barrier proving a deferred application's
paste has consumed the clipboard; uncertain delivery must be inspected before
retry. Durable transcript recovery does not make external paste transactional.

The existing production API on 8123, TypeWhisper, original model weights and
protected user history remain outside the update. Future candidate restarts must
use the updated checkout to retain its changed generation policy.
