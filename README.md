# Local Dictation

Windows dictation with local Whisper transcription, optional verification,
compact phrase Review, and protected transcript/audio history.

The client uses C# / WPF and Material Design controls. The separate Python API
uses original Transformers Whisper models in FP16. Published client files are
generated from this repository; model weights and personal data stay outside Git.

## Start here

- [Product requirements](docs/PRODUCT_REQUIREMENTS.md): current expected behavior.
- [Offline user Help](src/LocalDictation.App/Help/index.html): published as
  Help.html; open it with Help or F1 in the app's main window.
- [Fresh checkout, build, and recovery](docs/development/rebuild-and-recovery.md):
  prerequisites, service setup and safe client replacement.
- [Agent instructions](AGENTS.md): development constraints and verification.
- [Workspace and data locations](docs/development/workspace-layout.md).

## Current workflows

The main sections are General, Models, Dictation and History. A normal manual
launch opens Dictation. Record/Stop and Help remain available across sections.

- **General:** opt-in Start with Windows, appearance, global shortcut, recording
  retention, and Advanced local-service controls. Start with Windows registers
  the current user's client and opens it in the tray after sign-in.
- **Models:** manage installed and available models. Choose the active dictation
  and verification model roles in Dictation.
- **Dictation:** choose microphone, language, After recording, live transcription
  and verification. Advanced contains the personal dictionary and application
  compatibility controls. Dictionary recognition hints and corrections remain
  disabled pending runtime qualification and paired quality evaluation.
- **History:** search saved text, reopen Review, copy text, and manage retained
  audio. Filters opens a floating draft panel beside search. Apply commits the
  conditions; Cancel, Escape or outside click discards the draft. Only active
  filters occupy a chip row; Clear filters preserves the search text.

Choose **Insert directly** to return completed text to a validated original
field when the workflow permits it. **Preview first** waits for explicit Insert.
Verification can require wording choices; further transcripts also require
explicit Insert. Unsupported or changed destinations stop automatic delivery.
Nothing submits a message or sends the inserted text.

**History -> Import audio** processes files serially without microphone capture
or automatic insertion. Per-file errors explain what to correct; earlier
successful imports and any recoverable partial text remain available. Supported
formats depend on the capabilities of the already running session API.

**Private for next microphone recording** keeps its transcript in memory rather
than saved History or pending storage. A selector lets you choose among pending
private transcripts and explicitly open Review. Exit warns before discarding
private work. Imports do not consume the one-shot private choice.

## Build the Windows client

Prerequisites: Windows x64, a .NET 10 SDK, and access to the NuGet packages.
Running the resulting app requires the .NET 10 Windows Desktop runtime.
Python, a GPU, and model weights are not required to compile the client.

Work in C:\Git\Local-Dictation\repo with ordinary branches. Do not create
additional worktrees. From the checkout root:

~~~powershell
.\scripts\Build.ps1
~~~

This restores packages as needed, runs Core tests, and publishes Release output
into C:\AI\build\Local-Dictation\client-candidate. It does not launch the client,
start/restart an API, capture audio, change startup registration, or deploy files.

To use another installed SDK or separate build/output folders:

~~~powershell
.\scripts\Build.ps1 `
    -DotnetPath C:\AI\tools\dotnet-10.0.403\dotnet.exe `
    -BuildRoot C:\AI\build\Local-Dictation-restore `
    -AppDirectory C:\Git\Local-Dictation\candidate-restore
~~~

The default AppDirectory is always client-candidate beneath the selected
BuildRoot. Build refuses output overlapping the installed app, source checkout,
configured-PC data/backend locations, or a running client's directory. It also
rejects junction/symbolic-link output paths. If client identity cannot be checked,
it stops without publishing. No process is terminated. These checks are a guard,
not a lock: do not launch a client from the output folder during a build.

Keep the complete published folder together, including Material Design DLLs,
runtime configuration and Help.html. This is a framework-dependent application,
not a single-file self-contained EXE or a supported MSI distribution.

Deployment is separate: finish active work, exit the installed client normally
through tray -> Exit, verify it has ended, back up the installed payload, and
replace it from the candidate. Preserve external settings, History, models and
API processes. See the recovery guide for details.

## Run on the configured development PC

Launch the published client normally:

~~~powershell
& 'C:\AI\build\Local-Dictation\client-candidate\LocalDictation.exe'
~~~

Do not run a candidate alongside the installed client. The client preserves an
existing session API listener. If no listener is present, its local-service
startup uses the configured checkout and existing candidate Python environment;
see the recovery guide to prepare missing dependencies or start it manually.
Building the client does not prepare that environment or download models.

Select microphone, model and language in Dictation. Configure the shortcut in
General. The default is Ctrl+Shift; quit TypeWhisper before using that same
shortcut here. Start from the desired editable destination field. Preview first
provides an explicit review step before insertion.

`--no-hotkey` is available for an isolated UI check; it does not guarantee that
service initialization is disabled. `--startup` is the tray-only Windows startup
mode. F1 in the main window opens the offline Help.

## Validation and remaining acceptance

The [17 November filter validation](docs/development/history-filter-popover-20261117.md)
records 640 Core tests, 216 detached WPF checks and 185 synthetic backend tests.
These are dated results for that candidate, not a statement about the version
currently installed or which source a running API has loaded.

Build.ps1 runs Core tests and publish. It does not run the detached WPF,
backend or deployment test suites. The build-script and historical MSI contract
checks can be run without launching a client, installing an MSI or contacting APIs:

~~~powershell
py -B -m unittest discover -s tests/deployment -p 'test_*.py' -v
~~~

Backend tests use the existing candidate environment and synthetic inputs:

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

Automated and detached checks do not prove microphone quality, physical latency,
playback, real popup focus/calendar interaction, Windows sign-in startup, or
insertion into a particular application. Keep those acceptance checks separate.
The older [Outlook check](docs/development/native-acceptance-20261113.md) verified
insertion of prepared Review text, not automatic Finish from a new recording.
Acoustic evaluation and dictionary activation remain deferred.

The [Windows Installer pilot](docs/development/msi-pilot.md) is obsolete for the
current client. Its fixed payload omits Material Design dependencies. Do not use
its MSI to install or distribute this build; passing its fixture-based contract
tests does not prove current payload completeness or successful installation.

## Data and deployment boundaries

| Purpose | Location on this PC |
| --- | --- |
| Canonical source | C:\Git\Local-Dictation\repo |
| Implementation branch before integration | feature/native-windows |
| Installed app | C:\Git\Local-Dictation\app |
| Settings, pending Review, transcripts and retained audio | C:\AI\LocalDictation\data |
| Operational logs and diagnostics | C:\AI\LocalDictation\logs and diagnostics |
| Build output and candidate environment | C:\AI\build\Local-Dictation |
| Candidate registry/imported models | C:\AI\LocalDictation\server-data and models |
| Existing production backend/original models | C:\AI\PalaNote-ASR |

History, retained audio and pending Review use current-user Windows DPAPI.
A Git checkout restores source, not private data, environments, models or keys
needed by another Windows account. General settings and explicit exports are
ordinary files. Private transcripts remain in memory only.

Open **General -> History -> Keep recordings for** to change audio retention.
The default is 7 days. Expiry removes audio only; transcripts, edits and saved
Review choices remain until explicit deletion. Forever keeps recordings until
deletion. A longer retention period cannot restore audio already removed.

## Repository layout

~~~text
src/LocalDictation.App/          WPF client, Windows services, icon and offline Help
src/LocalDictation.Core/         Protocol, Review, settings, History and ownership
tests/                          Core, build and packaging tests
server/                         Batch engine, session API, model registry/tests
scripts/                        Build and candidate API lifecycle
diagnostics/                    Controlled probes and their acceptance limits
docs/PRODUCT_REQUIREMENTS.md     Current user requirements
docs/development/               Setup, recovery and dated validation records
docs/superpowers/               Designs and implementation plans
docs/prototypes/                Design references and synthetic previews
~~~

Older reports under docs/development describe the candidate/date named in each
report. They do not supersede current requirements or certify a later deployment.
