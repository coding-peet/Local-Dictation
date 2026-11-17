# Live quality and cursor delivery validation

Date: 11 November 2026. This record describes the live-quality update after the
frozen experiment commit `2a917936e1cee7d7f617e3d8cc3e5af5aec67fa0`.

Follow-up: the [12 November comparison](dual-lane-validation-20261112.md) adds
three paired schedules, 27 new outputs and two user-corrected references. It
rescores all 126 measured outputs while preserving this initial one-reference
record and its frozen raw results.

## Result and scope

Ten alternatives completed all nine model/recording combinations each. Together
with nine previous-scheduler controls, the report contains 99 actual live-replay
outputs. Nine separate same-model whole-recording controls were also run.
The selected policy is alt10, adapted for production as described below.

All inference used original local Transformers weights in FP16. No conversion,
quantization or cloud ASR was used. Actual recordings, intended text, transcripts,
traces and reports remain private outside Git. This document contains anonymous
aggregate metrics and local evidence locations only.

The installed client is under `C:\Git\Local-Dictation\app`. Candidate API 48125
runs the new session policy. The separate production batch API on 8123 was not
replaced, restarted or contacted by the production-path replay.

## Corpus and interpretation

The main matrix uses three complete Slovak recordings: 81.060 s, 56.2980625 s and
80.8880625 s. The manifest SHA-256 is:

~~~text
81436aafbab6a8de7fa87131fef18398addd3d4708d4be49eb79b3a26a09c0ff
~~~

Only the first recording has a user-supplied intended-text reference, containing
63 normalized words. Its clip alignment used two existing downloaded recordings
and model-assisted matching; no new phonetic listening verification was done.
This is intended wording, not a newly audited verbatim acoustic transcript. The
other two recordings have no independent human reference and remain unscored.

An auxiliary 16.388 s English/quiet recording checked OpenAI Turbo's functional
automatic language detection. It was not part of the fair three-model Slovak
quality matrix, because KInIT is a Slovak-specific profile.

The report scorer applies NFC normalization and case folding, retains diacritics,
and preserves internal dots, underscores, hyphens and apostrophes in identifiers.
It ignores surrounding punctuation. The benchmark's optional raw WER is separate;
the table below uses `report.py` normalized WER throughout.

Model agreement and agreement with batch controls establish consistency. They do
not establish independent accuracy. This small case set cannot establish broad
Slovak accuracy or a statistically reliable model ranking.

## Ten-alternative comparison

Every table cell is word error rate (WER) against the single 63-word intended
reference.
Lower is better. It is not an average across three human-scored recordings.

| Policy | KInIT WER | OpenAI Turbo WER | Large-v3 WER |
| --- | ---: | ---: | ---: |
| Frozen previous scheduler | 31.746% | 36.508% | 46.032% |
| alt01: fixed 3 s | 46.032% | 74.603% | 66.667% |
| alt02: fixed 6 s | 28.571% | 61.905% | 44.444% |
| alt03: fixed 10 s | 31.746% | 39.683% | 49.206% |
| alt04: fixed 15 s | 25.397% | 44.444% | 25.397% |
| alt05: 350 ms silence / 10 s maximum | 65.079% | 77.778% | 90.476% |
| alt06: 700 ms silence / 20 s maximum | 39.683% | 52.381% | 52.381% |
| alt07: 1100 ms silence / 27 s maximum | 36.508% | 44.444% | 46.032% |
| alt08: 8 s preview / 6 s hop | 28.571% | 61.905% | 44.444% |
| alt09: 12 s preview / 8 s hop | 26.984% | 31.746% | 33.333% |
| alt10: growing / quiet valley / whole final | 20.635% | 22.222% | 22.222% |

alt10 produced the lowest final WER in this case for each tested profile. KInIT's
13 edits versus the other profiles' 14 edits differ by one word edit. This does
not establish KInIT as generally more accurate than either OpenAI profile.

The fixed short and silence-triggered policies sometimes exposed repeated words
or phrases and lost context. Repeat flags are diagnostic observations, not proof
that legitimate speech was repeated incorrectly. No lexical repetition deletion
was applied. Overlap preview policies spent extra inference without matching the
whole-recording final result in this case set.

All nine alt10 finals exactly matched their same-model whole-recording controls.
All alternative policies covered the complete source sample sequence. In contrast,
one previous-scheduler Large-v3 control hit its 32-second buffer limit and covered
only 87.996% of the original recording. That is a coverage failure, not a valid
complete-recording success. Disjoint sample coverage also does not prove every
spoken boundary word was recognized.

## Timing and model tradeoffs

The experiment uses accelerated 20 ms discrete-event replay with actual measured
inference wall time. Models are warm; activation and Python/CUDA initialization
are recorded separately. The cold first-preview figure is an additive estimate,
not an independent cold-start test. These are prototype replay measurements,
not production-optimized timings or physical microphone latency.

alt10 medians across the three main recordings:

| Profile | First visible text, median | Final after Stop, median |
| --- | ---: | ---: |
| KInIT | 3.490 s | 4.273 s |
| OpenAI Turbo | 3.563 s | 4.537 s |
| Large-v3 | 5.315 s | 50.809 s |

KInIT retains Slovak greedy decoding. Original OpenAI Turbo uses multilingual
greedy native long-form decoding with its encoder on CUDA. Large-v3 retains
beam search with encoder-layer CPU offload to fit this GPU. Its much longer final
latency here is a profile/hardware tradeoff. These settings do not support a
model-only speed comparison independent of inference configuration.

The imported original OpenAI Turbo revision is:

~~~text
41f01f3fe87f28c78e2fbf8b568835947dd65ed9
~~~

The import contains 11 files, 1,622,443,339 bytes total. Its safetensors file is
1,617,824,864 bytes, with 587 FP16 tensors and SHA-256:

~~~text
542566a422ae4f3fd23f1ba11add198fca01bbf82e66e6a2857b3f608b1eb9d1
~~~

Original model source files were preserved. Installation and successful inference
establish installed/runtime-tested state. They do not automatically set the
registry's `quality_verified` state, which remains false for this import.

## Production adaptation and direct API replay

Production keeps growing previews after approximately three seconds, quiet-valley
commits within 12–24 seconds, and a final pass over the full original recording.
Every accepted sample is retained to the configured 10-minute recording limit;
the old 32-second backlog cutoff is removed.

Compared with the frozen alt10 lab scheduler, production also:

- suppresses isolated quiet fragments while retaining original samples;
- avoids a redundant tail decode immediately before whole-recording refinement;
- reuses a whole preview only for exactly unchanged audio and generation profile;
- excludes incomplete centered 100 ms energy windows at the capture edge;
- preserves prior nonempty text if an empty preview, commit or refinement arrives,
  with a recoverable error when final refinement cannot provide text.

The frozen lab selector could examine up to 30 ms beyond its virtual capture
edge for the last centered energy window. Its inference requests did not use
unavailable audio. Production excludes this selector lookahead. Whole-recording
final comparisons remain valid; exact cut/timing equality with the lab scheduler
is not claimed.

Actual replay through the candidate HTTP/WebSocket production path passed 9/9:
three profiles times three complete recordings. All checks confirmed full audio,
exact same-model batch text, one refined final and completed result, previews,
monotonic revisions, and no session errors. Six cases sent backlog while decoding
was busy and three after a preview. This verifies the actual session path beyond
the pure scheduler simulation, while remaining accelerated replay rather than
physical microphone capture.

The replayed sessions.py source SHA-256 is:

~~~text
b00339e6bc7d4a21d1c9c732bc425b2dff1caf76ae3732e56562f0c2266a57b3
~~~

OpenAI Turbo also returned `en` for the auxiliary automatic-language API test.
This is functional, model-assisted evidence, not a new listening-based score.
The candidate ended ready, idle and with GPU model weights unloaded. The existing
production batch process identity remained unchanged.

## Cursor delivery evidence

The controlled native WPF delivery suite passed all 18 checks. A transient
post-paste noncollapsed selection was reproduced as a failure before the fix.
The update retries readback only within a bounded deadline: it never issues a
second paste to obtain acknowledgement. Exact original-field identity, complete
text and exact caret requirements remain enforced.

Persistent invalid selection and focus-switch negatives still fail safely.
Long Unicode text, rich text, surrounding text, clipboard restoration and delayed
paste recovery were included. Historical Chrome incident metadata showed failed
confirmation after 47–83 ms without an inspection success; it did not identify
which of the new detailed guard codes would have failed.

These controlled fields do not establish Chrome, Outlook or Codex compatibility.
Positive Chrome insertion remains unvalidated. Outlook and Codex-field insertion
still require Peter's application acceptance.

## Automated checks and deployment

Fresh command-line verification after the final functional changes passed:

- 116 backend tests, including 21 new live-quality cases and four Turbo cases;
- 84 Core tests and successful Release publish;
- 16 pure benchmark/report checks;
- 18 controlled native WPF delivery checks.

The backend and pure diagnostic suites were rerun independently for this record.
They use synthetic data/lightweight engines and do not establish real ASR quality.
The existing broader controller/UI scenario evidence is recorded in the earlier
[delivery incident validation](delivery-incident-validation-20261111.md); it is
historical evidence rather than a fresh rerun of every scenario for this update.

Static Help checks passed: 20 unique IDs, 12 valid internal links, restrictive
Content Security Policy, and no JavaScript, remote resources or remote CSS.
The private HTML report contains 99 output articles, 11 expandable details and
34 table rows; its static self-contained checks also passed. Static checks do
not establish visual layout or an application opening the intended Help file.

Deployment verified all 14 published file hashes, unchanged settings, and seven
unchanged preserved original input-file hashes. The app was launched normally;
content-free delivery diagnostics are enabled only in that process. Process
startup does not establish visible Ready state or successful microphone use.

## Remaining application checks

Positive browser insertion and visible F1 Help validation were not completed.
An earlier browser route stopped on URL attribution. After Peter clarified that
Escape was accidental and requested resumption, the CUA turn's existing stop
latch still rejected window enumeration. No additional user Escape press is
inferred. No reset or alternate GUI/input route was used after that rejection.

A new authorized application-control turn is needed to finish visible Settings /
Ready and F1 Help inspection and a controlled Chrome insertion test. Real microphone
latency, Outlook insertion and Peter's Codex-field acceptance remain outstanding.
No assertion of general destination compatibility follows from this deployment.

## Local evidence and reproducibility

The following paths are local private evidence, not Git inputs or shared data:

~~~text
C:\AI\LocalDictation\benchmarks\20261111-live\corpus\manifest.json
C:\AI\LocalDictation\benchmarks\20261111-live\report\summary.json
C:\AI\LocalDictation\benchmarks\20261111-live\report\comparison.csv
C:\AI\LocalDictation\benchmarks\20261111-live\production-replay\report.json
C:\Git\Local-Dictation\delivery-fix-probe\selection-green-final.json
C:\AI\build\Local-Dictation-live-quality-final\deployment.json
~~~

The frozen experiment commit preserves its original engine/session implementation
and data-free diagnostic runner. Later production changes have distinct source
identity. Use a new output directory for changed source/profile/corpus identity;
never relabel or overwrite the earlier measurements. See
[benchmark instructions](../../diagnostics/LiveBenchmark/README.md) for commands,
mutex ownership, resume rules and the complete policy definitions.
