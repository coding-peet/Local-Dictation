# Kubo Note display pages implementation plan

**Goal** — Give the Note independently enabled, reorderable pages for Tasks,
Events, the current calendar month, and the next calendar month.

**Architecture** — Store the full four-page order and enabled state in one
Note preference, served through the existing shared settings route. The upper
button walks the enabled order after Home and returns to Home at the end.
Calendar grids use the existing local clock and ISO week calculation.

**Stack** — Arduino C++, ArduinoJson and Preferences, shared HTML/JavaScript,
and the existing Python Bridge and verification tools.

## Constraints

- Use the canonical checkout and the working integration branch.
- Retain Tasks and Events pagination, a short lower press to return Home, and upper hold
  to record on display pages. Preserve the Home lower-button actions and skin.
- Enable Tasks, Events, current month, and next month in that order on
  the paired firmware. Home is fixed. A disabled page keeps its position. With all pages disabled a short upper press
  on Home leaves Home visible.
- Calendar pages are offline. Highlight today only on the current month and
  use an explicit unavailable date state if the device clock is invalid.
- Keep synchronization changes and deployment outside this calendar change.

## Checks and implementation

- [x] Extend the C++ calendar test for six rows, leap February, and December
  to January including ISO week 53. Exercise the ordered page cursor with
  disabled and reordered entries, all disabled, and malformed saved input.
- [x] Verify Bridge validation and readback of the compact four-page setting
  across its normal proxy. Reject duplicates and unknown IDs. Confirm older
  firmware omits the section and settings without pages remain usable.
- [x] Add the NVS preference, firmware renderer and gestures, and shared
  settings controls. Retain disabled rows in the editor and save the complete
  list together. Update the device guide and product requirements.
- [x] Run the firmware calendar and page checks, shared web and Bridge
  suites, then build both PlatformIO environments to separate build output.
  Hardware and e-paper acceptance follow in a separate authorized session.

Both firmware environments built. All 149 Bridge tests, nine web tests,
and eleven C++ test files passed their checks. The shared settings portal
and four configurable pages are in the paired variant. Physical acceptance
remains open.
