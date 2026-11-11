# Implementation plan: live quality and cursor delivery

Date: 11 November 2026

## 1. Reproduce failed cursor acknowledgement

Owner: delivery_reliability. Files: WindowsTextDelivery and native DeliveryProbe.
Create a real paste that leaves a selection briefly non-collapsed. Observe RED.
Add bounded read-only acknowledgement retries and content-free failure-stage
diagnostics only if the hypothesis is supported. Keep pre-write inspection and
ownership strict. Test a persistent invalid selection and focus switch too.

## 2. Install the original third model and prepare private corpus

Install pinned openai/whisper-large-v3-turbo via the existing importer. Verify
original files and safetensors integrity. Do not equate installation with runtime
or quality readiness. Export only the selected three complete user recordings;
verify source/export hashes and preserve the independent reference provenance.

## 3. Freeze benchmark engine profiles and get offline controls

Add a data-free profile helper under diagnostics/LiveBenchmark. Keep KInIT and
Large-v3 existing settings, and use OpenAI Turbo's original greedy FP16 path.
Run all nine full-recording controls, checkpointing actual transcript and timings.
Confirm whether the first recording matches the supplied intended text.

## 4. Build and verify reproducible live replay

Add a CLI and pure planner tests under diagnostics/LiveBenchmark. Ensure exact
sample coverage, safe boundaries, honest virtual time/backpressure, no dropped
tail, no overlap deduplication and deterministic policy identifiers. Acquire the
same named GPU mutex before importing/loading the engine. Persist per-job results
and refuse stale resume artifacts after code/profile/corpus changes.

## 5. Dispatch one subagent for each of ten alternatives

Each runs the scenario for all three models and three recordings with actual
inference. Serialized GPU execution is mandatory. Each delivers nine transcripts,
timelines, timing/quality limitations and a scenario-specific assessment. Run the
existing scheduler as a separate control and preserve any buffer-limit failures.

## 6. Rank and implement the supported winner

Compare coverage/errors/repetition, reference-based quality where valid, first
preview/commit, revision churn and final latency. Explain model/policy tradeoffs.
Implement the selected segmentation in server/local_dictation/sessions.py with
synthetic regression tests, and add Turbo's live-capable profile/catalog support
with independent runtime evidence. Do not silently change user model selection.
Replay selected benchmark cases through the actual session implementation.

## 7. Review, Help, deployment and Git

Run backend/Core/relevant WPF/native checks. Review new code independently.
Update requirements, offline Help, README and validation status. Keep actual
private transcripts/audio out of Git. Build to a separate candidate, preserve
settings/history, restart only candidate API and deploy once safe. Commit scoped
reviewed files with English message, push existing branch without force and verify
remote HEAD plus clean checkout.
