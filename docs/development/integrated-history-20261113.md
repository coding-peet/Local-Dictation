# Integrated History validation and deployment

## Implemented source

Peter clarified that the whole Local Dictation History workspace belongs in the
main window. HistoryPanel now embeds the list, transcript/model/language detail,
player and existing actions in SettingsWindow's History section. The tray History
action opens and selects that same section. HistoryWindow and its launcher are
removed. Review and the second-model picker are not relocated.

Wide layouts keep a finite list/detail viewport with internal scrolling, so a
full 20-item history and long transcript do not push playback/actions away.
Compact layouts stack the list above the transcript and scroll the whole page
when its comfortable minimum exceeds available height.

History decrypts the selected recording only when its section becomes active.
Leaving clears/stops playback; returning retains selection without auto-playing.
Metadata refresh for the same selected item does not reload its audio. Closing
the main window unsubscribes and disposes the panel/player.

## Verification

- 421 Core tests passed.
- 58 established windowless WPF/controller checks passed. Embedded-content checks
  failed against the old launcher before implementation. The full-history and
  long-transcript case reproduced the unbounded-scroll regression before its fix.
- Actual History content was rendered in Light/Dark at wide and compact sizes.
  The final wide render shows internal list/text scrolling and visible playback.
- Release build/publish succeeded without warnings/errors.
- Focused review confirmed the viewport fix; no remaining actionable finding.
- The backend is unchanged here. Its preceding publication passed 149 tests.

Before deployment, no desktop control, microphone capture, audio playback, real
inference, app restart or API restart was used. These checks do not establish native insertion
into OpenAI/Outlook or actual audio-device behavior.

## Deployment and source state

Peter requested a pause within five minutes, then resumed with "go" on
13 November 2026. The source and prepared candidate were rechecked after resume.
The client and both API listeners were stopped, so no running process needed
termination. The deployment replaced the exact 17-file client payload after
verified backup. Current settings remained byte-identical through startup.

Installed version: 0.1.0-history.20261113.1+d6e159c3aaa7a0377720b8753795a155314f5b90.
Client PID at verification: 43708.
Verified session API PID: 26540, loopback port 48125.
Its health was ready and idle before launch. OPTIONS confirmed the new
POST /v1/audio/verifications route without sending audio or running inference.
The model catalog contained 4 entries.

The TypeWhisper API on 8123 was already stopped; it was not started or modified.
No desktop control, recording or playback was used during deployment/startup.
Native OpenAI/Outlook and actual audio-device acceptance remain unverified.

Installed client: C:\Git\Local-Dictation\app.
Candidate: C:\Git\Local-Dictation\history-candidate-20261113.
Artifacts: C:\AI\build\Local-Dictation-integrated-history.
The external deployment-manifest.json records backup paths/hashes;
runtime-verification.json records startup PID/version/settings/service checks.

Canonical source: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows.
Source base before this publication: d6e159c3aaa7a0377720b8753795a155314f5b90.
Peter requested a manual update, comment, commit and push for the completed
History integration. This publication includes the implementation, its focused
checks, current product requirements, updated offline Help and settings guide.
The installed executable remains the verified build identified above.

## Manual update after deployment

The guide now explains integrated History navigation, Play/Pause/Stop, seeking,
saved-result insertion, second-model comparison, language correction and audio
lifetime. It reflects automatic settings saving and retention of captured audio
even when recognition returns no words. The installed Help.html is updated as
a separate documentation-only copy, without replacing DLLs or restarting the
client. Backup/hash details are in the external manual-update-manifest.json.
