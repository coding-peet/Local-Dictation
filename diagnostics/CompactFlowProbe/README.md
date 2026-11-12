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
receipts and compact Review lifecycle. Completion regressions send actual
`transcript.final`, verification and `session.completed` events through the
controller before cleanup. A change observer rejects even a transient interactive
Review during ordinary successful completion or verification agreement. Other
cases check that Finish retains the captured destination, a missing destination
requires an explicit decision, and an older Review cannot change the UI during
a new destination check.

Outlook cases configure the captured in-memory destination with exact synthetic
process names (`olk` and `OUTLOOK`); they do not enumerate or inspect native
applications. Actual partial/final/completion events verify that live recognition
continues in the banner, partial and resume delivery perform no writes, and
successful completion inserts the whole text once at the original destination.
The persisted insertion setting stays enabled. Verification differences wait for
the final wording choice before the one insertion. A later non-Outlook session
retains ordinary live delivery. Failed and cancelled verification must expose
their blocking reason while the run is still active, rather than an automatic
insertion hint. These policy checks do not establish native Outlook insertion
compatibility.

The clipboard-recovery cases hold empty, unchanged native transactions. They
check that closing the idle banner hides it without releasing the backup or
losing protected Review. A queued older recovery notification must not undo
that later dismissal. Deliberate Exit remains blocked and resurfaces recovery;
a genuinely new recovery event can also resurface it.

The primary-action case checks Restore clipboard… and invokes the real banner
handler with a private confirmation callback replaced by a synthetic Cancel.
It verifies that confirmation is required and cancellation retains the lease.
The production callback always uses the inspection warning dialog. The fixture
does not automate that real dialog or bypass it through application settings.
The action stays disabled while recovery is running. A long compact Review
keeps its existing panel without duplicating the transcript in the ordinary
preview; recovery remains available as the primary action.

These transactions have no clipboard formats or payload; the cases do not
read, replace or restore the user's clipboard content. Native queued-paste and
clipboard-format recovery belong to DeliveryProbe. Run this helper only when
the PC has been released for controlled checks: its own non-activating banner
can appear, even though it never supplies native keyboard input.

A separate empty reservation checks the period before a native worker settles:
Exit stays blocked even though explicit recovery is not yet available. The
controller keeps the protected Review and leaves Restore previous clipboard
disabled while that operation remains active.

Regression cases also cover stale History
snapshots, protected pending/history receipt reconciliation after a controller
restart, and shutdown preparation awaiting delayed synthetic recording cleanup.
The shutdown case checks persisted synthetic PCM; it does not close a real
application or capture a microphone.

The destination is an in-memory implementation; startup microphone capture and
the real UIA destination check are not executed. These checks do not establish
native cursor compatibility, microphone latency, model quality or physical
keyboard behavior. Use the separate native DeliveryProbe and application
acceptance tests for those boundaries.
