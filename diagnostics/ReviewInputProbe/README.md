# Review input regression probe

Exercises the real compact WPF Review panel, protected pending-Review storage,
and invalid reconnect handling. Data is synthetic and saved under a unique OS
temporary directory, which the fixture removes when it exits.

## Build and run

Run these commands from the native-windows checkout:

~~~powershell
& 'C:\AI\tools\dotnet-10.0.403\dotnet.exe' publish `
    '.\diagnostics\ReviewInputProbe\ReviewInputProbe.csproj' `
    -c Debug --no-self-contained `
    -o 'C:\Git\Local-Dictation\review-input-probe' `
    --artifacts-path 'C:\AI\build\Local-Dictation\review-input'
~~~

~~~powershell
& 'C:\AI\tools\dotnet-10.0.403\dotnet.exe' `
    'C:\Git\Local-Dictation\review-input-probe\ReviewInputProbe.dll' `
    --report 'C:\AI\build\Local-Dictation\review-input\results.json'
~~~

The first command builds a separate probe. It does not publish or launch the
installed client. The second opens short-lived fixture-owned WPF windows and
writes a JSON report. Exit code 0 means every case passed; 1 means a regression
or setup failure. No microphone, transcription API, model, external destination,
or existing client data is used. The invalid reconnect test rejects its URL
before any network request.

## Coverage and limits

- Enter down/repeats cannot choose unseen phrases. Release chooses once.
- Losing keyboard focus disarms an unfinished Enter gesture.
- Custom wording survives phrase changes and protected-store reload without
  accepting the phrase.
- An invalid reconnect preserves active and persisted settings.
- The production release gate stops at its bounded wait, permits released keys,
  and observes cancellation and lease changes.

Panel cases use actual WPF tunnel/bubble routes and ButtonBase class handlers;
they generate no native keyboard input. Release-gate cases supply deterministic
key-state and delay delegates. They verify the gate, while placement of that
gate before native focus restoration requires code review.

This probe does not prove physical held-key timing, external UI Automation
compatibility, or real-app insertion. Native delivery can be checked separately
with DeliveryProbe's owned synthetic fields. Codex-field acceptance remains
Peter's manual test.
