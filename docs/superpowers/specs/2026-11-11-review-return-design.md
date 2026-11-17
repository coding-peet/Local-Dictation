# Return to cursor and accept verified dictation

Peter requested this amendment on 2026-11-11. It supersedes the permanent
pause and manual-copy Review workflow in the earlier designs.

## Requested flow

- Outside clicks/focus changes pause delivery while recording continues.
- Returning to the original unchanged field resumes delivery at the owned
  draft's end, even when the user clicked inside that draft.
- Moving the banner preserves the destination and manual banner position.
- Verify after recording always requires confirmation, including agreement.
- Persist the complete transcript before removing any owned live text.
- Remove only the owned draft, preserving surrounding text. Keep its target
  lease until Review is accepted or abandoned.
- Accept and insert restores focus to the original field, validates its
  exact expected contents, inserts/replaces the owned draft once, and closes
  Review after success. It never submits the destination.
- Keep one Review window. Closing it preserves pending choices. On conflict,
  leave the Review open and offer choosing a new destination with the shortcut.
- Persist a successful insertion receipt before pending cleanup to prevent
  retries from inserting twice.

## Boundaries

Never recapture a different field automatically. A manual document edit or
uncertain partial insertion is a conflict; preserve the full Review. Older
Reviews without a live destination need an explicit destination selection.
All data and ASR stay local, original model precision remains unchanged.

Live qualification for Large-v3 is a separate measured experiment. Do not
advertise a low-latency guarantee based only on successful batch inference.

## Approach

Extend the existing target guard and WPF client. A new editor or clipboard
automation would add unnecessary state. Separate transient suspension from
permanent invalidation, retain the original UIA element and document snapshot
in memory, and validate again immediately before each operation.
