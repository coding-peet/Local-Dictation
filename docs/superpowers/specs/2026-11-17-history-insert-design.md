# Direct History Insert at cursor

## User decisions

The user rejected the placement window on 2026-11-17. Selecting Insert from
History More actions must perform insertion directly, with no intermediate
window. Use highlighted transcript text, or the whole selected transcript when
there is no highlight. Preserve the saved source and its insertion receipt.

## Approach

Remember the most recently foreground external application using a native
foreground-event observer. Keep only its native window and process identity in
memory; do not read text or the clipboard while observing application changes.
Local Dictation windows and taskbar/desktop surfaces do not replace this target.

After closing the History menu, restore that external window once and capture
its current editable field/caret through the existing guarded delivery path.
Do not choose an arbitrary field or synthesize keyboard shortcuts to switch apps.
A missing/closed/replaced window, denied activation, unsupported field, intervening
user input or clipboard recovery stops insertion with an inline History message.

The prior placement-window approach is rejected. Restoring a validated previous
window is the necessary tradeoff of the requested direct menu action. Windows
may deny activation; failure must preserve text and avoid automatic retry.

## Data and operation lifetime

Snapshot the exact selected text before closing the menu. Insert an independent,
memory-only copy so saved text, comparison choices and previous receipts remain
unchanged, including for already-inserted source items. Do not create a saved
History or private-review duplicate. Retain published carriers and unfinished provider work for delayed-confirmation/
no-duplicate handling regardless of the transport-failure flag. Expire terminal
unused attempts after two minutes; never expire pending acknowledgements/work. A fresh explicit successful
History reuse is a separate action. Prevent double-click races and recording or
exit during the active insertion check. Preserve existing clipboard, integrity,
field identity, key-release and delivery guards.

## Validation and scope

Use synthetic controller tests for selection/full text, immutable source data,
failed restoration, intervening input, concurrent clicks, retries and receipts.
Use detached layout/menu checks. An owned external-field test may check capture;
it is distinct from real paste acceptance. No voice tests or running API changes.
Client deployment uses the verified client-only update procedure. Commit and
push were subsequently requested on 2026-11-18; merge is outside scope.

Status: direct workflow explicitly requested, implemented and deployed; placement
window rejected. Native paste acceptance remains a separate manual check.
