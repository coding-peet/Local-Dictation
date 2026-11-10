# Local Dictation

Local Windows dictation through a reusable, local speech-to-text API.

The planned client uses Ctrl + Shift to start or stop dictation, shows a
bottom recording banner, and inserts live text into the active text field.
Pending text can be reviewed when the destination changes. Optional
verification uses a separately selected model after recording.

All application copy and project documentation are English.


## Current status

This repository contains the existing working batch ASR backend, its tests,
the product design, and an interactive UI concept.

The Windows client, live audio sessions, configurable verification workflow,
and model-management interface are not implemented yet. The HTML concept
does not record audio or insert text into another application.

Start with:

- [Product design](docs/superpowers/specs/2026-11-10-live-dictation-design.md)
- [Workspace layout](docs/development/workspace-layout.md)
- [Backend baseline](server/README.md)
- [UI concept](docs/prototypes/recording-review.html)


## Source, build output, and installed application

This repository is the source of truth for application code, documentation,
tests, and build/deployment scripts.

The local checkout is:

~~~text
C:\AI\repos\Local-Dictation
~~~

The published Windows application will be outside the checkout:

~~~text
C:\AI\LocalDictation\app\LocalDictation.exe
~~~

Runtime settings, pending reviews, and logs will also be outside Git:

~~~text
C:\AI\LocalDictation\data
C:\AI\LocalDictation\logs
~~~

Build output is reserved for:

~~~text
C:\AI\build\Local-Dictation
~~~

The existing deployed backend, Python environment, and model files remain at:

~~~text
C:\AI\PalaNote-ASR
~~~

The runtime folders are prepared, but no Windows executable has been
published yet. Repository initialization does not replace the running
backend or change TypeWhisper.


## Repository contents

~~~text
server/                         Existing batch ASR source and unit tests
docs/superpowers/specs/          Product design and confirmed decisions
docs/development/               Workspace and development instructions
docs/prototypes/                Browser-only UI concept
~~~

The Windows client and build/deployment scripts will be added here during
implementation. Their generated binaries will be published outside this
checkout.


## Run the existing backend tests on this machine

Open PowerShell and run:

~~~powershell
Set-Location 'C:\AI\repos\Local-Dictation'
$env:VIRTUAL_ENV = 'C:\AI\PalaNote-ASR\.venv'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

VIRTUAL_ENV tells the Windows py launcher to use the existing Python
environment. This command runs the backend unit tests from the checkout.
It does not start the production server, download models, record a
microphone, or change the installed application.

Other machines need a compatible Python environment and the backend
dependencies first. The pinned lock file describes the currently tested
Windows/CUDA environment; portable setup will be documented during
implementation.


## Models and local data

The current backend uses the original FP16 KInIT Turbo Slovak and Whisper
Large-v3 checkpoints. Model conversion and quantization are not part of
the design.

Model weights, Python environments, SDKs, build output, audio, transcripts,
pending reviews, logs, and credentials do not belong in Git. The ignore
rules provide a guard against accidentally adding common runtime files.

Only pending Review items will be retained by the client. Normal successful
dictations are not archived. Audio remains temporary, including when a
second model needs the complete recording for verification.
