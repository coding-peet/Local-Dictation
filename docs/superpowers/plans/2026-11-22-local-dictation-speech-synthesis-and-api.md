# Local Dictation speech synthesis and API implementation plan

> **For implementers:** Use the existing checkout and a separate client and
> service candidate. Work in the order below and verify each task before
> changing the installed app or the paired Note workflow.

**Goal**  Add local read-aloud, one-chapter playback and MP3 export, and
scoped loopback access to transcription and speech synthesis.

**Architecture**  Extend the current Starlette loopback API. The Windows
client and Bridge remain separate clients of this service. Run synthesis
through a model adapter and coordinate GPU ownership with ASR admission.

**Specification**  [Approved first-release scope](../specs/2026-11-22-local-dictation-speech-synthesis-and-api.md)

## Starting point

- `server/local_dictation/application.py` owns `/health`, the model routes,
  batch transcription, normalization, verification, and the live WebSocket.
  `server/api_main.py` binds to `127.0.0.1`. The loopback guard checks the
  connection. A separate credential check will identify applications and
  enforce permissions.
- `src/LocalDictation.Core/ApiClient.cs` calls the HTTP and WebSocket endpoints.
  `devices/kubo-note/mac-bridge/bridge.py` calls batch transcription from
  paired Note sync. Both clients must transition with the candidate server.
- `server/local_dictation/sessions.py` already owns primary ASR admission.
  `workers.py` joins cancelled work before releasing ownership. The speech
  scheduler must use the same ownership rules for GPU jobs.
- The configured Piper Lili runtime and model are on this PC. The existing
  `speech_piper.py` loads the voice on CPU and writes a mono PCM WAV for the
  Note. Use it as a baseline for the adapter. The candidate API environment
  has PyAV and its `libmp3lame` encoder. Use the configured voice environment
  to qualify the synthesis dependencies for the candidate.

## Preflight on 2026-11-22

- The unchanged server baseline passed 185 tests with synthetic inputs.
- The configured Lili CPU adapter synthesized a 2.635-second 24 kHz mono
  PCM WAV from a synthetic Slovak sentence. The first separate process took
  19.9 seconds wall time including runtime startup and model load. Measure
  first audio with a warm worker before selecting the playback architecture.
- The candidate API environment encoded a second Lili sample to MP3 with
  `libmp3lame` and decoded it back at 24 kHz. These were file validation
  checks. Voice quality and responsiveness remain part of live acceptance.

## Task 1. Scoped loopback API and built-in clients

- [ ] Add endpoint tests in `server/test_application.py` for absent, revoked,
  valid and wrong-scope keys. Cover batch, model tests, normalization,
  verification and the live WebSocket. Check auth before accepting upload
  bodies or admitting an inference job.
- [ ] Add shared authorization in `server/local_dictation/` and wire it into
  `application.py`. Generate random per-application keys, store key digests,
  and record scope and last use. Keep the local admin credential and the two
  built-in client credentials in owner-protected storage outside the repo.
  Use the app's API settings page for create, reveal once, and revoke. Limit
  `/health` to operational readiness. History and notes stay outside the API.
- [ ] Provision separate keys for Local Dictation and Bridge and wire both
  clients before enabling enforcement in the integrated candidate. Send the
  Windows client key on HTTP and WebSocket requests and the Bridge key on
  transcription. Keep configuration and logs free of application secrets.
- [ ] Verify both built-in clients against the same candidate during live
  dictation and Note sync, including the Note's Ask Kubo flow. Add the **API**
  page in the Windows Pala settings with the service address, readiness and
  the application name, scopes, last use and revoke controls. Treat key
  administration as an owner-local operation.

## Task 2. Local synthesis and model lifecycle

- [ ] Add a speech adapter that returns PCM plus the backend that actually
  ran. Test it with Piper Lili CPU and a bounded synthetic Slovak input.
  Reuse the Note adapter's model loading where useful and keep the Note's
  existing output format and launch path available during migration.
- [ ] Add `POST /v1/audio/speech` behind the `Speech` permission. Validate
  text length, language, voice, model availability and compute selection
  before inference. Add scope, input-limit and disconnect tests with a fake
  engine. Return audio to the caller without writing to user history.
- [ ] Extend the existing **Models** workflow with a speech model catalog.
  Separate model IDs from voice IDs. Inspect supported public Hugging Face
  URLs, known formats, size and runtime compatibility before downloading.
  Reuse installation publication and removal safeguards. Retain unavailable
  selections in settings so users can identify and replace them.
- [ ] Report actual backend, time to first usable output and peak GPU memory
  in model tests. Test unsupported compute options and failed GPU runs.
  Keep Lili for short Slovak and Kokoro for English. Qualify Supertonic 3 on
  the same Slovak prose and technical text before making it the chapter
  default. Confirm the voice by listening on the target PC.

## Task 3. Scheduling, playback and Windows workflows

- [ ] Extend admission in `server/local_dictation/sessions.py` with a bounded
  speech GPU job that can yield between paragraphs. Give interactive ASR
  priority. Explicit GPU speech waits with a status. Auto short speech may
  use a verified CPU backend. Keep one paragraph ready ahead of playback.
  Test ASR arrival during synthesis and cancellation, model removal and
  failure recovery. Measure first audio and paired ASR latency on this PC.
- [ ] Add a read-aloud shortcut to the Windows input/controller workflow.
  Read the focused app's UI Automation selection and offer a separate
  clipboard action. Preserve the primary-language default and manual voice
  and language selection. The shortcut stops playback and dictation can
  interrupt playback without losing chapter position.
- [ ] Add the distinct chapter view for pasted text or one TXT or Markdown
  file. Preserve text, paragraph order, position and one voice per chapter.
  Reuse the local audio output lifecycle for pause and resume. Encode full
  chapter MP3 through PyAV and publish the file only after successful export.
  Test the complete playable file, cancellation, and GPU priority during
  playback and export.

## Verification and first candidate

Start with Piper Lili CPU audio returned by the scoped loopback API. Use
synthetic HTTP and WebSocket tests before introducing a real model or the
Windows UI. Exercise this candidate on a free loopback port and keep its
app, service data and logs separate from installed Local Dictation.

Run the server suite, .NET Core tests and Note/Bridge tests before an
integrated handover. Record physical Windows selection and playback, the
actual backend with time to first audio and peak VRAM, playback interruption,
MP3 readback, and paired Note results. Full-document reading and the LAN
HTTPS listener belong to later releases.

### Candidate verification on 2026-11-22

- The scoped candidate ran on `127.0.0.1:48126`. Local Dictation and Bridge
  used separate keys. A missing key returned 401. The Bridge key could
  transcribe, and a speech request with that key returned 403.
- Piper Lili on CPU returned a 4.585-second Slovak WAV in 2.351 seconds in
  one API check. The published .NET client also synthesized and normalized
  speech, then used its authorized live WebSocket to obtain the expected
  Slovak transcript. Both keys passed live batch transcription checks.
- The physically attached Note returned the exact 348,204-byte test WAV
  over USB. The Bridge used its key for transcription. A retry succeeded
  after the first attempt did not complete the return transfer. The saved
  transcript read back from the Note matched the candidate output.
- The server, Note/Bridge and .NET Core suites passed 186, 197 and 654 tests,
  respectively. The isolated Windows client was published for this candidate.
