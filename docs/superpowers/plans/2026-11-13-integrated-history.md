# Integrated History implementation plan

Goal: Show the entire Recent dictations workspace in the main History section.

User clarification: The whole Local Dictation History window, not the second
model picker or Review, should be integrated. Reuse existing Material 3 controls
and playback. Keep source/runtime data separation and do not operate the desktop.

Approach: Extract the existing History content into a disposable HistoryPanel.
Embed one instance in SettingsWindow, select it from the tray History action,
and remove HistoryWindow. Keep Review and native export/confirmation dialogs.
Use a side-by-side list/detail layout with a stacked compact layout and vertical
page scrolling. Decrypt/load audio only on navigation to History, stop/clear it
when leaving, and dispose subscriptions/player when the main window closes.

- [x] Add a failing windowless check against the real History page factory.
- [x] Extract/host the panel and route main/tray navigation to its section.
- [x] Verify selection retention, hidden-page audio lifetime and compact layout.
- [x] Update requirements/Help, build a new separate candidate, preserve runtime.

Working folder: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows. Base HEAD: d6e159c3aaa7a0377720b8753795a155314f5b90.
