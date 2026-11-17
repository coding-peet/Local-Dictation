# Direct History Insert verification

Status: client deployed; native target-application paste acceptance remains open.
Branch: codex/review-window-resize, base af589a0 with local changes.

## Result

History More actions includes Insert at cursor. Highlighted text is copied into
an independent memory-only insertion attempt; no highlight selects the whole
transcript. The menu closes and the last external application is restored once.
Its current field/caret is freshly validated. No placement window is created,
and the source transcript/choices/insertion receipt remain unchanged.

Foreground tracking reads only window/process/class metadata. It ignores our
windows and shell taskbar/desktop surfaces. Command-time cancellation is reserved
before menu closure and linked through restoration, scoped capture and Apply.
Native work and receipt carriers survive caller timeouts. Pending acknowledgements
block replay; terminal abandoned copies expire after two minutes.

## Validation

- 648 Core tests passed (including 8 foreground-memory policy cases).
- 244 detached WPF state/layout checks passed (including 18 direct History checks).
- 185 synthetic backend unit tests passed.
- 18 build/MSI contract tests passed; these do not qualify the obsolete MSI pilot.
- Build: zero warnings/errors. Release candidate: 17 files, Help identical to source.
- Help: 26 unique IDs, 15 valid internal anchors.
- Independent read-only review findings were resolved and regression tested.

The History regressions include source immutability, exact highlight/full text,
already-inserted source reuse, concurrent operation/recording gates, menu-defer
cancellation, worker-entry cancellation before reading a field, expected and
unexpected focus, Apply cancellation, late native receipts, pending acknowledgement
expiry protection, and provider timeout followed by a late receipt. Test targets
and text are synthetic; no native paste, microphone or audio output was used.

Native insertion in Peter's chosen application remains manual acceptance.
No API process was restarted and no model inference was run.

## Candidate

C:\AI\build\Local-Dictation-history-direct-insert-20261118\client-candidate

Version: 0.1.0-historyinsert.20261118.1+working.af589a0.
Keep private hashes and deployment backups outside Git under the same build root.

The user subsequently requested comment, commit and push on 2026-11-18.
Publication includes this implementation, its diagnostics, Help and the project
interaction priorities. Merge remains outside scope. Local UI skills were
clarified separately; they are not build inputs or files in this repository.


## Client deployment

No LocalDictation process was running before exchange. A verified backup of all
17 prior client files and all 142 data files was created before replacing the
client. The new client runs from C:\Git\Local-Dictation\app, PID 36688, title
Local Dictation, responsive and with no new client errors.

All 17 installed payload files match the candidate manifest. All 142 data files
remained hash-identical after startup. Start with Windows is unchanged. API PIDs
55220 (8123) and 56912 (48125) retain their original start-time ticks.

Private backup, before/after state and hashes:
C:\AI\build\Local-Dictation-history-direct-insert-20261118\deployment-1

The temporary client-candidate payload was removed only after installed hashes,
process path and preserved data were verified. The manifest, build output and
rollback backups remain. No force stop, API restart or native paste was used.
