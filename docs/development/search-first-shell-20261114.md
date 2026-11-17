# Search-first shell and accumulated change review

Date: 2026-11-14

## Final behavior

Search begins at the upper left. The duplicate application title/header is gone.
Ordinary recording and connection state sits in a compact bottom status bar.
Routine saving is contextual to Dictation/Settings; saving failures remain on
all pages. Actionable global warnings stay expanded above content.

Record/Stop is above Help in the navigation footer, with existing callbacks,
metadata and global availability preserved. Help still supports F1. This is a
workflow choice rather than a universal Material rule; see docs/UI_DESIGN.md
for alternatives, tradeoffs and authoritative links.

History's divider grip is invisible at rest, with the existing transparent
10-DIP hit target and resize cursor. Hover reveals a quiet cue, keyboard focus
uses Accent. Resizing, clamping, stacking, audio and selected text are preserved.

## Review of all accumulated changes

Three read-only subagent scopes reviewed all dirty source and diagnostics from
base 07a9fe6f843db9b2bf2d62ef409cec01bd113528:

- Startup/global input/shortcut capture and Hold ownership/lifecycle.
- Delivery/clipboard/storage/controller/Review preservation and diagnostics.
- Settings/History/player/delete dialog/local help/search/desktop layout.

The sole actionable finding was absent Hold/Press mode search. It now has a
Recording shortcut behavior entry with hold, press, release, toggle and
push-to-talk terms. A regression verifies navigation finds the real control
without changing the radio choices or saved settings. Follow-up shell and
source/Help review found no remaining concrete issue. Older dated delivery and
plan documents are explicitly marked historical to avoid stale UI guidance.

Accumulated source includes automatic service startup, physical shortcut
capture, Hold recording, safe per-row History deletion, compact playback,
installed-model filtering, inline retranscription keyboard handling and improved
Insert failure feedback. The rejected focus-interception change is not restored.
Original icon and native delivery guards are retained.

## Verification

- 531 Core tests passed, zero failed or skipped.
- 175 backend unit tests passed in the candidate service environment.
- 172 integrated windowless WPF/controller checks passed.
- Release diagnostic build: zero warnings and errors.
- Help parsed: 20 unique IDs, 12 internal links; required copy verified.
- Real composed shell/History/settings and warning states inspected in Light/Dark
  wide/compact render artifacts.
- Source whitespace and unchanged icon checked before commit.

Artifacts:

    C:\AI\build\Local-Dictation-search-first

These are synthetic checks and source reviews. They did not use native input,
microphone, audio playback or real-data deletion. Actual Codex/Outlook insertion,
physical key routing, native dialog focus/dragging and Windows high contrast
remain manual acceptance. Help was parsed/content-checked, not browser-rendered.

## Delivery

Commit and push the reviewed source/docs/tests on feature/native-windows without
force, then verify remote HEAD and clean tree. Package/deployment evidence is
recorded separately under the artifact root; a build alone is not installation.

Both API processes, models, protected data and the normal-Exit boundary must be
preserved. Do not overwrite loaded client DLLs or restart APIs incidentally.
