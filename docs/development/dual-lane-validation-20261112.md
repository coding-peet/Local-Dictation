# Paired live schedules and corrected-reference comparison

Date: 12 November 2026 (local). Follow-up to the
[initial validation](live-quality-validation-20261111.md).

## Scope and result

Three additional dedicated scenario agents completed 27 actual replays:
2/6, 3/5 and 3/12 second short-draft/authoritative-repair schedules, each with
three original FP16 model profiles and three complete frozen recordings.
The new series contains 763 actual requests. All 27 final repair partitions
covered every source sample; all 81 transcript/timeline/checkpoint artifact
hashes and source/audio identities passed the coordinator audit.

The combined comparison contains 126 measured outputs: the original 99 plus
these 27. Nine extra views expose the original adaptive text immediately before
whole-recording refinement. They are saved snapshots, not extra inference runs.

Retain the deployed adaptive policy: growing previews, quiet-valley commits and
whole-primary finalization. KInIT is the supported recommendation for this tested
Slovak workload. None of the three fixed paired schedules improved final reference
agreement over that combination. Do not automatically switch finalization by model
on three references. Longer context does not guarantee a better result.

## References and scoring

Peter supplied corrected wording for the other two recordings directly in reply
to the reference-review request. The reference lengths are 63, 112 and 82 normalized
words, 257 total. The first reference is earlier intended wording with model-assisted
clip alignment. The later corrections followed model-prefilled drafts; no separate
complete word-by-word acoustic audit declaration was inferred. These scores measure
agreement with supplied/corrected wording, not blind acoustic recognition accuracy.

The frozen corpus, original reference and all raw model outputs were left unchanged.
Two verbatim correction files and an audio/reference-hash-bound overlay live outside
Git. Rescoring does not require new inference. The overlay preserves supplied wording
provenance and limitations instead of labelling it a verbatim acoustic audit.

Scoring uses NFC case folding, keeps accents and internal identifier punctuation,
and ignores surrounding punctuation. Micro WER is summed word edits divided by all
257 reference words, not the unweighted mean of three percentages. Lower is better.

## Quality comparison

| Policy | KInIT WER | OpenAI Turbo WER | Large-v3 WER |
| --- | ---: | ---: | ---: |
| Frozen previous scheduler | 17.121%* | 24.903%* | 25.681%* |
| alt01 | 28.794% | 47.860% | 42.802% |
| alt02 | 19.455% | 34.241% | 30.350% |
| alt03 | 17.899% | 24.903% | 27.626% |
| alt04 | 15.175% | 22.957% | 22.957% |
| alt05 | 36.187% | 50.973% | 42.412% |
| alt06 | 21.790% | 31.907% | 28.405% |
| alt07 | 19.066% | 26.070% | 26.848% |
| alt08 | 19.455% | 34.241% | 30.350% |
| alt09 | 18.288% | 23.346% | 27.626% |
| Adaptive before whole refinement | 14.397% | 14.786% | 15.953% |
| Adaptive with whole refinement | 9.728% | 17.121% | 15.564% |
| Dual 2/6 s | 19.455% | 34.241% | 30.350% |
| Dual 3/5 s | 24.125% | 40.467% | 36.965% |
| Dual 3/12 s | 15.175% | 19.844% | 21.790% |

The original scenario definitions are in the
[benchmark instructions](../../diagnostics/LiveBenchmark/README.md).

* The previous-scheduler Large-v3 series includes a recording stopped by its old
32-second buffer limit. Only 87.996% of that recording was accepted; this is a
coverage failure, not a valid complete-recording quality result.

The 2/6 s final text exactly matched the original fixed-6-second text in all nine
cases. Its short pass changed preview availability, not the authoritative final
recognition. No lexical deduplication or suspected-repeat cleanup was applied.

## Context and finalization tradeoff

| Profile | Adaptive pre-final errors | Whole-final errors | Added whole-inference compute, three clips |
| --- | ---: | ---: | ---: |
| KInIT | 37 / 257 | 25 / 257 | 8.455 s |
| OpenAI Turbo | 38 / 257 | 44 / 257 | 10.662 s |
| Large-v3 | 41 / 257 | 40 / 257 | 113.504 s |

KInIT improved on every recording. Its previous-scheduler result had 44 word edits;
the selected whole-final result had 25, a 43.2% reduction on this small case set.
Large-v3 gained only one aggregate edit and worsened on one clip. OpenAI Turbo
worsened by six edits overall, concentrated in the third clip. Its pre-final
adaptive text is a better scored variant here; do not claim whole refinement
improves every profile or every recording.

The stream-only snapshots are accuracy diagnostics, not separately timed production
implementations. Production currently relies on whole refinement to process final
accepted audio. Simply disabling that pass can omit the remaining tail. A future
stream-only policy must explicitly decode the complete tail and pass production-path
quality/coverage checks before deployment. The current measurements do not justify
a hidden model-specific switch or a general model-superiority claim.

## Timing and work

Warm accelerated 20 ms replay uses actual synchronized request wall times to advance
virtual audio arrivals. It measures compute/backpressure, not physical microphone,
desktop rendering or transport latency. The two lanes share one loaded model/GPU
and execute serially. Long repair has priority; stale short work cannot replace it.

Each row summarizes the three recordings for one scenario/profile:

| Schedule | Model | First text median | First repair median | After Stop median | Total generation |
| --- | --- | ---: | ---: | ---: | ---: |
| Dual 2/6 s | kinit | 2.366 s | 6.456 s | 0.434 s | 43.578 s |
| Dual 2/6 s | turbo | 2.459 s | 6.616 s | 0.469 s | 51.399 s |
| Dual 2/6 s | large | 3.338 s | 9.484 s | 3.542 s | 217.932 s |
| Dual 3/5 s | kinit | 3.473 s | 5.514 s | 0.384 s | 38.822 s |
| Dual 3/5 s | turbo | 3.622 s | 5.723 s | 0.507 s | 57.511 s |
| Dual 3/5 s | large | 5.341 s | 8.042 s | 4.949 s | 217.673 s |
| Dual 3/12 s | kinit | 3.545 s | 12.996 s | 0.821 s | 44.817 s |
| Dual 3/12 s | turbo | 3.683 s | 13.354 s | 0.998 s | 48.552 s |
| Dual 3/12 s | large | 5.866 s | 19.989 s | 3.753 s | 213.756 s |

Large-v3 skipped 64, 36 and 45 seconds of interim short-draft intervals in the
2/6, 3/5 and 3/12 schedules respectively because inference was busy. The long
repair still covered all original samples. Skipped preview work is recorded
explicitly and must not be presented as complete live-draft coverage.

The initial adaptive whole-final replay had KInIT median first text 3.490 s and
4.273 s after Stop. Large-v3 median final delay was 50.809 s, with individual
delays 32.52–64.01 s. These are prototype warm replay measurements, not a promise
of production microphone latency. Large-v3 is a poor latency recommendation on
this machine, even though its profile technically supports live transcription.

## Review, checks and deployment

Independent read-only review found two reporter issues: missing medians for valid
all-empty groups, and report output allowed to overlap measured-input directories.
Both were reproduced with failing checks and fixed. Reference provenance also has
a regression preventing supplied wording from becoming an invented acoustic audit.

All 31 synthetic benchmark/report tests pass. They cover scheduling, backlog,
sample ownership, stale drafts, resume identities, empty results, protected output
roots, multiple experiment identities and reference hashes. No production inference
source changed in this supplement; the earlier 116 backend, 84 Core, 18 native WPF
and nine actual production-path replay results remain the recorded evidence.

Static Help checks passed: 20 unique IDs, 12 valid internal links, restrictive CSP,
no JavaScript or remote resources. Updated Help.html was copied to candidate and
installed payloads with matching hashes. The other 13 installed files and settings
were unchanged; the client was not restarted for this content-only update.

After all GPU jobs exited, the candidate API on 48125 was restored ready and idle.
The existing production process on 8123 retained its verified identity and was not
contacted, stopped or replaced. Browser/Outlook/Codex positive insertion and visible
F1 inspection remain subject to the explicit application-control/acceptance limits
in the initial validation; these ASR measurements do not validate cursor delivery.

## Local private evidence

```text
C:\AI\LocalDictation\benchmarks\20261112-dual-lane\report\report.html
C:\AI\LocalDictation\benchmarks\20261112-dual-lane\report\summary.json
C:\AI\LocalDictation\benchmarks\20261112-dual-lane\report\comparison.csv
C:\AI\LocalDictation\benchmarks\20261112-dual-lane\assessments
C:\AI\LocalDictation\benchmarks\20261112-dual-lane\coordinator-audit.json
C:\AI\LocalDictation\benchmarks\20261112-dual-lane\reference-review\reference-overlay.json
C:\AI\build\Local-Dictation-live-quality-final\help-supplement-20261112.json
```

See the [paired-schedule instructions](../../diagnostics/LiveBenchmark/DUAL_LANE_AGENT.md)
for exact commands, source/resume identities, transcript paths and overlay format.
Actual audio, reference wording, transcripts, logs, models and environments are
private runtime data and are not committed.
