# General and Windows Startup Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline work and
> dispatching-parallel-agents for the independent startup component.

**Goal:** Provide four workflow sections and optional current-user sign-in startup.

**Architecture:** Preserve the existing WPF shell and controller. Name sections
explicitly instead of relying on old numeric routes. Startup registration uses
injected storage and a small launch policy; only its Windows adapter accesses
its own HKCU Run value. No settings migration changes existing values.

**Tech stack:** .NET 10, C#, WPF, existing MaterialDesignThemes.

**Spec:** ../specs/2026-11-17-general-and-windows-startup-design.md

## Global constraints

- Canonical C:\Git\Local-Dictation\repo, feature/native-windows; no worktrees.
- Preserve all pre-existing uncommitted import/private-picker work.
- English application copy/docs; py for Python scripts.
- No audio, inference, API HTTP tests or API restarts/reconfiguration.
- No real startup registration mutation during automated verification.
- Separate candidate; deploy only after verified normal tray Exit.
- No commit or push without another instruction.

## Review focus

- External startup registration conflicts/read/write failures leave honest state.
- Startup invocation and duplicate-instance handling must remain silent.
- Search must reveal hidden Advanced targets without changing values.
- Section reorder must preserve History activation and save-error visibility.
- Dictionary drafts and compatibility warnings remain discoverable when collapsed.

## Task 1: Startup registration and launch policy

Files: new Core startup policy/registration + tests; new App Windows registry
adapter/controller partial; App.xaml.cs.

- [x] Write failing tests for registration off/readback, quoted paths, idempotence,
  failure/conflict preservation and manual versus --startup launch decisions.
- [x] Run the focused Core tests and observe expected failures.
- [x] Implement the injected manager and scoped Windows adapter. Read never writes;
  explicit toggles affect one owned current-user value only. Never edit StartupApproved.
- [x] Use the tested policy in App startup: startup hides the main window and
  duplicate-instance message; manual launch keeps both existing behaviors.
- [x] Run the focused and full Core suite without touching real registration/API.

## Task 2: General and workflow navigation

Files: MainWindowShell.cs, SettingsWindow.cs and partials/search;
new GeneralStartupChecks.cs; existing affected detached diagnostics.

- [x] Add failing detached checks for General/Models/Dictation/History order,
  initial Dictation, General controls and startup toggle with an injected store.
- [x] Introduce named MainSection routes; preserve all four page instances.
- [x] Build General from Startup, Appearance, Shortcut, History and collapsed
  Advanced service cards. Move dictionary/compatibility to collapsed Dictation
  Advanced and expose unsaved/error summaries outside collapsed content.
- [x] Update search categories/routes and expand Advanced before revealing targets.
- [x] Retarget existing diagnostics semantically; retain their original assertions.
- [x] Run focused checks and full detached suite; inspect Light/Dark compact/wide renders.

## Task 3: Review, documentation and rollout

- [x] Update requirements and offline Help. Record decision: corrected user order
  applies; retention remains General per explicit approval text.
- [x] Obtain independent read-only review, resolve material findings, rerun needed checks.
- [x] Publish candidate, verify 17-file payload and preserve data/API listener identities.
- [x] After normal Exit, back up and replace client, launch and verify process/logs.
- [x] Finish documentation and report acoustic/logon/native acceptance limits.

## Ledger

- Approved scope recorded before implementation. Earlier uncommitted work remains.
- Ruling: preserve General retention from the explicit approval while honoring
  the user's corrected section order. No new History-options UI is added.

- Task 1 complete: startup39/39 and Core640/640 pass; initial and command-limit
  regressions observed RED before fixes. Real registry/API/audio untouched.
- Task 2 complete: navigation RED observed; final detached WPF209/209 pass,
  compact/wide Light/Dark renders inspected, including genuinely expanded bodies.
- Task 3 review complete: Run command limit and stale requirement labels fixed;
  offline selection-animation residue corrected in diagnostics. Backend185/185.

- Task 3 complete: reviewed final candidate deployed after confirmed tray Exit;
  17/17 payload and 107/107 protected-file hashes match; client34312 responds,
  no new client error bytes, both API identities unchanged. User independently
  enabled Start with Windows and confirmed the observed valid own registration.
