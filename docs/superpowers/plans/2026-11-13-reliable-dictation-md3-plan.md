# Reliable dictation and Material Design 3 Implementation Plan

> For agentic workers: use test-driven implementation and scoped independent workers;
> integrate and review the complete working-tree diff without committing user work.

Goal: Reliable shortcut-driven dictation with a minimal nonactivating banner and MD3 UI.

Architecture: Keep the WPF client and local ASR. Repair native acknowledgement/ownership
separately from rendering. The controller owns delivery/focus/fallback policy; views
consume it without assuming the recording banner is an editor.

Tech stack: .NET 10 WPF, existing Core, MaterialDesignInXamlToolkit MaterialDesign3.

Spec: docs/superpowers/specs/2026-11-13-reliable-dictation-md3-design.md

## Global constraints

- Canonical source C:\Git\Local-Dictation\repo; branch feature/native-windows.
- Preserve all pre-existing dirty/untracked files. No commits, pushes or worktrees.
- Use py. Keep private data, logs, models and generated output outside Git.
- Never synthesize Enter/send, retarget arbitrary focus or replay uncertain input.
- Preserve original text/clipboard and the TypeWhisper production runtime on 8123.
- Product copy/docs English. Capture/interview Slovak.

## Review focus

- Placeholder projection must not accept arbitrary deletion of surrounding real text.
- A later correction must replace the complete owned suffix after the first baseline.
- Finish while elsewhere must remain pending even after the original field returns.
- Focus pauses/user edits must not blacklist an otherwise compatible application.
- Theme changes/high contrast must preserve readable field/focus/selection states.

## Task 1 — Native insertion acknowledgement

Files: Services/WindowsTextDelivery.cs; Core/InputState.cs and a focused acknowledgement
policy if useful; Core.Tests regression file; existing DeliveryProbe fixture only if
needed for characterization. Do not edit UI or AppController.

- [x] Read the full current path, metadata evidence and existing native fixture.
- [x] Write regression tests reproducing initial 16-unit placeholder readback, first
  payload-only result, later continuation/earlier-word correction, actual surrounding
  text, wrong caret/provider, non-first insertion and partial input. Run to RED.
- [x] Repair the provider ownership boundary without normalization or replay; require
  the spec's first collapsed native paste proof before a bounded rebase, if used.
- [x] Run targeted then full Core tests to GREEN. Build product Release using a separate
  artifact root. Native fixture execution may be blocked; report rather than bypass it.
- [x] Write a concise report with exact commands, changed paths and remaining proof limits.

## Task 2 — Material Design 3 and minimal views

Files: App.csproj, App.xaml, Theme.xaml, Services/ThemeManager.cs, Views/*.cs.
Do not edit AppController, Core or delivery services. Read controller APIs as needed.

- [x] Verify toolkit package/docs; add pinned MaterialDesignThemes and MD3 resource defaults.
- [x] Replace custom primitive templates with toolkit component styles, semantic role
  brushes, 8-unit spacing and correct typography across every window.
- [x] Rebuild BannerWindow as the compact nonactivating movable capsule from the spec.
  No transcript/editor/clipboard restore UI in the normal banner.
- [x] Create CompactReviewWindow hosting CompactReviewPanel. Expose a constructor taking
  AppController and keep the existing callbacks. The controller will own opening it.
- [x] Keep the small explicit Insert action through existing controller APIs.
- [x] Build with a separate artifact root. Verify resources and dynamic theme switching;
  report any required controller integration precisely. Do not add mirror-image UI tests.

## Task 3 — Controller, settings and integration

Files: AppController.cs, Core/DestinationDeliveryPolicy.cs, Settings storage/model,
Core.Tests policy tests, PRODUCT_REQUIREMENTS.md, Help/index.html, validation record.

- [x] Add failing tests for persisted per-app final-only preference and finish-outside
  policy. Implement a small pure policy shared by controller/tests; do not mock the UI.
- [x] Record original destination process, focus at Finish and terminal live failures.
  Prevent automatic completion from restoring focus when Finish occurred elsewhere.
- [x] Persist compatibility fallback only for terminal transport failures; Outlook stays
  final-only. Expose the reset action to Settings through the controller.
- [x] Open separate compact Review only for unresolved choices/manual editing and keep
  ready text available for the banner's small Insert action. Hide banner on success.
- [x] Run Core/backend tests and Release publish. Inspect MD3 Light/Dark rendering.
- [ ] Native acceptance: physical banner drag/DPI, Codex pause/continue and Outlook.
  These remain explicitly unverified; no blocked diagnostic was bypassed.
- [x] Obtain a fresh whole-change review; fix important findings and rerun relevant checks.
- [x] Publish candidate and deploy using existing authorization and hash-verified backup
  after checking running product state. Do not discard pending clipboard transactions.
- [x] Record actual native/manual acceptance limits and final source/build identity.

## Execution closeout

Implementation and deployment completed 2026-11-13. Fresh whole-change review has no
unresolved findings. Final evidence: 343 Core tests, 116 backend tests, Release publish,
17 installed payload hashes, actual client window Ready and Ctrl+Shift enabled.
Only native/manual acceptance listed above remains open. See
docs/development/reliable-dictation-md3-20261113.md for exact limits and runtime state.


## Recovery and shape follow-up

Implemented and deployed the recovery/receipt and true-capsule/cloud follow-up.
All identified scoped review findings are closed. Latest evidence: 362 Core
tests, 116 backend tests, zero-warning Release publish and 17 verified installed
files/backups. Process/API checks passed without further desktop control.
Native acceptance remains open; no real insertion was inferred from these checks.
See docs/development/clipboard-recovery-and-shapes-20261113.md.
