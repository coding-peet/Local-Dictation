# Compact phrase review in the recording banner

Peter requested this flow on 2026-11-11. It supersedes automatic opening of
the large Review window and mandatory whole-text acceptance on agreement.

- Keep ordinary Review in the existing movable compact banner.
- Underline disputed phrases. Clicking one shows two exact wordings: live
  dictation first, verification second, without source/model labels.
- A choice automatically selects the next unresolved difference. The last
  choice inserts the complete reviewed text once at the original target.
- Successful completion with no differences inserts automatically. Cancelled,
  interrupted, or failed verification keeps the received text for explicit action.
- Capture the original destination when starting with the shortcut, even if
  live cursor insertion is off. That setting controls delivery while recording,
  not the final destination. Preview-only must never insert before completion.
- If no target exists, require Choose destination. Never adopt arbitrary focus.
- Preserve exact text, custom edits, source provenance, durable insertion receipts,
  and the existing original-field validation. Failure waits for explicit retry.
- Large Review is available only through an explicit Details action.
- Closing the compact banner keeps pending text; reopening it does not insert.

The banner can activate for phrase editing after recording, while remaining
non-activating during live capture. Keep its existing placement and drag behavior.
