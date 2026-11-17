# Approved execution and protected installation design

Date: 2026-11-10
Status: proposed recommendation, prepared at Peter's request for a solution and
implementation plan. This is not organization-policy or implementation approval.
Execution recommendation: inline, using superpowers:executing-plans.

## Outcome

Run the existing native Local Dictation client under Peter's normal Windows
account, then install its binaries in C:\Program Files\LocalDictation.

Success means an observed native Settings window, connection to the candidate
API, and successful manual recording/Review. Compilation, a policy entry, and an
installer exit code do not establish native acceptance.

## Evidence and uncertainty

Defender identifies LocalDictation.exe and ASR rule
01443614-CD74-433A-B99E-2ECDC07BFC25. Its TrustCheck reports that the file is not
known good. The rule was read as Warn (6), but no completed override event 1129
was found. Manual and administrator launches failed.

Global .NET Desktop/Core 10.0.14 are installed. All eight runtime/native entries
declared in the published dependency file exist. Native startup remains untested.
The build has no signing step; actual EXE signature inspection was denied.
No code-signing certificate was found in the two inspected Personal stores.

Storage.cs was checked: AppPaths.Root uses LOCAL_DICTATION_HOME or the absolute
C:\AI\LocalDictation fallback. Relocating binaries does not change the default
data root; no application source change is needed for that separation.

Detailed machine evidence remains outside Git:
C:\AI\LocalDictation\diagnostics\20261110-windows-execution\DEEP-DIAGNOSIS.md

## Approaches

1. Recommended: policy-owner approval for this application in the identified ASR
   rule, followed by protected installation. This directly addresses the observed
   blocker and needs no new certificate/runtime. It depends on effective policy
   delivery, not merely on an administrator account.
2. Warn-mode Unblock: useful for a temporary test if its supported confirmation
   becomes available. The UI has not worked and no successful override is logged.
   Do not repeat blind launch attempts.
3. Signed MSI/MSIX: suitable for a later distribution channel, but no signing
   identity is available and a signed wrapper does not guarantee acceptance of
   its payload by ASR. It is not the first experiment.

The recommendation is the assistant's choice in response to Peter's request,
not a claim that Peter approved a security-policy change.

## Approval and sequence

Prepare a concrete request for the single current executable
C:\AI\LocalDictation\app\LocalDictation.exe and the single rule above, on this
machine. Ask the policy owner for a temporary pilot allowance with a defined
expiry/removal. The writable development path is not a durable exception target.

The organization policy owner applies approval through its authoritative
management channel. Peter can act in that role if actually delegated that scope;
an installation admin account alone does not establish it. App/build/install
scripts do not change security policy.

After effective approval, make one ordinary --no-hotkey launch as Peter. A
centrally applied exception need not emit event 1129; that event proves a Warn
override specifically. Confirm effective policy and actual native execution.
A newly blocked DLL/rule requires its own owner review, never automatic expansion.

If hashing/copying the original EXE remains denied, report artifact_unreadable.
The owner must provide approved access or an approved build/distribution route.
Do not rebuild in another directory merely to seek an enforcement gap.

Prove startup before investing in the installer. Pure script/fixture-test work
may proceed while approval is pending.

## Installation contract

- Final client directory: C:\Program Files\LocalDictation.
- Source stays in the Local-Dictation repository/worktree.
- Preserve C:\AI\LocalDictation\data and LOCAL_DICTATION_HOME behavior.
- Preserve backend/model paths; candidate 48125 and production 8123 stay separate.
- Retain framework-dependent Windows x64 / .NET 10 deployment.
- Keep UI, documentation, and errors in English.
- Preserve temporary audio, protected pending Review, generic verification models,
  and previously approved dictation behavior.
- Never auto-launch the client from an elevated installer.
- No Defender/UAC/certificate changes in application or installation code.

Use a bounded PowerShell first-install workflow, not a new MSI framework.
It consumes a frozen flat payload and SHA-256 manifest, installs with ordinary
admin elevation, and creates a common Start menu shortcut. Binaries inherit
protected Program Files permissions. Peter launches the client afterward.

The manifest carries schema/product, version, source commit, runtime, filenames,
byte lengths and SHA-256 values. Supply its approved digest separately to the
installer. This is an integrity check, not publisher trust. Include EXE, runtime
DLLs and dependency/runtime JSON; exclude PDBs and all user/model/environment data.

Reject traversal/rooted/duplicate names, reparse points, extra/missing files,
hash mismatches, unknown existing destinations, and running clients.
First installation only: report an existing target instead of overwriting it.
General upgrade and uninstall workflows are out of scope.

Stage beneath the protected Program Files parent. Recheck copied bytes before
publishing the directory. Bound ordinary-error cleanup to this operation's files.
After a process crash, report orphaned staging; do not claim crash atomicity or
perform an unbounded recovery sweep. Never remove user data.

Before final execution, the policy owner authorizes the installed EXE path for
the same narrow rule. Verify normal-user read/execute and lack of program-file
write access. After acceptance, the owner removes the temporary development-path
allowance and records the lifecycle of the final-path allowance.

## Acceptance and rollback

Record native startup, API readiness, microphone/Stop, Review, pending Review
restart, and a disposable native destination field. Test Ctrl+Shift after quitting
TypeWhisper. Cursor/focus changes stop insertion and retain full Review. Codex
insertion is Peter's manual test. Live quality remains separately experimental.

If execution reaches WPF and fails, diagnose that actual application fault.
The security diagnosis does not certify the program as bug-free.

Failure preserves the old portable folder/data. Before publication, remove only
validated owned staging. After publication, report owned artifacts and explicit
rollback steps; do not silently delete an installed folder. The policy owner
removes pilot allowances on rejection, expiry or rollback.

## Primary references

- https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-configure
- https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-faq
- https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-asr
