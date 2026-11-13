# Review transcription and saving

User requirement: diagnose the matching Band 8–43 history item; replace the
separate second-model popup with an inline model choice and confirmation;
provide a clear whole-alternative action and save edited text independently of
insertion. Wording: Transcribe again, Model, Transcribe, Use this transcript,
Save text. Implement directly as requested; no additional approval round.

## Constraints

Work in C:\Git\Local-Dictation\repo, feature/native-windows. Preserve the existing
uncommitted clipboard-return patch and private history/model/runtime data. No
desktop control, microphone, playback, inference or service restart for checks.
Preserve original model transcripts/provenance and archive overridden manual
drafts. Saving or selecting a whole transcript must never insert elsewhere.

## Tasks

- [x] Locate matching protected history and distinguish ASR output, alternatives
  and manual drafts. Do not export decrypted user data or modify originals.
- [x] Add failing Core/windowless checks for whole-alternative selection and
  save-without-insertion, including persisted edits and prior manual drafts.
- [x] Implement whole-transcript selection and explicit save; keep Insert
  separate, show saved/failed status honestly and preserve raw provenance.
- [x] Replace VerificationWindow with an embedded model picker in both Review
  surfaces; keep the compact Review open while retranscription runs.
- [x] Update requirements/Help and run all Core tests, complete windowless
  checks, Release publish and independent review. Inspect offline renders.
- [x] Prepare updated candidate; installation still requires normal Exit of the
  running old client. No commit/push or native acceptance is implied.

## Evidence and rulings

- User's saved manual edits may already exist even when the UI made saving
  unclear. Verify storage before changing that behavior.
- The requested phrase list is an ASR-quality incident, independent of paste.
  Diagnose the actual matching record before proposing model tuning.
