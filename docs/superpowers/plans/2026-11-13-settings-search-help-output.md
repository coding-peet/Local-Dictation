# Settings search, contextual help and final output mode

## Approved scope and design

Peter requested settings search, compact information buttons and After recording.
He asked for critical assessment of a three-level slider. Use two independent
controls: After recording (Insert directly / Preview first) and Verify after
recording. The four combinations are valid; a three-step slider would hide one
or imply a misleading quality ordering. Preview first suppresses all automatic
target writes, including live insertion and verification agreement. Live ASR may
continue in the banner. Preserve the saved live-insertion preference when it is
temporarily unavailable. Settings apply to new dictations; do not alter an active
run's snapshot. No Home or Appearance redesign is in scope.

Use the existing WPF stack, MD3 resources and named navigation. English UI/docs.
Canonical checkout only: C:\Git\Local-Dictation\repo, feature/native-windows.
Preserve all earlier uncommitted changes. No new worktrees, commit/push, private
data changes, microphone/audio, desktop control, API restart or model tuning.

## Task 1: Search (delegated isolated files)

- Create Core SettingsSearchEntry(Id, Category, Label, Description, Keywords) and
  SettingsSearch.Find(entries, query), with matching/ranking unit regressions.
- Create a reusable WPF SettingsSearchControl and integrate it into
  MainWindowShell. Expose shell.Search for SetEntries, Selected event and keyboard
  focus. Search results remain inside the main window; selection does not modify
  settings. Root integrates actual setting IDs/targets in SettingsWindow.
- Match labels, categories, descriptions and keywords without case/diacritic
  sensitivity; exact/prefix labels rank first. All query terms must match.
- Keep narrow named navigation, selected page state and actual content usable.
- Add isolated windowless checks; do not edit SettingsWindow, Ui, Program or
  existing diagnostics shared files in this task.

## Task 2: Contextual help (delegated isolated files)

- Add WPF SettingsHelp and use it from Ui.Label/Ui.SwitchRow for static hints.
- Show small info glyph with tooltip and click/keyboard popover, accessible name
  and help text. Do not create a dialog or move task errors into help.
- Info interaction must not toggle a switch row. Preserve actual controls,
  floating labels, current styling and ordinary row click behavior.
- Add isolated windowless checks; do not edit SettingsWindow, MainWindowShell,
  Program or root's output policy files.

## Task 3: Output mode and integration (root)

- Add AfterRecordingAction enum and Settings.AfterRecording, default InsertDirectly
  for old settings. Reject unknown saved values and fail closed in delivery.
- Extend CompactCompletion.Decide with the mode; Preview first opens existing
  Review even with no differences or verification agreement, and never inserts.
- Guard all live/resume/final delivery paths. Keep explicit Review Insert working.
- Add output selector and dependent row visibility, preserve live-insertion
  preference, and wire settings search IDs to pages/controls. Hidden dependencies
  navigate to their parent without enabling them or changing settings.
- Test all four combinations, Preview live suppression, settings roundtrip/default,
  and continued explicit Insert. Register delegated windowless checks.

## Task 4: Verification and delivery

- Update requirements, Help and validation report. Keep runtime warnings visible.
- Run full Core tests, complete windowless checks and Release publish. Inspect
  real narrow/wide renders and one independent review; fix findings.
- Prepare a separate candidate and manifests. The running installed client is
  0.1.0-review.20261113.1; replacing it requires another normal Exit, never force
  termination or writing loaded binaries. Preserve APIs/models/settings/history.

## Progress

- Baseline: HEAD 1236dd8 with prior reviewed changes still uncommitted.
- A new deployment is not implied by preparation alone. No general permission
  questions are needed for the requested feature implementation.
- Search, contextual help and output mode implemented. Independent review issues
  fixed, including fail-closed selection and durable explicit-insertion policy.
- 468 Core, 175 backend and 115 windowless checks passed; Release build/publish
  clean. Narrow/wide Light/Dark renders inspected without native UI or audio.
- Candidate 0.1.0-settings.20261113.1 prepared separately with 17-file manifest.
- On 14 November, after confirmed normal Exit, deployed and verified all 17
  files and their backup. Started build 0.1.0-settings.20261113.1 as PID 2516;
  all 115 protected data/settings files and both API identities were preserved.
