# Silent pause continuation recovery implementation plan

> For agentic workers: use superpowers:executing-plans inline. Preserve the canonical dirty checkout; do not create worktrees or run desktop/audio automation.

**Goal:** Allow later live drafts after a temporary target-check timeout has actually ended, while retaining cancellation and exact destination ownership checks.

**Architecture:** Keep the single bounded background MTA worker. Replace its lifetime-wide unavailable flag with the completion task of the timed-out work. Refuse admissions while that task remains incomplete; permit fresh work after it completes. Do not retry a timed-out action or bypass native delivery/clipboard safeguards.

**Tech stack:** C#/.NET 10, xUnit, Python unittest with synthetic PCM bytes in memory.

**Spec:** docs/PRODUCT_REQUIREMENTS.md, Banner and live cursor delivery.

## Evidence

Four headless tests against the actual AutomationWorker source fail after released work has completed. Each failure is the existing permanent unavailable refusal. Existing native metadata separately shows exact live acknowledgements and an input-generation interruption; that interruption is not evidence of a worker timeout and will not be reclassified.

## Task 1: recover a quiescent worker

Files: Services/AutomationWorker.cs, AppController.cs, Core.Tests.csproj,
AutomationWorkerRecoveryTests.cs.

- [x] Preserve source before edits and run the regressions to assertion RED.
- [x] Track the actual completion task rather than permanent unavailability.
- [x] Keep blocked-provider refusal, serialized thread identity and expired queue skipping.
- [x] Journal the existing live-check exception without transcript/clipboard data.
- [x] Run targeted tests to GREEN, then the full suite and integrated Release publish.

## Task 2: verify the speech-pause boundary silently

Files: server/test_live_quality.py, requirements, offline Help, validation record.

- [x] Drive the actual AudioSession with scripted recognition and synthetic voiced/quiet byte spans in memory. Exercise a quiet commit, a long quiet gap, resumed text and final earlier-word correction.
- [x] Assert complete accumulated transcript updates, ordered revisions and full retained input for final refinement. No microphone, speaker, real model or running API is used.
- [x] Run the backend suite and obtain a focused independent code review.
- [x] Save exact passing evidence, candidate identity and native acceptance limits.

## Constraints

No PC control, windows, native input/UIA queries, clipboard operations, audio capture or playback. Do not execute or rehost the blocked DeliveryProbe. Preserve running product/API processes, existing models and protected data. No commit or push. Candidate preparation is separate from replacing a running client.


## Deployment closeout

Peter confirmed normal Exit. The old process was verified absent; all 17 files
were backed up and the pause build installed with matching hashes. The new
process and its build journal agree; API ready and saved settings unchanged.
No desktop control, microphone capture or playback was used. See the final
validation record for manifest paths and native acceptance limits.
