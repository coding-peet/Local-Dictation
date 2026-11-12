# Local Dictation product requirements

Status: current input specification, consolidated from Peter's requests on
10–12 November 2026. Update this document when the user changes a requirement.
Older design documents explain decisions; this file defines current behavior.

## Purpose and scope

Provide Windows desktop dictation with local speech recognition, text at the
cursor, optional independent verification, and recoverable recordings.
Application copy and engineering documentation are English. Collaboration with
Peter is normally Slovak. Slovak dictation, diacritics, English technical terms,
punctuation, whitespace, and Unicode must be preserved.

## Recording and model selection

- Select the microphone, language, and installed Dictation model in Settings.
- Keep live transcription and insertion of live text as independent options.
  After-recording transcription remains available when live mode is unavailable.
- Models use original Transformers weights in FP16, without conversion or
  quantization. Keep weights outside the repository under C:\AI.
- Include original OpenAI Whisper Large-v3 Turbo alongside KInIT Turbo Slovak
  and original Whisper Large-v3. Preserve multilingual capability for the OpenAI
  models; do not inherit KInIT's Slovak-only constraint or Large-v3's costly
  encoder offload/beam profile for the smaller original Turbo.
- Select Verification as a generic model role. Large-v3 is the initial option,
  not a hardcoded workflow. It must differ from the Dictation model and support
  the effective language.
- Distinguish installed, runtime-tested, quality-checked, and live-ready states.
  A successful download or test does not prove quality or live suitability.
- The default shortcut is Ctrl+Shift. Make it configurable in Settings, with
  repeat suppression and a clear disabled/collision state.
- Keep the Settings header compact, with connection and autosave in one status
  row. Avoid duplicate Recording/Text and verification headings and introductory
  paragraphs on the Dictation tab. Put Insert live text at the cursor immediately
  after Live transcription. Separate Verify after recording with spacing/a rule;
  reveal its model/details only while enabled, retaining the eligible selection
  when hidden. Keep actionable errors visible even when details collapse.
- Persist valid settings changes automatically. Model/microphone choices and
  toggles must not require a separate Save action before shortcut recording.
  Debounce text settings, preserve the last valid configuration on errors, and
  show saving/saved/error status. Changes during a session apply to the next one.
- Enable live capability for the built-in Large-v3 profile, independently of
  measured latency/readiness. Explain that its partial updates may be slow;
  do not disable a technically supported mode merely because it is experimental.
- Start/stop with the shortcut, or record in the banner from Settings. Escape
  cancels recording or verification. Stop normally at the 10-minute limit.

## Live transcription quality

- Retain every accepted original sample through the configured recording limit.
  Slow recognition must not stop capture merely because 32 seconds are queued.
- Use the evaluated adaptive live policy: growing previews after about three
  seconds of audio, quiet-valley commits within 12–24 seconds, and a final
  whole-recording primary pass. Suppress isolated quiet live fragments.
- Final primary refinement is separate from optional second-model verification.
  Avoid decoding a tail that will immediately be replaced by the whole pass;
  reuse only an exact unchanged whole-recording inference from the same profile.
- Empty recognition must never erase a nonempty received draft or its recovery
  state. Preserve text for explicit Review when final recognition fails.
- Evaluate paired short-draft/long-repair schedules against the full-context
  method. Longer acoustic corrections own their complete audio span and must
  not be overwritten by stale short hypotheses. Compare repaired-stream quality
  before any global final replacement, with compute and latency costs explicit.
- Keep human corrections separate from frozen experiment inputs. Hash references
  and record their supplied/intended/verbatim provenance before rescoring saved
  outputs; model agreement alone never establishes independent accuracy.
- Evaluate changes with explicit audio identity, inference/profile provenance,
  actual transcripts, coverage and latency. Model agreement is consistency, not
  independent accuracy. Keep private benchmark transcripts/audio outside Git.

## Banner and live cursor delivery

- Use the approved compact native banner and pink waveform icon. Keep spacing,
  selected states, status text, and controls readable; offer System/Light/Dark.
- Show live text in the banner as segments arrive, including when cursor
  delivery pauses. Do not imply character-by-character recognition.
- The banner must be movable by its header, microphone tile, or waveform.
  Initial placement should avoid the native caret when available.
- A shortcut-started session captures the original external destination even
  when live cursor insertion is off. In that case keep partial text in the
  banner and return the completed text automatically after successful completion
  and any required word choices.
- In desktop Outlook, keep live transcription in the banner and insert the
  complete final text once after Finish and any required verification choices.
  Detect the destination application without reading message content or relying
  on window titles. Keep the original validated caret and surrounding signature.
  This session-specific delivery policy must not disable live recognition or
  overwrite the user's live-insertion preference for other applications. Make
  the Outlook completion behavior clear in the banner.
- Only replace the app's owned draft in the validated original field. Preserve
  surrounding text. Switching focus pauses delivery; returning to the same
  unchanged field resumes it, including after a click within that field.
- Before final insertion, restore only the captured original window and exact
  field identity. Allow activation up to 100 ms to restore that identity itself;
  if it is already ready, avoid a redundant provider SetFocus. Otherwise request
  focus on the captured original field once and allow up to 500 ms for that exact
  identity. Do not adopt another field or queue input/clipboard replacement while
  waiting. Cancellation, invalidation, input generation and foreground changes
  still stop the operation. Preserve acceptance-key release, full-text ownership,
  exact caret and paste-acknowledgment checks.
- Prove that a captured UI Automation field belongs to the captured native window:
  use its nearest native host and require the same root window. Recheck that
  association before activation and insertion. A matching PID or text in another
  window is insufficient; an unavailable or foreign association pauses delivery.
- Deliver nonempty live and final replacements as a guarded native paste, with
  exact destination acknowledgement. Preserve supported previous clipboard
  formats and exclude temporary dictation from Windows clipboard history/cloud.
  Refuse unsupported clipboard formats before replacing them.
- Accept a specific initial-paragraph readback projection only for desktop
  Outlook processes named exactly `olk` or `OUTLOOK` (case-insensitive). Require
  final delivery, a first insertion at document offset zero with no owned text,
  a nonempty payload without control characters, and an exact caret at the end
  of that payload. The original full document must begin with exactly two LF
  characters followed by a character other than CR or LF. The observed document
  must equal the complete payload plus the original document with only its first
  LF removed, using ordinal equality. Every other character must remain exact.
- Keep this Outlook projection separate from raw exact-text diagnostics:
  `ExactText` remains false and `OutlookParagraphProjection` records the accepted
  case. Adopt the actual observed full document as the ownership baseline and
  acknowledge the existing paste without replay. Do not trim/normalize text,
  accept other CRLF/NBSP changes or additional edits, or change the payload.
- When appending at an already validated correct caret, avoid reselecting its
  collapsed UIA range. Select a real owned suffix for replacement and retain
  explicit caret restoration when needed. Keep all exact acknowledgment guards.
- A transient post-paste accessibility/selection state may retry only readback
  within a bounded deadline. Keep the original field, full text and exact caret
  requirements; do not issue another paste to obtain acknowledgement.
- Idle recovery can be hidden with the banner X without discarding protected
  text, a clipboard lease or its backup. Old queued notifications must not undo
  dismissal; a new recovery event or explicit Start/Exit can resurface it.
- Offer the existing inspected clipboard restoration in the idle banner. Keep
  recording Finish first, block active/inserting restoration and recheck after
  confirmation. Recovery must use one transcript surface in compact Review.
- Opt-in support journals record PID/build and fixed capture/readback stages,
  never transcript, field names/values/text, audio or clipboard contents. Ordinary
  operation and fixed-stage logging add no UIA queries. Only an opted-in
  text-pattern capture failure may read the focused provider's bounded technical
  shape: framework/class, numeric control type, PID, focus flags and pattern IDs.
  This metadata must not read Name, Value, text range, AutomationId, siblings or
  ancestors, and must preserve the original failure if collection fails.
- Opt-in focus-interruption diagnostics may use only foreground/identity values
  already read by the ownership guard. Record numeric window handles, process
  IDs parsed from the existing identities and bounded identity hashes. Never log
  raw runtime identities, field text or UI names, or add UIA reads to attach this
  metadata. Opt-out must retain no focus-diagnostic payload.
- An explicit child-only diagnostic switch may expose the recording banner's
  taskbar entry for controlled tests. Preserve its nonactivating behavior and
  destination guards; this does not establish native tool visibility.
- Run UIA operations on a dedicated, windowless MTA worker. Preserve clipboard
  ownership on its separate STA thread. A threading correction, installation or
  successful Notepad delivery does not establish Outlook compatibility.
- Manual edits, a closed field, or failed ownership validation require an
  explicit destination decision. Never adopt arbitrary focus.
- Never submit text or synthesize a send shortcut. Wait for acceptance keys to
  be released before restoring target focus.
- With verification off, normal completion inserts directly into the original
  valid field without opening interactive Review first. Verification agreement
  follows the same path; differences and actionable failures remain in Review.

## Verification and compact Review

- With Verify after recording enabled, save the received transcript before
  removing the owned live draft from a validated destination for Review.
- Keep ordinary Review inside the original small banner. Underline disputed
  phrases; clicking one shows two exact alternatives without model/source names.
  The live wording comes first, the verifier's wording second.
- Selecting wording automatically activates the next unresolved phrase.
  The last choice inserts the complete reviewed text at the original cursor.
  Successful verification with no differences inserts automatically.
- Allow custom phrase wording and empty alternatives. Preserve unapplied custom
  drafts when changing phrases, closing, or restarting. A held Enter gesture
  must accept only one choice, on release.
- Allow direct editing of the entire visible transcript in the same compact
  banner, including common text and punctuation. Keep originals and choices.
  A manually edited final draft is authoritative and saved as it changes;
  approve it explicitly before insertion so ordinary typing never inserts it.
  Pause phrase alternatives while whole-text edits are authoritative, rather
  than guessing new comparison offsets or overwriting the user's changes.
  Keep the current single-paragraph delivery constraint explicit; Enter is not
  approval, and pasted control characters remain recoverable for correction.
- The full editor is optional through Details. Preserve immutable original
  transcripts, provenance, choice history, and the authoritative manual draft.
- Cancelled, interrupted, or failed verification keeps text pending for explicit
  action. Failed insertion waits for explicit retry. Closing/reopening a pending
  item must not insert it automatically.
- Use Choose destination when no destination was captured or its lease is lost,
  including after restart. Approval must return text without manual copy/paste.
- Keep insertion receipts durable where possible and recover them across History
  and Pending Review. Older snapshots must not overwrite current edits or clear
  an existing receipt. Already inserted items are read-only in compact Review.

## History and recovery

- Retain the latest 20 dictations with their original audio. Evict the oldest
  history transcript and audio when adding a 21st item.
- Protect saved transcripts and audio for the current Windows account with
  DPAPI. Pending Review is separate and must not be evicted by the history limit.
- Provide playback, text copy, Review, explicit WAV export, and deletion.
  Exported audio is an ordinary user-selected file.
- Preserve unsaved text in memory and show saving failures. Skip/report malformed
  individual pending files without deleting them or blocking valid recovery.
- Normal application Exit must await recording cleanup and protected saving.
  Do not silently discard text still marked unsaved.
- If paste was queued but cannot be confirmed, retain its payload and all saved
  clipboard formats in memory. Block new delivery and normal Exit until deliberate
  recovery or a newer clipboard copy resolves it. Offer Restore previous
  clipboard in the tray after destination inspection. A crash or forced process
  termination cannot preserve this in-memory clipboard backup.
- Operational diagnostics must not record actual transcript text or audio.
- Whisper repetition recovery must re-decode audio using its native generation
  fallback, rather than deleting repeated words from the transcript. Preserve
  legitimate spoken repetition and original model weights/precision.

## Help, build, and agent continuity

- Provide self-contained offline HTML Help. F1 in Settings and Help (F1) open
  the published Help.html in the default HTML viewer. Keep it current with UI.
- Git must contain all required source, icon assets, Help, scripts, tests,
  specifications, and agent instructions needed to rebuild from a fresh checkout.
- Generated EXE/DLL files are build output. Keep the complete published app
  folder together; an EXE alone is insufficient for this framework-dependent app.
- Keep source and application under a common C:\Git\Local-Dictation parent on
  this PC. Keep models, protected data, environments, and SDKs under C:\AI.
- Perform all development in C:\Git\Local-Dictation\repo with ordinary Git
  branches. Do not create additional worktrees. Preserve prior work and report
  the working folder, branch, and latest commit at handoff.
- Do not retain installers as development clutter. Do not commit binaries,
  model weights, environments, recordings, transcripts, logs, or credentials.
- Agent continuity must come from committed requirements, AGENTS.md, build and
  recovery instructions, and honest validation status, not an old chat or local
  scratch directory. New requirements belong here and in Help where relevant.

## Acceptance boundaries

Automated tests must distinguish policy/controller simulation, real protected
storage, WPF input handling, and native UIA insertion. Successful compilation is
not microphone, timing, model-quality, or application-compatibility acceptance.
Codex-field and Outlook insertion require Peter's manual acceptance tests.

Preserve the existing TypeWhisper setup and production API on port 8123 until an
explicit controlled handover. Use the separate candidate API on 48125. Never
change security controls as an implementation shortcut.

See the latest dated validation record and README for deployed build status and
remaining manual checks. A process crash between insertion and receipt persistence,
especially when every write fails, is not claimed to have transactional recovery.
