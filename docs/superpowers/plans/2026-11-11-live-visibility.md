# Live transcript visibility and cursor feedback

> **For agentic workers:** Use superpowers:executing-plans inline.

**Goal:** Show the transcript while recording and explain why delivery moves to Review.

**Architecture:** Keep the existing non-activating banner and its header. Add a collapsible, read-only transcript panel, initially visible for each recording. Exempt only this non-activating window from mouse/focus invalidation. Keep the original destination guard for all other user input.

**Tech Stack:** Existing .NET 10 WPF client and xUnit tests; no added packages.

**Spec:** User feedback on 2026-11-11 and the existing live-dictation design.

## Constraints and decisions

- Original destination only; do not follow clicks into other fields.
- Keep FP16 models and production API unchanged.
- Large-v3 remains after-recording until live latency and quality are verified.
- No recording or transcript content in diagnostic logs.
- No commit or push is authorized.

## Tasks

- [x] Add regression tests for non-activating controls versus outside input, including zero window handles.
- [x] Update DestinationEpoch and GlobalInput to exempt only the banner window.
- [x] Show the reason for cursor fallback immediately; retain the last transcript during the completed banner.
- [x] Add the collapsible transcript panel and accurate live/batch labels.
- [x] Run Core tests, publish to a candidate folder, visually inspect native rendering, review, then replace and launch the idle client.

## Execution ledger

Initially 50 Core tests passed and two synthetic WPF renders were inspected.
The subsequent review-return work extends this to 62 Core tests and seven
native delivery checks. Peter authorized stopping/updating; the new client
was deployed and launched normally. See 2026-11-11-review-return.md.

## Review focus

- Clicking Finish or hiding/showing text must preserve the external cursor.
- Clicking or typing elsewhere must permanently stop automatic delivery for this recording.
- Empty/batch recordings must not promise an immediate transcript.
- Completion must retain the correct session's preview without duplicating delivery.
- The existing compact banner header must remain usable when the preview is hidden.
