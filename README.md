# Local Dictation

Native Windows dictation with a local, reusable transcription API.

The client is implemented in C# / WPF. The backend uses the original
Transformers Whisper checkpoints in FP16. Model conversion and quantization
are not used.

**Current status:** an initial native build is published on the development
machine. Core and API tests pass. Corporate Microsoft Defender currently
blocks the unsigned new EXE, so native visual, microphone, global-shortcut
and cursor-insertion acceptance remain pending. Live transcription is
experimental: its first measured Slovak result was less accurate than the
after-recording result.

See [the completion follow-up](docs/development/completion-followup.md) and
[validation and remaining checks](docs/development/validation.md) before
using direct insertion for everyday work.


## Start on the development machine

After official approval to run the executable:

1. Start the separate session API from this checkout.

~~~powershell
.\scripts\Start-CandidateServer.ps1
~~~

This opens a hidden local Python process on port 48125. It reuses the existing
original models read-only. It does not restart the deployed server on 8123. Wait for ready=true before
recording; cold library startup can take one to two minutes.

~~~powershell
Invoke-RestMethod http://127.0.0.1:48125/health
~~~

2. Open the native client without registering the shortcut for the first check.

~~~powershell
& 'C:\AI\LocalDictation\app\LocalDictation.exe' --no-hotkey
~~~

This opens Settings. Choose the actual microphone, Dictation model and
language. Use Record to Review to check the microphone from this window.

3. For direct dictation, quit TypeWhisper before enabling Ctrl+Shift in
Local Dictation. Close the test launch, start LocalDictation.exe normally,
and use Recheck shortcut. The client blocks its shortcut while TypeWhisper
is running.

4. Place the cursor in an editable field outside Local Dictation.
Press and release Ctrl+Shift to start. Repeat to stop.

The bottom banner shows the real recording state, microphone level and time.
It does not activate another window. Local Dictation never sends Enter or
a submit shortcut.

When a field, cursor, window or user edit invalidates delivery, the client
keeps the complete transcript for native Review. Unsupported, protected,
read-only and elevated destinations also use Review.


## Review and verification

Enable **Verify after recording** and select a **Verification model**.
Verification is a generic model role. Large-v3 is initially available;
additional successfully tested compatible models can appear in the dropdown.

After Stop, the primary result is finalized first. The verification model
then transcribes the original recording. Review highlights different words
or phrases. Choose Dictation, Verification, or custom wording for each
difference. Empty alternatives can remove or insert text.

Keep remaining dictation resolves only untouched differences. Undo restores
prior choices. A final manual draft is separate and takes precedence until
you explicitly rebuild it from choices.

The Original tabs show the complete immutable source transcripts. Recording
details preserve model revisions and comparison identity when available.

Copy reviewed text returns plain text. It does not automatically replace
text already inserted in another app. Copy and Close preserve the pending
Review item. Dismiss explicitly removes it.

Both models can be wrong; comparison identifies differences rather than
establishing which wording is correct.


## Try another model

In Settings, select Models. Missing catalog models can be selected there for
pinned installation or same-revision repair.

- Choose an installed model from the Dictation dropdown and use Test selected
  model with audio.
- To import a public checkpoint, paste its Hugging Face URL or owner/name.
- Inspect compatibility and download size.
- Start the explicit download; its original files are pinned to a commit.
- Select the installed model and run a transcription test.

The initial runtime accepts built-in Whisper Transformers models with original
safetensors files. It rejects remote-code architectures, unsupported formats,
and private or gated repositories. No Hugging Face token is needed.

Installed, runtime-tested, batch quality-checked, and live-ready are distinct
states. Downloading or completing one runtime test does not validate quality
or live performance. Imported models start in after-recording mode.


## Where files live

| Purpose | Development-machine location |
| --- | --- |
| Canonical source checkout | C:\AI\repos\Local-Dictation |
| Isolated implementation checkout | C:\AI\repos\Local-Dictation\.worktrees\native-windows |
| Published client | C:\AI\LocalDictation\app\LocalDictation.exe |
| Settings | C:\AI\LocalDictation\data\settings.json |
| Pending Review | C:\AI\LocalDictation\data\review |
| Backend registry | C:\AI\LocalDictation\server-data |
| Imported original models | C:\AI\LocalDictation\models |
| Logs | C:\AI\LocalDictation\logs |
| Build output | C:\AI\build\Local-Dictation |
| Existing deployed ASR and initial models | C:\AI\PalaNote-ASR |

Only pending Review items persist. They are protected with Windows current-user
DPAPI encryption and atomic file replacement. Successfully delivered dictations
are removed from that pending store.

Audio stays in bounded RAM. Live mode without verification uses a rolling
buffer. An after-recording primary or optional verifier needs the complete
recording in RAM. The visible maximum is 10 minutes. Audio is released after
the last required pass, cancellation, failure or disconnect. It is not
recoverable after a restart.

A failed protected write keeps the item in memory and shows an unsaved warning.
Copy it before exiting. There is no audio archive or automatic transcript
history.

Source, private audio, model weights, environments, SDKs and generated binaries
are kept separate. Never add runtime data or recordings to Git.


## Build and test

Requires a .NET 10 SDK and the Windows Desktop runtime. This machine uses the
portable SDK at C:\AI\tools\dotnet-10.0.403; global PATH was not changed.

~~~powershell
.\scripts\Build.ps1
~~~

This runs native core tests, compiles Release, and publishes outside the
checkout. It does not launch the application or change startup settings.

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

VIRTUAL_ENV tells the Windows py launcher which Python environment to use.
These tests use lightweight test engines. Real model measurements and native
application checks are separate.

See [development setup](docs/development/native-windows.md) for environment
preparation, API use and controlled handover.


## Repository layout

~~~text
src/LocalDictation.App/          Native WPF client and Windows services
src/LocalDictation.Core/         Protocol, Review, settings and ownership logic
tests/LocalDictation.Core.Tests/ Native core tests
server/local_dictation/         Session API, registry and public model imports
server/                         Original batch engine and backend tests
scripts/                        Build, candidate lifecycle and local API checks
docs/development/               Setup, validation and limitations
docs/superpowers/specs/          Approved product design
docs/prototypes/                Earlier browser-only design concepts
~~~

The browser concepts are design artifacts. Runtime Review belongs to the
native client.
