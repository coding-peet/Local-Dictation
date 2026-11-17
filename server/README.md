# Existing batch ASR backend

The native Windows client uses the additional api_main.py session server
and local_dictation package on candidate port 48125. It supports live sessions,
verification, and model imports separately from the existing batch service.

See the [current client guide](../README.md),
[rebuild and recovery guide](../docs/development/rebuild-and-recovery.md),
and [session API documentation](../docs/development/api.md).
The legacy batch service described below remains available separately.

## What is included

This directory contains the working Python batch-server source imported
from the deployed C:\AI\PalaNote-ASR installation on 2026-11-10.

- server.py: loopback HTTP service and request admission.
- model_engine.py: original FP16 model profiles and GPU/CPU lifetime.
- asr.py: model path/audio helpers and the earlier standalone file utility.
- stop_server.py: guarded stop helper.
- benchmark_tools.py: word-error calculation helpers.
- test_*.py: existing unit coverage.
- requirements-lock.txt: the pinned environment used by this deployment.

The earlier standalone utility is separate from the native Windows dictation
client.


## Existing API

The deployed service listens on http://127.0.0.1:8123.

~~~text
GET  /health
GET  /v1/models
POST /v1/audio/transcriptions
~~~

The transcription route receives a complete recording and returns a complete
result. Live sessions, verification orchestration, and the model import
registry belong to the separate session service on port 48125.

The separately deployed legacy service on 8123 still has these two initial
model profiles:

~~~text
kinit/whisper-large-v3-turbo-sk
openai/whisper-large-v3
~~~

The repository candidate source has three supported production profiles,
including original OpenAI Turbo below; it does not imply that the deployed
legacy service has gained the third profile. The batch engine has
profile-specific settings.
The native client's verification choices use registry capabilities and selected
model IDs; neither of these IDs is a fixed verifier.

## Current candidate profiles and live policy

The repository's candidate source also supports original
`openai/whisper-large-v3-turbo`, pinned at
`41f01f3fe87f28c78e2fbf8b568835947dd65ed9`. Import it with the existing model
registry; original weights belong in managed C:\AI storage, not this checkout.
Source changes here do not update the separate deployed service on 8123.

KInIT keeps its Slovak greedy profile. Original OpenAI Turbo uses greedy native
long-form recognition entirely in CUDA FP16. Original Large-v3 keeps beam search
and encoder-layer CPU offload to fit this PC's GPU. No model is quantized.

The candidate live policy uses growing previews at roughly three seconds of new
audio, quiet-valley commits within 12–24 seconds, and a whole-recording primary
refinement after Finish. It preserves queued input through the configured
recording limit. Optional verification remains a separate model pass. Empty
replacement results preserve prior text for recovery.

The ten-alternative experiment, controls and interpretation are documented in
`docs/development/live-quality-validation-20261111.md` from the repository root.
Data-free experiment tooling is in diagnostics/LiveBenchmark. All actual private
recordings, intended text, timelines and reports remain outside Git.


## Test without changing the production service

From the repository root:

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

VIRTUAL_ENV tells the Windows py launcher to use the prepared candidate
Python environment. The full checkout suite includes session API tests, whose
dependencies are not all installed in the deployed batch environment.
Use the rebuild and recovery guide above to prepare the candidate environment.

These tests use synthetic data. They do not start/stop a listener, download
models, capture audio, or validate real transcription quality.


## Runtime separation

The imported baseline resolves models relative to its script directory.
Do not start another production listener from this checkout or download
duplicate weights just to make that baseline path resolve.

The session service already uses an explicit external model root and separate
candidate configuration. scripts/Start-CandidateServer.ps1 defaults to
C:\AI\PalaNote-ASR\models for existing weights and port 48125 for the
candidate API. It refuses the batch service's port 8123. See the rebuild and
recovery guide for setup and custom paths.

The native client keeps the latest 20 transcripts and recordings in protected
local History; Pending Review is separate. Client data belongs outside this
source directory. See the current client guide for retention and export.

Keep the production .venv, models, recordings, benchmarks with private
payloads, logs, and previous deployment backups outside this repository.
