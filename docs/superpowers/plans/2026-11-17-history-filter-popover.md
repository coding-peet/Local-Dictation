# Compact History Filters Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline tasks.

**Goal:** Recover unused History space with a filter popover and applied chips.

**Architecture:** Keep HistoryQuery immutable and existing search semantics.
HistoryPanel.Search owns toolbar/query/model choices; a Filters partial owns
form, popup lifecycle, validation and chip rendering. No persistent state added.

**Tech stack:** C#/.NET10, WPF, existing MaterialDesignThemes.

**Spec:** ../specs/2026-11-17-history-filter-popover-design.md

## Constraints

Canonical C:\Git\Local-Dictation\repo, feature/native-windows; no worktrees.
Preserve existing uncommitted work and the user's enabled Windows startup.
No native windows/input, audio/inference, real API requests or service changes
in automated checks. Build under C:\AI\build\Local-Dictation-personal-tools.
No commit/push. Deploy only after normal Exit and verified backup/manifests.

## Review focus

- Inactive controls reserve no extra row; narrow toolbar remains usable.
- Draft cancellation and invalid dates cannot hide or silently change filtering.
- Chip removal/clear preserve search text and selected transcript identity.
- Nested DatePicker/ComboBox popups and Escape/focus restoration retain usable flow.
- Navigation/disposal closes the floating surface and releases no audio resources.

## Task 1: Behavior and compact toolbar

- [x] Write failing detached checks for absent inactive row, draft/apply/cancel,
  invalid ranges, chip removal and clear preserving the text query.
- [x] Observe expected RED against current always-present filter card.
- [x] Replace card with Filters in the search/import row. Add draft form popup,
  explicit actions and keyboard/lifecycle handling in HistoryPanel.Filters.cs.
- [x] Render chips from applied query only. Collapse the entire row at zero.
- [x] Update older History checks to drive Apply while retaining their original
  full-text, identity, privacy and zero-native/audio assertions.
- [x] Run focused checks and inspect narrow/wide Light/Dark popup/toolbar states.

## Task 2: Integration and rollout

- [x] Update requirements and offline Help; document current verification.
- [x] Run full Core and detached WPF suite; required backend synthetic tests.
- [x] Independent read-only review; fix material findings with regression tests.
- [x] Publish, normal Exit, backup/copy/hash validation, launch/process/log checks.
- [x] Preserve existing API identities, startup registration and protected data;
  clean verified redundant candidate and report remaining native acceptance limits.


## Historical pause checkpoint (resolved)

Paused at Peter's request. No filter rollout, commit or push occurred.
Core640/640, backend185/185 and WPF215/215 passed before the review regression.
Review P2 is now reproduced RED and remains unfixed: invalid DatePicker textbox
input reverts on LostFocus, and Apply commits other draft conditions anyway.

- [x] Retain invalid/raw date draft state; validate raw uncommitted input.
- [x] Verify correction/cancellation recovery and run focused RED-to-GREEN.
- [x] Rerun full WPF suite, update report and republish corrected candidate.
- [x] Complete rollout after confirmed normal Exit.

Read ../../development/history-filter-popover-20261117.md first when resuming.
The existing candidate is explicitly not deployable until this fix is complete.


## Resume progress

Reproduced the DatePicker P2 again RED, then fixed its invalid-state retention
and raw textbox validation. Focused seven-group suite passes, including keyboard,
calendar/cancel recovery and Slovak/US/GB date formats. Full WPF rerun pending.
No Core/backend source changed since the earlier640/185 passes. No client or API
was running at resume; the user's enabled startup entry remains intact.

Resume verification complete: focused7/7 and full WPF216/216 passed, corrected
.2 candidate published, no voice/native/API activity. Newly running client35024
blocked copy safely before mutations; awaiting its normal tray Exit. Current
API identities are48125/49972 and8123/33624 and will be preserved dynamically.


Completed: corrected .2 package installed after confirmed normal Exit; all17
payload hashes and all107 protected-file hashes matched before launch. Client53908
responds, no new client error bytes, both current API processes and startup value
unchanged. Post-launch settings.json change was confirmed by Peter as his own
microphone switch; other106 files remained identical. No voice tests, commit or
push. Redundant candidate removed after verification; previous-app backup kept.
