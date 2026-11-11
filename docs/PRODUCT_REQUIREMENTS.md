# Local Dictation product requirements

Status: current input specification, consolidated from Peter's requests on
10–11 November 2026. Update this document when the user changes a requirement.
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
- Select Verification as a generic model role. Large-v3 is the initial option,
  not a hardcoded workflow. It must differ from the Dictation model and support
  the effective language.
- Distinguish installed, runtime-tested, quality-checked, and live-ready states.
  A successful download or test does not prove quality or live suitability.
- The default shortcut is Ctrl+Shift. Make it configurable in Settings, with
  repeat suppression and a clear disabled/collision state.
- Persist valid settings changes automatically. Model/microphone choices and
  toggles must not require a separate Save action before shortcut recording.
  Debounce text settings, preserve the last valid configuration on errors, and
  show saving/saved/error status. Changes during a session apply to the next one.
- Enable live capability for the built-in Large-v3 profile, independently of
  measured latency/readiness. Explain that its partial updates may be slow;
  do not disable a technically supported mode merely because it is experimental.
- Start/stop with the shortcut, or record in the banner from Settings. Escape
  cancels recording or verification. Stop normally at the 10-minute limit.

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
- Only replace the app's owned draft in the validated original field. Preserve
  surrounding text. Switching focus pauses delivery; returning to the same
  unchanged field resumes it, including after a click within that field.
- Deliver nonempty live and final replacements as a guarded native paste, with
  exact destination acknowledgement. Preserve supported previous clipboard
  formats and exclude temporary dictation from Windows clipboard history/cloud.
  Refuse unsupported clipboard formats before replacing them.
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
