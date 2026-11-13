# Local Dictation API v1

## Boundary

The server binds only to 127.0.0.1. Host must identify localhost or 127.0.0.1.
Any Origin header is rejected, including WebSocket origins. This interface is
for local native clients, not browser JavaScript. LAN access and Pala Note
firmware are future work.

No API key or cloud account is needed. Do not expose this unauthenticated
loopback listener to a network.


## Discovery and batch compatibility

~~~text
GET  /health
GET  /v1/models
POST /v1/audio/transcriptions
POST /v1/models/test
~~~

Batch transcription accepts multipart/form-data with one file and optional
model, language, prompt and response_format fields. Send Content-Length.
Maximum multipart body: 26 MiB. Formats: json, verbose_json, text.

The test route uses the same engine/admission and marks a model runtime-tested
only after inference and model cleanup both succeed.

GPU ownership is shared by batch, model tests and live sessions. A busy primary
returns a conflict. A new primary can cooperatively stop optional verification;
admission stays held until its worker and cleanup finish.


## Verify a saved original recording

~~~text
POST /v1/audio/verifications
~~~

Send multipart/form-data with Content-Length and these fields:

- file: original mono 16 kHz PCM16 WAV, within the 600-second recording limit.
- model: installed, compatible, runtime-tested verifier ID.
- primary: the exact already received primary text, up to 64 KiB UTF-8.
- primary_model: original primary model ID; it need not remain installed.
- primary_revision: known original revision, or omit when unknown.
- language: effective language, or auto.
- preferred_languages: optional nonempty JSON array of Auto language codes.
  Omit this field when there are no preferences; do not send an empty array.

The verifier must differ from the primary and support the requested language.
Only the verifier runs; the primary is not transcribed again. The response has
primary, verification, ordered pieces, comparison_id, both model IDs/revisions,
language/language_mode and duration. These use the same exact-string comparison
contract as verification.completed below.

The route uses verification admission and releases its model after completion,
failure or disconnection. A new primary can preempt it cooperatively. Wait for
ready and idle before uploading a replay; admission remains held through cleanup.
Validation errors return 400, unavailable/busy/cancelled work returns 409, and
inference/cleanup failures return 500. Errors do not include transcript content.


## Remove an app-managed model

~~~text
POST /v1/models/remove
{"model_id":"owner/model"}
~~~

Model discovery exposes can_remove and removal_restriction. Only a verified
app-owned managed installation can be removed. Shared originals stay in place.
Removal is serialized with inference and downloads; unsafe paths, links and
overlapping ownership are refused. Owned files are quarantined before atomic
registry persistence, with rollback if persistence fails. The response contains
model_id, removed, cleanup_pending and message. Cleanup leftovers are reported
without falsely restoring the model to the installed list. This endpoint is an
explicit user action; ordinary inspection/tests must not call it on real models.


## Duplex audio

Connect a native WebSocket client to:

~~~text
ws://127.0.0.1:48125/v1/audio/sessions
~~~

Do not attach an Origin header. First send:

~~~json
{
  "type": "start",
  "protocol_version": 1,
  "model": "kinit/whisper-large-v3-turbo-sk",
  "language": "sk",
  "live": true,
  "verification_model": null
}
~~~

The server validates model installation, language and live capability.
Send PCM16 little-endian mono 16 kHz in aligned binary frames, at most 64 KiB
each. There is no WAV header in these frames.

Finish normally:

~~~json
{"type":"finish"}
~~~

Cancel:

~~~json
{"type":"cancel"}
~~~

Cancel preserves received text on the client and releases audio after safe
worker cleanup. It does not revert previously inserted text.

Events carry type, session_id and increasing revision. Text events contain full
snapshots; apply them only within their matching session and increasing revision.

~~~text
session.started
session.progress
recording.limit_reached
transcript.updated
transcript.final
verification.started
verification.completed
verification.failed
verification.cancelled
session.completed
session.cancelled
session.error
~~~

A validation error before session admission can be a session.error without an ID.
session.started additionally snapshots model_revision, verification_model,
verification_revision and session_started_utc. Unknown revisions stay null.

transcript.updated carries text, committed_text and draft_text, plus decoder/
audio-backlog measurements. The current live policy grows a preview after three
seconds of audio, commits quiet-valley spans within 12–24 seconds, and suppresses
isolated quiet fragments. Both visible wording and the primary final can change.
The complete original audio is retained through the configured duration limit;
inference backlog no longer triggers a 32-second preserving stop.

transcript.final means primary text is final. In live mode it follows a complete
original-audio primary refinement, or reuses an exact unchanged whole-audio
preview. The optional audio_received_seconds field reports accepted sample
duration; refined indicates that live primary finalization used this path.
No isolated tail is decoded immediately before the complete final pass.
An unexpectedly empty replacement after received text fails without blanking the
preserved draft. Final does not mean optional verification or model cleanup has
finished. Wait for session.completed for the complete lifecycle.

verification.completed includes immutable primary and verification strings and
ordered pieces, both pinned revisions and comparison_id. Each piece has id, primary, verification and different. Compose
by concatenating the chosen exact strings. Empty alternatives are meaningful.
Do not interpret string offsets across Python Unicode and .NET UTF-16.

session.progress reports actual phase/audio receipt/decode activity. It is a
liveness signal, not evidence of recognition quality. Client finalization deadlines
are absolute and are not extended by heartbeats.

Start-message timeout is 10 seconds; audio idle timeout is 30 seconds.
The recording wall/sample limit is 600 seconds. Reaching it finalizes accepted
audio rather than cancelling it. Model work has a bounded processing deadline;
cancellation still joins the model worker before another job can enter.

The Windows client separately bounds startup acknowledgement (10 seconds), missing
progress (15 seconds), and absolute finalization (600 seconds, matching batch).
Heartbeats do not extend that absolute deadline. Timeout/error routes received
text to Review; Escape remains cooperative cancellation.


## Public model import

~~~text
POST /v1/model-imports/inspect
POST /v1/model-imports
GET  /v1/model-imports/{job}
POST /v1/model-imports/{job}/cancel
~~~

Inspect with a repository URL or owner/name:

~~~json
{"repository":"https://huggingface.co/openai/whisper-tiny"}
~~~

For a catalog model, send catalog_model_id instead of repository; the server
uses its pinned revision. Registered models also retain their pin when inspected
for repair. Healthy installed models cannot be replaced by this operation.

The response identifies the pinned commit, original files, total bytes and
compatibility. It does not download the model or establish quality.

Start an explicit download using the returned server-owned inspection ID:

~~~json
{"inspection_id":"the-returned-id"}
~~~

Poll the returned job ID. States: queued, downloading, completed, cancelled, failed.
Cancel is cooperative; publication is the installation boundary.

Model discovery distinguishes compatible, installed, runtime_tested,
quality_verified, live_supported, live_ready and verification_eligible.
Consumers must not substitute one state for another.


## Reproducible local replay

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B scripts\replay-session.py 'C:\path\sample.webm' --output 'C:\AI\build\Local-Dictation\private-replay.json'
~~~

This replays an existing file at real-time pace. It does not use the microphone,
type into another application, or upload audio beyond loopback. Its output contains
private transcripts: keep it outside Git.

--batch selects after-recording primary mode. --verify takes a registry model ID.
--pace changes replay speed; only --pace 1 is a real-time latency measurement.
