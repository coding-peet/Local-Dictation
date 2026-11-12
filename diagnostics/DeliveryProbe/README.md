# Native delivery probe

This interactive fixture exercises the actual WindowsTextDelivery,
AutomationWorker, GlobalInput and AppController code against synthetic WPF
text fields. It does not record audio, load models, contact an ASR server or
insert into an external application.

## Build and run

Build into a separate task output directory. This command changes only build
output and restores required .NET packages when needed:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe publish `
    diagnostics\DeliveryProbe\DeliveryProbe.csproj `
    -c Release --no-self-contained `
    -o C:\Git\Local-Dictation\delivery-fix-probe\candidate `
    --artifacts-path C:\AI\build\Local-Dictation-delivery\candidate
~~~

Start the built fixture, then click Run checks:

~~~powershell
C:\Git\Local-Dictation\delivery-fix-probe\candidate\DeliveryProbe.exe `
    --report C:\Git\Local-Dictation\delivery-fix-probe\candidate.json
~~~

The fixture manages its own fields and temporary Review window. Actual product
code performs bounded native input. Do not interact with other applications
until the PASS or FAIL result appears. Every nonempty live or final update uses one ordinary paste
command; the fixture and product never synthesize Enter or a send shortcut.

## What is checked

The 18 checks cover:

- Native clipboard memory, bitmap, palette and enhanced-metafile cloning,
  including refusing an unsupported owner-managed format without changing it.
- Inserting between preserved prefix and suffix text.
- Pausing at another field, and resuming inside the original unchanged draft.
- Withdrawing only the owned draft, then restoring accepted wording.
- Rejecting a manual document edit and a closed destination.
- The real controller transcript.final, session.completed and completion chain,
  with verification disabled and with completed verification agreement.
- Long Unicode live and final transcripts, delivered by one paste burst, with
  original clipboard formats verified before and after the operation. Live
  delivery retains its original two-second worker deadline.
- A native RichTextBox with surrounding text and existing formatting.
- A real post-paste noncollapsed selection that settles after350ms; read-only
  retries must confirm the same full text/caret without another paste.
- A permanently noncollapsed selection and focus loss during unstable readback;
  both must remain unconfirmed with protected clipboard recovery.
- A forced native partial-restoration failure, retaining every original saved
  format handle until a successful retry.
- A real native paste deferred for 1.6 seconds. Unconfirmed delivery keeps its
  own payload available and retains the previous clipboard for deliberate
  recovery; the late paste never consumes an automatically restored old value.

Controller cases use isolated temporary protected storage. They disable the
controller's timers and shortcuts; they do not start a microphone or connection.
The JSON report includes only synthetic fixture text and operational metadata.
Original clipboard values and their in-memory comparison hashes are not logged.

The long-input case waits for its operation to finish after cancellation before
moving on to another controlled field. It uses a fresh worker so an unavailable
provider does not prevent the separate rich-text case from running.

## Native clipboard checks without a UI

This mode creates and clones synthetic native memory and GDI resources, then
checks that the original clipboard sequence did not change. It publishes no
clipboard data and generates no input:

~~~powershell
C:\Git\Local-Dictation\delivery-fix-probe\candidate\DeliveryProbe.exe `
    --clipboard-checks `
    --report C:\Git\Local-Dictation\delivery-fix-probe\clipboard-native.json
~~~

A pass proves resource cloning, not application-level paste acceptance.

## Diagnostic journal checks without a UI

This separate mode performs only isolated temporary-file operations. It returns
before creating a WPF Application, AppController or GlobalInput. It does not
access the clipboard, query accessibility providers, show windows, send input,
record audio or contact a server.

Build into its own candidate folder:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe publish `
    diagnostics\DeliveryProbe\DeliveryProbe.csproj `
    -c Release --no-self-contained `
    -o C:\Git\Local-Dictation\delivery-capture-probe\candidate `
    --artifacts-path C:\AI\build\Local-Dictation-capture-probe
~~~

Run only the diagnostic journal checks:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
    C:\Git\Local-Dictation\delivery-capture-probe\candidate\DeliveryProbe.dll `
    --diagnostic-checks `
    --report C:\AI\build\Local-Dictation-capture-probe\diagnostic-checks.json
~~~

The seven checks cover opt-out without writes, enabled startup identification,
delivery-event PID, fixed interruption-stage serialization, capture serialization without exception messages, bounded
journal rotation, and nonfatal IO failure. Each run uses a unique temporary
root, restores the original process environment, and verifies the resolved
temporary path before recursive cleanup. The report contains check names and
results only. This does not exercise actual destination capture or insertion.

## Clipboard and confirmation boundaries

Each nonempty live or final update snapshots every supported original clipboard format in memory
before changing the clipboard. Memory-backed formats and standard bitmap,
palette and metafile formats are cloned. Owner-managed/private GDI formats,
unreadable delayed formats or memory-backed backups over 64 MiB stop delivery
before the clipboard is replaced. Unsupported data is never silently dropped.

The temporary paste excludes Windows clipboard history and cloud synchronization
using the documented Windows exclusion formats. Its dedicated hidden native
owner survives disposal of the UI Automation worker. A unique per-lease marker
and exact Unicode payload establish ownership; a sequence change alone does not
prove that the user copied something else.

Restoration is automatic only before any input was queued or after exact text
and caret acknowledgement. Each restoration attempt clones the complete saved
original set before clearing the clipboard. Partial failure retains every saved
original handle for another attempt; already-transferred attempts are not the
only backup.

Unconfirmed queued delivery keeps the dictation payload and the complete old
clipboard in memory. New paste operations are blocked until recovery resolves.
After inspecting the destination and stopping a deferred paste, the user may
choose Restore previous clipboard. Normal Exit is blocked while recovery is
pending. A newer clipboard owner takes precedence and is never overwritten.

Native Windows input and the destination are separate processes. There is no
generic native barrier proving that a deferred paste has consumed its payload.
An unconfirmed partial insertion stays in Review and must be inspected before
another explicit destination decision. Do not force-terminate the client while
it retains a clipboard backup: a process crash or forced termination cannot
preserve this in-memory recovery state.
Do not claim arbitrary-application compatibility from a passing WPF fixture.
Outlook/WebView, Codex, Notepad and other providers need their own acceptance.

## Optional text-free diagnostics

Start the candidate client with LOCAL_DICTATION_DELIVERY_DIAGNOSTICS=1 in its
process environment to enable a bounded delivery-metadata.jsonl under the local
client logs directory. Leave it unset for ordinary operation.

The same opt-in writes delivery-session.json once during accepted application
startup, containing UTC time, ProcessId and BuildVersion. Confirm that its PID
matches the client used for the physical test: a manual restart does not inherit
a flag set only for the earlier process. An older marker alone does not prove
that diagnostics are enabled in the current client.

Every delivery event includes ProcessId alongside its existing Delivery object.
LastInterruptionStage identifies the most recent interrupted ownership check:
invalidated, input-generation, foreground-window, focused-field or
delivery-keys-down. These fixed codes distinguish the existing short-circuit
guards without extra native calls. The top-level interruption catch also records
its technical exception type and HRESULT. No destination values are recorded.
Capture also writes capture-metadata.jsonl with fixed stage codes, stage-entry
Success=null, stage-completion success, elapsed milliseconds, UTC time and PID.
Failures include only exception type and HRESULT. It never logs exception
messages, field text, UI names, clipboard contents, audio or document paths.

Capture stages bracket the existing foreground/focus, own-process, integrity,
inspection callbacks, cancellation, postcheck window/identity and construction/
provider-metadata operations. Logging makes no extra accessibility calls, even
after failure. A stage entry without completion can identify a still-blocked
native call when the controller deadline expires; cancellation is observed only
at the existing cancellation check. A completed capture does not prove insertion.

New journal writes rotate before the active file would exceed 256 KiB, retaining
one .previous file. Pre-existing logs can retain their earlier size. Writes
are best effort and do not change destination guards, operation ordering, retry
policy, cancellation, ownership or insertion behavior. Diagnostic IO failure can
leave evidence incomplete. Provider metadata retains its existing best-effort
behavior; its stage completing does not prove that every provider attribute was
available. This instrumentation is not an Outlook insertion fix.

The log records phases, input counts, elapsed time, document lengths, caret
positions, exact-match and normalization-equivalence booleans, technical
provider metadata, and clipboard restoration status. ReadbackRetries and LastReadbackStage/type/code
identify transient native inspection failures without recording their contents. It records no transcript,
audio, UI name, clipboard value or clipboard hash. Equivalence flags diagnose
provider differences; they do not relax the ownership or insertion checks.

## Provider readback transitions

Preflight still requires a validated editable field with an empty caret. After
queuing input, a transient provider exception is retried only as read-only
confirmation inside the existing one-second window. Every successful attempt
still requires the original identity before/after inspection, exact complete
text and the exact collapsed caret. Focus loss or a changed input epoch stops
confirmation; retries never send text again.

Stage codes distinguish availability, text-pattern, read-only, selection-count,
selection-collapse, document-text, caret-prefix and prefix-coherence, plus the
focus checks around readback. A WPF pass proves these native transition rules;
the separate BrowserDeliveryProbe exercises only our nonce-scoped local
Chromium fixture. Real Outlook/Codex acceptance remains separate.


## Launch the diagnostic client reproducibly

From a source checkout, after the real client has exited normally and every
fixture has released its clipboard transaction:

~~~powershell
.\scripts\Start-DiagnosticClient.ps1
~~~

The script starts the installed client with child-process-only diagnostics and
normal hotkeys. It verifies that delivery-session.json names the actual new PID.
It does not terminate a running client or fixture, restore any clipboard, start
recording, or change machine/user environment settings. A missing marker leaves
the client running for inspection rather than killing a possible backup.

Use -AppDirectory for a separately published candidate and -DataRoot for an
explicit isolated test data root. The defaults preserve the installed C:\Git
application and C:\AI\LocalDictation settings/data. A later ordinary restart
without this launcher does not inherit its process-only diagnostic opt-in.
