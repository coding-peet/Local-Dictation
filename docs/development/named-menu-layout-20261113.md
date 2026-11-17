# Selected named-menu layout validation

## Selected design and implementation

Peter selected variant 2, Zachovane menu, from the History layout proposal.
MainWindowShell provides compact 64-DIP ordinary chrome, a fixed 160-DIP named
menu and collapsing empty alerts. Help moves to the header; native title-bar
Close replaces the duplicate footer action. Saving/connection failures and
clipboard recovery retain readable wrapping controls instead of being hidden.

History shows short date/duration rows with optional status and two preview
lines. The selected text occupies the remaining space; its Review/Copy/More
actions belong to that detail. Language/export/delete are available through
More. The unchanged player spans both columns at the bottom. Narrow content
stacks the list/detail and scrolls without hiding navigation names.

## Verification

- 443 Core tests passed.
- 78 established windowless WPF/controller checks passed after the final fix.
- Actual shared MD3 buttons/chrome and real History content were rendered in
  Light/Dark at 1040/640 widths. The full-history/long-text case gains more text
  height, retains playback and keeps normal header height at 64 DIP.
- Expanded error/recovery states remain visible at compact width.
- The selected-row high-contrast issue was reproduced and fixed with semantic
  SelectedInk styles for all row text. A detached-tree palette fixture checks
  the visible Needs review state without changing Windows settings.
- Focused source review found no remaining P1/P2 finding after remediation.
- Backend/model/transcription/audio-output behavior was not changed.

No desktop control, microphone/device enumeration, playback or inference was
used. These checks do not establish native insertion or actual device playback.

## Candidate and source state

Source: C:\Git\Local-Dictation\repo, feature/native-windows.
Base HEAD: 0c22c86d92deff3c63055593bc1cf48088698032.
Installed version: 0.1.0-layout.20261113.1.
Candidate: C:\Git\Local-Dictation\layout2-candidate-20261113.
Artifacts: C:\AI\build\Local-Dictation-layout2.

The installed package was built before the layout changes were committed.
Its Git suffix identifies the base HEAD, not the later layout commit. The
implementation and validation are recorded together in this branch's history.

## Deployment and startup

After Peter confirmed normal Exit, exactly 17 client files were backed up and
replaced. SHA-256 hashes of the installed payload and prior-file backup matched.
Settings remained byte-identical after startup. History, protected recordings
and model storage were outside the replacement scope.

- Verified at: 2026-11-13T19:19:02.709928+00:00.
- Client PID: 13816.
- Startup version: 0.1.0-layout.20261113.1+0c22c86d92deff3c63055593bc1cf48088698032.
- Session API PID: 34080; ready, unchanged.
- Both API listener states were unchanged; neither API was restarted.
- Backup: C:\AI\build\Local-Dictation-layout2\previous-app-20261113-201846.
- Evidence: C:\AI\build\Local-Dictation-layout2\runtime-verification.json
  and deployment-manifest.json.

Startup and deployment were verified through processes, hashes and logs.
No desktop control, recording, playback or real model operation was used.
Native application acceptance remains unverified by this update.
