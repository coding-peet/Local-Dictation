# Intuitive controls and hold shortcut integration

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

Date: 2026-11-14

## Authorized scope

Peter requested confirmed row deletion in History, a lower player, removal of the
duplicate History title, compact shortcut controls with press/hold behavior, and a
whole-application Material 3 usability review.

This implementation extends 2026-11-14-history-row-delete.md and the earlier
startup-shortcut-capture plan. Existing delivered behavior and dirty work remain.

## Decisions and boundaries

- One binding has two mutually exclusive modes: Toggle (existing default) or Hold.
  Existing settings without the new property retain Toggle; do not rewrite them
  just by opening the app. Hold owns only its own Run, and release must never
  stop a replacement/unrelated recording.
- Enable shares the shortcut header. Native Material radio choices expose both
  modes. Keycaps are display elements beside a 48-DIP edit icon, not an input box.
  Capture is explicit, acquires focus, and is blocked while a run is busy.
- History × reuses the existing protected deletion path and confirmations default
  to No. Tests use only synthetic records; no actual user deletion is authorized.
- Keep 48-DIP transport controls and put the seek/time in the same 60-DIP player.
- Repair audit findings through existing WPF components: installed model choices,
  picker focus/local Escape, empty History, model status wrapping and progress
  visibility. Do not invent a new navigation or replace the framework.
- The Models floating-label overlap was a detached-render lifecycle artifact.
  Correct the harness; retain production field semantics.

## Execution ledger

- History row cancellation, stable clicked ID, empty/text-only/pending deletion,
  busy-guard changes, row alignment and empty state: scoped tests green.
- Core hold mode: 39 added cases and 529-test full suite green before final
  Escape/deferred-config review correction.
- Root hold ownership/settings and compact Settings controls: integrated checks
  green, including the installed-model filter.
- Inline picker focus/Escape and model-render settling: three regressions green.
- Integrated suite: 144 silent WPF/controller checks green before final audit fixes.
- Final review found deferred settings after Escape and an Escape reset/suppression
  edge; those corrections are in progress with dedicated regressions.
- Final Models wrapping/progress checks are in progress. Consolidated verification,
  final review, versioned publish and normal-Exit deployment remain.

## Completion

Final source checks: 531 Core tests, 175 backend tests, 153 silent WPF checks;
Release build 0 warnings/errors. Independent findings were fixed and rerun.
Deployed 0.1.0-ux.20261114.1 as PID 39844 after normal Exit. All 17 app and
backup files verified, 117 protected data files unchanged, API PIDs unchanged.
Native hold/input/audio acceptance remains manual. No commit/push in this task.
