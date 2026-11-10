# Approved Execution and Protected Installation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Obtain approved native execution and install the existing client in a protected Program Files directory without losing runtime data.

**Architecture:** Prove the current build after the policy owner authorizes the specific ASR scope. Then produce an integrity-checked payload and bounded first-install workflow. Installer code never changes security policy or launches the client elevated.

**Tech Stack:** Existing C# / WPF / .NET 10 win-x64; Windows PowerShell 5.1-compatible deployment scripts; existing py backend and .NET core tests. No new packaging dependency.

**Spec:** ../specs/2026-11-10-approved-windows-execution-design.md

**Status:** Proposed plan only; no organization-policy approval inferred.
**Execution recommendation:** Native/inline with superpowers:executing-plans; the small tasks share one manifest/validation interface.

## Global Constraints

- Final client directory: C:\Program Files\LocalDictation.
- Source stays in the Local-Dictation repository/worktree.
- Preserve C:\AI\LocalDictation\data and LOCAL_DICTATION_HOME behavior.
- Preserve backend/model paths; candidate 48125 and production 8123 stay separate.
- Retain framework-dependent Windows x64 / .NET 10 deployment.
- Keep UI, documentation, and errors in English.
- Preserve temporary audio, protected pending Review, generic verification models, and previously approved dictation behavior.
- Never auto-launch the client from an elevated installer.
- No Defender/UAC/certificate changes in application or installation code.

## Review Focus

1. Missing override evidence does not imply permission or prove failure of a central exception: Task 1 filters events and preserves unknown outcomes.
2. Payload substitution/traversal/reparse entries: Task 2 pins an external manifest digest and verifies source and copied bytes.
3. Admin credentials belong to a different account: Tasks 3/4 do not initialize data elevated and verify Peter's Review afterward.
4. Mid-install failure or a pre-existing target: Task 3 refuses overwrite and confines cleanup to its own staging.
5. Native startup succeeds but another DLL is blocked or insertion retargets: Task 4 captures new blockers and exercises delivery guards.

## File structure

Create:
- scripts/LocalDictation.Deployment.psm1 — event filtering, manifest/path validation.
- scripts/Test-ClientExecution.ps1 — read-only execution evidence.
- scripts/New-ClientPackage.ps1 — frozen flat payload and integrity manifest.
- scripts/Install-Client.ps1 — bounded elevated first installation.
- tests/deployment/Deployment.Tests.ps1 — standalone PowerShell assertions, no Pester dependency.
- docs/development/windows-installation.md — operator procedure and approval scope.

Update after acceptance:
- README.md and AGENTS.md — installed client location.
- docs/development/native-windows.md and completion-followup.md — results/limits.

Application/server source changes require a reproduced defect, not speculation.
Generated reports, fixtures, payloads, transcripts and model data remain outside Git.

## Task 1: Reviewable approval scope and reliable pilot evidence

**Files:** Create the module, Test-ClientExecution.ps1, test script and operator guide.
**Interfaces:**
- Select-ClientAsrEvent -Events [object[]] -TargetPath [string] -SinceUtc [datetime]:
  returns only events 1121/1129 for the exact normalized target and rule
  01443614-CD74-433A-B99E-2ECDC07BFC25; never prefix-path matches.
- Test-ClientExecution.ps1 -TargetPath [string] -SinceUtc [datetime]
  -ReportDirectory [string]: writes evidence.json, schemaVersion=1 with targetPath,
  sinceUtc, matchingBlocks[], matchingOverrides[], observedProcesses[]
  (PID/path/window handle), and readErrors[]. It never launches or grants permission.

- [ ] Add failing assertions: ignore a same-name EXE in another folder, older
      events and another rule; retain matching 1129; zero overrides is an empty
      list, never an inferred allowed=true. Log-access errors stay explicit.
- [ ] Run tests/deployment/Deployment.Tests.ps1 and observe the absent-contract failures.
- [ ] Implement filtering and read-only reporting. Avoid unrelated documents,
      complete system-policy dumps, credentials, or security-setting writes.
- [ ] Run fixtures and a live read-only report. Confirm the blocked target appears
      correctly and nothing is executed or reconfigured.
- [ ] Prepare a local request naming the current EXE, rule, machine scope, owner,
      and temporary duration/removal. Record a hash only if readable; otherwise
      artifact_unreadable. Do not send it without Peter's instruction.
- [ ] External gate: the authorized policy owner applies and confirms the allowance
      through the authoritative channel. Installation admin rights alone are not
      approval of a security-policy change.
- [ ] After effective approval, make one normal --no-hotkey launch as Peter.
      Pass: responsive native Settings window and matching process path.
      Diagnose a WPF failure as a new app fault; record any new Defender blocker
      without automatically expanding the scope.
- [ ] Commit source/tests/operator guide. Keep live evidence under the external
      C:\AI\LocalDictation\diagnostics directory.

Read-only verification commands:

~~~powershell
.\tests\deployment\Deployment.Tests.ps1

.\scripts\Test-ClientExecution.ps1 -TargetPath 'C:\AI\LocalDictation\app\LocalDictation.exe' -SinceUtc '2026-11-10T00:00:00Z' -ReportDirectory 'C:\AI\LocalDictation\diagnostics\execution-pilot'
~~~

Expected: all synthetic assertions pass; evidence represents observations, not
permission. Use a fresh UTC baseline for the actual pilot.

## Task 2: Frozen payload and integrity manifest

**Files:** Extend module/tests; create New-ClientPackage.ps1.
**Depends on:** Approved readable artifact from Task 1 for real packaging.
Fixture development may proceed while approval is pending.

**Interfaces:**
- New-ClientPackage.ps1 -AppDirectory [string] -OutputDirectory [string]
  -SourceCommit [string] -Version [string] creates a new package directory.
- client-manifest.json: schemaVersion=1, product="LocalDictation", version,
  sourceCommit (40 hex), targetRuntime="win-x64",
  requiredFramework="Microsoft.WindowsDesktop.App", minimumFrameworkVersion="10.0.0",
  files[{name,length,sha256}].
- Test-ClientPackage -PackageDirectory [string] -ExpectedManifestSha256 [string]:
  validates the externally supplied digest first; returns a valid manifest or throws.

- [ ] Add failing assertions for changed DLL, missing runtime JSON, unexpected
      file, case-insensitive duplicate, absolute/traversal/nested path, reparse
      source, invalid schema/commit/hash, and replaced manifest.
- [ ] Run the test script and observe the new failures.
- [ ] Implement the flat payload and manifest. Derive required runtime filenames
      from deps.json; require LocalDictation.exe and both dependency/runtime JSON
      files. Exclude PDBs and user/model/environment data. An unreadable file
      fails packaging; never produce a partial success.
- [ ] Copy to new staging beneath the explicit output parent; rehash all copied
      files before publishing. Refuse an existing output. Do not seek another
      location to evade a blocked read.
- [ ] Run fixtures; after approved access, package the real artifact and record
      its manifest digest separately in the review record.
- [ ] Commit source/tests only.

Example after approved artifact access:

~~~powershell
.\scripts\New-ClientPackage.ps1 -AppDirectory 'C:\AI\LocalDictation\app' -OutputDirectory 'C:\AI\build\Local-Dictation\packages\0.1.0-pilot' -SourceCommit '<reviewed 40-character commit>' -Version '0.1.0'
~~~

The manifest is an integrity record, not an Authenticode signature or ASR approval.

## Task 3: Protected first installation

**Files:** Create Install-Client.ps1; extend module/tests/operator guide.
**Depends on:** Task 2's package and separately approved manifest digest.

**Interfaces:**
- Install-Client.ps1 -PackageDirectory [string]
  -ExpectedManifestSha256 [string] [-WhatIf], using SupportsShouldProcess.
- Resolve the x64 Program Files special folder, append LocalDictation; no
  arbitrary destination parameter in the public installer.
- installation.json: schemaVersion=1, product, sourceCommit, version,
  manifestSha256, installedFiles[].
- Common shortcut:
  C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Local Dictation.lnk.
- Module path helpers accept explicit fixture roots for tests; the production
  entry point resolves fixed roots itself.

- [ ] Add failing assertions for wrong digest, non-admin mutation, existing
      target/shortcut, running client, reparse target/parent, path escape,
      interrupted copy and -WhatIf. Assert no target overwrite, data writes,
      security-setting changes, or client launch.
- [ ] Implement preflight: 64-bit process, elevated token for mutation, global
      .NET 10 Desktop availability, verified package, absent target/shortcut,
      and no running LocalDictation client. Report a missing runtime; do not
      install system dependencies automatically.
- [ ] Stage under the protected Program Files parent; copy/rehash, validate
      inherited restrictive ACLs, then publish with a same-volume directory move.
      Reject ordinary-user write rights rather than changing system policy.
- [ ] Write the marker and common shortcut. Never use the admin account's
      Desktop/AppData, initialize settings/Review, or auto-start the client.
- [ ] Inject failure before publication; verify cleanup only of this operation's
      validated staging and unchanged existing data. After publication, report
      exact owned artifacts for explicit rollback. Crash recovery/general upgrade
      is not claimed.
- [ ] Run synthetic tests as normal user. Run -WhatIf with no installation writes.
      Privileged installation acceptance is a distinct approved operation.
- [ ] Commit implementation/tests.

Preview:

~~~powershell
.\scripts\Install-Client.ps1 -PackageDirectory 'C:\AI\build\Local-Dictation\packages\0.1.0-pilot' -ExpectedManifestSha256 '<approved digest>' -WhatIf
~~~

After review, the operator uses normal Windows admin elevation and runs the same
command without -WhatIf. Do not add ExecutionPolicy Bypass.

## Task 4: Final-path approval and native acceptance

**Files:** Update operator guide, README.md, AGENTS.md, native-windows.md and
completion-followup.md. Native changes only for reproduced defects.

- [ ] Before final execution, the policy owner approves exactly
      C:\Program Files\LocalDictation\LocalDictation.exe for the identified rule.
      Confirm effective policy; moving bytes alone is not proof of trust.
      Additional blocked dependencies require their own reviewed scope.
- [ ] Install the frozen payload; verify marker/hashes, normal-user read/execute
      and lack of normal-user modification rights on program files.
- [ ] As Peter, launch --no-hotkey; verify window, API Ready on 48125 and existing
      settings/pending Review. Do not use an admin-token launch for acceptance.
- [ ] Select the microphone, use after-recording Record to Review, Stop and native
      Review. Test optional verification, original-source tabs, phrase choices,
      Copy, Close/reopen and explicit Dismiss.
- [ ] Quit TypeWhisper for Ctrl+Shift testing. In a disposable native field test
      start/stop and cursor/focus changes: no Enter/send, stopped delivery after
      retargeting, and full Review preserved. Peter performs Codex acceptance.
      Live quality remains a separately labeled experiment.
- [ ] Diagnose any actual app defect, add its focused regression, fix the cause,
      run appropriate Core/API tests and re-review a changed binary. Never replace
      the frozen reviewed payload silently.
- [ ] After acceptance, the owner removes the temporary C:\AI pilot allowance
      and records the approved lifecycle of the final-path allowance.
- [ ] Update installed location/results, review diff, push the requested branch,
      verify remote HEAD and a clean worktree.

## Completion and rollback

Complete only after native acceptance and removal of the temporary allowance,
or an explicit owner record of its retained scope/expiry. Keeping the inert old
portable folder is allowed; its deletion is not required.

If authorization is declined, record the dependency and preserve implementation
and data. Follow bounded staging cleanup on install failure. Removing a published
install requires review of the marker/actual files and an authorized exact-target
cleanup; never delete C:\AI\LocalDictation\data. Policy rollback is the owner's job.

## Plan self-review

- Evidence, policy ownership, unreadable-artifact bootstrap: Task 1.
- Data/source separation and payload integrity: Tasks 2/3.
- Protected target, admin-account boundary, failure handling: Task 3.
- Native behavior and temporary allowance retirement: Task 4.
- Signing/MSI, general upgrades, data migration and server promotion are excluded:
  they do not establish whether the existing native build can run.

## Builder check

A fresh-context Windows deployment review found no consequential unanswered
question or technical contradiction. The absolute data-root fallback in
src/LocalDictation.Core/Storage.cs was verified during review. Organization-policy
approval and observed native acceptance remain execution gates.
