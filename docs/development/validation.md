# Validation — 2026-11-10

The later [completion follow-up](completion-followup.md) supersedes the initial
deferred list below: those four items are implemented; native acceptance remains
open. Current automated totals are 86 Python and 39 native core tests.

## Build and automated tests

- Backend: 77 tests passed.
- Native core: 28 tests passed.
- Native WPF build: zero errors and zero warnings.
- Repository Build.ps1 tested end to end; Release published outside source.
- PowerShell scripts parsed successfully.
- Source whitespace check passed.

Unit regressions cover request/task cancellation while inference is running,
joining before unload, normal limit completion with accepted batch audio,
silence, cleanup failure, model-language eligibility, incomplete installations,
registry rollback, pending Review durability failures, immutable phrase choices,
manual draft preservation, held third modifiers, Escape command routing,
startup destination generations, and absolute session deadlines.

These checks exercise domain/transport behavior. They do not certify Windows
microphone capture, real shortcut routing or delivery into a third-party app.


## Native execution blocker

An attempted launch of the development EXE was denied. Read-only diagnosis found
ordinary executable ACLs and Microsoft Defender event 1121 for corporate ASR rule:

~~~text
01443614-CD74-433A-B99E-2ECDC07BFC25
~~~

No native application window opened. Native visual, DPI/high-contrast, real
microphone, global-hotkey and UIA insertion acceptance were therefore not completed.

Later, Peter's manual launch of the published package triggered the same rule.
Read-only Get-MpPreference and Policies registry inspection confirmed action 6
(Warn). Event 1121 alone did not establish an unconditional Block-mode policy.
The native prerequisite is now the built-in Windows Security Unblock flow and
its administrator approval, completed by Peter directly in Windows. Successful
execution remains pending. See [the current procedure](native-windows.md#native-acceptance-and-execution-check).

No policy was changed, exclusion added, certificate created, or alternative
application host used to evade the rule. The published artifact is:

~~~text
C:\AI\LocalDictation\app\LocalDictation.exe
~~~

Keep the adjacent dependency DLLs and runtime configuration with it.


## Real-time API measurement

Hardware: RTX 3050 Laptop 4 GiB. Original local checkpoints loaded in FP16.
Input: the previously supplied private 81-second Slovak recording and corrected
89-word reference. Private audio, transcripts, reference and detailed output
remain outside Git.

Final replay used real-time PCM pacing and enabled optional Large-v3 verification.

| Measurement | Observed |
| --- | --- |
| Recording length | 81.0 s |
| First draft from stream start | 2.574 s |
| Largest observed pending audio at a decode result | 0.9 s |
| Largest reported individual live inference | 0.685 s |
| Last live text update | 79.498 s |
| Primary final after Stop | 0.0006 s |
| Verification result from stream start | 115.342 s |
| Complete lifecycle wall time | 115.455 s |
| Live primary word errors | 15 / 89, 16.85% |
| Verification word errors | 4 / 89, 4.49% |

The near-zero Stop-to-primary value means live recognition had already processed
the final spoken phrase before Stop. It does not mean a batch model transcribed
81 seconds in a millisecond. The measured first draft also includes connection/
model activation overhead.

A separate after-recording API run on the same recording produced 7 / 89 errors
(7.87%) and primary finalization 2.741 seconds after accelerated upload/Stop.
Its full primary-plus-verification lifecycle took 37.425 seconds. Accelerated
upload is not a live-latency measurement.

The live segmentation result is currently less accurate than after-recording
transcription. Live readiness remains false and the client labels it experimental.
The initial first-draft target of normally at most 2 seconds was not achieved in
this measured run. No general Slovak quality claim follows from one recording.

After verification, health reported ready=true, busy=false, model_loaded=false.
The process reported 9.125 MiB CUDA allocations and 22 MiB reserved; this is not a
zero-allocation idle state. Small residual allocation ownership has not been
investigated. No full GPU model remained loaded.

The API's Preparing health response was observed 1.136 seconds after the final
candidate restart; ready became true after 7.177 seconds in that run. A previous
cold/contended initialization took substantially longer, so clients must use
readiness rather than a fixed warm-up delay.


A real cancel during Large-v3 generation returned session.cancelled after
1.296 seconds, with ready=true, busy=false and model_loaded=false. Task-level
cancellation additionally has a blocking-worker regression proving unload does
not run concurrently with inference.


## Real public-model lifecycle

The public importer downloaded original openai/whisper-tiny files:

~~~text
Pinned commit: 169d4a4341b33bc18d8881c4b69c2e104e1cc0af
Inspected and downloaded: 155434424 bytes
~~~

Publication completed into the separate model store. An actual English sample
transcription then succeeded:

- Original FP16 runtime load: 1.746 s.
- Inference: 0.913 s.
- Decoded audio duration: 11.298 s.
- Runtime-tested became true only after successful inference/cleanup.
- Verification eligibility became true.
- Quality-verified and live-ready remained false.

This is runtime compatibility evidence, not a Tiny quality recommendation.
English-only metadata/generation handling additionally has unit coverage; a
separate downloaded English-only checkpoint has not been runtime-tested.


## Independent review and remaining acceptance

A fresh-context senior review identified nine Important findings. A focused
RED-to-GREEN fix pass addressed cancellation ownership, preserving recording
limits, manual draft undo/history, startup retargeting, deadlines, language
eligibility, installation completeness, active Escape, and third-modifier chords.

Registry/publication rollback was regraded from Minor to Important because a
disk-write failure could strand a downloaded model and prevent retry; it was
also fixed with regression coverage. Slow runtime initialization and the
cooperative-cancellation outcome received additional regressions during validation.

Initially deferred items, completed in the later follow-up:

- Pending Review provenance lacks both pinned revisions/comparison snapshot identity.
- Banner placement uses the primary monitor.
- Missing initial built-in models cannot yet be installed through the catalog GUI.
- Full immutable source transcripts are stored but not separately exposed as full-text views.

Native acceptance awaits Windows approval and a successful launch. Codex insertion
is specifically a manual user test. TypeWhisper and production handover have not
been performed; the existing deployment remains available.
