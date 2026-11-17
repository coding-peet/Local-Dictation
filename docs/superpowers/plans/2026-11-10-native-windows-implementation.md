# Native Windows implementation

> Execute inline using superpowers:executing-plans. The user approved the
> product design and explicitly requested implementation. Do not reopen discovery.

**Goal:** Publish a native WPF dictation client with a local duplex ASR API,
safe cursor delivery, generic optional verification, and persistent pending Review.

**Spec:** ../specs/2026-11-10-live-dictation-design.md and
../specs/2026-11-10-interactive-verification-review.md.

**Global constraints:** English UI/source/docs. Original FP16 weights.
No cloud audio, no model conversion, no automatic submit. Production port 8123
and TypeWhisper remain untouched during candidate work. Candidate port 48125.
All generated output outside source. Audio RAM-only, maximum 600 seconds.
Only pending reviews persist, protected with Windows current-user encryption.
Codex insertion acceptance is manual.

## Task 1: Runtime paths and immutable comparison

Files: server/local_dictation/config.py, comparison.py, server/test_comparison.py,
server/test_runtime_config.py, server/asr.py.

Interfaces: RuntimeConfig defines external model/data roots and limits.
compare(primary, verification) returns ordered immutable pieces with stable IDs,
primary/verification alternatives, exact whitespace and Unicode preserved.

1. Write tests for external paths, invalid limits and insert/delete/Unicode diffs.
2. Run them and confirm missing implementations fail.
3. Implement path configuration and deterministic token comparison.
4. Run all server tests, then commit the reviewed paths.

Expected: original 45 tests and all new tests pass.

## Task 2: Duplex sessions and shared GPU ownership

Files: server/local_dictation/sessions.py, application.py, engine_adapter.py,
server/api_main.py, server/test_sessions.py, server/test_application.py,
server/requirements-api.txt.

Interfaces: WebSocket /v1/audio/sessions, PCM16 mono 16kHz, 64KiB frame limit,
protocol_version 1. Events include session.started, transcript.updated,
transcript.final, verification.started/completed/failed/cancelled, session.completed.
Snapshots carry session_id and increasing revision. One admission gate for live,
batch and model tests. Backend owns stable/draft text and bounded audio.
Full audio retained only for batch primary or verification. Cancel/disconnect
releases audio and model ownership after cleanup, never before.

1. Write asynchronous fake-engine tests for revision order, paused/final text,
limits, disconnect, cancellation, one-owner admission and verification failure.
2. Observe RED, implement state machine and loopback Host/Origin guards.
3. Keep legacy HTTP format contracts and add cooperative model cancellation.
4. Run complete Python suite; commit.

Expected: deterministic lifecycle tests pass without GPU; health stays responsive.

## Task 3: Registry, public model import and verification choices

Files: server/local_dictation/catalog.py, downloads.py, server/test_catalog.py.

Interfaces: backend-owned catalog exposes capabilities and distinct installation,
runtime-test and quality status. Public HF repository inspection pins a commit,
checks built-in Whisper/safetensors compatibility and download size. Explicit
download jobs support progress/cancel, staging and atomic publication.
A test transcription shares the GPU gate. Verification model is any eligible
installed registry ID distinct from primary; no verifier-specific workflow code.

1. Write tests for compatibility rejection, pinned revisions, path safety,
cancelled staged downloads, unknown/same verifier and dynamic eligible models.
2. Observe RED, implement API endpoints and engine registry integration.
3. Run suite and controlled public tiny-model lifecycle test; commit.

Expected: failed/cancelled imports cannot alter an installed model.

## Task 4: Native core and protected Review

Files: Directory.Build.props, src/LocalDictation.Core/*,
tests/LocalDictation.Core.Tests/*.

Interfaces: protocol DTOs use snake_case; immutable Review pieces are selected by
ID. Unresolved choices start as primary, bulk keep respects custom choices, undo
works, manual final draft is separate. Settings JSON and current-user DPAPI Review
use atomic replacement outside source. Copy/close never deletes Review.
Hotkey state recognizes modifier-only Ctrl+Shift and ignores other chords.

1. Write domain, chord, protocol and protected storage tests; observe RED.
2. Implement minimal core, run tests, commit.

Expected: exact text reconstruction including empty choices; restart recovery.

## Task 5: Native shell, banner and interactive Review

Files: src/LocalDictation.App/*.

Interfaces: WPF Windows executable, tray, settings/model management, bottom
non-activating banner. English controls. Quiet Optotune blue/neutral profile,
Windows conventions, explicit focus/selection/errors. Review is a native window
with working text, source alternatives, custom edits, undo and copy.
Verification checkbox and model selectors use registry capabilities.

1. Build on core with real UI states rather than simulated progress.
2. Compile and launch --no-hotkey; inspect actual native windows at useful sizes.
3. Fix layout, keyboard focus and copy behavior; commit.

Expected: no browser dependency; native Review opens without recording.

## Task 6: Capture, cursor ownership and end-to-end controller

Files: src/LocalDictation.App/Services/*, native integration tests.

Interfaces: selected microphone stable device ID, 16k mono PCM, real meter.
Bounded capture startup/transport queues. Ctrl+Shift toggle, no collision with
running TypeWhisper. Observe user input/focus changes and invalidate cursor
ownership. Only replace the app-owned draft range after UIA full-text,
selection and identity checks. Never reactivate an old target or issue Enter.
Unsupported targets fall back to persistent Review.

1. Test guard and transport failure paths before implementation.
2. Integrate microphone, session events and guarded Unicode insertion.
3. Verify own controlled native text field; leave Codex test to Peter.
4. Run complete .NET/Python suites; commit.

Expected: edits/focus changes preserve transcript and stop delivery.

## Task 7: Publish, measure and review

Files: scripts/*, README.md, docs/development/*.

Interfaces: published C:\AI\LocalDictation\app\LocalDictation.exe,
data C:\AI\LocalDictation\data, build C:\AI\build\Local-Dictation.
Portable SDK/runtime dependencies documented. Candidate backend uses original
shared model root and separate environment/listener.

1. Add reproducible build/start/replay scripts; publish outside checkout.
2. Replay the existing private Slovak sample at real-time pace; measure actual
draft/final latency, text quality, queue bounds and RAM/VRAM release separately.
3. Run native UI and safety checks without controlling Codex or changing TypeWhisper.
4. Request one fresh-context whole-branch review using requesting-code-review.
5. Fix critical/important findings with regression tests and run required suites.
6. Report exact completed checks and outstanding manual acceptance.
7. Commit/push feature branch non-force; verify remote HEAD and clean source tree.

Expected: runnable native EXE and honest measured limits; no generated/private data in Git.
