# Reliable cursor dictation and Material Design 3

Date: 2026-11-13

## Authority and outcome

Peter completed the Slovak interview and explicitly requested implementation.
The capture is brainstorms/2026-11-13-local-dictation.md in the Pala Note workspace.
This document translates the agreed behavior into an English engineering spec.
Implementation decisions belong to the builder; no further preference interview is needed.

Ctrl+Shift starts recording without stealing focus. A second Ctrl+Shift finishes,
delivers the final result automatically, and closes the minimal banner on success.
Live cursor updates are preferred, including corrections after a speech pause.
Reliability takes priority: Outlook uses final-only insertion, and a failed live
destination is remembered for final-only insertion from the next session.

## Delivery and ownership

Keep live recognition independent of cursor delivery. A session owns only text it
inserted in the exact original field and native window. Preserve surrounding text,
Unicode, and user edits. Never synthesize Enter or any send command.

Switching focus pauses insertion while recording continues. Returning to the same
unchanged field during recording resumes updates. If Finish is pressed elsewhere,
retain the result for an explicit small Insert action. Returning later must not
trigger automatic delivery or steal focus. Check this before ending capture, not
after activating the target. Explicit Insert may restore the validated original field.

Current evidence: a Chrome/ProseMirror editor exposes generated placeholder content
through TextPattern, managed ValuePattern and native legacy values. A normal first
paste replaces that accessibility projection with the exact payload. The old guard
expects placeholder plus payload, fails acknowledgement and blocks all later updates.
LF trimming and an empty ValuePattern test do not solve this case.

Correct the first-paste ownership boundary using explicit, testable evidence. Do not
guess placeholder text, assume a nonempty field is empty before input, normalize
surrounding text, or replay an uncertain paste. If adopting a changed provider baseline,
limit it to the known provider, a first insertion through the already validated collapsed
caret without range selection, exact complete payload readback and exact ending caret.
Rebase the owned start to the observed position before any later revision. A reviewer
must assess this evidence and negative cases. If it cannot be made safe, use the approved
final-only compatibility strategy and document native acceptance limits.

Treat partial/unknown native dispatch as uncertain, never as permission to retry.
Keep clipboard capture, exclusion from history/cloud, supported formats and protection
of newly copied clipboard data. Restoration is automatic after acknowledged delivery.
No Restore clipboard control belongs in the normal banner. An unresolved technical
failure must remain recoverable through exceptional diagnostics rather than silently
discarding clipboard ownership or pretending delivery succeeded.

Remember failed live applications by executable identity (no window title or field text).
Persist a case-insensitive executable-name set in settings. Apply it only to later
sessions. Offer a reset/re-enable action in Settings. Do not mark focus pauses or
user edits as application incompatibility. Only terminal live transport/ack/range
failures qualify; capture unsupported by the provider may use final-only up front.

## Minimal banner and separate Review

Replace the large preview/review banner with a small movable capsule, approximately
280-320 logical pixels wide and 56-64 high before transparent shadow margins.
Show a recording indicator/waveform, short phase label and duration. Small finish
and cancel icon controls remain available; the shortcut is the normal interaction.
Show a discreet Insert text button only when an explicit result awaits delivery.
Put model, microphone, transcript preview and technical metadata in their own views.

Keep WS_EX_NOACTIVATE, ShowActivated=false, exact original caret capture and movable
header/waveform. Preserve user placement and monitor/DPI handling. Close promptly after
successful delivery; do not require a review or clipboard step when verification is off.

Verification remains optional. Matching results deliver automatically. Disagreements
open a separate compact Review window hosting the existing phrase-choice and editing
logic. Keep full Details available. Do not put an interactive editor inside the recording
banner. Pending results and protected history survive hiding/closing views.

## Material Design 3 across the application

Use the existing WPF platform with MaterialDesignInXamlToolkit's MaterialDesign3 defaults.
This is a community WPF implementation, not an official Google WPF SDK. Pin a verified
stable package version compatible with net10.0-windows; no platform rewrite is required.

Use semantic tonal surfaces, primary/on-primary roles, Roboto typography supplied by the
toolkit, rounded Material buttons, outlined/filled text fields, switches, tabs/lists,
menus, progress indicators and accessible focus states. Keep English copy and support
System/Light/Dark plus Windows high contrast. Use 8-unit spacing and avoid custom control
templates that replace real toolkit behavior. Update Settings, Models, Pending Review,
History and full/compact Review, not just colors on the banner.

TypeWhisper's minimal nonactivating overlay and automatic clipboard transaction are
references. Its inspected release/1.2 streaming path is preview-only; it does not prove
our repeated live replacements. Reference source was not integrated into this app.

## Scope and verification

Work only in C:\Git\Local-Dictation\repo, feature/native-windows. Preserve dirty work.
No new worktrees, commits or pushes. Models/user data stay outside Git. Use py and the
existing SDK C:\AI\tools\dotnet-10.0.403\dotnet.exe. Do not modify the TypeWhisper backend
on 8123 or security controls. A Defender-blocked diagnostic executable must not be
renamed or rehosted to bypass its block; tests and normal product builds remain separate.

Use meaningful failing-then-passing regression tests for delivery behavior, focus-at-Finish,
provider projection boundaries, later corrections, per-application fallback and settings
roundtrip. Build Release and run existing Core/backend suites. Verify native insertion
where available and visually inspect light/dark UI. Codex and Outlook require Peter's
manual application acceptance; do not claim it from compilation or simulated fixtures.

Deployment is already authorized with a verified backup. Publish a separate candidate,
then replace only the product payload if the running client has safely exited. Preserve
all model/history/settings data and report the exact deployed build and test limitations.
