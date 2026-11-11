# Direct Review editing and immediate Settings

User input: 11 November 2026. Current product specification remains
docs/PRODUCT_REQUIREMENTS.md. This change preserves the compact review flow.

## Design decisions

- The same transcript area can become a native text editor. Ordinary text click,
  F2, or Edit text allows whole-text correction without opening another window.
  Existing underlined phrase choices remain the fast path until manual editing.
- Save an actual edit as the authoritative ManualFinalDraft. Keep exact text,
  original model outputs, previous choices, selection, and undo behavior.
- Manual text editing pauses phrase comparison. Explicit Use edited text
  approves the displayed final draft, resolves remaining default wording, and
  attempts insertion at the existing validated target. No insertion on typing,
  Enter, opening, or restoring the editor. Already inserted items are read-only.
- Retain the existing single-paragraph delivery restriction. Do not silently
  truncate pasted text. Control characters disable approval with guidance.
- Settings persist valid discrete choices immediately; text settings debounce
  and validate. Errors preserve last valid settings. Current recordings retain
  their original snapshot. Stop never depends on saving valid settings first.
- Remove competing Save action and nested content frames. Keep Windows native
  chrome, familiar tabs, flat sections, a single primary recording action, and
  passive saving/saved/error state. Keep F1, models/imports, history and service.
- Advertise Large-v3 live support, with readiness false and an honest latency
  hint. This uses the previously tested original FP16 session path, not a new
  inference algorithm. Imported models keep their existing capability gates.
- Bind custom button alignment, add press feedback, and use semantic selected
  text colors and banner colors in Windows High Contrast.

## Verification plan

- Actual WPF Settings regression probe: saved model/checkbox choice, no revert
  on refresh, incompatible verifier recovery, invalid text protection, no initial
  load writes, and Stop independent of settings validation.
- Actual WPF inline editing probe: exact Unicode/whitespace, punctuation and
  deletion, saved restoration, caret stability across refresh, explicit approval,
  blocked controls, Enter behavior, and inserted read-only state.
- Catalog regression: Large live supported without claiming live readiness.
- Existing Core/backend/controller/input checks; native screenshots for final
  Settings hierarchy and inline editor. No automated typing into Codex itself.
- Build and controlled client update with settings/history/model preservation.
