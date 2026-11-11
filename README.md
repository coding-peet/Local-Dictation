# Local Dictation

Windows dictation with local Whisper transcription, optional verification,
compact phrase Review, and protected transcript/audio history.

The client uses C# / WPF. The separate Python API uses original Transformers
Whisper models in FP16. The published EXE and its adjacent files are generated
from this repository; model weights and personal data are stored separately.

## Start here

- [Current product requirements](docs/PRODUCT_REQUIREMENTS.md): user input and
  current expected behavior.
- [Agent instructions](AGENTS.md): what a new agent must read and preserve.
- [Fresh checkout, build, and recovery](docs/development/rebuild-and-recovery.md):
  rebuild without the original worktree or old chat.
- [Offline user Help](src/LocalDictation.App/Help/index.html): also published as
  Help.html and opened by F1 in Settings.
- [Workspace and data locations](docs/development/workspace-layout.md).

## Build the Windows client

Prerequisites: Windows x64, a .NET 10 SDK, and access to the NuGet packages.
Running the resulting app requires the .NET 10 Windows Desktop runtime.
Python, a GPU, and model weights are not required to compile the client.

From the checkout root:

~~~powershell
.\scripts\Build.ps1 -AppDirectory 'C:\Git\Local-Dictation\candidate'
~~~

This runs Core tests and publishes Release output. The default SDK is
`C:\AI\tools\dotnet-10.0.403\dotnet.exe`; use `-DotnetPath` for another installed
.NET 10 SDK. Override `-BuildRoot` when building another checkout concurrently.
The default app output, without `-AppDirectory`, is
`C:\Git\Local-Dictation\app`.

Keep the entire published folder together, including DLLs, runtime configuration
and Help.html. This is a framework-dependent application, not a single-file
self-contained EXE or installer. Build does not launch or update a running app.
Publish to a candidate folder before replacing an installed client.

## Run on the configured development PC

1. Start the session API from the checkout that owns its source.

~~~powershell
.\scripts\Start-CandidateServer.ps1
Invoke-RestMethod http://127.0.0.1:48125/health
~~~

Wait for `ready=true`. The script uses the existing candidate Python environment
and original model files. It does not restart the separate production API on
8123. See the recovery guide if dependencies have not been prepared.

2. Launch the published client normally for shortcut testing.

~~~powershell
& 'C:\Git\Local-Dictation\candidate\LocalDictation.exe'
~~~

`--no-hotkey` is available for an isolated UI check. Configure the microphone,
models, language and shortcut in Settings. F1 opens Help. The default shortcut
is Ctrl+Shift; quit TypeWhisper before using the same shortcut here.

3. Start with the shortcut from an editable destination field. Live insertion
can be enabled or disabled independently of live transcription. After normal
completion, the final text returns to the original valid field. Verification
differences stay in the compact banner: choose the wording for each underlined
phrase, and the last choice inserts the result. Details opens the optional full
editor. Nothing is submitted.

## Data and deployment boundaries

| Purpose | Location on this PC |
| --- | --- |
| Canonical source | C:\Git\Local-Dictation\repo |
| Current branch worktree | C:\Git\Local-Dictation\repo\.worktrees\native-windows |
| Installed app folder | C:\Git\Local-Dictation\app |
| Settings, Pending Review, latest 20 transcripts and audio | C:\AI\LocalDictation\data |
| Operational logs and diagnostics | C:\AI\LocalDictation\logs and diagnostics |
| Candidate environment and compiler output | C:\AI\build\Local-Dictation |
| Candidate registry/imported model data | C:\AI\LocalDictation\server-data and models |
| Existing production backend and original models | C:\AI\PalaNote-ASR |

History and Pending Review use current-user Windows DPAPI. Deleting source is
not a data backup or migration. A fresh Git checkout restores source, not model
weights, environments, recordings, or keys needed by another Windows account.

## Validation status

The live-quality and cursor-acknowledgement update is installed under
C:\Git\Local-Dictation\app, with candidate session API 48125. It passed
116 backend tests, 84 Core tests, 16 benchmark/report tests, 18 controlled native
WPF delivery checks, and all nine actual production-path model/recording replays.
Ten alternative policies produced 90 measured outputs plus nine baseline controls;
the selected policy preserves original audio and refines the complete recording.
See [live-quality validation](docs/development/live-quality-validation-20261111.md)
for anonymous metrics, exact evidence boundaries and deployment verification.

Only one 63-word intended-text reference was independently supplied; agreement
with model controls is consistency, not independent accuracy. Replay timing is
not physical microphone latency. Positive Chrome insertion and visible F1 Help
inspection remain unvalidated after the application-control turn stopped.
Outlook and Codex insertion still require Peter's application acceptance.
Static offline Help checks passed. Historical checks are recorded in
[delivery incident validation](docs/development/delivery-incident-validation-20261111.md)
and [UI update validation](docs/development/ui-usability-validation-20261111.md).

## Repository layout

~~~text
src/LocalDictation.App/          WPF client, icon, offline Help, Windows services
src/LocalDictation.Core/         Protocol, Review, settings, history and ownership
tests/                          Core and packaging tests
server/                         Batch engine, session API, model registry/tests
scripts/                        Build and candidate API lifecycle
diagnostics/                    Controlled diagnostic source and probe instructions
docs/PRODUCT_REQUIREMENTS.md     Current user input specification
docs/development/               Setup, recovery, validation and limitations
docs/superpowers/               Decision history, designs and implementation plans
docs/prototypes/                Design references and synthetic native previews
~~~
