# Material dialog and workspace refinement delivery

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

Date: 2026-11-14

## Delivered behavior

- Keep the existing Local Dictation waveform icon.
- Replace the native History deletion question box with a theme-aware Material
  dialog. Cancel is the initial/default action. Delete is explicit. The prompt
  identifies the timestamp and permanent deletion scope without showing text.
  Escape, Alt+F4 and closing cancel; an intentionally focused Delete remains
  keyboard accessible. Global shortcut capture is restored in finally.
- Use 10-DIP History scrollbars and a keyboard-accessible draggable divider.
  Wide panes have 160/240-DIP minimums and clamp to available width. Preferred
  width survives narrow/wide transitions within the window lifetime. Compact
  panes stack and hide the divider. Selection, text and audio lifetime remain.
- Place Record above Search in the named navigation column and Help at its
  bottom, also reachable through F1. Record becomes Stop during recording.
  Its tooltip distinguishes app-window Review recording from shortcut recording
  at an external cursor.
- Keep identity and connection in the compact header. Routine saving feedback
  appears in a footer on Dictation and Settings, aligned vertically with Help.
  Saving errors remain visible on every page and search.
- Put Auto preference and live-preview delay explanations in local information
  help. Render the model-support qualifier smaller and muted; preserve warnings.
- Update offline Help for the actual controls and behavior.

## Validation

- 531 Core tests passed, zero failed or skipped.
- 170 integrated windowless WPF/controller checks passed. The build reported
  zero warnings and errors.
- Independent read-only review found no remaining concrete issues in modal
  safety, shortcut restoration, shell/error visibility or pane layout guards.
- Inspected Light/Dark dialog, shell, History resize and Dictation render artifacts.
- Help parsed; 20 unique section IDs and 12 local links verified. Required new
  copy was checked and published Help is byte-identical to its source.
- Existing icon has no Git diff. Source whitespace check passed.

These checks did not open a native product window, inject input, record a
microphone, play audio, or delete real data. Native modal focus, drag behavior,
Windows high contrast and actual cursor insertion remain manual acceptance.
Offline HTML was parsed/content-checked; no browser visual check was performed.

## Installed package

- Checkout: C:\Git\Local-Dictation\repo
- Branch: feature/native-windows
- Base commit: 07a9fe6f843db9b2bf2d62ef409cec01bd113528
- Product version: 0.1.0-layout.20261114.1+07a9fe6f843db9b2bf2d62ef409cec01bd113528
- Candidate: C:\Git\Local-Dictation\refinement-candidate-20261114
- Installed app: C:\Git\Local-Dictation\app\LocalDictation.exe
- Client PID at deployment: 43820
- Verified previous-app backup: C:\AI\build\Local-Dictation-refinement\previous-app-20261114-201304Z
- Candidate, backup and installed package: 17 matching files each.
- Protected data files: 115; all hashes unchanged across deployment.
- Session API: port 48125 / PID 4364, ready=true and busy=false.
- Existing batch API: port 8123 / PID 22352; listener preserved, no restart.

The client was already stopped when deployment began. The guarded deployment
script rechecked absence before copying, verified the complete backup, compared
installed/candidate hashes, started the client normally and verified preservation.
The APIs were not restarted. API readiness is separate from native transcription
acceptance; batch health was not probed.

The informational suffix identifies the committed base. This prerelease was
built from the reviewed working changes. No new commit/push was requested for
this follow-up; existing dirty/untracked source was retained.

## Evidence and reproduction

Artifacts and deployment manifest:

    C:\AI\build\Local-Dictation-refinement

Core:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe test `
  tests\LocalDictation.Core.Tests\LocalDictation.Core.Tests.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-refinement\core
~~~

Windowless checks (use a new output directory for every independent run):

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe build `
  diagnostics\VerificationBannerRender\VerificationBannerRender.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-refinement\final-ui

& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
  C:\AI\build\Local-Dictation-refinement\final-ui\bin\VerificationBannerRender\release\VerificationBannerRender.dll `
  --output C:\AI\build\Local-Dictation-refinement\fresh-review-output
~~~

Optional diagnostic isolation accepts `--only CHECK_CLASS` after the output
argument. It does not replace the integrated suite. These commands build/test
synthetic fixtures; they do not deploy, restart APIs or use physical input/audio.
