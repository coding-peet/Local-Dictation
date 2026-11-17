# Compact History filters

Date: 2026-11-17
Status: implementation approved by Peter.

## Intent and selected approach

Replace the always-present History filter card with a small Filters trigger in
the search/import row. A floating, clearly separated panel holds the advanced
conditions. Only applied filters have removable chips. No inactive filter row
or empty card consumes transcript space.

Considered: a smaller inline expander still permanently consumes a row; always
visible filter chips also consume space when no restriction is used. The selected
button/popover plus applied-chips approach keeps the ordinary History workspace
compact and makes active restrictions discoverable.

## Behavior

Search remains immediate and independent. The popover edits a draft of From,
To, Model, Needs review, Text only and Inserted. Apply validates and commits;
Cancel, Escape, outside click, navigation and disposal dismiss the draft. Opening
alone changes nothing. Invalid dates/ranges retain the applied query and show
an inline error. Enter applies unless a child picker/dropdown is handling it.

Applied restrictions show a count on Filters and a single horizontally scrollable
chip row. A date range is one chip; model and each selected status are separate.
Removing a chip updates only that condition. Clear filters clears restrictions,
not the search text. No active restrictions means no chip row or reserved space.

Use the existing Material WPF button templates, semantic surfaces and capsule
geometry. The popup is a form, not a ContextMenu. Keep it within available width
with normal keyboard focus and explicit focus restoration on Cancel/Apply/Escape;
outside clicks must retain the clicked target's focus.

## Boundaries

Preserve transcript contents, selected identity, filter semantics, imports,
private-next guards, playback lifetime and existing dirty work. Synthetic tests
and detached rendering only: no voice, native input/audio, model inference or
API changes. Normal tray Exit is required before replacing the installed client.
No commit/push without a separate instruction.
