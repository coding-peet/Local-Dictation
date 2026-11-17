# Clipboard recovery and desktop shapes

Date: 2026-11-13

## Source and scope

Canonical checkout: C:\Git\Local-Dictation\repo

Branch: feature/native-windows

Base HEAD: b494dca1845394af04d0d1e18eec06568ce1292a

This update preserves the existing uncommitted implementation. No commit, push,
additional worktree, model conversion, security change or TypeWhisper change
was made. Models, settings, recordings, protected History and Pending Review
remain outside the app payload replacement.

## Reported issue and evidence

Peter reported text reaching Outlook while the banner stayed in Clipboard
recovery, with no usable recovery action. The old client had metadata logging
disabled, so the precise Outlook readback for that occurrence is unavailable.
No new Outlook whitespace/projection exception was inferred from the report.

Source inspection found no delayed read-only acknowledgement after the initial
paste check, no acknowledged-only restoration retry, repeated recovery
notifications for the same lease, and no accessible recovery window. Review
also found that a confirmed receipt could be lost across a worker timeout or
target dismissal. The update addresses those code paths without replaying input.

The previous process exited before deployment preparation. The agent did not
force-terminate it. Restoration of its original clipboard was not confirmed;
its in-memory backup cannot be reconstructed from this new build.

## Resulting behavior

- Normal shortcut dictation retains automatic delivery and clipboard restoration.
  Outlook remains final-only after Ctrl+Shift finishes recording.
- A fully dispatched uncertain paste can receive read-only acknowledgement for
  up to ten monotonic seconds from dispatch, while the exact original field,
  provider, input generation and held clipboard payload remain valid. Checks
  never focus, select, paste, delete or send anything.
- For deferred acknowledgement and immediate successful final clipboard paste,
  a confirmed receipt is published before acknowledged clipboard state becomes
  visible. It survives timeout and later invalidation until the controller
  consumes it. Restoration happens separately after receipt handling.
- Dismissal/replacement invalidates further acknowledgement and drains proven
  receipts before removing their delivery target. History retains insertion proof.
- Exceptional recovery opens a separate usable window from the small Resolve
  action, Settings or tray. Unconfirmed restoration requires the explicit
  destination-inspection checkbox. An already confirmed backup can retry
  restoration without sending text. X still hides the banner and retains data.
- Action buttons use true semicircular endcaps computed from their actual layer
  dimensions, with 48-DIP hit targets and 40-DIP visible bodies. The earlier
  fixed 4-DIP interpretation is superseded. State/ripple/focus layers stay aligned.
- The banner removes the toolkit's opaque window canvas. Its decorative edge
  fades to fully transparent pixels while text and controls remain sharp.

## Verification

The final full Core suite passed 362/362 tests. After the immediate-final receipt
extension, the relevant 19 queue/deferred cases passed again; Core policy source
was unchanged. Final integrated Release publish passed with zero warnings and
errors. Scoped code review closed all identified recovery findings. The backend
suite passed 116/116 tests. Its first restricted run encountered temporary-directory
cleanup permission errors; the unchanged suite passed with normal temporary-directory
access in the candidate environment. Whitespace validation passed.

Evidence:

    C:\AI\build\Local-Dictation-recovery-native\results\core-full-retained-receipt.trx
    C:\AI\build\Local-Dictation-recovery-native\results\final-paste-receipt-green.trx

Actual WPF Light/Dark resource renders verified the action-state geometry, ripple
clip, focus, outer hit corners and non-overlap. The normal 40-DIP body has radius 20,
and its concentric 48-DIP focus boundary radius 24. Growing text, resizing the same
control and uniform 150% layout scale were checked. Banner captures have maximum
outer-edge alpha 0, 12,802 graded-alpha pixels and central alpha 255. Parent inspection
included the action-state gallery, Models/Download and cloud preview. Native recovery
window interaction and OS high contrast were not tested.

The standard product was launched once with an isolated synthetic Review profile
for a native Outlook test. Before dispatch, unexpected text appeared in the
destination during concurrent desktop use. The test stopped without pressing
the placement shortcut or performing native delivery. The isolated profile had
no delivery journal, and its client was then disarmed and stopped. No production
profile data was changed, and the potentially user-edited Outlook draft was left
untouched. This is not a passing native insertion test.

The previously blocked DeliveryProbe executable was not executed, renamed,
rebuilt elsewhere or loaded through another host. No security setting was changed.
Render checks use plain WPF Application resources without product startup,
microphone capture, native input or clipboard manipulation.

Native Outlook timing, real microphone pause/continue, banner dragging across
monitors/DPI and OS high-contrast acceptance remain unverified by this update.
Compilation, pure policy tests and rendered pixels do not establish those results.


## Deployment and runtime readback

The 17 installed payload files were backed up before replacement. All backup and
installed SHA256 hashes were verified; a later readback found zero mismatches.
The real settings file remains byte-identical to its pre-start snapshot.

    ProductVersion: 0.1.0-recovery.20261113.2+b494dca1845394af04d0d1e18eec06568ce1292a
    Backup: C:\AI\build\Local-Dictation-recovery-final\previous-app-before-recovery
    Manifest: C:\AI\build\Local-Dictation-recovery-final\deployment-manifest.json
    Startup record: C:\AI\build\Local-Dictation-recovery-final\startup.json
    Settings snapshot: C:\AI\build\Local-Dictation-recovery-final\settings-before-start.json

The installed client is running as PID 46672, started at 10:03:44 CET on 13 November 2026.
Its delivery-session.json independently identifies that PID and the exact build.
Candidate API PID 60388 reports ready=true and busy=false on 127.0.0.1:48125.
TypeWhisper was not running at the readback. The production 8123 API was not restarted.

No client error-log update occurred after this start. There are zero delivery
attempts for the current PID, so this does not establish successful native insertion.
Bounded metadata-only delivery diagnostics are enabled in this client process;
they exclude transcript/audio/clipboard contents and do not change saved settings.

The post-start desktop inspection ended on Escape. Continuation used only processes,
files, hashes and the API, following Peter's minimal-desktop-control preference.
No additional computer-control session was opened, and no native acceptance result
is inferred from process health or static rendering.


## Subsequent native acceptance check

The silent prepared-Review placement into Outlook passed through Ctrl+Shift,
with exact full-document/caret acknowledgement and automatic clipboard restoration.
Controlled speech pause and automatic Finish remain unconfirmed. See
[native acceptance](native-acceptance-20261113.md) for the exact scope and cleanup.
