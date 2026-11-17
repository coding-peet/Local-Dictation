# Personal Dictation Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add searchable History and serial audio import, then protected personal-tool settings and explicit private microphone recording within the approved quality gates.

**Architecture:** Core owns pure search, import orchestration and transformations. WPF adapts those services without destination ownership for imports. A bounded in-memory API normalizer feeds the existing session transport; privacy policy is attached before recording and prevents every persistence route.

**Tech Stack:** .NET 10/WPF, existing MaterialDesignThemes 5.4.0 and NAudio 3.3.0, Python/Starlette/PyAV and the unchanged original FP16 engines.

**Spec:** ../specs/2026-11-14-history-import-and-personal-tools-design.md (approved by Peter in this chat on 2026-11-14).

## Global Constraints

- Work only in C:\Git\Local-Dictation\repo on feature/native-windows; no new worktrees.
- Keep generated artifacts under C:\AI\build\Local-Dictation-personal-tools and private corpus outside Git.
- Preserve both existing APIs and models; do not restart either as setup.
- No native input, microphone, playback or software installs. Commit/push required
  separate instruction, received from Peter on 2026-11-15 for feature/native-windows.
- Imports never capture a destination, insert, activate another app or open per-item Review.
- Encoded request <=26 MiB including multipart; decoded audio <=600 seconds, mono 16 kHz PCM16, no truncation.
- New dictionary behavior remains off until measured; formatting/protection changes require labelled comparisons.
- Private-next applies only to microphone recording; imports cannot consume it.

## Review Focus

- A query match past the 80-character preview must remain findable, including unsaved manual variants.
- Equal audio under different filenames and a changed source during import must not overwrite prior results.
- Cancellation during decoding or inference must join ownership before another job/recording starts.
- Storage failure must retain text in existing unsaved recovery and never lose completed batch items.
- Private text must not reach History, pending files, retries, export or automatic Exit persistence.

## Task 1: Full-content History query and controls

Files: create src/LocalDictation.Core/DictationSearch.cs and tests/LocalDictation.Core.Tests/DictationSearchTests.cs; modify Views/HistoryPanel.cs; add detached HistorySearchChecks.

Interfaces: HistoryQuery(Text, From, To, Model, NeedsReview, TextOnly, Inserted); DictationSearch.Find(IEnumerable<DictationItem>, HistoryQuery) -> IReadOnlyList<DictationItem>.

- [x] Write tests for normalized all-term full-content matching, archived/manual variants, local inclusive date range, primary/verifier model and AND filters without mutations.
- [x] Run the focused Core tests and observe missing implementation failure.
- [x] Implement the pure query and History-local controls. Preserve selection identity across filtering; no-match clears detail/player and clearing restores selection.
- [x] Run focused tests and detached search states in Light/Dark at wide/narrow sizes.

## Task 2: Bounded in-memory audio normalization

Files: create server/local_dictation/audio_normalization.py and server/test_audio_normalization.py; modify application.py, ApiClient.cs; add client normalization tests.

Interfaces: normalize_audio(data: bytes, cancelled: Event, max_seconds=600) -> WAV bytes; POST /v1/audio/normalizations returns audio/wav; ApiClient.NormalizeRecording(byte[], CancellationToken) -> byte[].

- [x] Test 16 kHz WAV preservation, stereo/rate conversion, empty/corrupt/nonfinite/overlong input, cancellation and admission/loopback behavior with synthetic audio.
- [x] Observe failing tests, implement incremental PyAV limits and serial ownership; never use disk or audio devices.
- [x] Run focused tests including encoded codec fixtures supported by installed PyAV, and Core response-bound/cancellation checks.

## Task 3: Serial import queue, session transcription and History UI

Files: create Core/AudioImportQueue.cs, Core/ImportedTranscript.cs, Core/AudioImportSource.cs; create App/AppController.Imports.cs; modify AppController.cs, HistoryPanel.cs and ReviewDocument.cs; tests AudioImportQueueTests, ImportedTranscriptTests, AudioImportSourceTests and detached import checks.

Interfaces: AudioImportQueue runs immutable per-batch settings with delegates for prepare/transcribe/save; snapshot rows contain phase, filename and error; AppController.StartAudioImport(paths) -> error-or-null, CancelAudioImport(), ImportItems/ImportSummary/IsImporting. ImportedTranscript handles existing session events without native input.

- [x] Test serial order, cancel remaining, partial-text save, per-item errors, completed persistence, source changes and exact no-destination operation.
- [x] Observe RED, implement queue and dedicated transport adapter with idle drain. Source provenance stores basename/hash/duration protected; input is unchanged.
- [x] Add multi-file picker and explicit Start summary with model/language/verification, phases, count and Cancel. No automatic popup or insertion.
- [x] Run focused Core, backend and detached controller/UI checks.

## Task 4: Explicit private microphone policy

Files: Core/ReviewDocument.cs, AppController.cs plus focused AppController.Privacy.cs and SettingsWindow control; Core/PrivateDictationPolicy.cs and tests; detached privacy-storage checks.

Interfaces: private policy snapshot on ReviewDocument (nonserialized IsPrivate); controller one-shot PrivateNextRecording and explicit in-memory discard flow. SaveReview/history/export/replay/Exit all consult attached policy.

- [x] Test private snapshot, no serialization/persistence on success/error/replay, ordinary behavior unchanged and imports cannot consume private-next.
- [x] Observe RED; implement centralized guards, explicit memory-only Review/dismiss/Exit warning and clear private indicator before capture.
- [x] Run focused and detached real protected-storage checks without capture/input/playback.

## Task 5: Personal dictionary preparation and evaluation gates

Files: focused Core/PersonalDictionary.cs, protected personal settings storage, tests and settings editor; backend hint capability/wiring only with tested protocol compatibility; diagnostic metric tests.

Interfaces: explicit hint list and exact correction pairs with validated single-pass Unicode boundaries, longest match, no recursion. Original transcripts remain separate. Per-operation immutable dictionary snapshot. Qualification flag remains false until real runtime short/long checks and paired labelled evaluation.

- [x] Test conflicts, Unicode boundaries/diacritics, repeated occurrences, absent-term insertion metrics and empty-reference metrics before implementation.
- [x] Implement separate settings/editors and derived final text machinery without rewriting manual edits/history/inserted text.
- [ ] Confirm actual prompt path for batch vs streaming in installed source/runtime; keep unavailable/unevaluated controls honestly gated.
- [x] Record missing exact audio labels/verbatim references/clean holdouts; do not invent an acoustic score or add a formatting layer without evidence.

## Task 6: Product documentation, validation and final review

- [x] Update Help and requirements for actual implemented behavior, limits and quality gates.
- [x] Run full Core/backend suites, Release build and detached WPF/controller checks; inspect representative renders.
- [x] Fresh reviewer checks full uncommitted change against spec/plan and failure cases; fix important findings with RED/GREEN regression evidence.
- [x] Record source/candidate state and remaining native/audio/quality checks. Deploy only after verified candidate and normal client Exit, preserving APIs and all private data.

## Execution record

Peter explicitly approved implementation of the concrete design. Native execution continues without another generic plan/execution-method approval. The initial implementation excluded commits/pushes. Peter separately authorized
committing and pushing the complete change set to feature/native-windows on
2026-11-15. The local ledger carries task evidence across context compaction.


## Deployment completion (15 November 2026)

Peter explicitly approved the local package, client replacement and controlled
restart of only session API 48125. The previous client had exited normally.
Installed 17 files match the approved package; backup17 files verified. Client
PID51436, new session PID40612 ready/idle, original batch PID22352 preserved.
Protected data107 files and watched private source148 files unchanged; model72
file sizes/timestamps unchanged. See development/personal-tools-20261114.md for
full evidence and remaining quality/native-acceptance boundaries.


## Source integration (15 November 2026)

Peter requested a commit and normal push of all implementation, tests, Help and
project documentation to the existing feature/native-windows branch. Preserve
private corpus/build/runtime files outside Git. Verify intended staged paths,
whitespace, test evidence, remote HEAD and clean status. Do not force-push,
merge, redeploy or restart services as part of source integration.
