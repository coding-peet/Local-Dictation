# Local Dictation engineering instructions

## Scope and language

This repository owns the Windows client, shared ASR server source, tests,
design documents, prototypes, and build/deployment scripts.

Use English for application copy, documentation, and commit messages.
Use py instead of python in commands on this Windows machine.

Read the product design in docs/superpowers/specs before implementing.


## Source and runtime separation

Canonical checkout: C:\AI\repos\Local-Dictation.

Published client: C:\AI\LocalDictation\app.
Client data: C:\AI\LocalDictation\data.
Client logs: C:\AI\LocalDictation\logs.
Build output: C:\AI\build\Local-Dictation.
Existing backend runtime and models: C:\AI\PalaNote-ASR.

Keep generated binaries and real user data outside the checkout. Never
commit recordings, transcripts, pending reviews, logs, credentials,
downloaded model weights, virtual environments, or SDK installations.

Edit source in the repository. Treat C:\AI\PalaNote-ASR as a deployed
runtime; do not edit or restart it as incidental setup work. Use a separate
candidate listener and shared read-only model files during development.


## Product boundaries

- All recording and transcription are local.
- Preserve original model weights and FP16 behavior; no conversion or quantization.
- Verification is a generic role with a registry-backed model dropdown.
  Large-v3 is the initial selection, not a hardcoded workflow.
- Keep only pending Review items; audio is temporary.
- Never submit the text inserted into another app or synthesize a send shortcut.
- Stop automatic delivery when the owned destination cannot be validated.
- Codex insertion requires Peter's manual application acceptance test.
- Preserve the working TypeWhisper setup until controlled handover.


## Verification

Run the existing backend tests with the deployed Python environment:

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\PalaNote-ASR\.venv'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

These are unit tests, not proof of live microphone latency, native insertion,
or quality in a particular application. Keep those claims separate.

Write readable engineering documentation with headings, short paragraphs,
and separate command blocks. Explain what setup commands change.
