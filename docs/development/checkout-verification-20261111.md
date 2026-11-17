# Clean-checkout verification — 11 November 2026

Verified implementation commit:
`1876f1a6f4f2e0f9e757a144ae10e1e483f77a89` on `feature/native-windows`.

## Independent source checkout

Created a separate local clone with `--no-hardlinks --single-branch` at:

~~~text
C:\Git\Local-Dictation-recovery-check\repo
~~~

The clone contained only committed files. It did not copy the old checkout's
untracked files, .superpowers directory, generated output, or chat scratch files.
No existing checkout was removed. The clone was clean before and after building.

## Build and tests

Ran the committed Build.ps1 from the new clone using the existing SDK and a
previously unused build/output root:

~~~powershell
.\scripts\Build.ps1 `
    -BuildRoot C:\AI\build\Local-Dictation-recovery-check `
    -AppDirectory C:\Git\Local-Dictation-recovery-check\app
~~~

- Core: 84 passed, zero failures/skips.
- Release publish: passed with all source/icon/Help inputs from the new clone.
- CompactFlowProbe rebuilt from the clone: 15/15 passed.
- ReviewInputProbe rebuilt from the clone: 8/8 passed.
- Backend suite on the identical implementation tree: 86 passed.
- Temporary packaging contract tests: 4 passed; no installation or retained MSI.

The SDK was 10.0.403. NuGet restore used the configured package cache/feeds.
This verifies a clean source rebuild, not byte identity across arbitrary SDKs,
an offline package bootstrap, or recreation of external Python/GPU/model assets.

## Published content and checkout binding

A separate .NET reflection check loaded the new LocalDictation.dll and read
its actual AssemblyMetadataAttribute. `LocalDictation.SourceRoot` was:

~~~text
C:\Git\Local-Dictation-recovery-check\repo
~~~

The referenced launcher script exists in that new clone. The original linked
worktree path was not used. The check did not launch or restart an API.

Required EXE, application/Core assemblies, NAudio, dependency/runtime
configuration, and Help.html were present. Published Help matched the new
clone's source byte-for-byte by SHA-256. A source build also verified the
required embedded icon inputs.

HTML checks passed: balanced tags, 20 unique IDs, 12 valid internal anchors,
no remote resource dependency, and the restrictive offline CSP. Markdown
entry-point links were checked for existing targets.

## Acceptance limits

Controller/input probes use synthetic text and temporary protected data. They
do not establish microphone latency, recognition quality, physical held-key
timing, or native destination compatibility. The preceding native delivery
probe was stopped by Escape; that acceptance was not repeated in this task.
Codex-field insertion remains Peter's manual test.

Help content and structure were reviewed; visual/application-level Help opening
was not repeated. The candidate was not deployed over the installed app.
No production API, model precision/weights, security settings, or TypeWhisper
configuration changed.
