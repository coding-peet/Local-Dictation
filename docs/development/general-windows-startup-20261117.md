# General and Windows startup verification

Date: 2026-11-17
Baseline: 586ed67d4e0e1947c70c01d55c708b456e42c098, feature/native-windows.
Status: implemented, independently reviewed, verified and deployed.

## Delivered behavior

Navigation is General, Models, Dictation, History. Manual launch starts on
Dictation. General replaces Settings and groups app-wide choices, including
Start with Windows. Service controls are in General / Advanced; dictionary and
compatibility are in Dictation / Advanced. Search expands the required area.
Draft/error summaries remain discoverable when Advanced is collapsed.

Retention remains in General per the explicit approval text. The agent's
intervening History-options suggestion was not treated as approved scope.
Existing import-error/private-selector work is preserved in the same checkout.

Startup reads actual registration; only an explicit switch action changes
HKCU Run's LocalDictation value. The registry adapter independently rechecks
ownership before write/delete. Unknown/conflicting entries are preserved.
The switch does not edit StartupApproved or bypass Windows restrictions.
Automatic launch uses --startup, skips the main window and duplicate-instance
notice, and retains the normal controller/API initialization without recording.

## Automated verification

- Core: 640/640 passed; 39 focused startup cases passed with fake stores.
- Detached WPF: 209/209 passed, no native HWND/input/audio. Includes startup
  toggles and failures, unchanged settings, navigation/search, Advanced summaries,
  existing import/private flows and expanded compact/wide Light/Dark layouts.
- Backend: 185/185 synthetic unit tests passed using the existing test runtime;
  no service processes or real model weights were started or loaded.
- Release build and final publish: zero warnings/errors. All 17 installed payload hashes matched the final candidate.

Startup RED evidence includes 15 expected initial failures and one regression
for an initially unknown registry state. Navigation RED verified the missing
General order before implementation. A stronger expanded-layout assertion
caught in-progress Material Expander animation in detached rendering. Its own
250 ms transitions do not consume TransitionAssist.DisableTransitions; the
render helper now requests an opposite state then the actual state without
transitions. Real product templates/animations are retained.

A full-run failure identified an old History fixture array position after
navigation was reordered. Its page was moved to the proper History index;
the original transcript-space assertions remain and the full suite passed.

Evidence:

    C:\AI\build\Local-Dictation-personal-tools\general-startup-core\results
    C:\AI\build\Local-Dictation-personal-tools\general-navigation-red-20261117
    C:\AI\build\Local-Dictation-personal-tools\general-expanded-red-20261117
    C:\AI\build\Local-Dictation-personal-tools\general-expanded-green2-20261117
    C:\AI\build\Local-Dictation-personal-tools\general-final-20261117
    C:\AI\build\Local-Dictation-personal-tools\general-backend-tests-20261117.log

## Independent review

Read-only review found no critical/important remaining product defects. Two
minor findings were resolved before rollout: conflicting older navigation labels
in requirements and missing validation of Windows Run's 260-character command
limit. The latter was treated as correctness because registry readback alone
can falsely imply a usable long-path registration. New boundary tests failed
first, then passed: a 248-character EXE path produces an allowed 260-character
command; 249 produces 261 and is refused with shorter-path guidance before
store access. Full Core passed 640/640 after the fix.

Detached navigation initially retained a previous selection animation, making
two pills appear selected. The render helper now settles each ListBoxItem via
opposite then actual state, like Expander. The final 209-check suite passed;
General screenshots now show only the actual selected section. These changes
are confined to diagnostics; normal product animations/templates remain intact.

No minor finding is deferred. Actual Windows logon, OS Startup apps overrides,
native keyboard/focus during animated search reveal, audio and inference remain
outside this verification. No sign-out/reboot or real startup-value mutation was
performed for testing.

## Client candidate

    C:\Git\Local-Dictation\general-candidate-20261117

Version:

    0.1.0-general.20261117.1+working.586ed67.586ed67d4e0e1947c70c01d55c708b456e42c098

The candidate had 17 files and was removed after verified installation to avoid
keeping a redundant published copy. Backup, manifests and controlled deployment script:

    C:\AI\build\Local-Dictation-personal-tools\general-deployment-20261117

## Runtime boundaries

Passive baseline: client PID 44376, session API port 48125 / PID 54728,
batch API port 8123 / PID 51740. Own Windows startup entry was absent before
verification. No test changes the real startup registration. No voice recording,
playback, native insertion, real model inference or Windows sign-out/logon test
was performed. Keep both APIs and all user data/model weights intact.


## Deployment outcome

Peter confirmed normal tray Exit; process absence was verified before copy.
The previous 17-file client was backed up and hash-verified. All 17 new installed
files matched the candidate. All 107 protected data files matched their
pre-deployment hashes both before and after normal launch.

Installed client PID 34312 responds from C:\Git\Local-Dictation\app.
The client error log grew by zero bytes. Session API 48125 / PID 54728 and
batch API 8123 / PID 51740 remained unchanged; no API was stopped/restarted or
reconfigured. Normal startup connection checks retained their usual behavior.

The real startup value remained absent through automated tests, packaging and
copy. After launch, the new valid Run value was observed and Peter confirmed
he had enabled the switch himself. Its command matches the installed EXE with
--startup and its kind is String. It was preserved without agent registry
mutation. This verifies the user action and registration/readback, not a real
Windows sign-out/logon cycle. The enabled choice remains in effect.

No new recordings, playback, native input/insertion or model inference were
used. Previous import/private-selector features and protected files are kept.
At this deployment checkpoint, changes were uncommitted. Peter subsequently
authorized commit and push of the complete change set on feature/native-windows.


## Git publication authorization

On 2026-11-17, Peter requested commit and push of all reviewed changes to
feature/native-windows and asked to include current Help. This report belongs
to that publication change set. The Help troubleshooting paths and tray-menu
names were checked against the current General/Models/Dictation/History UI.
No runtime data, recordings, models, logs or generated binaries belong in Git.
