# Personal dictation tools: implementation and validation

Implementation validation: 2026-11-14
Deployment: 2026-11-15

## Result and rollout status

Packaged and deployed on 2026-11-15 after Peter explicitly approved local packaging,
client replacement and controlled restart of only the session API on 48125.
The client had exited normally before any files were replaced. No force-stop of
the client, commit, push, merge or external release occurred.

Installed version:

    0.1.0-personal-tools.20261115.1+working.b7c4cd6.b7c4cd62d72054e63c36b3c2f585f616b8752064

Installed application and local candidate:

    C:\Git\Local-Dictation\app
    C:\Git\Local-Dictation\personal-tools-candidate-20261114

All 17 candidate files match the installed payload. All 17 previous application
files were backed up and hash-verified before replacement:

    C:\AI\build\Local-Dictation-personal-tools\previous-app-20261115-233142Z

The original server source at b7c4cd6 was archived and extracted for rollback:

    C:\AI\build\Local-Dictation-personal-tools\previous-api-source-b7c4cd6\server

Fresh process/readiness verification after startup:

- Client PID 51436, installed path, expected version, no new client-error log bytes.
- Session API 48125: old PID 4364 replaced by PID 40612 from the canonical checkout;
  ready=true, busy=false, audio_normalization=true and vocabulary_prompt=true.
- Batch/TypeWhisper API 8123: original PID 22352 unchanged, ready=true, busy=false.
- Runtime remains original FP16 with the fast CPU cache; no recognition model is
  loaded on the GPU after preparation. Warm-up reported 32.575 seconds.

The live normalization endpoint passed a synthetic stereo 48 kHz WAV -> mono
16 kHz PCM16 check (160 output frames). It ran no ASR inference, microphone,
playback or native input. This check created no History item.

After client startup, all 107 protected user files were byte-identical with no
added/deleted data files. All 148 watched private source/corpus/reference files
were hash-identical. Size and modification time of all 72 model files were
unchanged. This is a metadata preservation check for model files, not a new
full-weight hash qualification.

At deployment, the working branch was feature/native-windows in
C:\Git\Local-Dictation\repo, based on
b7c4cd62d72054e63c36b3c2f585f616b8752064 with uncommitted implementation changes.
Peter subsequently requested committing and pushing this complete change set to
feature/native-windows on 2026-11-15. Git records the resulting source commit; the
installed executable retains its original working-tree build stamp.
No temporary synthetic API remains running on 48226.

## Implemented behavior

- History-local full-content search, inclusive local date/model/status filters,
  matching/total count, and selection restoration. Search/Filters/Import share
  one compact idle row. No disk index or audio playback follows filtering.
- Serial multi-file import with settings/source snapshots, explicit Start,
  per-file phases and Cancel. Source files remain unchanged. Results and partial
  text use protected History/Review with explicit insertion. No destination,
  clipboard, native activation, microphone or per-file completion popup.
- Bounded in-memory WAV/MP3/FLAC/M4A/Vorbis/Opus normalization. No codec install,
  download or disk spool. Client/session limits and accepted sample coverage
  prevent silent truncation; exact-limit complete audio is accepted.
- One-shot private microphone recording, propagated to language retry. Every
  persistent Review/History/pending store refuses private documents; copies
  retain the marker. Private Review stays in memory with explicit discard/Exit
  warnings. Import cannot consume private-next.
- Protected personal dictionary editor, deterministic Unicode exact-correction
  implementation, separate streaming hint protocol plumbing and evaluation
  metrics. Unsaved editor text survives Settings close/reopen after validation,
  busy or storage errors. Exit requires explicit discard of an unsaved draft.

## Explicit quality gates

Dictionary hints/corrections remain off. Technical prompt forwarding tests are
not actual tokenizer/model short/long qualification or proof of acoustic benefit.
No speech/noise filtering change, phrase blacklist or voice-formatting layer was
enabled. Automatic incognito has no verified browser bridge and remains unavailable.

The private corpus inventory links 29 existing source entries in 25 PCM/encoded
groups; current protected History has 14 entries representing 10 unique PCM
recordings. Exact keyboard/blowing/children/office/household labels, independent
verbatim references and three clean holdouts remain unfilled. Historical model
output is not human gold; old corrected wording retains its stated limitations.
No existing recording was played or transcribed in this implementation run.

## Verification evidence

| Check | Result | Practical boundary |
| --- | --- | --- |
| Full Core suite | 565 passed, 0 failed | Includes real current-user protection with synthetic fixtures |
| Full backend suite | 185 passed, 0 failed | Fake engines; no actual model/GPU inference |
| Full detached WPF/controller suite | 180 passed | No native window handles, input or audio devices |
| Dictionary draft focused check | Passed again after adding its render | Explicitly injected fresh build-only store |
| Actual HTTP/WebSocket import integration | Passed, including lower 1-second API cap | Synthetic engine/catalog on separate 48226 only |
| Codec fixture execution | WAV, MP3, FLAC, M4A, Vorbis, Opus passed | Synthetic encoded audio via installed PyAV |
| Release build and local publish | 0 warnings, 0 errors | Installed payload verified separately on 2026-11-15 |
| Offline Help links | 26 unique IDs, 15 valid internal links | No network Help dependency |
| Original source/data preservation | 148/148 watched files hash-identical | Existing private manifests/data/audio/reference files |

Test outputs:

    C:\AI\build\Local-Dictation-personal-tools\backend-final.log
    C:\AI\build\Local-Dictation-personal-tools\renders-final
    C:\AI\build\Local-Dictation-personal-tools\renders-dictionary-final
    C:\AI\build\Local-Dictation-personal-tools\integration-02
    C:\AI\build\Local-Dictation-personal-tools\validation-manifest.json
    C:\AI\build\Local-Dictation-personal-tools\deployment-20261115.private.json

Visual inspection completed for wide/narrow History in Light/Dark, private
Dictation controls, and the actual dictionary editor composition. This is
windowless visual verification. Microphone, physical playback, native keyboard,
Windows high contrast integration and Codex/Outlook insertion acceptance were
not exercised. Source compilation and fake-model transport do not prove them.

Both synthetic 48226 fixture runs were stopped through their own normal stop
endpoint; no background fixture is intentionally left running. The original
APIs remained in place during implementation; the later explicitly approved
48125 handover is recorded above. API 8123 was preserved throughout.

## Independent review and corrections

The fresh read-only reviewer found no Critical issues and two Important issues.
Both were reproduced before correction:

1. Shorter server duration caps could silently truncate native WAV imports.
   Added health/session cap checks before sending, a start handshake, and accepted
   sample coverage handling. The 1-second synthetic API rejects a 2-second file
   without truncating its preserved History copy; exactly one second succeeds.
2. Closing Settings could discard unsaved dictionary text. Drafts now remain on
   the controller through close/reopen and invalid/busy/storage failures; normal
   Exit asks before discarding. The isolated storage-failure regression passes.

Visual checks also reproduced and fixed Dark filter contrast and narrow search
control overlap. Layout checks account explicitly for one new 48-DIP row while
retaining an absolute useful transcript area and the prior combined content budget.

Deferred minors from review:

- More specific safe per-file messages for changed/oversize/unsupported sources;
  the current generic message preserves originals and earlier results.
- A selector for several private in-memory transcripts; only the latest is
  directly accessible, so newer ones must be handled before reaching older ones.

## Safety and authorization record

An initial draft-regression command was automatically rejected because it appeared
to target default app settings. It did not execute. The approved replacement
injects a new store under a validated build root, checks ownership, and removes
only its own verified empty failure placeholder. Real settings were untouched.

The later local dotnet publish rejection was resolved by Peter's explicit
2026-11-15 approval for packaging/deployment and controlled 48125 restart. The
approved operation then succeeded; no alternate route or rename bypassed review.

## Remaining acceptance

Deployment is complete. Dictionary hints/corrections remain off pending actual
runtime/tokenizer qualification and paired labelled acoustic evaluation. Voice
formatting, additional noise protection and automatic incognito remain at their
stated design gates. Native Codex/Outlook insertion, microphone capture, physical
playback and Windows high contrast integration were not tested in this quiet run.

Keep the deployment backups. Source integration is explicitly authorized for
feature/native-windows; it does not require another application deployment. Do not
delete the preservation worktree or restart 8123 as cleanup. The client runs normally; no diagnostic no-hotkey mode was left enabled.
