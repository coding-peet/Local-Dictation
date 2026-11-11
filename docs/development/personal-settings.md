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
The preview updates immediately. Select Save settings to keep the choice.
System follows Windows when the app starts; Windows high contrast takes priority.

The default shortcut is Ctrl+Shift. To change it, enter a modifier combination
and Space, a letter, digit, or F1 through F24, then Save settings.
For example: Ctrl+Alt+Space or Ctrl+Shift+D.

When TypeWhisper is running, the default Ctrl+Shift shortcut is disabled here
to avoid a collision. A custom shortcut can be used independently, but other
applications may also use the combination you choose. A --no-hotkey test launch
keeps all global shortcuts disabled until the app is relaunched normally.

## Recent transcripts and audio

Open History from the tray menu or the History tab.

- Play or stop the selected recording.
- Copy the current transcript or open it in Review.
- Save audio exports an ordinary WAV to your chosen location.
- Delete removes that history item and its audio.

The client keeps the latest 20 dictations containing received text and recorded
audio. After the 21st is saved, the oldest transcript and audio are removed.
Pending Review is stored separately and is not removed by this limit.

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
focus, or select Help (F1). The bundled Help.html opens in the Windows default
HTML viewer, normally a browser. Opening Help does not save settings.

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
