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
