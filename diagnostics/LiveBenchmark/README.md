# Live ASR segmentation experiment

This diagnostic compares ten segmentation alternatives with the frozen previous
live scheduler on complete private recordings. It changes no product behavior and
does not start, stop, or contact either API service.

Every reported decode runs the original local Transformers weights in FP16.
There is no quantization, weight conversion, transcript cache, or cloud ASR.
`profiles.py` records the pinned model paths and generation options shared by
the live experiment and offline full-recording controls.

## Run the synthetic checks first

From the repository root:

```powershell
py -B -m unittest discover -s diagnostics/LiveBenchmark -p test_benchmark.py -v
```

These tests use synthetic PCM and fake inference times. They check scheduling,
sample coverage, backpressure, and preservation of legitimate repetitions.
They do not load models or establish ASR quality.

## Validate the selected jobs without loading models

```powershell
py -B diagnostics/LiveBenchmark/benchmark.py `
    --manifest C:\AI\LocalDictation\benchmarks\20261111-live\corpus\manifest.json `
    --output C:\AI\LocalDictation\benchmarks\20261111-live\results `
    --scenario alt01 --plan-only
```

The command checks the complete WAV hashes and prints only job identifiers.
The manifest supplies `clips[]` with `clip_id`, `language`, and
`audio.file`/`audio.sha256`. Audio must be complete mono 16 kHz PCM16 WAV.
The private manifest, recordings, text, results, and traces remain outside Git.

## Run one scenario across all nine model/recording combinations

First establish that the candidate is idle/stopped and that no production
inference is running. This runner does not stop production or candidate services.
The benchmark mutex cannot serialize an unrelated service that ignores it.

```powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B diagnostics/LiveBenchmark/benchmark.py `
    --manifest C:\AI\LocalDictation\benchmarks\20261111-live\corpus\manifest.json `
    --output C:\AI\LocalDictation\benchmarks\20261111-live\results `
    --scenario alt01 --resume
```

`--scenario`, `--model`, and `--recording` may each be repeated. Model aliases
are `kinit`, `large`, and `turbo`. Recording IDs must match the manifest exactly.
Omitting a selector includes all its values. A model-group run can select one
model and several scenarios to amortize activation without concurrent models.

All GPU runners must acquire the same Windows named mutex,
`Local\LocalDictation.LiveBenchmark.GPU`, before engine construction or torch
initialization. The CLI waits for this mutex, holds it for the entire selected
job group, then unloads GPU weights and releases it. It never resets a lock or
terminates another process. Windows abandoned-owner status is recorded.

`--resume` skips only complete result files with identical source hashes,
manifest/audio hashes, and generation profile. A partial job restarts actual
inference from the beginning; its previous trace is preserved with a `.partial`
suffix. Earlier measured results are never reused as timings for a new replay.

## Scenario definitions

| ID | Trigger and commitment policy |
| --- | --- |
| baseline | Frozen previous `AudioSession.run_live`: 20 ms PCM frames, first eligible draft at 0.5 s, 0.75 s after previous inference completion, 0.7 s silence commitment, 27 s buffer cut at the quietest point from 20 to 26 s, 32 s live buffer limit. |
| alt01 | Disjoint fixed 3 s segments; decode and commit each, including final tail. |
| alt02 | Disjoint fixed 6 s segments. |
| alt03 | Disjoint fixed 10 s segments. |
| alt04 | Disjoint fixed 15 s segments. |
| alt05 | Commit on 350 ms silence after at least 1.5 s; maximum segment 10 s. |
| alt06 | Commit on 700 ms silence after at least 3 s; maximum segment 20 s. |
| alt07 | Commit on 1100 ms silence after at least 4 s; maximum segment 27 s. |
| alt08 | 8 s overlapping previews at 6 s hops. Independently decode and commit disjoint 6 s hops, plus final tail. |
| alt09 | 12 s overlapping previews at 8 s hops. Independently decode and commit disjoint 8 s hops, plus final tail. |
| alt10 | Growing drafts every 3 s of uncommitted audio. At 24 s, choose minimum energy within 12–24 s and commit there. At stop, commit the tail and actually re-decode the complete recording as the final replacement. |

All energy measures use the current RMS threshold `0.008` on 20 ms frames.
Quiet-valley cuts minimize energy over a centered 100 ms window, with candidate
positions every 20 ms. alt10 candidates are restricted to already received
audio. The search interval excludes its upper endpoint, matching Python range.
The frozen experiment's final 24-second quiet-window candidates can inspect up
to 30 ms beyond the current capture edge when evaluating their centered energy
window. Inference requests themselves never include unavailable samples. The
production adaptation excludes incomplete centered windows at that edge. Final
whole-recording output comparisons are unaffected by this selector limitation;
exact live timing/cut equality with the frozen experiment is not claimed.
Alternative pause/fixed methods keep the entire source sample sequence,
including silence. The baseline keeps its current silence-discard policy and
reports those spans separately.

Overlap reconciliation deliberately uses extra acoustic inference: preview
windows can overlap, while final commitment windows are disjoint. No lexical
suffix/prefix deletion or guessed word timestamps affect the final text. Raw
preview strings, commitment strings, and complete snapshots are retained.
Preview text can briefly repeat a committed boundary; its UI churn is measured.
If compute spans several preview ticks, only the latest already received
scheduled preview window is decoded next. Disjoint commitments still process
all chronological hops, including backlog and the final tail.
Disjoint commits still cannot prove that ASR recognized every boundary word.

## Time model and interpretation

This is accelerated discrete-event audio replay, not real-time microphone
validation. Audio arrives virtually in 20 ms frames. Each inference executes
on the actual GPU; its synchronized measured request wall time advances the
virtual clock. Incoming audio continues to accumulate during that interval.
The next request sees the resulting backlog. Preview requests are coalesced
to current relevant audio; fixed commitment intervals remain sequential.
There are no sleeps and no use of future audio in a request.

Models remain loaded across their selected jobs. Warm replay excludes shared
model activation and Python/CUDA initialization. These measured costs are
reported separately. The cold first-preview value is explicitly an additive
estimate, not a separate cold-start experiment. Per-request wall time includes
audio decoding/preprocessing/generation; `inference_seconds` is the engine's
synchronized generation timing. Scheduler CPU overhead is not modeled as
microphone transport latency.

The frozen previous-scheduler baseline can hit its 32 s buffer limit while a
slow decode runs.
The result records the accepted sample count, unprocessed tail, stop reason,
and coverage fraction against the complete original recording. Such a result
is a coverage failure; it is not a successful full-recording comparison.

## Output and scoring

Each combination creates a JSON result, final text, readable text timeline,
and append-only per-inference JSONL checkpoint under
`OUTPUT/SCENARIO/MODEL/CLIP_ID.*`. Do not commit any of these private files.
Results contain actual request counts, compute time, decode-audio duration,
GPU peak allocation, first visible text/commit times, finalization after stop,
word revision count, exact boundary repeat counts, and longest repeated word run.

Revision counts measure removal/replacement of a previously visible word
suffix. Boundary repeat counts compare exact suffix/prefix words (up to 32)
as a diagnostic only. These values do not distinguish legitimate speech from
ASR failure. They never delete or rewrite recognized words.

WER runs only when the manifest explicitly enables scoring and confirms an
independent human reference (`reference.score_enabled` and
`reference.confirmed_human`). Otherwise the result remains unscored. Historical
model outputs and agreement with offline controls are consistency evidence,
not human ground truth. The runner's optional raw WER is case-sensitive and
whitespace-tokenized; no punctuation or diacritics are silently normalized there. The separate report below uses
normalized WER/CER, so its numbers must not be mixed with the raw metric.

Three recordings are a small diagnostic case set. This experiment does not
validate microphone capture, cursor insertion, native paste ownership,
application compatibility, or broad Slovak transcription accuracy.

The original experiment snapshot is commit 2a917936e1cee7d7f617e3d8cc3e5af5aec67fa0.
Subsequent production policy/profile source changes intentionally produce a new
identity. Use a new private output directory for a new source version; resume
must not overwrite or relabel earlier measurements.

## Generate the private comparison report

~~~powershell
py -B diagnostics/LiveBenchmark/report.py `
    --manifest C:\AI\LocalDictation\benchmarks\20261111-live\corpus\manifest.json `
    --results C:\AI\LocalDictation\benchmarks\20261111-live\results `
    --output C:\AI\LocalDictation\benchmarks\20261111-live\report
~~~

The report preserves every exact transcript and writes summary.json, comparison.csv
and a self-contained HTML comparison. Normalized WER/CER uses NFC case folding,
preserves accents and identifiers' internal punctuation, and scores only enabled
independent intended-text references. It distinguishes disjoint sample coverage
from a complete-recording refinement. No report contents are committed to Git.
