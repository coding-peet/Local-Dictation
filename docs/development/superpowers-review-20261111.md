# Superpowers code review and regression testing

Date: 11 November 2026

Scope: the native Windows worktree at
`C:\Git\Local-Dictation\repo\.worktrees\native-windows`, including current
uncommitted and untracked implementation. Base HEAD:
`7498ace2bd61ed7cc4d5e81e43ba5742ebb368b3`.

Three independent review passes covered controller/session orchestration,
native delivery and WPF interaction, and protected storage/backend/packaging.
Confirmed findings were reproduced before correction. The working branch was
preserved; no commit, push, installer deployment, or production API change was
part of this audit.

## Corrected findings

1. A detached History snapshot could replace a newer pending manual draft.
   Resolve open/save/accept operations against the current document by ID.

2. An insertion receipt saved in History could be ignored after a failed
   pending receipt write and restart. Recover durable receipts from either
   store, and prevent stale snapshots from clearing them.

3. Exit could shut down before asynchronous recording cleanup saved audio.
   Retain recording tasks, await cleanup before shutdown, and recheck insertion
   state after the exit confirmation dialog.

4. Focus restoration happened before release of Enter, Space, or modifiers.
   Keep focus at the Review surface until those keys are released; timeout,
   cancellation, or changed ownership must leave the destination untouched.

5. Held Enter could accept successive unseen phrase alternatives.
   Consume the key-down sequence and commit one choice on key-up. Losing
   focus cancels the unfinished gesture.

6. Unapplied compact custom wording was lost when changing phrases or closing
   the banner. Persist the exact custom draft without resolving the phrase.

7. An invalid decrypted pending record could prevent application startup.
   Recover valid items, report invalid filenames, and preserve their bytes.
   Keep errors reading the entire directory visible.

8. Reconnect mutated active settings before validating the endpoint.
   Validate a separate settings snapshot so a rejected value leaves the active
   and persisted configuration unchanged.

The MSI contract fixture was also updated to include required `Help.html`,
with a check that a missing Help file is rejected. These tests build only a
temporary synthetic package; they do not install it and retain no installer.
The documented backend test environment now names the candidate environment,
which contains the session API dependencies. The earlier live-visibility guide
was aligned with the compact Review flow.

## Evidence and limits

Baseline: 80 Core tests, 86 backend tests, and 11 controller scenarios passed.
The original packaging suite failed because its synthetic payload omitted
`Help.html`; the corrected suite passes all four cases.

Red/green evidence is kept outside the checkout under
`C:\AI\build\Local-Dictation\review-controller`, `review-storage`, and
`review-input`. All regression transcripts and PCM data are synthetic; protected
storage tests use isolated temporary directories.

The shutdown check exercises actual controller orchestration with delayed
synthetic cleanup and real protected PCM storage. It does not prove physical
microphone shutdown or every WPF process-exit path. Keyboard tests exercise
actual WPF routed handlers and the production release gate; they do not prove
physical held-key behavior in every destination application.

Native destination compatibility in Codex still requires Peter's manual
acceptance test. Crash-proof exactly-once insertion cannot be claimed when
every receipt write fails or the process dies between native insertion and
receipt persistence.

Backend tests run without model inference. Original model weights, precision,
production listener, and TypeWhisper configuration were preserved.
