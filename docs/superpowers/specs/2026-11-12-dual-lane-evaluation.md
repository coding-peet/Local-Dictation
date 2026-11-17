# Dual-lane live transcription evaluation

Date: 12 November 2026.

## User input

Peter requested three additional paired schedules after the initial ten-policy
comparison. Short live hypotheses may be corrected by a longer acoustic pass.
The longer result takes priority. Select behavior using measured context/quality
and latency, rather than assuming that more passes improve recognition.

Peter also offered to check the remaining two recordings. Existing model text
must remain an unverified draft until he supplies corrected reference wording or
confirms it. Preserve the distinction between intended/corrected wording and an
explicitly audited verbatim transcript.

## Design

Evaluate 2/6, 3/5 and 3/12 second short/long intervals. Assign one independent
scenario agent to each schedule, with all three original FP16 profiles and all
three complete frozen recordings. The GPU mutex serializes scenario execution.
Within a scenario, the two lanes are logical schedules on one loaded model;
they do not call generate concurrently.

Long repair epochs are disjoint and own their complete sample interval. Short
intervals restart at each long boundary. A long repair atomically replaces all
short hypotheses within its epoch. Repair wins at coincident ticks and under
backlog. Obsolete short ticks may coalesce; their omitted draft spans are recorded.
Every original sample must still occur in one authoritative final repair.

Keep original silence and repetitions. Do not infer word alignment, deduplicate
text, remove suspected hallucinations, or silently alter model generation options.
The final repaired stream contains no whole-recording replacement, so its quality
can be compared independently with the existing full-context controls.

## Evidence and selection

Run actual inference for all 27 combinations. Preserve raw text, request spans,
snapshots, request timings, GPU/profile/source provenance, and checkpoint hashes.
Use the same accelerated 20 ms arrival model as the original experiment; do not
describe its virtual latency as measured microphone latency.

Compare the repaired final against both the adaptive policy's pre-refinement
text and its whole-recording final. Agreement with a model control measures
consistency. Human reference WER measures agreement with that reference, subject
to its intended-wording or verbatim provenance.

Keep the original corpus and 99 results frozen. New results use a separate root
and source identity. Human corrections use a separate hash-bound reference overlay
and rescore saved outputs without requiring new inference. An offered correction
or edited draft alone does not constitute explicit human confirmation. A corrected
reference supplied directly in reply to the reference request establishes its
user-supplied wording provenance; it does not establish a separate acoustic
listening audit.

Retain the implemented adaptive policy unless a paired schedule demonstrates a
useful improvement under the measured quality, completeness, compute and latency
tradeoffs. If only one reference remains available, keep that limit explicit.

## Verification

Synthetic scheduler tests cover exact repair coverage, mismatched 3/5 cadence,
slow inference, stale short work, missing draft intervals, silent/final partial
audio, invalid timings and strict resume identity/artifacts. Reporter tests cover
repair ownership, pre-refinement isolation, confirmed reference hashes, separate
experiment provenance and profile mismatches.

After all jobs finish, independently inspect their identities and sample spans,
generate the private combined comparison, review the data-free code changes,
and update the validation record. Production delivery/GUI acceptance limitations
remain separate from recognition experiments.
