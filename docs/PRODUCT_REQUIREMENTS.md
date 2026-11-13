# Local Dictation product requirements

Status: current input specification, consolidated from Peter's requests on
10–13 November 2026. Update this document when the user changes a requirement.
Older design documents explain decisions; this file defines current behavior.

## Purpose and scope

Provide Windows desktop dictation with local speech recognition, text at the
cursor, optional independent verification, and recoverable recordings.
Application copy and engineering documentation are English. Collaboration with
Peter is normally Slovak. Slovak dictation, diacritics, English technical terms,
punctuation, whitespace, and Unicode must be preserved.

## Recording and model selection

- Models has two visual cards: Installed models and Add model. Keep the installed
  table first, with Test selected model with audio and Remove below it. Both
  actions operate on the table selection, independently from Dictation settings.
  Remove introductory implementation prose. The lower card offers missing catalog
  models or a public Hugging Face URL, Check size and compatibility, Download model
  and download cancellation. Preserve actionable errors and inspection identity.
- Expose removal only for verified app-owned managed installations. Preserve
  shared original TypeWhisper weights; show a concise restriction. Confirm the
  selected model before removal. Refuse active inference/downloads, unsafe paths
  and links; preserve registry/files on persistence failure and report incomplete
  cleanup. Do not perform any real removal during automated tests or rollout.

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

## Input language and correction

- Show a discreet language indicator in the banner: `Auto · SK` / `Auto · EN`
  from the actual primary detector, or the explicit language code. Show `Auto · …`
  before a result. A fixed Slovak-only model shows `SK`, without claiming detection.
- Configure preferred Auto languages as removable tags with a `+` add button.
  Use the heading "Preferred Auto languages (supported by installed models)"
  without a helper sentence under the tags. Keep removal marks visually quieter
  than the language codes. Leave enough banner width to display elapsed time
  and the complete detected language, including a queued correction.
  Populate choices from installed models' actual supported language metadata.
  Initial tags are SK and EN; RU, SR and CS are absent. More languages, including
  ES, can be added when installed models support them. Never silently broaden
  KInIT's Slovak-only capability based on its tokenizer tokens.
- With configured preferences, constrain acoustic Auto detection to the selected
  model's supported preference intersection. Reject a nonempty preference list
  with no supported intersection before recording or autosave. Allow all tags to
  be removed; with no preferences, use the model's complete supported language set.
- The banner correction picker offers only configured preferred languages that
  installed models support, plus Auto. Preserve tag order. With no preferences,
  offer all supported languages. Do not silently broaden a configured but
  unavailable preference set. Use the active recording's settings snapshot.
  The verifier must support all effective candidates and use the primary's
  resolved language. A restricted detector still has no accuracy/confidence guarantee.
- Clicking the language opens an explicit correction picker naming any needed
  model switch. During recording, continue capture and queue a complete same-audio
  retranscription after Finish. During verification, cancel that pass and replay
  the recording with the selected language. Reuse the existing original target
  and Finish guard; foreign input/focus still blocks automatic insertion.
- Retranscription never starts the microphone. Retain original text, edits and
  audio in protected History, including audio from empty recognition. A History
  retry creates a separate Review and never automatically replaces inserted text.
- Escape cancels both the current session and any queued retranscription.
  Transfer destination/receipt ownership to the new Review without aliases.

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

- Use Material Design 3 throughout the WPF application through the compatible
  MaterialDesignInXamlToolkit controls and MaterialDesign3 defaults. Preserve the
  waveform identity, semantic tonal colors, Roboto typography, readable focus and
  selection, System/Light/Dark and Windows high contrast.
  Action/toggle buttons use true stadium geometry, with semicircular ends and
  straight sides. Compute each layer from its actual dimensions; never stretch
  an ellipse. Keep a 48-DIP hit target around the 40-DIP visible body.
- Keep the movable recording banner minimal: indicator/waveform, short phase,
  duration, small active model name and small finish/cancel actions. The model
  name shares the phase row in subdued 11-DIP text; keep the complete time and
  language line visible. Show Dictation while recording and the verifier during
  Verify, with the full name and role available on hover. Never take keyboard
  focus. Do not show a transcript editor, microphone metadata or clipboard-recovery
  button in the normal banner. Review uses a separate compact window.
- Close the banner promptly after successful automatic insertion. A protected
  pending result may expose a small, low-emphasis Insert action.
- The banner must be movable by its status area or waveform.
  Initial placement should avoid the native caret when available. Fade its
  decorative window edge to fully transparent pixels, keeping text and controls
  sharp. Do not leave an opaque rectangular toolkit window behind it.
- A shortcut-started session captures the original external destination even
  when live cursor insertion is off. In that case keep partial text in the
  recognition pipeline and return the completed text automatically after successful
  completion and any required word choices, provided Finish occurred in the original field.
- In desktop Outlook, keep live recognition running and insert the
  complete final text once after Finish and any required verification choices.
  Detect the destination application without reading message content or relying
  on window titles. Keep the original validated caret and surrounding signature.
  This session-specific delivery policy must not disable live recognition or
  overwrite the user's live-insertion preference for other applications. Make
  final-only completion behavior clear in Settings and the short status.
- Only replace the app's owned draft in the validated original field. Preserve
  surrounding text. Switching focus pauses delivery; returning to the same
  unchanged field resumes it, including after a click within that field.
- Explicit Insert places ready Review text at a freshly selected external caret.
  Keep the compact Review reachable above the destination. Clicking its Insert
  must preserve that external focus; ordinary editing and wording choices may
  activate Review normally. Capture and validate the selected field after the
  click, with an insertion reservation before asynchronous work. Refuse input
  or foreground changes during capture, unsupported fields, and unsettled pastes.
  Do not reuse a permanently rejected original lease for explicit retries.
- When a fresh capture proves the exact original field, complete document and
  owned draft ending at the selected caret, replace that owned draft. Otherwise
  insert at the chosen caret and leave earlier text elsewhere intact. Never
  guess ownership or delete text from another destination. Preserve exact
  readback, clipboard recovery, durable receipts and duplicate-click protection.
- At Finish, latch whether the exact original field is still current without
  activating anything. If it is elsewhere, preserve the result for explicit Insert;
  a later return must not start delayed automatic insertion. Automatic completion
  also checks the finish-time input epoch and never activates or focuses a target.
- Remember terminal live transport/ack/range failures by executable name for later
  sessions. Use final-only delivery there while keeping live recognition enabled.
  Focus pauses, cancellation and user edits must not blacklist an application.
  Offer Reset remembered apps in Settings; Outlook remains final-only after reset.
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
- Pauses in speech must not require a banner action. Continue updating the complete
  owned live draft, including corrections to earlier wording; Ctrl+Shift finishes
  and returns the final text automatically. Outlook retains final-only insertion.
- TextPattern can expose generated placeholder content in an empty editor. Treat
  an Edit field as empty only when its caret prefix is empty and its own writable
  ValuePattern independently returns an empty value. Preserve real matching words,
  whitespace and all nonempty values; an absent pattern is not proof of emptiness.
  Retire this initial projection after any acknowledged write, including a later
  withdrawal. Retained dictated text equal to the placeholder must never count as empty.
- A first ordinary collapsed native paste may remove a Chromium/ProseMirror
  accessibility placeholder projection. Accept only the exact raw known Edit
  provider, unchanged pre-input identity/text/edge caret, no range selection or
  caret-restoration selection, first-ever insertion, a complete four-event paste,
  complete observed document equal to the entire payload and its exact ending
  caret. Atomically adopt start zero, actual full document and owned payload.
  This acknowledges one already issued paste; it never proves emptiness before
  input, trims text or permits replay. Use the same policy for late readback and
  retain strict complete-document ownership for every subsequent correction.
- If a provider cannot construct the owned suffix through character movement,
  permit a read-only exact backward FindText lookup bounded by the validated caret.
  Require exact full prefix, suffix, document bounds and end endpoint before
  selecting it. Preserve all full-text, identity, focus, input and paste checks.
- A target-check timeout must cancel the waiting operation and refuse fresh work
  while that operation is still running. Once it actually exits, the same bounded
  serialized worker must accept fresh checks again; one slow call must not disable
  cursor delivery for the lifetime of the app. Never replay the expired action.
  Keep all field, input-generation and uncertain-paste checks in force.
- A transient post-paste accessibility/selection state may retry only readback
  within a bounded deadline. Keep the original field, full text and exact caret
  requirements; do not issue another paste to obtain acknowledgement.
- After a fully dispatched but unconfirmed paste, permit read-only late
  acknowledgement for at most ten monotonic seconds from dispatch. Require the
  same unchanged foreground field, input generation, clipboard payload/marker
  and exact complete-document/caret proof. Cancel on focus/input/invalidation;
  never replay input. Publish and retain a confirmed receipt before clipboard
  restoration, so a worker timeout cannot lose an already proven insertion.
- Idle recovery can be hidden with the banner X without discarding protected
  text, a clipboard lease or its backup. Old queued notifications must not undo
  dismissal; a new recovery event or explicit Start/Exit can resurface it.
- Preserve and restore the clipboard automatically on acknowledged delivery.
  Keep temporary text out of clipboard history/cloud and preserve newly copied
  content. The normal banner must not expose Restore clipboard. If dispatch or
  restoration remains uncertain, retain the backup and protected transcript;
  exceptional Resolve opens a usable recovery window from the banner, Settings
  or tray. Require explicit destination-inspection acknowledgement before
  restoring an unconfirmed paste. Offer automatic/manual restoration retry
  only for an already acknowledged backup. Clear stale recovery status once
  resolved; repeated notifications for the same lease must not undo X dismissal.
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

## Verification and separate compact Review

- With Verify after recording enabled, save the received transcript before
  removing the owned live draft from a validated destination for Review.
- After Finish, keep the first-model text in a bounded, read-only preview while
  final primary processing and verification run. The banner remains nonactivating,
  movable and scrollable for a long transcript; it is not an editor.
  During verification, only the banner holds the withdrawn first transcript.
  Agreement inserts the final result at the original validated cursor and closes
  the banner; differences open separate Review.
- Open ordinary Review in its own small floating window. Keep the recording banner minimal.
  Underline disputed
  phrases; clicking one shows two exact alternatives without model/source names.
  The live wording comes first, the verifier's wording second.
- Selecting wording automatically activates the next unresolved phrase.
  The last choice inserts the complete reviewed text at the original cursor.
  Successful verification with no differences inserts automatically.
- Allow custom phrase wording and empty alternatives. Preserve unapplied custom
  drafts when changing phrases, closing, or restarting. A held Enter gesture
  must accept only one choice, on release.
- Allow direct editing of the entire visible transcript in the same compact
  Review window, including common text and punctuation. Keep originals and choices.
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
- For explicit placement, select a destination cursor and click Insert. Keep
  Choose destination plus the recording shortcut as an alternative, including
  after restart. Neither route requires manual copy/paste.
- Offer Second transcript in compact Review and Details when protected original
  audio and an eligible different model are available. Transcribe that audio
  only with the chosen verifier; preserve the primary output, edits, source
  recording identity and model revisions. Create a separate comparison without
  changing the source item or its insertion receipt. Show the complete second
  transcript read-only, alongside phrase choices. Agreement in this post-hoc
  workflow still waits for explicit Insert; it must not replace earlier text.
- Preserve cancelled/failed second transcripts for recovery, including source
  provenance. Escape cancels; a language correction reuses the same recording
  through the guarded replay path. Never start the microphone for these actions.
- Keep insertion receipts durable where possible and recover them across History
  and Pending Review. Older snapshots must not overwrite current edits or clear
  an existing receipt. Already inserted items are read-only in compact Review.

## History and recovery

- Present protected unfinished Review items in History with Needs review or Not
  saved status, including Text only items whose audio has expired. Deduplicate
  IDs and preserve current pending edits and durable insertion receipts. Remove
  the separate Pending Review navigation and use History's Open Review route.
  Storage lifetimes remain independent: the 20-recording limit never evicts
  protected unfinished text. Explicit Delete removes the selected dictation and
  closes its matching editors before deleting audio/text/choices.

- Embed the complete Recent dictations workspace in the main window's History
  section: list, selected transcript/model/language, player and existing actions.
  History in the notification-area menu opens that same section and main window.
  Do not open a separate History window or require a launcher button.
- Use a list/detail layout with a stacked compact layout and vertical scrolling
  when space is limited. Preserve the selected item during refresh/navigation.
  Load its protected audio only while History is selected; leaving the section
  stops/clears playback. Closing the main window disposes its player/subscriptions.

- Retain the latest 20 dictations with their original audio. Evict the oldest
  history transcript and audio when adding a 21st item.
- Protect saved transcripts and audio for the current Windows account with
  DPAPI. Pending Review is separate and must not be evicted by the history limit.
- Provide Play, Pause and Stop icons, elapsed/total time, a seekable timeline
  with a round thumb, and a faint RMS waveform under the slider. Keep the
  transport approximately 108 DIPs tall and text actions above it. Use a fixed
  -54 to -12 dBFS RMS display scale, never per-recording maximum normalization.
  This is an energy aid rather than speech/noise classification. Support
  keyboard seeking. Stop returns to the beginning; Pause retains position.
  Load/render/seek before Play must not open an audio output device. Retire the
  previous output safely before replay, seeking or changing recordings.
- Preserve text copy, Review, explicit WAV export, and deletion. Exported audio
  is an ordinary user-selected file.
- Preserve unsaved text in memory and show saving failures. Skip/report malformed
  individual pending files without deleting them or blocking valid recovery.
- Normal application Exit must await recording cleanup and protected saving.
  Do not silently discard text still marked unsaved.
- If paste was queued but cannot be confirmed, retain its payload and all saved
  clipboard formats in memory. Block new delivery and normal Exit until deliberate
  recovery or a newer clipboard copy resolves it. Offer Resolve clipboard
  recovery in the tray, with an explicit destination-inspection checkbox. A crash or forced process
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
