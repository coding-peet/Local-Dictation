# Local Dictation

Windows dictation with local Whisper transcription, optional verification,
compact phrase Review, and protected transcript/audio history.

The client uses C# / WPF. The separate Python API uses original Transformers
Whisper models in FP16. The published EXE and its adjacent files are generated
from this repository; model weights and personal data are stored separately.

## Latest installed build: 13 November 2026

Version `0.1.0-banner-model.20261113.1` is installed and running in
C:\Git\Local-Dictation\app, with candidate session API 48125 ready.
The banner shows actual `Auto · SK` / `Auto · EN` language metadata and a small
active model name. Preferred-language tags with `+` restrict correction choices;
clearing all tags restores the full supported language list. Language correction
reuses the original audio. Verify keeps the first transcript in a read-only
banner and uses the resolved source language for its second pass.

Validation: 376 Core tests and 36 windowless UI/lifecycle checks passed; Release
publish had zero warnings/errors. The unchanged backend last passed 132 tests.
All 17 installed files and backups were hash-verified; settings are byte-identical.
Both API processes were preserved. No desktop control, recording or playback
was used for this update. Earlier guarded cursor and pause-worker fixes remain.

See [preferred picker and model caption](docs/development/preferred-picker-model-20261113.md),
[language and Verify](docs/development/input-language-verification-20261113.md)
and [banner layout](docs/development/language-banner-layout-20261113.md) for evidence.
Earlier prepared-Review placement into Outlook through Ctrl+Shift passed a native
check. Complete live speech-pause and automatic recording-to-Outlook Finish
acceptance remain unconfirmed; see [native acceptance](docs/development/native-acceptance-20261113.md).

The source prepared for publication also waits for the previous API owner to
release admission before a language replay and retains duration during Verify.
These final review corrections passed 379 Core tests and 38 windowless checks;
the backend passed 132 synthetic tests. They were not deployed during this Git
task. See [pre-commit verification](docs/development/pre-commit-verification-20261113.md).

## Prepared selected-cursor and Review update

Version `0.1.0-review.20261113.1` is prepared separately; it is not yet the
installed client. Explicit Insert uses the external cursor selected immediately
before the click. Review can compare the original audio with a second model,
and History provides Play/Pause/Stop, seeking, time and an amplitude waveform.

Validation: 421 Core tests, 149 backend tests and 50 windowless controller/UI
checks passed. Release publish succeeded. No desktop control or sound was used;
native OpenAI/Outlook and audio-device acceptance remain manual checks.
See [selected-cursor and Review validation](docs/development/selected-cursor-review-20261113.md).

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

Work directly in `C:\Git\Local-Dictation\repo`, using ordinary branches in
that folder. Do not create additional worktrees. From the checkout root:

~~~powershell
.\scripts\Build.ps1 -AppDirectory 'C:\Git\Local-Dictation\candidate'
~~~

This runs Core tests and publishes Release output. The default SDK is
`C:\AI\tools\dotnet-10.0.403\dotnet.exe`; use `-DotnetPath` for another installed
.NET 10 SDK. Override `-BuildRoot` for a separate validation output directory.
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
| Working branch in this folder | feature/native-windows; ordinary branches, no new worktrees |
| Installed app folder | C:\Git\Local-Dictation\app |
| Settings, Pending Review, latest 20 transcripts and audio | C:\AI\LocalDictation\data |
| Operational logs and diagnostics | C:\AI\LocalDictation\logs and diagnostics |
| Candidate environment and compiler output | C:\AI\build\Local-Dictation |
| Candidate registry/imported model data | C:\AI\LocalDictation\server-data and models |
| Existing production backend and original models | C:\AI\PalaNote-ASR |

History and Pending Review use current-user Windows DPAPI. Deleting source is
not a data backup or migration. A fresh Git checkout restores source, not model
weights, environments, recordings, or keys needed by another Windows account.

## Earlier validation milestones

These dated milestones describe earlier candidates. The installed-build summary,
current product requirements and pre-commit verification above define the latest
state and its remaining acceptance limits.

The [earlier live cursor repair](docs/development/live-cursor-value-range-20261112.md)
addresses empty-field projection and owned-range lookup failures. That snapshot passed
281 Core tests and Release builds. Current native GREEN is blocked by the
company ASR rule on the new diagnostic EXE; real application acceptance is pending.
The first candidate failed Peter's test and its earlier counts are superseded.

The live-quality and cursor-acknowledgement update is installed under
C:\Git\Local-Dictation\app, with candidate session API 48125. It passed
116 backend tests, 84 Core tests, 31 benchmark/report tests, 18 controlled native
WPF delivery checks, and all nine actual production-path model/recording replays.
Ten alternative policies produced 90 measured outputs plus nine baseline controls;
the selected policy preserves original audio and refines the complete recording.
See [live-quality validation](docs/development/live-quality-validation-20261111.md)
for anonymous metrics, exact evidence boundaries and deployment verification.

A follow-up added three dedicated paired-schedule agents and 27 measured outputs,
for 126 total. All outputs were rescored with three supplied/corrected wording
references (257 normalized words). The adaptive whole-final KInIT combination
remains the recommendation for this small workload; longer context does not
improve every profile. See [paired-schedule validation](docs/development/dual-lane-validation-20261112.md)
for the ranking, pre-final comparison, latency and reference-provenance limits.
Agreement with model controls is consistency, not independent accuracy. Replay
timing is not physical microphone latency. Positive Chrome insertion and visible F1 Help
inspection remain unvalidated after the application-control turn stopped.
Outlook and Codex insertion still require Peter's application acceptance.
Static offline Help checks passed. Historical checks are recorded in
[delivery incident validation](docs/development/delivery-incident-validation-20261111.md)
and [UI update validation](docs/development/ui-usability-validation-20261111.md).

The [Outlook investigation](docs/development/outlook-delivery-investigation-20261112.md)
records the physical Notepad acceptance and the remaining Outlook confirmation
mismatch. Avoiding redundant caret selection passes 19 native regression cases;
focused-provider/text-shape diagnostics pass nine pure checks and the windowless
MTA worker passes six. An earlier Outlook candidate received the first partial, then a
one-character projected-document difference blocked complete dictation.
That historical first-paste difference remains distinct from the later owned-range
correction failure.

The current [Outlook delivery update](docs/development/owned-range-repair-20261112.md)
keeps desktop Outlook's live wording in the banner and returns the completed text
once after Finish and any required choices. Other apps retain the saved live
insertion preference. Exact owned-range construction passes 23/23 native fixture
checks, Core passes 125/125, and controller/Review checks pass 35/35. Actual Outlook
acceptance remains separate from those controlled fields and synthetic checks.

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
