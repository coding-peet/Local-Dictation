# Live transcription comparison and cursor insertion repair

Date: 11 November 2026

## Requested result

Fix failed automatic insertion at the remembered cursor. Download and test the
original OpenAI Whisper Large-v3 Turbo alongside existing KInIT Turbo and
Whisper Large-v3. Give each of at least ten segmentation alternatives its own
subagent. Each scenario must produce actual transcripts for all three models on
the same three complete Peter recordings. Rank the evidence, implement the best
supported live policy, update offline Help, review, commit and push.

## Evidence and boundaries

The current client is c3878d9. Four real Chromium destination failures queued
four native input events, then failed the first confirmation inspection after
47–83 ms. None reached a successful text/caret snapshot. The exact throwing guard
was not logged; a transient selection is a hypothesis, not an established fact.
Reproduce that case in synthetic native fields before changing confirmation.
Keep strict original field identity, full-document ownership and exact caret
checks. No arbitrary focus adoption or send action.

The client can retain a previous clipboard backup. Do not terminate it or mutate
the clipboard for a fixture while that backup remains unresolved. Ask Peter to
exit normally if the hidden client cannot be accessed with the available tool.

Use one serialized GPU owner. Temporarily stop only the verified idle candidate
on 48125 while measuring; preserve production 8123 and TypeWhisper. Keep weights
original FP16 and all private audio/transcripts outside Git. Use existing tools
and environments; no dependency upgrades or security changes.

## Corpus and references

Use the complete 81.060-second supplied WebM, 56.298-second natural programming
dictation, and 80.888-second specification discussion. The last two come from
protected Local Dictation history. Export copies only; verify source hashes.
The 16.388-second quiet incident remains an auxiliary failure case: the new
multilingual output suggests English speech, whereas its earlier forced-Slovak
output repeated Slovak words. It is unsuitable for a common-language comparison
against the Slovak-only KInIT profile. No new acoustic listening was performed.

The chat contains an independent intended-text reference for the Defender story.
The first model passes identify the 81-second clip as its first part; the separate
10.740-second supplied download contains the last two sentences. Score the first
recording against the exact user-supplied prefix through "trvalú." Preserve the
complete supplied reference and source-alignment audit. Topic matching identifies
the recording but does not establish new word-by-word acoustic ground truth.
The other two clips have model outputs, not independent references. Do not present
agreement or distance from a model's batch output as WER accuracy.

## Approaches

1. Fixed disjoint chunks make compute and coverage predictable but cut words and
   sentences without respecting speech.
2. Pause-aware and adaptive chunks retain phrase context while bounding latency.
   Their behavior depends on microphone level, pauses and inference throughput.
3. Overlapping previews or a final whole-recording refinement can improve visible
   context/final consistency, at the cost of extra inference and revisions.

Test all three families rather than selecting one before collecting evidence.
The implementation choice is delegated by Peter's request to implement the best
method. Ranking assumptions below are engineering choices, not preferences he
explicitly supplied.

## Scenario matrix

Keep the existing live scheduler as an additional control, including its 32-second
buffer behavior. It is not one of the ten alternatives.

| ID | Segmentation / preview | Commit and final behavior |
| --- | --- | --- |
| alt01 | Fixed 3 s | Disjoint chunks, final remainder |
| alt02 | Fixed 6 s | Disjoint chunks, final remainder |
| alt03 | Fixed 10 s | Disjoint chunks, final remainder |
| alt04 | Fixed 15 s | Disjoint chunks, final remainder |
| alt05 | Pause 350 ms, minimum 1.5 s, maximum 10 s | Disjoint phrase commits |
| alt06 | Pause 700 ms, minimum 3 s, maximum 20 s | Disjoint phrase commits |
| alt07 | Pause 1100 ms, minimum 4 s, maximum 27 s | Disjoint phrase commits |
| alt08 | Overlapping 8 s preview windows, 6 s hop | Separately decode disjoint hop commits; no lexical deduplication |
| alt09 | Overlapping 12 s preview windows, 8 s hop | Separately decode disjoint hop commits; no lexical deduplication |
| alt10 | Quiet-valley adaptive cut within 12–24 s, preview every 3 s | Whole-recording final refinement |

Operational rules are frozen in diagnostics/LiveBenchmark/benchmark.py. Use RMS
0.008 on standardized 20 ms frames; this is not a claim about actual WASAPI
callback packet sizes. Quiet cuts search centered 100 ms energy windows every
20 ms and choose the earliest minimum on a tie. Fixed/pause methods include
quiet samples rather than silently dropping them; their hallucination risk on
quiet chunks is an experimental outcome. Maximum-length cuts apply without a
pause. Adaptive drafts use the entire current uncommitted span, bounded by the
24-second cut trigger; final refinement uses all source audio.

Ingest received frames before deciding work, including frames accumulated during
inference before releasing a completed chunk. The stop state flushes every tail.
Overlapping scenarios infer a preview before separately committing its hop, skip
missed preview ticks under backpressure and process all commitment backlog.
A latest overlapping preview can be discontinuous with the committed prefix;
retain that gap in timeline diagnostics rather than pretending a continuous
transcript. No lexical boundary deletion is permitted. These are candidates to
evaluate, not behavior approved for shipping without results.

Also run nine offline full-recording controls. Freeze generation profiles and
record their hashes: existing KInIT greedy, existing Large-v3 beam 5, original
OpenAI Turbo greedy with timestamped native long-form and compression fallback.
Avoid score-retaining thresholds that exceed this machine's VRAM budget.
This compares intended usable profiles, not identical decoding hyperparameters.

Every scenario subagent owns its nine actual model/recording results and private
summary. Concurrency slots are limited; dispatch successive agents as earlier
ones finish. A shared Windows mutex serializes GPU work before engine imports.

## Measurement and selection

Accelerate idle periods with discrete-event audio replay. Measure real inference
cost and advance the simulated audio clock while decoding; model backpressure
and preview coalescing. Never report accelerated execution wall time as live
microphone latency. Report load separately from warm inference.

Collect full transcript, event timeline, first preview/commit, final latency after
stop, total compute, revisions, sample coverage, failures and repeated-word runs.
No transcript cache may stand in for an actual measured scenario result.

Reject incomplete coverage, errors and pathological loops first. Compare final
WER/CER only where a usable human reference exists; label the other clips as
qualitative/consistency checks. Present a Pareto comparison of quality and live
latency instead of hiding tradeoffs behind an arbitrary weighted score. Prefer a
method whose quality does not regress and whose responsiveness improves across
the available recordings. State uncertainty from this small corpus.

## Delivery

Commit data-free runner/source, synthetic tests, requirements, Help, design, plan
and a public validation summary. Keep the 90 alternative transcripts, controls,
private reference and audio in a local report under C:\AI\LocalDictation\benchmarks.
After the winning policy is implemented, replay the same cases through the
production session path, run existing suites, build a candidate and deploy it.
Outlook acceptance remains a real application test, distinct from native fixtures.
