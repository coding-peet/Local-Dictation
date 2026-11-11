# Compact Review controller probe

This diagnostic exercises the actual AppController completion and choice flow
with an in-memory ITextDelivery implementation. It never captures microphone
audio, connects to the API, restores another application's focus, or injects
keyboard input.

The helper initializes application resources without OnStartup, disables the
controller timers before yielding, and uses isolated temporary data folders.
Temporary Review text is synthetic and protected by the normal local store.
The controller may show its own non-activating banner while checking the flow.

## Run

Publish this project into a task-owned diagnostic directory under C:\Git.
Do not replace the real application executable with this helper.

~~~powershell
.\CompactFlowProbe.exe --report C:\AI\LocalDictation\diagnostics\compact-flow-probe.json
~~~

The optional report contains synthetic test results only. A failing case exits
with code 1; a fully passing run exits with code 0. Test-owned temporary data is
removed after validating that its absolute path remains under the temp folder.
Cleanup is bounded. After disposing every test controller and writing the report,
the fixture terminates its own test host explicitly; it does not run production
WPF/WinForms shutdown after deliberately skipping App.OnStartup.

These checks verify controller orchestration, cancellation gates, placement
receipts and compact Review lifecycle. Regression cases also cover stale History
snapshots, protected pending/history receipt reconciliation after a controller
restart, and shutdown preparation awaiting delayed synthetic recording cleanup.
The shutdown case checks persisted synthetic PCM; it does not close a real
application or capture a microphone.

They do not establish native cursor
compatibility, microphone latency, model quality or physical keyboard behavior.
