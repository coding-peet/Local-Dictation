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
audio-backlog measurements. The committed prefix is finalized at phrase boundaries;
the draft may change.

transcript.final means primary text is final. It does not mean optional verification
or model cleanup has finished. Wait for session.completed for the complete lifecycle.

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

The Windows client separately bounds startup acknowledgement, missing progress,
primary finalization and verification. Timeout/error routes received text to Review.


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
