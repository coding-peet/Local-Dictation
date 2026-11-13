# Personal settings and history

## Models are missing

The native client uses the session API on http://127.0.0.1:48125.
The TypeWhisper batch server on port 8123 is a different service.

1. Open Local Dictation.
2. If the service is offline, select Start local API.
3. Wait for API connected. Initial preparation may take a minute.
4. The model selectors then show the existing installed model registry.

The app retries while disconnected. Starting the local API uses the existing
task-local launcher; it does not install a Windows service or change startup.

## Appearance and keyboard shortcut

In Settings, choose System, Light, or Dark under Appearance.
The preview updates immediately and valid choices save automatically. Check the
saving status in the main window; there is no separate Save settings step.
System follows Windows when the app starts; Windows high contrast takes priority.

The default shortcut is Ctrl+Shift. To change it, enter a modifier combination
and Space, a letter, digit, or F1 through F24. Text changes save after a short
pause or focus change; invalid text leaves the previous shortcut active.
For example: Ctrl+Alt+Space or Ctrl+Shift+D.

When TypeWhisper is running, the default Ctrl+Shift shortcut is disabled here
to avoid a collision. A custom shortcut can be used independently, but other
applications may also use the combination you choose. A --no-hotkey test launch
keeps all global shortcuts disabled until the app is relaunched normally.

## Recent transcripts and audio

Select History in the main window. The tray History action opens the same
section, with the full list, selected transcript and player. No separate
History window is opened.

- Use the Play, Pause and Stop icons. Pause keeps the current position; Stop
  returns to the beginning.
- Drag the round timeline thumb to seek. Elapsed/total time and the amplitude
  faint waveform under the slider show the position. The waveform uses a fixed
  RMS energy scale, so a loud burst does not shrink the rest of the recording. Left/Right seek one second; Page Up/Page Down
  seek five seconds while the slider is focused.
- Copy the current transcript or open it in Review. For a pending result,
  resolve/edit the text, click a destination cursor, then click Insert.
- In Review, Second transcript compares the retained audio with an eligible
  different model. History More actions -> Change language reprocesses that
  same audio in another supported language. Both preserve the source and create a new Review.
- More actions (...) -> Save audio exports an ordinary WAV to your chosen
  location.
- More actions (...) -> Delete removes that dictation, its audio and saved
  review choices after confirmation. Its open Review editors close first.

The client keeps the latest 20 retained recordings, including audio with no
recognized words. After the 21st is saved, the oldest transcript and audio are removed.
Unfinished text is stored independently and remains in History with Needs review
or Not saved status, even after its audio expires. These entries show Text only.

On a narrow window, the list moves above the transcript; scroll the page to
reach the detail actions and player. Review/Copy/More stay with the selected
text; playback spans both columns. The named main menu remains visible.
Leaving History stops playback and clears its loaded audio. Returning keeps selection without automatically playing.

History is under C:\AI\LocalDictation\data\history, or the corresponding
LOCAL_DICTATION_HOME data directory. Text metadata and WAV audio are protected
with Windows DPAPI for the current user. Playback decrypts audio in memory.
An explicit WAV export is not encrypted by this application.

Earlier recordings cannot be recovered retroactively: previous versions did
not retain completed transcripts or audio. History starts with new dictations.

## Icon choices

Assets/icon-options.png in the app source shows four vector concepts.
Peter approved the pink waveform icon on 2026-11-11. Assets/waveform.ico is
the application, window, taskbar, and tray icon. It includes sizes from 16 to
256 pixels. The source is Assets/waveform.svg, generated together with the
ICO and PNG previews by scripts/Create-WaveformIcon.ps1.

The earlier four options remain as design references. Their generation script
is scripts/Create-IconOptions.ps1.

## Offline Help

Press F1 anywhere in the Settings window, including while a text field has
focus, or select Help in the compact main header. The bundled Help.html
opens in the Windows default HTML viewer, normally a browser. Opening Help does not save settings.

The guide is self-contained and requires no network connection. Use Ctrl+F
in the browser to find a topic. Source: src/LocalDictation.App/Help/index.html.
Normal builds and publish output place it beside LocalDictation.exe, and the
flat MSI payload list includes it. Building the client does not create an MSI.

## Verification on 2026-11-11

Core tests cover settings, shortcut repeats, protected history roundtrip,
20-item eviction, WAV contents, and an incomplete history entry.
The actual Windows app shows installed models and was visually checked in dark
and light themes. Live microphone capture, real shortcut collisions and guarded
insertion still need manual application acceptance.
