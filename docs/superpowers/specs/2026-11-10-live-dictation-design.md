# Local Dictation: live Windows client and reusable ASR sessions

Status: requirements recorded; repository layout updated 2026-11-10. Implementation planning follows.

Date: 2026-11-10

Working product name: Local Dictation. The name is a proposal.


## Intent

Build a small Windows alternative to TypeWhisper that records a microphone,
uses the existing local ASR API, and inserts speech into the text field where
the user is working. Peter wants text to appear and be corrected while he
speaks, with a visible recording banner and a useful recovery window.

The speech API is shared infrastructure. A later Pala Note client or another
application should be able to send audio without depending on Windows cursor
insertion. Device firmware and network deployment are separate work.


## Confirmed choices

- Build our own small Windows client over our API. Peter selected this
  approach explicitly after comparison with extending TypeWhisper.
- All application labels, settings, errors, and documentation are English.
  Project conversation remains Slovak.
- Ctrl + Shift toggles recording: press once to start, again to stop.
- A compact banner appears near the bottom of the screen while recording
  and while the last speech is being processed.
- Live insertion updates and corrects text directly in the destination field.
  Showing partial text only in the banner does not satisfy this requirement.
- Offer Review before inserting as a separate delivery mode.
- Recover to a review window if the destination changes or delivery fails.
  Peter described this behavior in TypeWhisper and wants it considered here.
- Offer an optional checkbox, Verify after recording, and a Verification
  model dropdown. Peter approved a second pass over the original audio and
  explicitly required a configurable model, rather than a hardcoded Large-v3
  feature. Today the initial verification choice is Large-v3.
- Retain only outstanding Review items. Peter selected temporary audio,
  without a complete transcript history or recording archive.
- Preserve the previously selected model-management requirement: an approved
  catalog plus the ability to add a custom Hugging Face model URL.
- Reuse the local models and API. Do not convert or quantize the ASR weights.
- Keep project work under C:\AI. Do not send audio to a cloud provider.

Peter accepted a browser preview. The appearance of the first preview is
still a proposal, rather than an approved final design.


## Existing state, checked 2026-11-10

The production server is C:\AI\PalaNote-ASR. It binds to 127.0.0.1:8123.
It provides /health, /v1/models, and a batch /v1/audio/transcriptions route.
The batch route requires a complete upload and returns a complete result.
It does not currently provide live microphone sessions.

Installed original models:

- kinit/whisper-large-v3-turbo-sk, v2.0,
  revision eb2c35bf0971f836fbf045ebe6e897826172d5d7.
- openai/whisper-large-v3,
  revision 06f233fe06e710322aca913c1bc4249a0d71fce1.

The GPU is an RTX 3050 Laptop with 4 GiB VRAM. The current fast profile
keeps about 1.5 GiB of CPU model weights cached and releases GPU weights
after each batch request. The accurate profile offloads encoder layers.

The deployed benchmark already measured approximately 2.5 seconds for an
81-second Slovak recording with the fast profile. That is a batch result;
it is not a measurement or promise of live transcription latency.

.NET 10 desktop runtime is installed. dotnet --list-sdks returned no SDK.
The deployed runtime is separate from the new Git checkout at
C:\AI\repos\Local-Dictation. Existing runtime code, pinned model files,
startup behavior, and TypeWhisper settings must be preserved during setup.


## Approaches considered

### Selected: small native Windows client plus live API sessions

The client owns microphone capture, the shortcut, the banner, destination
tracking, insertion, and review. The API owns model execution and the
transcription stream. This fits Peter's narrow workflow and leaves other
clients independent of the Windows interface.

The tradeoff is that we must implement and verify Windows text delivery.
Live correction in arbitrary applications is the riskiest part of this work.

### Alternative: extend TypeWhisper

Reuse its shortcuts, recording interface, and result handling, and connect
a streaming provider. Its public Windows documentation describes live
partial results in the overlay when supported by the engine. That alone
does not establish editable live insertion into a destination field.
Host application changes could be needed, with continuing upstream upkeep.

Peter chose the independent client. Do not fork TypeWhisper or install a
development build as part of this design.


## First implementation scope

Deliver one Windows tray application and a compatible extension to the ASR
server. Settings cover microphone, shortcut, delivery mode, and model
management. The default is the fast Slovak model with live insertion.

The accurate multilingual model remains available for transcription after
recording. Do not advertise live support for that profile until it passes
its own timing and quality checks. Choosing it clearly selects an
after-recording workflow rather than silently changing the live mode.

A separately selected verification model can perform an optional second
pass after the primary dictation. Large-v3 is the initial configured choice.
This is an additional quality step, not a replacement for live insertion.
Default the checkbox to off as a builder proposal so ordinary dictation
does not unexpectedly start a second model job.

The approved catalog and custom Hugging Face URL import are required for the
completed client. Sequence them after the first live feasibility milestone,
not as a silently deferred release. The banner preview currently shows the
two installed models and does not yet depict the model-management screen.

This first build excludes a full editor, general transcription history,
rewriting workflows, translation, meeting recording, account sync, and
firmware changes. Review drafts are recovery items, not a history product.


## Client and runtime choices proposed by the builder

Use C# with WPF on .NET 10 for a native Windows tray app and small windows.
Do not add a Chromium runtime to the client. Keep all editable project
source, documentation, prototypes, and build scripts in the GitHub repository
coding-peet/Local-Dictation, checked out at C:\AI\repos\Local-Dictation.
Keep generated build/test output at C:\AI\build\Local-Dictation and
published client files at C:\AI\LocalDictation\app.

Acquire an SDK and any necessary build packages only after approval, in
task-local directories where practical. Do not change global PATH, package
feeds, company access controls, or the production Python environment just
to produce the first client build. Select exact dependency versions during
implementation planning and check their current primary documentation.

Develop the live backend in an isolated Git workspace under C:\AI with
a separate loopback port. Do not initially replace the production listener. Reuse the
current Transformers engine and original model files; never duplicate or
modify multi-gigabyte model files unnecessarily.


## Application location and everyday access

The proposed client is a normal local Windows executable. It is not yet
installed; the current browser page is a visual concept.

Use separate source, build, and runtime locations:

~~~text
C:\AI\repos\Local-Dictation\    Canonical Git checkout
  server\                      Shared API source and tests
  docs\                        Design, instructions, and UI concept
  src\                         Windows client source during implementation
  scripts\                     Build/deployment scripts during implementation

C:\AI\build\Local-Dictation\    Generated build/test output

C:\AI\LocalDictation\           Client runtime, outside the checkout
  app\LocalDictation.exe
  data\settings.json
  data\review\
  logs\
~~~

The app runs in the Windows notification area near the clock. Its tray menu
opens Settings and Review. A launcher/shortcut starts it without opening a
terminal. Automatic launch at sign-in is a setting used during the controlled
handover, rather than a prerequisite for trying the candidate client.

Keep the shared API, environment, and existing model store under the current
C:\AI\PalaNote-ASR location. The desktop client references the models through
the API; it does not keep a second copy of their weights.

Source/build artifacts are separate from runtime user data. A future app
update must preserve data and settings. Keep default user data outside
OneDrive by using the stated C:\AI location.


## Recording and banner behavior

Idle operation uses a system tray icon. Starting dictation immediately
shows a non-activating banner on the monitor containing the destination.
The banner does not take keyboard focus from that destination.

Use a compact dark surface, legible system typography, and restrained
controls. The first preview uses Segoe UI, a clear status icon and label,
and separate microphone/model information. This appearance is a proposal;
the implementation must also respect system scale, high contrast, and
reduced motion.

The banner exposes these distinct states:

1. Preparing model: microphone capture has begun; the local model is loading.
2. Listening: capture is active; show the actual microphone level and elapsed
   time. Show whether delivery is live or reserved for review.
3. Finishing last phrase: capture is stopped; remaining audio is being decoded.
4. Insertion paused: the destination is no longer safe to edit; subsequent
   results go to review.
5. API unavailable or microphone unavailable: name the failed part and offer
   a relevant retry or input-selection action.
6. Inserted or Ready for review: show the actual delivery outcome.

A microphone waveform is driven by captured audio. It does not stand in
for API health or decode progress. An unresponsive session must leave the
Listening state within a bounded timeout, not animate indefinitely.

The visible Finish button has the same stop behavior as Ctrl + Shift.
Esc cancels only an active dictation. Cancellation stops pending delivery
and leaves text already inserted in the destination intact.

The modifier-only shortcut must ignore Ctrl + Shift used with another key,
such as Ctrl + Shift + S, and ignore key repeat. Implement a testable chord
state machine with a native hook; do not assume RegisterHotKey handles this
modifier-only gesture. Check interaction with the current Windows keyboard
layout shortcut during native acceptance.

Settings remember a chosen microphone by device identity. If a pinned device
is disconnected, report it visibly and let the user choose another input.
Do not silently keep recording from an unintended device.


## Live text ownership and correction

Capture the destination when recording starts: process/window, focused
editable element, caret or selection, and a short local text anchor. Treat
the transcript as a stable prefix plus a mutable current phrase.

The API may revise the mutable phrase while speech continues. The client
updates only the range it owns, after checking that the destination and
the previously delivered text still match. Stable text is no longer
rewritten. This limits disruption and prevents late whole-transcript
replacement of manual changes.

Invalidate automatic insertion on a user caret move, selection change,
manual edit, window change, field change, or a failed range check. Do not
guess a new destination from whichever window is active later. Do not
automatically reactivate an old window and paste into it.

Use a destination-adapter boundary. Support only adapters that can validate
the owned range and perform a bounded edit. Native text controls and
Chromium/Electron text fields require separate native checks. Unicode
diacritics, punctuation, combining characters, selection replacement, and
line breaks must be verified. Do not implement a blind whole-field SetValue
or an unverified series of Backspace operations as the general solution.

Check focus immediately before each bounded insertion transaction and
invalidate queued delivery when user input changes the target. Because
Windows input and application focus can race, do not promise safe correction
in every application based on a foreground-window check alone.

Unsupported, read-only, password, elevated, or unidentifiable fields use
review. Do not request administrator rights or modify target application
accessibility/security settings to force insertion.

Never submit the destination text. Do not synthesize Enter or a sending
shortcut. Insert multiline text only through a verified text adapter;
otherwise route it to review.


## Review modes and recovery

### Live insertion

The user sees text appear and change in the destination. If the destination
changes, pause all further insertions and corrections. Continue processing
the current recording into review until the user stops; show that delivery
is paused. The review window may appear without stealing focus.

If the delivered prefix is known, distinguish Already inserted from Not
inserted. Offer Copy remaining text and Copy full transcript. This lets the
user avoid copying the same words twice.

If a partly inserted phrase cannot be reconciled after focus loss or a
manual edit, do not invent a clean remaining-text boundary. Show the full
transcript with a clear note that some text may already exist in the
previous field. Require manual review rather than another automatic edit.

### Review before inserting

Do not write into the destination while recording. After stopping, show
the complete editable transcript. The user copies and pastes it manually.
This mode works with the after-recording accurate profile as well.

### Optional second pass with a configurable verification model

Expose the English checkbox Verify after recording and the dropdown
Verification model in Settings. Both are confirmed requested features.
Keep the checkbox label and workflow independent of a model's name.
Initially the dropdown has one eligible option, the registered Large-v3
profile. That is catalog/configuration data, not a special case in the
client's verification code.

Populate choices from the API model registry, filtered by the verification
capability, supported source language, readiness, and resource policy.
Do not equate verification capability with parameter count or a fixed model
ID. A future registered model can become another choice without a client
code change. A new inference architecture may still require a backend
adapter; that does not change the client workflow.

Persist the enabled flag and selected verification model ID independently
of the primary dictation model. Exclude the identical primary model from
eligible candidates for this two-model comparison. If the saved selection
is unavailable or incompatible, explain it and let the user change it or
disable verification; never silently pick a replacement. A dropdown with
one option is still shown so the setting remains discoverable.

Its initial off state is a proposed default. Snapshot the flag, both selected
model IDs, and their pinned revisions when a dictation starts. Changes made
during a dictation apply to the next recording, because earlier audio may
already have been released when the option was off.

When enabled for a supported primary model:

1. The primary model provides and delivers its transcript as usual.
2. At stop, finish that transcript and make its outcome available immediately.
3. Run the selected verification model on the retained original recording,
   using the same source language and no translation or model conversion.
4. Open an interactive Review: one working transcript with highlighted
   disputed phrases. The user chooses Dictation, Verification, or custom
   wording for each region and obtains one assembled result. Keep both model
   originals accessible; neither disagreement nor a different model proves
   which wording was actually spoken.

See the focused [interactive verification review design](2026-11-10-interactive-verification-review.md).
The annotations live in our Review window; destination apps receive plain text.

Do not automatically replace text already delivered to the destination or
manual edits in Review. The user chooses which reviewed version to copy.
Keep any intermediate Review edits separate from immutable model results.

Show Verifying with the selected model's display name as a distinct,
cancellable state. The microphone has already stopped at this point.
Cancelling the check keeps the primary
transcript. A new dictation takes priority: request cooperative cancellation
of the optional check and release its GPU resources before activating the
fast model. Capture can begin into a bounded preparation buffer while this
handover completes; measure that delay and never drop audio silently.

The second pass uses the same serialized model worker. Do not load both
models onto the 4 GiB GPU simultaneously, or call the server's own batch
HTTP route while already holding its admission gate. Release the fast GPU
weights before loading the verifier, then release the verifier after every
outcome. Use the selected model's registry/runtime memory policy instead of
assuming every future verifier uses the same offload layout as Large-v3.

Once primary delivery has ended, do not close the audio session until the
optional check has completed/cancelled/failed and its resources are released.
Distinguish the primary transcript's final event from completion of the
whole session. An error in the second pass must not invalidate an already
successful primary transcript.

### Draft lifetime

Keep review content and manual edits when the review window is closed with
Keep for later. Persist review drafts locally until explicitly dismissed,
using Windows current-user protection and atomic
writes in the app's data directory. Do not persist a general transcript
history or raw microphone audio by default. Clearly distinguish copy from
discard; copying by itself must not discard a draft.

After an API disconnect, retain all text actually received. Do not invent
transcription for audio the server did not process. Do not automatically
replay a new session into an old destination.


## Audio and transcript retention: confirmed policy

Peter explicitly selected: only outstanding Review items are retained;
recordings are temporary. The following paths are the proposed concrete
layout for that confirmed policy.

| Data | Default location | Lifetime |
| --- | --- | --- |
| App settings | C:\AI\LocalDictation\data\settings.json | Across app restarts |
| Successfully delivered normal transcript | Its destination app; no automatic client history file | Controlled by the destination app |
| Text awaiting Review, including second-pass variants | Protected files in C:\AI\LocalDictation\data\review | Until explicitly dismissed; closing the window or copying does not delete it |
| Audio for a live primary when the second pass is off | Bounded rolling RAM buffers in the client/API | Release processed audio as the rolling window advances; release all remaining audio on session exit |
| Complete audio for an after-recording primary | Bounded RAM in the capture/API job; no recording file | Retain until the primary pass ends, or until the optional verification pass ends if enabled; release on every exit path |
| Complete audio when the second pass is on | Bounded RAM owned by the API session | Retain through the second pass, then release on completion, cancellation, error, or disconnect |
| Operational logs | Rotated logs in the client and API log folders | Bounded by the configured log limit; no audio or transcript content by default |

Retain complete audio whenever the primary or verification pass requires a
full recording. A rolling-only buffer is sufficient only for a live primary
with verification off. Release the complete audio after the last required
pass or on cancellation/error/disconnect.

Retaining a full recording in RAM for required passes is temporary processing,
not an audio archive. Use an explicit audio budget and show the supported
duration before recording. Set the exact limit in implementation planning,
consistent with the upload/processing limits. At the limit, stop visibly
and finalize/review; do not discard the beginning or silently write audio
to a temporary file.

Do not store WAV/MP3 recordings, completed normal dictations, destination
field contents, or short range-validation anchors as a general history
under this default. Persist only the dictated content and metadata needed
for a genuine pending Review item. Protect those files for the current
Windows user and save them atomically.

With a RAM-only audio policy, app/server restart cannot recover an unfinished
recording or resume its second pass. Checkpoint in-progress received text as
a protected recovery draft; remove that checkpoint after confirmed normal
delivery unless Review is still pending. Unexpected interruption leaves the
last durable checkpoint available in Review. Do not claim that unsaved audio
or a text update newer than that checkpoint survived the restart.

Once the second pass finishes, the Review item can keep both text variants
without retaining their source audio. Replaying that audio later requires
an explicit recording/history option, not an undisclosed background archive.

A complete text history and recording archive are outside Peter's selected
retention policy. Do not implement or enable them as an incidental feature.


## Model management

Start the approved catalog with the two pinned models already tested on this
machine. A catalog entry distinguishes installed/ready, downloadable, and
locally verified status. A successful download is not a quality benchmark.

Allow a public Hugging Face repository URL in Models settings. Parse a
huggingface.co owner/model identifier and an optional revision, resolve it
to a commit, and show model identity, approximate download size when known,
and declared runtime compatibility before the user starts the download.
Download model data directly; do not upload audio or destination text.

For the current runtime, custom imports must be supported Whisper-family
Transformers checkpoints with original safetensors weights and compatible
processor files. Keep trust_remote_code disabled and never execute code
from a model repository. An arbitrary Hugging Face URL does not imply that
every architecture is supported. Reject unsupported architectures clearly
instead of converting, quantizing, or silently choosing a different model.
Private/gated repositories needing additional credentials are outside this
first public-URL importer; show the access requirement rather than requesting
a token in chat or pretending that the download succeeded.

The shared server owns the registry, compatibility checks, pinned manifests,
download progress, cancellation, and atomic publication. The Windows UI
consumes that service rather than maintaining a second model list. Keep the
existing canonical model IDs and batch clients working. Advertise only
complete, validated installations as ready in /v1/models.

Stage downloads in a task-owned model directory and publish only after all
required files validate. A cancelled or failed import must not damage an
existing installed model or change the active profile. Reuse already
installed pinned weights. Do not delete models as an incidental cleanup.

Imported models initially have explicit Compatible, untested status.
Their declared capabilities control selectable language and delivery modes.
Do not advertise live capability from a compatible config alone: require a
local live readiness check with the same generation precision and memory
policy. An import that cannot fit the available resources reports the
limitation and releases its resources; it does not replace the verified
default profile.

Provide a test-transcription action and measured runtime/quality status.
Live readiness needs a paced streaming check rather than a batch timing.
Specific management route names, download limits, and manifest format are
builder choices for the implementation plan. Verify unsupported URLs,
failed/cancelled downloads, and an additional compatible model end to end.
Use an explicit small test model download after design approval, not a
multi-gigabyte exploratory model download during this design discussion.


## Shared live API

Add a documented live session API alongside the current batch endpoint.
The proposed transport is WebSocket, suitable for duplex audio and result
events from the Windows client and later device clients.

Initial scope stays loopback-only. Retain strict Host and browser-origin
checks. Do not open a LAN listener or relax the production service's access
policy for a future device that is not yet being implemented.

The client sends mono 16 kHz PCM audio in bounded binary frames, plus explicit
start, finish, and cancel messages. Model/language configuration belongs to
the session. The server validates format and limits before admitting it.

Events include session identity, monotonically increasing revision,
utterance identity, stable text, current draft text, audio position, and
an explicit state/final/error outcome. Repeated or stale events must not
duplicate text. A final event closes the revision lifecycle for its phrase.

Own capture buffers and the speech/stability policy on the server so that
Windows and future clients receive the same transcription behavior. Use
overlapping audio/context and stable-prefix agreement; sending isolated
short sentences to the batch endpoint is not the design.

Use a bounded rolling audio buffer and coalesce decode requests: when the
model is busy, process the latest relevant window next instead of accumulating
a queue of stale previews. The implementation plan must choose and measure
the CPU voice/pause detector and buffering thresholds with the pinned model.
Start from roughly 0.5-second preview scheduling and approximately 0.7-second
pause detection, treating both as tuning proposals rather than promises.

During an admitted live session, keep the fast model on the GPU. Release its
GPU weights when primary finalization ends, or on cancel, timeout, disconnect,
and error, retaining the fast CPU cache between sessions. If the optional
second pass follows, switch model ownership as described above. Do not move
the fast model off and back onto the GPU for every fragment.

Share admission ownership between live sessions and batch jobs. Initially
allow one model task/session at a time and return a clear busy outcome to a
second client. Keep health responsive and expose capture/processing/session
state separately. Document heartbeat, idle, backlog, and finalization limits.

Retain the existing batch route's response formats, language/profile rules,
and GPU cleanup behavior. No changes to model weights or generation precision.


## Feasibility and acceptance

Prove the live path with the fast Slovak model before polishing or replacing
the production client. Replay the existing 81-second recording paced as live
audio, and include natural pauses, uninterrupted speech, and silence.

Measure speech-to-draft delay, stable-text delay, finalization delay, quality,
RAM/VRAM, dropped audio, backlog, and disconnect cleanup. State precisely
where latency is measured; total batch processing time is a different metric.

Engineering targets proposed for the first live implementation:

- First visible draft: normally within 2 seconds of speech; report the
  distribution, including session startup, rather than a best case.
- Final primary text after a pause/stop: normally within 2 seconds after
  capture stops. Optional verification has a separate, longer latency and
  is not covered by this target. Any miss is reported and tuned; the target
  is not a measured result.
- No growing decode backlog during several minutes of speech.
- No new boundary omissions/duplications in the reference recording. Compute
  final WER with the existing reference and compare with the batch baseline.
- With the optional check on, verify that the selected verification model
  receives the complete original audio, the primary result arrives before
  its review, differences
  never overwrite manual edits, and cancelling/restarting leaves the primary
  transcript intact. Measure the second pass separately from live latency.
- With verification off, no recording archive or second model job is created.
  A live primary uses bounded rolling audio; an after-recording primary still
  receives its complete recording from bounded RAM. Verify disk contents and
  audio-buffer cleanup under
  success, failure, cancellation, disconnect, and the audio-duration limit.
- Verify the single-option verification dropdown, persisted independent
  selection, unavailable/same-primary rejection, and discovery of another
  eligible registered model without modifying the client. Test generic
  dispatch with a fixture/adapter as well as the installed initial model.
- GPU weights are released after every exit path and the batch profile
  benchmarks remain consistent with the accepted deployed behavior.

Use a native test field, Notepad, and a normal browser text field for
automated/manual delivery checks. Exercise focus switches during decoding,
clicking elsewhere within the same field, user edits, selection replacement,
cancel, short/silent input, API loss, and a disconnected microphone.

Codex is Peter's primary acceptance target. Peter must perform that final
manual application test; agent computer-use must not automate the Codex UI.
Do not claim Codex insertion was verified from a Notepad-only result.

If safe live correction or useful latency cannot be demonstrated in the
required target, report that limitation before rollout. Do not silently
replace live insertion with banner-only previews or review-only behavior.


## Handover and rollback

Keep the production ASR endpoint, TypeWhisper configuration, and login
launcher working during development. The browser concept has no microphone,
global shortcut, live API connection, or real cursor/clipboard delivery.

Use test-mode controls or a temporary alternate shortcut during development
so two clients do not both react to Ctrl + Shift. Switch the real shortcut
owner only at a deliberate handover after acceptance. Preserve a backup of
affected startup and shortcut settings and document how to return to
TypeWhisper. Do not uninstall it.

Promote the backend changes only after regression and live checks pass,
with the existing rollback backup supplemented by the actual changed files.


## References and preview

- Interactive concept: docs/prototypes/recording-review.html in this repository.
- Server source: server/server.py and server/model_engine.py in this repository.
- Existing server deployment: C:\AI\PalaNote-ASR.
- TypeWhisper Windows features:
  https://www.typewhisper.com/en/docs/windows/features/
- TypeWhisper source and branch boundaries:
  https://github.com/TypeWhisper/typewhisper-win
- Whisper architecture and chunked processing:
  https://huggingface.co/openai/whisper-large-v3
- Local-agreement streaming reference:
  https://github.com/ufal/whisper_streaming

These references inform the design. They do not prove that the exact local
model/runtime and destination apps already support this live implementation.


## Design and concept checks completed

The fresh builder check found no important unanswered planning questions
after the model catalog and custom-URL requirement was retained in scope.

The browser concept passed JavaScript syntax checks, unique element/reference
checks, and ten simulated interaction flows. These include live versus
review delivery, review edits surviving close/reopen, focus-change recovery,
known-text recovery after API failure, cancellation, microphone selection,
and accurate-profile delivery restrictions.

The checked main foreground/background pairs have text contrast ratios
between 5.91:1 and 14.40:1. The authenticated local preview serves the updated
screen with HTTP 200. The production ASR health check remains ready and idle.

The initial recording-banner rendering inspection was not completed: the
computer-use tool returned "Computer Use app approval timed out". The later
interactive verification review layout was observed in Chrome; see its
focused design for those checks. Native microphone,
shortcut, insertion, model import, and live API behavior are not implemented
or verified by this concept. Those require the implementation and acceptance
work above.

The subsequent confirmed design amendments add optional model-independent
verification and the explicit pending-Review-only retention policy. The
checkbox/dropdown prototype remains illustrative; it does not execute a
second transcription or write real Review files.

The amended concept passed 14 DOM-simulated interaction checks, including
the unchecked default, one-option dropdown, a different fixture verifier
without model-specific control code, explicit missing-model handling, and
same-primary exclusion. This is not a native application or inference test.

The amendment's builder recheck identified one storage-table omission for
after-recording primary models. It was resolved by retaining full bounded
RAM audio whenever either required pass needs it, and releasing it after the
last required pass. This preserves Peter's selected temporary-audio policy;
no additional product decision was needed.
