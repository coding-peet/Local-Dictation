# Import errors and private transcript selection

Date: 2026-11-17
Status: implementation authorized by Peter's request to complete items 1 and 2.
Baseline: 586ed67d4e0e1947c70c01d55c708b456e42c098, feature/native-windows.

## Scope and constraints

Work in C:\Git\Local-Dictation\repo, without additional worktrees.
Use synthetic files, text and HTTP handlers. No recording, playback, speech
inference, native insertion tests or changes/restarts to either running API.
Keep generated fixtures and builds outside Git. Preserve original inputs and
protected app data. The postponed acoustic evaluation and dictionary gates remain.

## Task 1: Actionable per-file import failures

Files: Core/AudioImportErrors.cs (new), AudioImportSource.cs, AudioImportQueue.cs,
ApiClient.cs; App/AppController.Imports.cs; Core import/source/normalization tests.

- [x] Add failing tests for empty/oversize/changed/missing/unreadable sources,
  unsupported or damaged audio, API failures/timeouts and failed result saving.
- [x] Define curated preparation/transcription/saving messages. Never expose raw
  exception paths, protocol bodies or transcript content as a failure message.
- [x] Preserve source-capture failures per path instead of collapsing them into
  a generic invalid-source set. Keep serial processing, cancellation ownership,
  partial-text recovery, earlier successes and buffer clearing unchanged.
- [x] Map normalization HTTP failures to safe messages; read only a bounded
  error envelope when recognizing existing fixed duration/no-audio messages.
- [x] Verify failures independently, combined transcription/save failures and
  continuation/cancellation using fake delegates and in-process HTTP handlers.

## Task 2: Select a pending private transcript

Files: AppController.Privacy.cs; Views/PrivateTranscriptPicker.cs (new),
SettingsWindow.Privacy.cs; detached PrivateFlow/selector checks.

- [x] Add failing controller/UI checks with several synthetic private documents.
- [x] Expose immutable ID/date/short-preview choices for the existing pending
  private set (nonempty, not already inserted), newest first.
- [x] Add a compact selector and explicit Open action in the privacy card.
  One item needs no visible selector. No selection action opens Review, captures
  a destination, plays audio or writes settings. Preserve selection on refresh;
  fall back safely after removal. Disable opening during busy/apply operations.
- [x] Resolve the selected ID only against current private memory. Keep the
  existing Review and explicit discard/Exit policy; do not add a persisted list.
- [x] Render Light/Dark at narrow/wide sizes and verify memory-only behavior,
  selected-ID callbacks, edits, removal and busy states without native windows.

## Completion

- [x] Update current requirements and offline Help for the resulting behavior.
- [x] Run relevant Core tests and the full detached WPF suite.
- [x] Request an independent read-only review and resolve important findings.
- [x] Package and deploy the client after verified normal Exit if needed;
  preserve API listener identities, user data and model files.
- [x] Report test evidence and any remaining native/acoustic acceptance limits.
  Commit/push only upon a separate user instruction.
