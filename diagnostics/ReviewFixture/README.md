# Synthetic Review storage fixture

This creates an isolated Local Dictation profile with two protected synthetic
Pending Review documents. It provides reproducible input for checking the real
product UI without microphone capture or modifying personal recordings.

The program references Core only. It does not load the app, drive windows,
insert text, access the clipboard, record audio or call a network service.
Use the normal product UI for any subsequent insertion check.

## Create a fixture

Run from the canonical repository:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe build `
  diagnostics\ReviewFixture\ReviewFixture.csproj `
  --artifacts-path C:\AI\build\Local-Dictation-review-fixture\dotnet

& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
  C:\AI\build\Local-Dictation-review-fixture\dotnet\bin\ReviewFixture\debug\ReviewFixture.dll `
  --home C:\AI\build\Local-Dictation-review-fixture\home
~~~

The explicit home must be new/empty and under C:\AI\build. The program refuses
to replace existing profiles or use the production data directory. Protected
Review files are bound to the current Windows account through DPAPI.

When launching the client for this check, set LOCAL_DICTATION_HOME for that
child process only. Do not change the user's persistent environment or profile.
This storage fixture is separate from native DeliveryProbe; it contains no
delivery or automation code and does not execute a blocked diagnostic payload.
