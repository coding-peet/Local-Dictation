# Dedicated live benchmark scenario assignment

Read repository AGENTS.md and diagnostics/LiveBenchmark/README.md first.
The parent task assigns one scenario ID. Own that scenario's nine actual results:
three original models multiplied by three complete private recordings.

## Frozen inputs

Use the manifest and output directory given by the parent. Do not modify the
manifest, recordings, model profiles, benchmark runner or product source.

Current experiment runner SHA256:
77d815dff0061c4ac96a09f5f8539b50ee5739f4f8cae20a68656718c9434c94

Shared profiles SHA256:
ab5797c0b2359c5952949f64a979c2ff8949517c17e6faaa84d37147d8f4fc8c

The parent will supply the corpus SHA256. Refuse an unexpected identity.
Only one GPU owner may run at a time. The script uses
Local\LocalDictation.LiveBenchmark.GPU before engine initialization. Never run a
second standalone model process or bypass that lock. Candidate 48125 was stopped
by the parent for the experiment. Never stop/contact production 8123.

## Run

Use py, not python. Select the existing candidate environment:

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B diagnostics/LiveBenchmark/benchmark.py `
    --manifest MANIFEST_PATH `
    --output PRIVATE_RESULTS_PATH `
    --scenario SCENARIO_ID --resume
~~~

This managed workspace requires a scoped elevated tool call for writes under
C:\AI. Use the already authorized private experiment scope; do not first load
GPU weights in a sandbox command that cannot create its output directory.
Do not request the user to reconfirm this authorized experiment.

The process may wait on the shared mutex. Report actual model completions and
meaningful failures; do not repeatedly narrate an unchanged queue position.
No model conversion, quantization, dependency installs or uploads are allowed.

## Verify and assess

Verify nine distinct complete JSON results with expected model/clip/hash/source
identity, matching transcript and event files, and actual inference events.
Audit chronological commit sample spans and final tail rather than trusting the
reported accepted-sample count alone. alt10 additionally has a whole-recording
refinement pass; overlapping previews are not final commitments.

Report first text, first commitment, after-stop time, compute, revisions and any
truncation/errors. Inspect multiword repetitions and empty/noisy chunks as well
as consecutive identical words. Repeated phrases may have been spoken; flag them
for inspection without deleting words or claiming acoustic verification.

Only the first clip has an independent user-provided intended-text reference.
Its source alignment is model-assisted, with no new word-by-word listening.
The other clips have no independent human reference. Do not present distance from
batch output as WER accuracy. Use report.score for the shared normalized WER/CER
when comparing the first clip; retain the runner's strict raw WER separately.

Write a private SCENARIO_ID-assessment.md/json under that scenario's result
folder. Include all nine transcript links, complete metrics, method-specific
tradeoffs and a model ranking limited by the actual evidence. Keep every real
transcript/audio file outside Git. Do not commit, push, deploy or change settings.

Return a concise completion report with 9/9 status, process exit result, mutex
release, output paths and important findings. Do not paste all transcripts into
the parent chat; the private report contains them in full.
