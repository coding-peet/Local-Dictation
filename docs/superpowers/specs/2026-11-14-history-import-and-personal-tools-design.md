# History import and personal dictation tools

Date: 2026-11-14
Status: approved by Peter on 2026-11-14, including implementation. An independent
builder check is complete; its import/privacy scope issue is resolved below.
Actual implementation/verification progress is recorded in the matching plan.

## Intent and authority

Peter approved personal dictionary, History search, file/batch transcription,
private dictation and optional voice formatting. Silence/noise changes and the
dictionary require measured benefit/regressions first. The agreed evaluation
shape and existing-recording addition are recorded in the product requirements
and ../../development/personal-asr-test-protocol-20261114.md.

Preserve local processing, original FP16 models, protected text/audio, explicit
insertion and the existing compact WPF Material UI. Do not revisit the rejected
focus-capture change, change the waveform icon or add a dashboard redesign.

## Existing implementation evidence

- DictationLibrary.Merge combines protected History and pending Review. OutputText
  can differ from raw Primary/Verification. Current previews are only 80 characters.
- HistoryPanel refresh preserves a selected ID but has no content/filter query.
- HistoryStore stores mono 16 kHz PCM16 as a protected WAV and protected document;
  audio expiry does not delete text. Review requires explicit insertion flags.
- Saved-recording replay can stream existing PCM without starting capture, but
  ordinary completion includes banner/focus/delivery policy. A batch importer
  must not accidentally inherit cursor ownership or repeated Review popups.
- The HTTP transcription route accepts one bounded in-memory upload (26 MiB),
  returns text/metadata and uses serial model admission. It does not expose the
  normalized PCM required by History/replay. Client HTTP cancellation alone is
  not an established cancellation acknowledgement from that server worker.
- PyAV decodes and resamples server audio to mono 16 kHz in memory. The current
  general decoder is not yet a bounded, cancellable import normalizer.
- Batch recognition forwards prompt -> get_prompt_ids (180-token limit) ->
  generate(prompt_ids). Streaming EngineAdapter.transcribe_pcm passes an empty
  prompt. A functioning batch parameter is not live-dictionary qualification.
- Operational logs omit transcript/audio by policy. ReviewStore and HistoryStore
  nevertheless persist normal runs; private mode needs policy at every such path.

## Approaches

The recommended first stage extends the existing client History and adds a serial
client import queue, reusing the session protocol and protected stores. Extract a
prerecorded-audio operation with no destination ownership or completion popups;
reuse the same model/language/verification provenance. Add only the bounded
in-memory decode capability necessary for supported compressed files.

A second approach is a new server-side durable batch job system. It would need a
second job store, recovery/retention rules, polling and cancellation ownership,
then conversion into client History. This duplicates lifecycle and privacy work
for a local desktop queue and is not recommended for the first stage.

A WAV-only importer is a smaller fallback, but must be explicitly advertised as
limited. It must not claim that MP3/M4A/FLAC/OGG support is complete merely because
the underlying decoder exists. The recommended target includes those formats
when their decode fixtures pass in the installed candidate environment.

This is a staged implementation of the approved scope. Search/import can proceed
independently of acoustic references. Dictionary/protection/formatting adoption
remains gated by actual comparisons. Privacy needs its own end-to-end storage
verification before exposure to real private content.

## Stage A: History search

Add a compact Search dictations field inside History, alongside Import audio....
It is distinct from the existing global settings search. Preserve palette,
list/detail sizing, keyboard focus and semantic selection colors.

Search the complete current output, raw primary/verification text and preserved
manual variants; do not search only the truncated list preview. Match all query
terms without case or accent sensitivity, retaining exact stored text.

Expose From/To local calendar dates, an exact model selector and independent
Needs review / Text only / Inserted filters. Filters combine with AND. The model
selector matches either primary or verification participation; offer IDs from
stored documents too, including models no longer installed. Invalid date input
shows a local error and retains the last valid query. No storage changes follow
search/filter actions. Unsaved edits update query membership after controller
refresh rather than resetting controls.

Preserve selection while it remains in results. A filtered-out selection clears
its detail/player safely; clearing filters restores the previous identity when
available. Keep the count as matching/total, and distinguish empty collection
from no matches. Filtering and selection never play audio, insert or delete text.
Search remains in memory; no plaintext disk index.

## Stage B: File and batch transcription

History -> Import audio... selects multiple local audio files. Use the existing
model/language/verification choices as an immutable snapshot for that batch.
Before starting, show the chosen model/language/verification in a compact summary;
ordinary completion never changes saved dictation preferences.

Queue states: queued, preparing, transcribing, saving, completed, cancelled,
failed. Run at most one item at a time; no parallel GPU jobs. Show file identity,
position/completed count and phase. Do not invent time-based percentages while
the recognizer has none. Cancel current/remaining jobs and await server release
before allowing a conflicting recording, verification or model removal.

Decode without opening input/output devices. Bound encoded upload at the current
26 MiB transport limit and decoded duration at 600 seconds; reject over-limit
files without truncating. Require an incremental decoder limit/cancellation check
rather than decoding an unbounded file then checking. Preserve complete original
input files and capture their hash/size before processing; refuse a changed file.
Normalize only the app-owned History copy. Advertise WAV/MP3/FLAC/M4A/OGG/Opus only
with passing local decode fixtures; unsupported codecs receive a per-file error.
No automatic downloads or codec installations.

For compressed input, a bounded normalization endpoint may return PCM16 WAV using
in-memory request/response buffers, with the existing native-loopback guard and
serialized admission. It must not persist audio or log filenames/content. Then
use the existing session transport in live=false mode for real cancellation,
primary/refinement/optional verification and pinned provenance. The normalization
worker checks its cancellation/decode cap before allocating an unbounded array.

Use a dedicated no-destination operation. No foreground/caret capture, target
stamp, clipboard call, native activation, automatic Insert or per-file Review
popup is allowed. Persist results in existing History with RequiresExplicitInsert.
Selecting a completed row later opens ordinary Review. The client queue owns
cancellation through transport shutdown and verifies service idle before advancing.
Do not treat cancelling an HttpClient request as server worker acknowledgement.

A completed file is saved immediately. Failure/cancellation of a later file does
not remove earlier results. Preserve valid partial text with a visible incomplete
reason and explicit Review. A decoded recording without text can remain in History
for retry, under existing retention. Saving failure retains text in the existing
unsaved recovery mechanism and blocks silent loss at Exit. Normal Exit cancels
remaining work, joins cleanup and saves recoverable text; never force-kill.

Queue persistence across app restart is not proposed. After restart, completed
History survives and unfinished source files can be selected again explicitly.
Each deliberately selected import is a new dictation; no hidden overwrite or
merging into an unrelated inserted Review. Store original basename/hash/duration
as protected provenance, not the complete absolute source path. Show those details
only where useful; do not add engineering transport details to the product flow.

## Stage C: Personal dictionary

Provide two distinct controls: recognition hints (preferred spelling/names) and
explicit exact correction pairs. Keep both opt-in and disabled until measured.
Check real tokenizer limits and model support; no Whisper CLI assumptions.

Snapshot the enabled hint/rule version per operation. Preserve the original
primary/verification transcripts and expose a derived final version with the
applied rule list. Exact corrections use Unicode word/phrase boundaries,
longest-match-first, a single non-recursive pass and duplicate/conflict validation.
Do not rewrite manual edits, already inserted text or stored history retroactively.
Do not treat hints as instructions for formatting. Evaluate hints alone, corrections
alone and combined, including absent-term insertion negatives.

Builder decisions: precise protected settings schema, rule editor details and
capability negotiation fields, provided they honor these behaviors. Do not claim
runtime support for streaming until nonempty hints actually reach the decoder and
pass the installed model's short/long-input checks.

## Stage D: Private dictation

Proposed first increment: an explicit Private for next microphone recording
control and a clear private indicator, snapshotted before capture. This increment
is live-capture-only: imports remain ordinary saved jobs. While private-next is
armed, Import audio... refuses to start and explains that imports are saved to
History; it never consumes or silently clears the private-next choice. While an
import batch is active, private-next activation is unavailable until its cleanup
finishes. Queued jobs keep their explicit ordinary-storage snapshot. Supporting
private imports later requires a separately visible batch privacy choice before
the first decode/save, not inference from an unrelated one-shot recording toggle.
These are proposed builder decisions, not an already shipped privacy behavior.

A private session bypasses
History, pending/unsaved persistence, normal export and transcript/audio logs on
success, error, cancellation, replay and Exit. Review can stay in memory until
explicit dismissal; warn before losing its last recoverable copy. No retroactive
purge of old history. Reset the one-shot choice only after the session is owned.

Do not promote this to a promise of secure deletion from OS paging, crash dumps,
clipboard history or a destination application's own records. Keep existing
clipboard preservation protections when insertion is explicitly requested.

Automatic browser incognito is a separate proposed integration. Chrome documents
an extension-visible incognito flag, but the current app has no browser-state
bridge or validated binding to the exact foreground native window. The current
loopback guard rejects browser Origin headers, so direct extension fetch is not
an established integration. Browser choice, minimal bridge protocol, freshness,
window identity and unavailable-signal behavior must be settled before enabling
automatic privacy. Do not guess from titles or broaden server access controls.
Do not install an extension/native host without explicit instruction.

## Stage E: Optional voice formatting

First establish actual model output on S08 and literal-command negative cases.
If native output already supplies the intended structure, preserve it unchanged.
Otherwise propose a small opt-in explicit grammar, retaining original text and
separate rendered text. A spoken phrase such as new paragraph may be literal;
activation/escaping and interaction with punctuation need evidence before release.
No blanket phrase replacement, duplicate paragraph/list layer or retroactive
rewriting. No new formatting behavior is approved by a favorable punctuation demo.

## Implementation order and verification

1. Record scope/test agreement and prepare a private read-only source inventory.
2. Search: meaningful Core query/selection regression tests and detached WPF states.
3. Import: bounded decode tests, serial queue/cancel/error/save-failure tests,
   no-device/no-delivery assertions, then existing Core/backend suites.
4. Render Light/Dark, wide/narrow, no results, long text and queue error/cancel
   states without native windows or audio devices. Update Help for shipped behavior.
5. Evaluate current dictionary/formatting behavior only on labelled audio using
   serial local inference and unchanged models. Separate intended wording from
   verbatim acoustic accuracy; no pretend holdouts from old benchmark cases.
6. Private-mode storage tests must cover failures, partial text, replay, normal
   Exit and restart. Browser auto activation remains unavailable until integrated
   and tested with a selected browser and exact target binding.

Keep all development in C:\Git\Local-Dictation\repo, feature/native-windows.
No new worktrees, software installation, commits or pushes are part of this design
preparation. Preserve both existing APIs and private data. Deployment follows a
separate verified candidate and normal client Exit; source changes alone do not
update the installed client. Native Codex/Outlook acceptance remains outstanding.

## Evidence sources

Local ownership: DictationLibrary.cs, HistoryStore.cs, ReviewDocument.cs,
AppController.cs, ApiClient.cs, HistoryPanel.cs, server/local_dictation/application.py,
engine_adapter.py, uploads.py, workers.py, server/model_engine.py and asr.py.

Chrome extension references checked on 2026-11-14:

- https://developer.chrome.com/docs/extensions/reference/manifest/incognito
- https://developer.chrome.com/docs/extensions/reference/api/tabs

The current tool catalog advertises no incognito/native-browser bridge capability.
That absence does not prove the operation impossible; it remains an unimplemented
or unverified integration for this product/runtime.
