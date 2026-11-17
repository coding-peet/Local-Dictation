# Pre-merge documentation and build safety plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation.

**Goal:** Refresh entry documentation, make client builds safe by default, and
mark the old MSI as an obsolete pilot before committing to feature/native-windows.

**Architecture:** Keep build and deployment separate. Build.ps1 publishes to
BuildRoot\client-candidate by default, refuses the installed app and a running
client's directory, and never stops a process. Keep MSI authoring unchanged;
label its historical packaging limits in the entry points and pilot guide.

**Tech stack:** Windows PowerShell, Python unittest, .NET 10 WPF.

**Spec:** Peter's approved scope, 2026-11-17; docs/PRODUCT_REQUIREMENTS.md,
sections Help/build/continuity and Acceptance boundaries.

## Constraints and review focus

- Work in C:\Git\Local-Dictation\repo on feature/native-windows; no worktrees.
- No recording, playback, inference, client deployment, API changes or merge.
- Preserve source, private data, installed payload, startup and running processes.
- Test omitted/custom output, case/relative aliases, running-client overlap,
  unreadable process identity, and test failure before publish.
- Reject reparse-point output paths instead of trusting lexical comparisons.

## Task 1: Safe build and regression tests

Files: scripts/Build.ps1; tests/deployment/test_build_script.py.

- [x] Run the real script against a temporary SDK double; observe default-output
      and running-output regression failures before implementation.
- [x] Derive the default output from BuildRoot; validate paths before invoking
      the SDK; refuse the installed app, checkout output and running-client
      overlap. Preserve unrelated clients and fail closed on unknown identity.
- [x] Run all deployment tests, then run the real build to an isolated candidate.
      Verify Core test success and complete publish without launching it.

## Task 2: Current entry documentation and historical MSI status

Files: README.md; docs/development/rebuild-and-recovery.md;
docs/development/msi-pilot.md; scripts/build_client_msi.py (description only);
docs/PRODUCT_REQUIREMENTS.md (build boundary and current status pointer).

- [x] Describe General/Models/Dictation/History, current output choices, imports,
      private transcript selection, compact filters, startup and Help.
- [x] Replace machine-live claims with dated validation links and acceptance limits.
- [x] Document safe build defaults and explicit deployment after normal tray Exit.
- [x] Mark MSI obsolete, name missing Material Design dependencies, and distinguish
      historical fixture tests from current payload/installation acceptance.

## Task 3: Verify, review, commit and push

- [x] Review intended diff and documentation links; run git diff --check.
- [x] Obtain a bounded independent review of this change and address real findings.
- [ ] Commit reviewed paths in English and push feature/native-windows, non-force.
- [ ] Verify remote HEAD and clean status; record remaining acoustic/native limits.

## Verification record

- Base: 7c779581a53c8270ba91c78ae9d014ecc55775f8.
- Original build regressions: five expected failures with the temporary SDK;
  no writes outside the fixture directory. Existing success/failure paths passed.
- Relative-output regression reproduced separately, then fixed by resolving
  PowerShell provider paths before GetFullPath. Real 8.3 aliases are normalized
  by this runtime; no extra production alias handling was needed.
- Deployment suite: 18 passed (14 build-safety tests, 4 legacy MSI contracts).
  The MSI fixtures do not certify completeness of the current client package.
- Real Build.ps1: 640 Core tests passed; Release publish succeeded in
  C:\AI\build\Local-Dictation-premerge-20261117\client-candidate. No client launch.
- Backend: 185 synthetic unit tests passed using the existing candidate environment.
- Local documentation link check: 12 links, none missing. git diff --check passed.
- Independent bounded review and follow-up path-resolution review: no actionable
  defects. This is not a new full-branch or acoustic acceptance review.
- No WPF/controller code changed; no new native UI, recording, playback,
  inference, Windows sign-in or external insertion acceptance was attempted.

- Final preservation check: both API listener PIDs/start times unchanged; all
  17 installed payload files have unchanged SHA-256 hashes. No merge or deployment.
