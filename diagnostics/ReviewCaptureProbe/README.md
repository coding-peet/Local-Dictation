# Review selected-caret capture probe

This is an opt-in manual diagnostic with two test-owned windows in separate
processes. It uses the
real compact Review and controller with synthetic pending text and a delivery
double that always refuses native paste. Its report contains focus process/control-type metadata and native handles,
not field names, values, selected text or external documents. After verifying
the exact test-owned Edit field, it also runs production Capture against that
synthetic field. It never calls Apply on the captured production delivery.
It never accesses the clipboard, microphone, ASR APIs or existing client data.

## Build

Run from the canonical repository using the existing .NET 10 SDK:

~~~powershell
& 'C:\AI\tools\dotnet-10.0.403\dotnet.exe' build `
    diagnostics/ReviewCaptureProbe/ReviewCaptureProbe.csproj `
    --artifacts-path C:\AI\build\Local-Dictation-review-insert-20261117\dotnet
~~~

## Run and interact manually

Use a fresh output directory; the probe refuses an existing one.

~~~powershell
& 'C:\AI\tools\dotnet-10.0.403\dotnet.exe' `
    C:\AI\build\Local-Dictation-review-insert-20261117\dotnet\bin\ReviewCaptureProbe\debug\ReviewCaptureProbe.dll `
    --output C:\AI\build\Local-Dictation-review-insert-20261117\manual-trial-new
~~~

1. Click inside the prepared field in Review capture test - target.
2. Click Insert in Review capture test - Review. Do not activate Review first.
3. Wait one second. No text is pasted; refusal is intentional.
4. The windows close after capture. Close capture test also ends both windows.

The windows also close automatically after ten minutes without a capture. The report records focus
at the gesture and immediately/200 ms into capture. A zero-capture report is not
a successful test. Compare focusedPid with ExpectedProcess and foreground with
ExpectedWindow. A Windows automation tool that activates Review before clicking
changes the conditions and cannot substitute for the inactive-window gesture.

## Activation comparison

Use --noactivate instead of --output to set WS_EX_NOACTIVATE on the diagnostic
Review window before it is shown. This is a diagnostic comparison, not an
application behavior change. The normal --output mode preserves the current
production activation behavior. Each run needs its own fresh output directory.

## Hidden-window regression check

Use --style-check with a fresh output directory. It sends native hit-test
messages only to its own invisible window, with no input injection or foreground
change. The eight checks cover early no-activation, ordinary controls, disabled,
hidden and non-hit-testable Insert, existing styles and absence of click dispatch.

## Limits

This does not validate actual insertion, transcription or native clipboard
restoration. It does not explain a historical recording regression on its own.
Do not weaken privilege checks or reinstall the rejected focus interception just
because a narrower fixture passes. Synthetic test artifacts remain outside Git.
