# Settings WPF regression probe

This diagnostic exercises the actual SettingsWindow event handlers and
AppController settings persistence using synthetic model metadata and isolated
LOCAL_DICTATION_HOME folders. It does not capture microphone audio, connect to
an API, inject keyboard input, or change the installed application's settings.
The controller's periodic timers are stopped before yielding to the dispatcher.
Microphone enumeration can occur while constructing the settings window; the
fixture then supplies synthetic device choices and never starts capture.

## Build and run

Use an installed .NET 10 Windows SDK. The commands below write only diagnostic
build output and its report. They do not deploy or launch the installed client.

~~~powershell
$env:DOTNET_CLI_HOME = 'C:\AI\build\Local-Dictation-settings\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

& 'C:\AI\tools\dotnet-10.0.403\dotnet.exe' publish `
    .\diagnostics\SettingsProbe\SettingsProbe.csproj `
    -c Release --no-self-contained `
    -o 'C:\Git\Local-Dictation\settings-probe' `
    --artifacts-path 'C:\AI\build\Local-Dictation-settings\dotnet'

& 'C:\Git\Local-Dictation\settings-probe\SettingsProbe.exe' `
    --report 'C:\Git\Local-Dictation\settings-probe\results.json'
~~~

The optional report contains synthetic assertions and their durations. A fully
passing run exits 0; a failed case exits 1. Test-owned temporary data is removed
only after validating its resolved path under the Windows temporary directory.
The fixture initializes application resources without production OnStartup,
disposes its controllers, and terminates its own test host explicitly.

## Coverage

- Installed Large-v3 autosaves immediately, selects a different eligible
  verifier, and survives an ordinary model-list refresh.
- Microphone, language, verifier, toggles, shortcut enablement, and theme persist.
- Verification controls collapse while off and return on enable with the eligible
  saved model intact. Opening/refreshing an off state does not expand controls or
  rewrite settings. Rejected enabling without another eligible model keeps its
  explanation visible outside the collapsed controls; recovery clears that warning.
- Valid text saves after a debounce or focus loss; closing flushes a valid edit.
  Invalid intermediate text is never persisted and cannot block another valid
  setting. Focus loss in one field preserves another field's debounce.
- A missing saved microphone retains its ID while unrelated settings save.
  Recording requires an available selected microphone.
  Selecting System default or an active device autosaves the recovered choice
  and clears its missing-device warning, preserving unrelated footer messages.
  Deterministic refresh cases cover warning recovery, a retained missing ID,
  fresh enumeration failure feedback, and preservation of unrelated messages.
- Failed writes remain visible and do not change the effective model. A later
  text save retries pending valid option changes instead of hiding the failure.
  Opening with an unavailable saved verifier shows that problem without saving
  or displaying an unpersisted fallback. An invalid
  model draft cannot block appearance or insertion settings or silently start
  with an earlier model.
- Active recordings retain their settings snapshot. Stop works despite invalid
  drafts and a dropped service connection.
- LiveSupported controls capability independently from LiveReady; models without
  a live-ready rating receive a latency hint. No available verifier produces coherent saved
  settings and explicit feedback.
- Passive offline refreshes preserve the saved file and choices without writes.

When --report is supplied, the probe also renders the WPF content to PNGs
beside the report for layout review: verification expanded/off, compact-width
Dictation, and a short preferences pane. These are
application-rendered content images; they do not show a native window frame,
physical keyboard operation, live microphone latency, or model quality.
