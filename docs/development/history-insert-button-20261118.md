# Visible History Insert button

The user requested Insert at cursor as a button rather than a More-menu item.
It is now the first primary action below the transcript, followed by Open Review,
Copy text and More. The supporting Review/Copy buttons use the existing outlined
style. More retains occasional language, export and delete actions.

The same guarded direct-insertion handler is reused. Highlighted text wins;
no highlight uses the whole selected transcript. No intermediate window or
change to destination capture, source protection or clipboard handling is added.
Selection changes refresh button availability.

## Verification

- 244 detached WPF state/layout checks passed.
- 8 focused History layout checks passed, including reachable Insert in stacked
  narrow layout. Synthetic default and scrolled narrow renders were inspected.
- 185 backend unit tests passed, without model inference or API restart.
- Release publish succeeded: 17 files, Help identical to source.

Private candidate manifest and renders remain outside Git under:
C:\AI\build\Local-Dictation-history-button-20261118

No microphone capture, playback or native paste was used. Native destination
acceptance remains separate. Commit and push were subsequently requested on
2026-11-18; merge remains outside scope.


## Client deployment

No client instance remained before file exchange. Verified backups of the prior
17 client files and all 142 data files were created before deployment.

Installed version: 0.1.0-historybutton.20261118.1+working.974bed6.
Client PID 30216 runs from C:\Git\Local-Dictation\app, is responsive, and has no
new client errors. All 17 installed files match the reviewed payload. All 142
data files remain hash-identical and Start with Windows is unchanged.

Session API 48125 (PID 19616) and batch API 8123 (PID 49672) retain their original
start-time ticks. No API restart was performed. Private hashes, before/after
state and rollback backups remain under the build root's deployment-1 directory.
The temporary candidate was removed after installed hashes were verified.
