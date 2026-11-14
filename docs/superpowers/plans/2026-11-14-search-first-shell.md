# Search-first desktop shell implementation plan

> Execute in the canonical checkout; preserve all earlier reviewed changes.

Goal: give History and settings more content space, retain a stable global
Record action, and stop repeating the native window title inside the content.

## Design and rationale

Search occupies the upper-left position. Named navigation remains 160 DIPs.
Record/Stop moves above Help at the bottom of the navigation column. This is
an application-specific choice: shortcut dictation is the primary workflow;
the app button starts a Review recording and remains visible on every page.
A top command row was considered for discoverability, but would keep consuming
content height; a page-only action would remove access from History/Models.

Remove the duplicate application heading and its 64-DIP header. Put ordinary
recording/connection and contextual saving feedback in a compact bottom status
bar. Keep actionable global alerts expanded above the content. Do not change
recording, clipboard, focus ownership, models, storage or API behavior.

Hide the History divider grip while idle. Keep its existing hit target, resize
cursor, drag and keyboard behavior; reveal a quiet grip on hover and Accent on
keyboard focus. Add the missing Hold/Press search entry found during review.

Sources reviewed on 14 November 2026:

- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/command-bar
  Documents persistent command placement; top is generally more discoverable
  on larger screens. Bottom placement here is our workflow tradeoff.
- https://learn.microsoft.com/en-us/windows/win32/controls/status-bar-reference
  Defines a bottom status area for desktop windows.
- https://learn.microsoft.com/en-us/windows/win32/uxguide/ctrl-status-bars
  Historical guide: critical errors must remain prominent in the primary window.
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview
  Separates navigation, search and footer regions.
- https://m3.material.io/
  Material component roles/visual language; the JavaScript-only component pages
  were not fully extractable in this research. Do not claim a mandated location.

## Work

- [x] Change shell layout and update its measured layout/visibility assertions.
- [x] Hide idle divider grip without removing keyboard/hover feedback.
- [x] Add Hold/Press settings search route and verify read-only navigation.
- [x] Update requirements and offline Help; retain historical delivery records.
- [x] Independent subagent review of all dirty changes; resolve concrete findings.
- [x] Run Core, backend and integrated windowless checks; inspect final renders.
Delivery procedure: commit the reviewed source/docs/tests and push the existing
branch without force. Verify remote HEAD and a clean checkout. Package/deployment
results belong in the artifact manifest and final delivery response; deploy only
after freshly confirming normal client shutdown.

No native input, microphone, audio playback or real-data deletion in verification.
