# Selected-cursor Insert investigation

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

Date: 14 November 2026.

Current status: .2 was rejected after Peter reported loss of transcription.
The installed client and source focus helper were rolled back to .1. The earlier
error-feedback/diagnostic patch remains; Review insertion is still unresolved.

## Evidence and limits

Peter reported that Insert from Review did not place text in the Codex cursor.
The latest client error at 2026-11-14 00:00:48 UTC stopped in
WindowsTextDelivery.Integrity at OpenProcess, before native paste. The older
error record contained only a generic HRESULT, so it does not establish the
native error code or which process query failed.

A normal manual client restart occurred after the prior deployment. Its process
was not the PID retained in the opt-in delivery-session marker; operational
diagnostics now record their own process ID to distinguish restarts.

With Peter's explicit one-time permission, the prepared target was checked using
only foreground/focused process IDs and UIA control type. Codex was hosted by
ChatGPT, the field was Edit, and the limited process query succeeded then. No
field name, text, value or selected text was read; no input or focus change was
sent. The earlier failing native process query was not reproduced. This is not
acceptance of native insertion and does not justify weakening privilege checks.

## Confirmed UI defect and correction

Compact Review discarded visible insertion feedback in favor of generic cursor
instructions. Its edited-transcript panel also preferred a generic saved-draft
message. A failed selected-cursor action now keeps its error visible in the
Review footer, with warning styling. A retry clears the previous feedback.
The transcript, pending Review and insertion receipt rules remain unchanged.

Native failures now retain the Windows error code, queried process ID and a
fixed operation identifier. General error records include their client process
ID. Logs still exclude exception messages, field names and transcript content.
An access-check failure remains fail-closed before clipboard/native input.

## Verification and candidate

The hidden-error regression failed before the correction and passed afterward.
137 windowless checks passed with no native input/audio. Release diagnostic build
and candidate publish completed with zero warnings/errors. Core results and
candidate hashes are recorded under the artifact directory below.

Candidate:
C:\Git\Local-Dictation\insert-diagnostic-candidate-20261114

Version: 0.1.0-insert.20261114.1, based on 07a9fe6 plus this uncommitted patch.
It includes the earlier reviewed source corrections that were not installed yet.

Artifacts:
C:\AI\build\Local-Dictation-insert-diagnostic

The running installed client's loaded assemblies must not be overwritten.
Normal tray Exit is required before replacement. Both APIs and protected data
must be preserved. Native placement remains unresolved until a manual attempt
confirms it or the improved metadata identifies a repeat failure.

## Installed update

After Peter confirmed normal tray Exit, the client was verified stopped. All 17
candidate files and all 17 backup files matched their manifests before replacement.
The installed app was verified and launched normally as PID 21684; its fresh
delivery-session marker matched 0.1.0-insert.20261114.1+07a9fe6. No --no-hotkey
argument or test taskbar banner was used.

Backup:
C:\AI\build\Local-Dictation-insert-diagnostic\previous-app-before-insert-20261114-003001Z

All 118 protected data/settings files remained byte-identical after startup.
API listeners retained 48125 / PID 34080 and 8123 / PID 53440; session health
remained ready=true, busy=false. Neither API was restarted. Core tests passed
468/468. Native selected-cursor insertion still requires Peter's manual result;
the update does not claim to have resolved the earlier process-query failure.

## Review versus banner comparison and focus correction

Peter then reproduced three Review failures followed by successful banner Insert
of the same ready text. At 00:33:12–22 UTC, error metadata identified access denied
while opening the focused process. Process lookup identified that PID as csrss,
not the target application. At 00:33:25 UTC, banner capture found the expected
Chrome/ProseMirror Edit field; final readback confirmed the provider baseline and
clipboard restoration. The payload was not the differentiating factor.

Review returned MA_NOACTIVATE but still delivered normal button mouse input.
Official WPF ButtonBase source calls Focus() and CaptureMouse() on mouse-down.
The correction consumes the inactive Insert preview down/up events before those
button operations. A completed release raises one managed Click. Mouse leave,
window activation, disabling and close cancel the gesture. Other Review controls
and normal active-window behavior remain unchanged. No privilege, field-ownership,
clipboard or insertion-receipt guard is weakened, and no prior target is guessed.

The managed-input regression failed on the old unhandled preview-down path.
After correction, all 139 windowless checks passed with a clean process exit;
Release build had zero warnings/errors. These checks exercise real routed events
but do not prove native focus behavior in Codex. A new candidate will include the
correction; native Review insertion still needs manual acceptance after deployment.

Focus candidate: C:\Git\Local-Dictation\insert-focus-candidate-20261114,
version 0.1.0-insert.20261114.2. Its 17-file manifest and verification record are
under C:\AI\build\Local-Dictation-insert-focus. Core passed 468/468. Independent
code review found no blocking issue; re-enable and activation cancellation checks
were added before the final 139-check clean-exit run. The currently running .1
client must exit normally before the .2 candidate can replace its loaded files.

After Peter confirmed that Exit, all 17 .2 candidate files and the 17-file .1
backup were verified. The .2 client started normally as PID 56240 with a matching
fresh delivery-session marker. All 117 protected data/settings files present
before this deployment remained byte-identical after startup. API listeners
retained 48125 / PID 34080 and 8123 / PID 53440; session health was ready=true,
busy=false. No native Review insertion was performed by the agent.

Focus-update backup:
C:\AI\build\Local-Dictation-insert-focus\previous-app-before-focus-20261114-005722Z

## Rejected focus update and rollback

Peter reported that .2 no longer transcribed and explicitly requested rollback.
The .2 focus-gesture change is rejected, not accepted as a working fix. It must
not be carried into a later build from this source. Its helper/test source and
installed binaries are archived outside the checkout for investigation.

The stopped client was restored from the verified .1 backup, all 17 app files
matched the backup manifest, and .1 started normally as PID 23720 with a matching
fresh build marker. The .2 gesture helper change and dedicated check registration
were removed from source; the earlier .1 diagnostic/error-feedback patch remains.
No recording, native insertion or audio test was performed during rollback.

Post-start rollback verification confirmed all 117 protected data/settings files
unchanged and both API identities preserved (48125 / 34080 and 8123 / 53440).
Session health remained ready=true, busy=false. The restored source build completed
with zero warnings/errors; its previous 137 windowless checks passed.

Primary references:

- https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/ButtonBase.cs
- https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseactivate
