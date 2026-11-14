# Material dialog and workspace refinement plan

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

> **For agentic workers:** Use superpowers:executing-plans in the canonical checkout.

**Goal:** Finish Peter's dialog, History, shell and local-help feedback while
preserving the approved icon, settings/data and running APIs.

**Architecture:** A small themed owner-modal decision window replaces MessageBox.
History keeps Material scrollbar templates and a constrained GridSplitter. The
shell moves primary Record and secondary Help into the named navigation column;
routine save feedback becomes a contextual aligned footer. Dictation explanations
use the existing local information helper.

## Completed implementation and review

- [x] Material deletion: safe Cancel/default/focus/close, explicit Delete, timestamp
  without transcript, no data changes from dialog; global shortcut guard/finally.
- [x] 10-DIP History scrollbars; wide splitter with 160/240 minimums, clamp and width
  restoration; selected text/audio retained; compact stacking hides divider.
- [x] Record above Search, Help below destinations/F1; compact identity/connection
  header; contextual save footer aligned vertically; errors visible everywhere.
- [x] Record/Stop wording and Review/external-cursor tooltip; local Auto/latency info;
  smaller muted support qualifier; preserve actionable model warnings.
- [x] Independent read-only review: no concrete findings after scoped corrections.
- [x] Integrated windowless suite: 170 passed; build 0 warnings/errors.
- [x] Final Core regression: 531 passed; Help parse/content/link checks passed.
- [x] Published 0.1.0-layout.20261114.1 and verified all 17 package/backup files.
- [x] Client was already stopped; guarded deployment started PID 43820 normally.
  All 115 protected data files and both API listener PIDs were unchanged.

Delivery evidence: ../../development/dialog-shell-refinement-20261114.md.

No native window/input/audio checks or real-data deletion during verification.
Native modal/drag/focus acceptance remains manual. Icon assets remain unchanged.
