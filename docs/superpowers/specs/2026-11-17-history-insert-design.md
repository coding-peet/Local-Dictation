# History Insert at cursor

## Requested behavior

Add Insert at cursor to History More actions. Insert the highlighted transcript
text; when there is no text selection, insert the whole selected transcript.
The user confirmed this selection rule on 2026-11-17.

## Approaches

Immediate insertion from the menu cannot capture the external field: opening
the menu has focused Local Dictation. Remembering an arbitrary previous field
would introduce stale-target behavior and is not proposed.

Proposed: Insert at cursor opens a small placement window showing the exact
text snapshot and one Insert button. The user clicks the destination field,
then Insert. Reuse the corrected native activation behavior and existing guarded
capture/delivery path. This extra step makes the destination explicit.

## Data and interaction

The text snapshot is independent of the saved source, including already-inserted
items. It is memory only, does not create a History item, and does not alter the
source's wording, choices or insertion receipt. Closing the placement window
cancels that copy. A successful action closes it. Failure keeps the text and
shows a recoverable message. Disable concurrent insertion and recording through
the existing controller guards; never dispatch a stale cursor automatically.

No microphone, inference, API restart, installer or Git publication is included.

## Verification

Test selection versus full text, an already-inserted source, no source mutation
or saved duplicate, failed and repeated clicks, and cleanup on close. Preserve
clipboard/epoch/integrity guards. Verify layout offline and the selected-cursor
capture with an owned synthetic external field, without native paste.

Status: proposed workflow awaiting user approval.
