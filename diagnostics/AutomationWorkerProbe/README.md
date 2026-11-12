# Automation worker threading probe

This probe runs production AutomationWorker with synthetic delegates only.
It creates no windows and makes no UI Automation, clipboard, microphone, API,
or destination-field calls. The worker itself creates one background thread.

## Build and run

From the checkout root:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe publish `
    .\diagnostics\AutomationWorkerProbe\AutomationWorkerProbe.csproj `
    -c Release --no-self-contained `
    -o C:\Git\Local-Dictation\worker-threading-probe\candidate `
    --artifacts-path C:\AI\build\Local-Dictation-worker-threading\dotnet

& C:\Git\Local-Dictation\worker-threading-probe\candidate\AutomationWorkerProbe.exe `
    --report C:\AI\build\Local-Dictation-worker-threading\results.json
~~~

Publishing writes only the requested build/output directories. Running the
probe writes the requested JSON report. It does not launch Local Dictation,
start a native insertion test, or change settings or clipboard data.

## Coverage and limits

The probe checks a dedicated background MTA worker, serialized stable-thread
execution, unmodified provider exceptions, bounded noncooperative calls,
deadline cancellation, and skipping a cancelled queue entry before its
provider runs. Every blocked synthetic call is released by the fixture.

Microsoft's [UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)
requires a separate windowless MTA thread for a client that can interact with
its own UI. The clipboard owner uses a different STA thread with its own
message-only window and is outside this probe.

These checks establish worker mechanics. They do not prove native provider
compatibility, text insertion, or that the worker apartment caused the Outlook
failure. Native fixtures and actual Outlook acceptance remain separate.

## Controlled native banner access

For a separate, authorized native application acceptance test, the diagnostic
launcher can temporarily expose the compact banner in the taskbar/window
inventory:

~~~powershell
.\scripts\Start-DiagnosticClient.ps1 `
    -AppDirectory C:\Git\Local-Dictation\candidate-uia-mta-testable `
    -ExposeTestBanner
~~~

This command starts the specified client with normal hotkeys and the existing
process-only delivery diagnostics. Exit the previous client normally first,
after resolving any clipboard recovery. The launcher never terminates a client
or restores its clipboard.

ExposeTestBanner sets LOCAL_DICTATION_TEST_BANNER_WINDOW=1 only in the launched
child. A visible banner gets a temporary taskbar button and can appear in
native window inventory. It keeps the existing activation behavior:
ShowActivated=false and non-activating recording, with interactive Review
unchanged. Destination ownership, paste validation and clipboard protection
remain unchanged. The ordinary launcher sets the child flag to 0, so an old
shell environment cannot silently enable this mode.

The default application has no banner taskbar button. Delivery diagnostics
alone do not enable the test mode. Neither launch mode changes user/machine
environment variables, security settings, the API or saved application settings.
This optional UI exposure is outside the pure worker probe and must be verified
in an actual controlled desktop test.
