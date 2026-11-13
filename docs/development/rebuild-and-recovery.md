# Fresh checkout, rebuild, and agent recovery

## What Git restores

A checkout restores application/server source, tests, scripts, icon assets,
offline Help, product requirements, and agent instructions. It must not depend
on an old chat, untracked file, generated EXE, or the original worktree path.

This is source reproducibility with documented dependencies, not a promise of
byte-identical builds across different SDK/package versions or machines.
The verified development SDK is .NET 10.0.403 on Windows x64.

Git does not restore model weights, Python environments, SDK installations,
settings, recordings, pending reviews, or operational logs. Preserve these
separately. Current-user DPAPI files require the original Windows user/key
context; copying them to another account is not a supported recovery procedure.

## 1. Preserve work before removing a checkout

Commit and push intended changes first. Confirm the remote branch and commit,
and separately back up private data that matters. Never delete a dirty checkout
or assume files under .worktrees have their own independent Git database.

The current implementation branch is `feature/native-windows`. A default clone
of main does not select that branch automatically. Until integration into main,
clone or check out this branch explicitly.

Stop any candidate service that imports code from the checkout before removing
or moving that checkout. The verified stop script refuses busy candidates and
the production port:

~~~powershell
.\scripts\Stop-CandidateServer.ps1 -Port 48125
~~~

This stops only the identified idle candidate. It does not delete source,
recordings, models, environments, or the separate production API on 8123.
An ordinary source cleanup must not stop that production API.

## 2. Recover the canonical checkout

On this PC, continue all development in C:\Git\Local-Dictation\repo with
ordinary branches. Do not create additional worktrees. If that folder already
exists, inspect its branch and changes and continue there; do not clone over it,
delete it, or replace its work. The detached former worktree is a preservation
copy and may still be needed by a running candidate service.

Only when the canonical source folder is absent, use these commands to restore
it. They create source files; they do not alter the installed app or runtime data:

~~~powershell
git clone --branch feature/native-windows --single-branch https://github.com/coding-peet/Local-Dictation.git C:\Git\Local-Dictation\repo
Set-Location C:\Git\Local-Dictation\repo
git status --short --branch
git rev-parse HEAD
~~~

If Git requests authentication, use the normal configured credential helper.
Do not paste credentials into project files, agent prompts, or command examples.

## 3. Build and test without the old output

Install/provide a .NET 10 SDK on Windows x64. An installed SDK path can be found
with `Get-Command dotnet`. On the configured PC use the existing portable SDK:

~~~powershell
.\scripts\Build.ps1 `
    -DotnetPath C:\AI\tools\dotnet-10.0.403\dotnet.exe `
    -BuildRoot C:\AI\build\Local-Dictation-restore `
    -AppDirectory C:\Git\Local-Dictation\candidate-restore
~~~

The command restores NuGet packages as needed, runs Core tests, and publishes
Release output into the separate app folder. It can use already cached packages;
otherwise configured package feeds must be reachable. It does not start the
client/API, capture audio, or replace the existing installed application.

Keep every published file together. `LocalDictation.exe` requires its adjacent
assemblies/runtime configuration and the .NET 10 Windows Desktop runtime.
`Help.html` is included. No prebuilt executable or installer is required as input.

The development Start local service action resolves the checkout that built
that EXE. Rebuilding from the new checkout records the new location; it does not
depend on a linked worktree called native-windows. If that source is later removed,
restore the canonical checkout and rebuild or start the API explicitly from
that restored canonical source. A running API is a separate
prerequisite, not part of the client EXE.

## 4. Reuse the existing local API environment

On this PC the candidate environment is outside Git:

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

`VIRTUAL_ENV` selects the existing environment for the Windows `py` launcher.
These tests use synthetic inputs/lightweight engines, not GPU model inference.
The deployed batch environment alone lacks some session API dependencies.

If the candidate environment is missing but the shared ASR environment still
exists, review and run:

~~~powershell
.\scripts\Setup-CandidateServer.ps1
~~~

This creates/prepares the candidate environment, references the existing shared
ASR packages, and installs pinned API dependencies into the candidate only.
It requires the same Python ABI as the shared environment. It is not an export
of the environment for an unrelated PC.

For another PC, prepare a compatible Python/CUDA environment from
`server/requirements-lock.txt` plus `server/requirements-api.txt`; see
[native development](native-windows.md) and [backend setup](../../server/README.md).
Provide the original model files and manifests separately. Package locks do not
install GPU drivers or download model weights.

Start from the new checkout when its source should own the candidate:

~~~powershell
.\scripts\Start-CandidateServer.ps1
Invoke-RestMethod http://127.0.0.1:48125/health
~~~

Do not start a second listener on an occupied port. Wait for `ready=true`.
Do not run inference concurrently with the production service on the same GPU.

## 5. Continue with a new agent

Open C:\Git\Local-Dictation\repo as the agent's workspace. Give it the task, then
ask it to read `AGENTS.md` and `docs/PRODUCT_REQUIREMENTS.md`. It should inspect
the branch, current changes, and validation status before editing.

The current requirements and Help take precedence over older design proposals.
`docs/superpowers` preserves decision history and prior plans. Temporary files
in a previous chat's workspace are not inputs to the build or future work.

Update the product requirements whenever a user changes behavior. Update Help
for changes visible to a user. Keep implementation commands and recovery details
here; keep agent workflow/safety constraints in AGENTS.md.

## 6. Validate an update before replacing the installed client

1. Build to a candidate folder and run the relevant tests.
2. Keep automated policy/controller results distinct from microphone and native
   destination acceptance. Diagnostic instructions are in `diagnostics/*/README.md`.
3. Finish active dictation and exit the installed client normally so it can save.
4. Replace the app folder files, preserving external settings/history/models.
5. Launch normally for shortcut testing. Use F1 in Settings to verify Help.

Outlook and Codex-field insertion are Peter's manual acceptance tests. The
delivery incident update passed all 15 controlled native delivery checks;
passing synthetic fields is not a substitute for a particular application's
acceptance. See delivery-incident-validation-20261111.md for the current evidence.

When testing a failed native destination, optionally launch the client with
LOCAL_DICTATION_DELIVERY_DIAGNOSTICS=1 in its process environment. This enables
bounded local delivery-metadata.jsonl under the client logs directory. It records
provider identifiers, phases, counts, timing and confirmation flags, with no
transcript, audio, clipboard values, UI names or document paths. Leave it unset
for normal operation. Do not substitute a custom external-window inspection tool
when application-control permission is missing.

If the previous client reports clipboard recovery pending, open Resolve clipboard
recovery from the banner, Settings or tray. Inspect the destination and stop any
delayed paste, tick the inspection checkbox, then restore before updating or
stopping that process. Its previous clipboard backup lives only in memory;
force termination cannot preserve it.
