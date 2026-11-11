# Supplementary two-lane replay

## Scope

This is a new experiment, not a production policy change. Do not edit the frozen
benchmark runner, profiles, original recordings or original experiment outputs.
Keep every private result outside Git. Do not run inference until the coordinator
has verified the candidate is idle/stopped and production inference is idle.
The shared mutex serializes benchmark jobs; unrelated services do not honor it.

The source snapshot of the original experiment is
`2a917936e1cee7d7f617e3d8cc3e5af5aec67fa0`. This supplementary runner records its own
source hashes and profile identity. New source needs a new private output root.

## Policy

| Scenario | Short draft interval | Authoritative repair epoch |
| --- | --- | --- |
| dual02_06 | 2 seconds | 6 seconds |
| dual03_05 | 3 seconds | 5 seconds |
| dual03_12 | 3 seconds | 12 seconds |

Both logical lanes use the selected model and the same original FP16 profile.
Only one inference request executes at a time on the GPU. Long repairs take
priority whenever their complete epoch is available, including a complete final
partial epoch. Long epochs are disjoint and cover every source sample.

Short draft intervals restart at each repaired epoch boundary. In the 3/5 method,
the first short interval is [0,3), repair owns [0,5), the next short interval is
[5,8), and the next repair owns [5,10). No short interval crosses a repair boundary.
At the epoch endpoint, repair wins over a coincident short tick. The final partial
epoch is repaired at stop even when it is shorter than either configured interval.

If several short ticks elapsed during inference, only the latest complete short
interval is decoded. Skipped short intervals are explicitly reported; earlier
visible short hypotheses remain in chronological order. A snapshot can therefore
have interim draft gaps. It must not be described as complete draft coverage.
Authoritative repair replaces all short hypotheses for its epoch atomically.
No stale short request can overwrite a repaired interval.

The final transcript is the chronological concatenation of authoritative repairs.
There is no global full-recording refinement in this method. `text` and
`final_repaired_text` store this repaired-stream result. Compare it separately with
the existing offline full controls and alt10's global final result. Agreement with
a model control is consistency evidence, not independent accuracy.

No energy gate, silence dropping, lexical deduplication or text loop cleanup is
applied. Silence remains input, allowing comparison with the frozen fixed methods.
Legitimate repetitions remain untouched. Segmentation can still lose recognized
boundary words even with complete sample coverage.

## Synthetic verification and job planning

From the repository root:

```powershell
py -B -m unittest discover -s diagnostics/LiveBenchmark -p 'test_*.py' -v
```

The tests use synthetic PCM and controlled inference durations. They verify
scheduling, coverage, backpressure, replacement and resume checks without GPU/model
loading. Tests create and remove temporary synthetic fixtures.

```powershell
py -B diagnostics/LiveBenchmark/dual_lane.py `
    --manifest C:\AI\LocalDictation\benchmarks\20261111-live\corpus\manifest.json `
    --output C:\AI\LocalDictation\benchmarks\20261112-dual-lane\results `
    --scenario dual02_06 --plan-only
```

Planning validates WAV package/hash identity and source hashes and prints only
job identifiers. It writes no outputs and does not import torch or load models.

## Measured run

Give each scenario its own agent. Run the following with that agent's scenario
selector after source freeze and GPU-idle verification:

```powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B diagnostics/LiveBenchmark/dual_lane.py `
    --manifest C:\AI\LocalDictation\benchmarks\20261111-live\corpus\manifest.json `
    --output C:\AI\LocalDictation\benchmarks\20261112-dual-lane\results `
    --scenario dual02_06 --resume
```

Use `dual03_05` or `dual03_12` for the other scenarios. With no model/recording
selectors each scenario runs all three models on all three complete recordings.
`--model kinit`, `--model large`, `--model turbo`, and exact `--recording CLIP_ID`
selectors can be repeated. No selector means all available values.

The runner acquires `Local\LocalDictation.LiveBenchmark.GPU` before importing the
engine, constructing torch or loading weights. It keeps the mutex for its entire
selected job group and unloads the engine on normal exit, error or Ctrl+C. It does
not contact or stop either API, reset a lock, install anything, or modify models.

## Outputs and interpretation

Each combination writes JSON, repaired final TXT, readable timeline TXT,
append-only inference events JSONL and a job-state JSON marker under
`OUTPUT/SCENARIO/MODEL/CLIP_ID.*`. `OUTPUT/source_manifest.json` binds this root to
the source and corpus identity. Completed results include hashes of their text,
timeline and events. `--resume` rejects different source, profile, corpus/audio,
scenario, language, sample count or damaged/missing artifacts.

A matching interrupted job starts inference from the beginning; its earlier
events are retained with a timestamped `.partial` suffix. Its recorded timings
are never reused as inference. Existing complete results require `--resume` and
are never overwritten by an ordinary invocation. Job markers record cancellation
or error type without printing private text.

Audio arrives virtually in complete 20 ms frames plus the actual final partial
frame. Each synchronized actual request wall time, including WAV packaging,
preprocessing and generation, advances virtual audio arrivals. Idle time is
accelerated, with no sleeping or future samples. Warm replay excludes separately
reported model activation and runtime initialization. Scheduler CPU overhead and
microphone/device transport are not measured.

Results include first visible text, first short draft, first repair, per-epoch
repair delay from its audio endpoint, revisions, final delay after stop, compute,
decode-audio duration, exact repair spans and skipped interim draft spans. Events
retain source spans, available samples at request/finish, actual measured costs,
visible draft/repair spans and snapshots. `first_preview_seconds` means first
nonempty visible snapshot, which may be a repair on a short recording.
`first_commit_seconds` means first authoritative repair, even if its text is empty.

The JSON `kind` values are `draft` and `repair`. No global `refine` event exists.
Text revision count is UI churn, not an accuracy score. WER is enabled only for
manifest references explicitly marked enabled and confirmed human; the runner's
raw WER is case-sensitive and whitespace-tokenized. No automated evidence here
proves live microphone latency, cursor delivery or broad ASR quality.

## Compare the supplement with the frozen experiment

After all 27 new outputs are complete, generate a separate private report:

```powershell
py -B diagnostics/LiveBenchmark/supplemental_report.py `
    --manifest C:\AI\LocalDictation\benchmarks\20261111-live\corpus\manifest.json `
    --original-results C:\AI\LocalDictation\benchmarks\20261111-live\results `
    --dual-results C:\AI\LocalDictation\benchmarks\20261112-dual-lane\results `
    --controls C:\AI\LocalDictation\benchmarks\20261111-live\batch-controls-main.json `
    --output C:\AI\LocalDictation\benchmarks\20261112-dual-lane\report
```

The command reads saved outputs only; it does not load models or change either
experiment. It requires all 126 measured results and keeps each series' source
fingerprint separate. Generation profiles and audio hashes must agree across
series and full-recording controls. The HTML, CSV, JSON and exact transcripts
remain outside Git.

Nine additional text views show alt10 immediately before its whole-recording
refinement. These are existing snapshots, not nine extra inference runs. Their
reported timing fields belong to the complete original replay, not a separately
timed policy without refinement. Compare their accuracy/context only.

Normalized WER uses the existing report scorer and only enabled human-confirmed
references. The first reference is user-supplied intended wording with
model-assisted clip alignment; it is not a newly listened verbatim transcript.
Model agreement remains consistency evidence. The report orders complete-audio
outputs ahead of incomplete ones and reports reference counts explicitly.

For later human corrections, leave the frozen corpus and results unchanged.
Pass `--reference-overlay PATH` to rescore without inference. The overlay is a
private JSON object with a `references` array. Each entry requires `clip_id`,
`audio_sha256`, `path`, `sha256`, `confirmed_human: true`, `score_enabled: true`,
and a nonempty `provenance` describing the supplied or confirmed reference wording.
Optional `kind` and `limitation` preserve intended/corrected/verbatim provenance
and remaining uncertainty. Human-supplied corrected wording is not automatically
a word-by-word acoustic audit. Paths may be absolute or
relative to the overlay. Changed reference/audio hashes, unknown/duplicate clips
or unconfirmed entries are rejected. Model-prefilled drafts are never promoted
merely because their files exist or were edited.
