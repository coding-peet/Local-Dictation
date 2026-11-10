# Existing batch ASR backend

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

The earlier standalone utility is not the planned Windows dictation client.


## Existing API

The deployed service listens on http://127.0.0.1:8123.

~~~text
GET  /health
GET  /v1/models
POST /v1/audio/transcriptions
~~~

The transcription route receives a complete recording and returns a complete
result. Live sessions, verification orchestration, and a dynamic import
registry are planned extensions.

The current initial model profiles are:

~~~text
kinit/whisper-large-v3-turbo-sk
openai/whisper-large-v3
~~~

The existing implementation has profile-specific engine settings. Future
verification dispatch must use registry capabilities and selected model IDs,
not assume that one of these IDs is always the verifier.


## Test without changing the production service

From the repository root:

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\PalaNote-ASR\.venv'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

This selects the existing Python environment and runs checkout unit tests.
It does not start/stop a listener, download a model, or capture audio.


## Runtime separation

The imported baseline resolves models relative to its script directory.
Do not start another production listener from this checkout or download
duplicate weights just to make that baseline path resolve.

The first backend implementation work must add an explicit external model
root and candidate configuration, with regression coverage. Until then,
the deployed installation remains the operational server.

Keep the production .venv, models, recordings, benchmarks with private
payloads, logs, and previous deployment backups outside this repository.
