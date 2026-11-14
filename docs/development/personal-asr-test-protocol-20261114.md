# Personal ASR evaluation protocol

Date: 2026-11-14

## Agreement and present status

Peter accepted twelve recordings, eight speech cases and four no-speech controls,
with three recordings withheld from tuning. He then requested existing recordings
as well: keyboard, blowing into the microphone, children shouting, office and
household noise. This is agreement on the test shape, not evidence of ASR quality.

Reuse existing sources first. Twelve fresh recordings are not required. Fill
missing cases and obtain genuinely unused holdouts; keep additional existing
noise cases as an expanded regression set. No playback, microphone capture,
native input or model inference is authorized by preparing this protocol.

## Core coverage

| Slot | Coverage | Reference requirement |
| --- | --- | --- |
| S01 | Normal spontaneous Slovak | Verbatim spoken words |
| S02 | Longer Slovak dictation | Verbatim words, hesitations policy recorded |
| S03 | Slovak/English engineering terms | Verbatim words plus exact target terms |
| S04 | Independent technical dictation | Same; no tuning from holdout reference |
| S05 | Numbers, units and part identifiers | Spoken form and intended written form separately |
| S06 | Quiet speech and long pauses | Do not mistake low level for absence of speech |
| S07 | Genuine thanks phrase and repeated words | All spoken words must survive protection |
| S08 | Spoken formatting and literal mentions | Verbatim and intended layout separately |
| N01 | Silence/room floor | Empty reference only after confirmation of no speech |
| N02 | Keyboard without intended dictation | Confirm whether any intelligible voice exists |
| N03 | Blowing into microphone | Note clipping; confirm absence/presence of speech |
| N04 | Office/household non-speech noise | Do not use this label to erase background words |

Additional office, household and children-shouting recordings are explicit
regression cases, including cases that also contain speech. For children shouting,
use speech / vocalization_without_words / uncertain until independently reviewed.
A background-word capture problem may require speaker/intent handling; a silence
gate alone cannot establish who intended to dictate.

Existing files retain their complete audio. If a derived interval is later needed,
record its parent hash and exact sample boundaries; never silently trim a source.

## Inventory and references

Private working inventory:

    C:\AI\LocalDictation\benchmarks\20261114-personal-tools-intake

The first inventory contains 29 source entries in 25 PCM/encoded groups: 14
current protected History entries collapse to 10 unique PCM recordings. Two
compressed downloads still need normalized-content deduplication. All 148 watched
source files were hash-identical before/after inventory preparation. No category
has yet been assigned to an exact file/interval, and no clean holdout is declared.
Some historically excluded protected sources are now missing; keep their absence
visible rather than assuming that every earlier microphone/noise test survives.

The inventory links existing sources without exporting more plaintext audio or
transcripts. History WAV hashes come from the installed public HistoryStore reader
and current-user DPAPI. Protected source files are read only. Group identical
WAV content and all retranscriptions/segments of the same parent before splitting.

Record per source: source path/ID, hash, sample rate, sample count, duration,
source kind, duplicates/parent, label provenance, language, reference kind,
reference hash, scoring eligibility and prior benchmark exposure.

Keep these reference kinds distinct:

- verbatim_human_audio_checked: independently checked against the complete audio;
- human_intended_or_corrected_wording: useful wording agreement, with limitations;
- model_output_only: no acoustic accuracy scoring;
- confirmed_no_speech: zero spoken words, with human label provenance;
- unknown: retain for review, do not fabricate expected text.

Never label keyboard, blowing or children from RMS or model output alone. Preserve
existing machine drafts as evidence; do not copy them into human gold unchanged
and mark them independently confirmed.

## Split before tuning

Choose nine development and three holdout recordings, stratifying for ordinary
speech, technical terms and noise. Every duplicate and derived segment follows its
parent into the same split. Previously assessed recordings remain regression data.
If no three genuinely unused sources are available, report holdout_missing; do
not rename an old benchmark case to make it appear unseen.

Freeze source/reference hashes and the dictionary/formatting configuration before
running holdouts. Reveal holdout results once for that candidate. A changed
candidate needs a new evaluation record; do not repeatedly tune against holdouts.

## Paired comparisons

Use identical original local model weights, revisions, FP16 precision, language
settings and full audio for each pair. Evaluate KInIT Turbo SK only for supported
Slovak; use installed multilingual profiles for SK/EN material. Runtime-tested
status is separate from dictionary and formatting acceptance.

Run separately:

1. Current behavior, no new hint/correction/formatting layer.
2. Recognition hints only, where the actual installed runtime supports them.
3. Exact correction rules only; no approximate or recursive replacements.
4. Combined candidate, and optional formatting as a separately reported factor.
5. Noise protection separately before reporting a combined result.

Freeze dictionary entries/rules before examining holdouts. Include dictionary
words that are absent from an audio clip to detect prompt-induced insertions.
Keep baseline raw recognition separate from transformed final text.

## Measurements

- Normalized word/character errors: preserve diacritics; report insertions,
  deletions and substitutions separately. Punctuation/case normalization must be
  stated; score formatting independently.
- Technical terms: exact token/phrase boundary matches per expected occurrence,
  including repeated occurrences and part numbers. Substring presence is
  insufficient. Report false insertions of absent dictionary terms too.
- No-speech controls: number of nonempty outputs and inserted words per clip and
  per minute. Empty-reference WER has no meaningful denominator; do not report
  WER=0 for a hallucinated output.
- Speech in noise/quiet speech: deleted words, complete lost utterances and
  unsupported extra words, alongside overall error counts.
- Safety phrases/repetition: exact preservation of the genuinely spoken content.
- Formatting: requested paragraph/list operations, missed operations, double
  formatting, and unintended changes when command words are literal speech.
- Latency: request wall time, inference and load time separately; separate warm
  and cold runs and report real-time factor. No invented microphone latency.

server/benchmark_tools.py already calculates normalized errors, but its fixed
term list and substring presence do not meet the term metric above. Empty-reference
scoring and term occurrence scoring require dedicated validation before use.

## Acceptance and limits

No candidate may erase genuine thanks, repeated words or complete quiet utterances
in the explicit safety cases. Confirmed no-speech controls must produce no text;
report every failure. Do not achieve that by a phrase blacklist.

Dictionary adoption requires improved target-term results without hiding extra
words, deletions or degraded holdout output. Report per-file and aggregate paired
results; a gain on tuned recordings alone is insufficient. Any speed/quality
tradeoff is explicit. A numeric latency budget has not been agreed.

Twelve recordings are an initial acceptance/regression set, not a population-level
accuracy estimate. Unlabelled recordings can reveal instability but cannot prove
that a transcript is acoustically correct. No listening or quality run has yet
been completed for this new evaluation.

## Execution boundaries

Work in C:\Git\Local-Dictation\repo on feature/native-windows. Use py and the
existing candidate environment. Keep output outside Git and OneDrive. Do not alter
models, restart either API, play audio or capture a microphone during preparation.
Run future GPU jobs serially through a verified admission path; the benchmark mutex
alone does not serialize unrelated services. Check live readiness/busy state again
before any inference. Preserve the existing frozen manifests and result files.
