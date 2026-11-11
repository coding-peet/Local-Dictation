# Synthetic Chromium delivery probe

This diagnostic exercises the actual WindowsTextDelivery adapter against only
our own local Chrome or Edge textarea/contenteditable fixture. It does not open
Outlook or Codex, inspect browser history, record audio, load a model, send text,
restore focus or scan other controls.

## Validation status and enforced access boundary

The probe builds successfully. Only the negative impossible-HWND refusal was
executed: no field text was read, no native input was queued, and no clipboard
transaction was opened. The valid Chromium insertion path is NOT validated.

During the 2026-11-11 validation attempt, the CUA Chrome extension opened the
synthetic tab in the background. The native foreground window remained on a
different page. CUA then stopped computer use because it could not determine
the current browser URL on Windows confidently enough to enforce policy.

No browser Apply was attempted. Do not use this helper, direct UIA, FindWindow,
or another native automation route to work around that enforced stop. A future
run requires restored, policy-approved CUA access to the verified native fixture
window and a new explicit validation step. Current WPF18/18 results do not
establish Chromium, Outlook or Codex compatibility.

## Build

From the repository root:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe publish `
    diagnostics\BrowserDeliveryProbe\BrowserDeliveryProbe.csproj `
    -c Release --no-self-contained `
    -o C:\Git\Local-Dictation\browser-delivery-probe\candidate `
    --artifacts-path C:\AI\build\Local-Dictation-browser-probe\candidate
~~~

The command builds the diagnostic and copies its one self-contained HTML asset
to candidate\site. It does not start a client, browser, server or microphone.
No additional packages or browser automation dependencies are installed.

## 1. Establish safe clipboard state

Finish dictation and exit the installed client normally. If it retains a
clipboard backup, inspect its destination and resolve the existing recovery
before continuing. Do not kill the client or supersede a backup with this test.

Finish or close older diagnostic fixtures only after their reports show both
ClipboardRecoveryPending and ClipboardTransactionUnsettled are false.

## 2. Serve only the synthetic asset directory

Check that the chosen port is free. This command starts a hidden read-only
static server bound only to 127.0.0.1, serving no repository or user documents:

~~~powershell
$fixtureServer = Start-Process py -WindowStyle Hidden -PassThru -ArgumentList @(
    '-m', 'http.server', '49181',
    '--bind', '127.0.0.1',
    '--directory', 'C:\Git\Local-Dictation\browser-delivery-probe\candidate\site'
)

$fixtureNonce = [guid]::NewGuid().ToString('N')
$fixtureUrl = "http://127.0.0.1:49181/fixture.html?nonce=$fixtureNonce"
$fixtureUrl
~~~

The nonce is a random identifier for these synthetic fields, not a credential.
The HTML refuses other hosts and invalid nonce values. It has no external
assets, recipients, network upload, storage or send action.

## 3. Select the fixture through CUA

Open fixtureUrl in a separate Chrome/Edge fixture window. Use CUA to verify the
page title contains the nonce, then obtain that exact native HWND from the CUA
app inventory. Click the requested textarea or contenteditable. Its own focus
and pointer-up handlers place the cursor between the synthetic prefix/suffix.

Keep this exact fixture foreground during the run. The helper never activates
or adopts another field. Do not type or change focus until its report completes.

## 4. Run the hidden one-shot helper

Replace the sample HWND with the current verified fixture window. Keep the
same nonce used by the page. Run one control at a time:

~~~powershell
$fixtureReport = 'C:\Git\Local-Dictation\browser-delivery-probe\textarea.json'

$fixtureHelper = Start-Process `
    'C:\Git\Local-Dictation\browser-delivery-probe\candidate\BrowserDeliveryProbe.exe' `
    -WindowStyle Hidden -PassThru -ArgumentList @(
        '--expected-window', '123456',
        '--nonce', $fixtureNonce,
        '--control', 'textarea',
        '--report', $fixtureReport,
        '--arm-ms', '5000'
    )
~~~

After the five-second arming delay, the helper verifies HWND, title nonce,
Chrome/Edge process, Chrome UIA framework, exact aria-label and exact AutomationId
BEFORE reading field text. If any check fails, it refuses without input.

It accepts only the exact known baseline, optionally with one known terminal
paragraph separator, and the exact original synthetic caret. No arbitrary
whitespace normalization or value logging is used.

The helper invokes the production private Inspect and Integrity checks on the
same validated AutomationElement, then constructs the actual adapter/TargetGuard.
This scope-locked test capture intentionally avoids public Capture recapturing a
different focused element between validation and field read. The report records
PublicCaptureUsed=false and ScopedProductionInspectUsed=true; native Apply and
its clipboard/confirmation behavior are the real product code.

The helper calls Apply once and independently checks the exact synthetic result.
A passing report requires four queued native input events, exact final content,
and no held clipboard backup. Inspect the result through CUA as well.

Reset the page's two synthetic fields and repeat with control=contenteditable,
a separate report path and the same verified window/nonce.

## 5. Recovery and process lifetime

Do not terminate a helper while either report flag is true. It deliberately
stays alive if a third-party provider is still active or a clipboard backup is
held. An active worker cannot be manually restored.

If Stage is awaiting-explicit-recovery, first inspect the OWN synthetic field
through CUA and stop any pending fixture action. Then deliberately request
restoration with the exact nonce:

~~~powershell
Set-Content -LiteralPath ($fixtureReport + '.restore') -Value $fixtureNonce -NoNewline
~~~

This is an explicit recovery decision, not a native input-processing barrier.
Only a held, stopped transaction accepts this signal. The helper retains the
complete backup on restoration failure and stays alive. A newer clipboard copy
takes precedence. A crash or forced termination cannot preserve an in-memory
backup.

After reports show no held/unsettled transaction and helpers have exited,
stop only the static server process started for this fixture:

~~~powershell
Stop-Process -Id $fixtureServer.Id
~~~

## Interpretation

A scoped refusal is not a transport result. It must be resolved before claiming
that Chromium insertion was tested. In particular, do not relax a missing or
unexpected native AutomationId without reviewing the exact allowed scope.

This probe establishes only the local synthetic controls and the observed
provider/version. It does not establish Outlook, Codex or every Chromium editor's
compatibility. Operational reports contain technical flags, lengths, error codes
and native delivery metadata, never actual field or clipboard values.
