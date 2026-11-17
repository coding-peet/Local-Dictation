# Compact History and row deletion plan

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

> **For agentic workers:** Use superpowers:executing-plans in the canonical checkout.

**Goal:** Put a confirmed delete action on each History row, remove the redundant
History heading, retain the count without another row, and lower the audio player
while preserving its controls and seeking.

**Architecture:** Reuse AppController.DeleteDictation and its busy/editor guards.
Bind each row's close icon to that row's identity and share confirmation with More
Delete. Use a non-destructive default. Move the count into the existing detail
header and put the 48-DIP transport buttons, seek track and time in one 60-DIP bar.

**Spec:** Peter's History feedback/screenshots, 2026-11-14; PRODUCT_REQUIREMENTS.md.

## Constraints

- Only C:\Git\Local-Dictation\repo; preserve existing dirty changes and both APIs.
- Do not delete any actual user data during verification. Use synthetic stores.
- A small visible cross retains an accessible label and 48-DIP interaction target.
- Cancel does nothing. Confirm targets the clicked ID even if selection changes.
- Preserve RMS scaling, native slider seeking and the existing audio lifecycle.

## Steps

- [x] Add failing real-panel checks for row actions, cancel, stable clicked ID,
  empty/text-only/pending deletion, removed files, compact header and player bounds.
- [x] Implement the row action and shared confirmation; compact History/player.
- [x] Run windowless/Core checks, inspect wide/narrow Light/Dark renders, and review.
- [x] Update Help/requirements and publish a versioned candidate outside the checkout.
- [x] After normal client Exit, deploy with verified backup/data/API preservation.

## Review focus

- Wrong-row deletion when a dialog pumps selection/refresh events.
- Active operation protection and cancellation without side effects.
- Selected-row contrast, narrow text/action bounds, slider usability.
- No audio output or actual history mutation during tests.

Completed in 0.1.0-ux.20261114.1. Final shared checks: 531 Core, 175 backend,
153 windowless WPF. Deployment verified 17 app/backup files and preserved all
117 protected files plus both API identities. See material-usability-audit-20261114.md.
