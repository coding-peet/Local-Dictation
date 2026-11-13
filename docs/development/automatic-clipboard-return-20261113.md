# Automatic clipboard return validation

Date: 13 November 2026.

## Requested behavior

Every insertion attempt uses a fresh backup of the user's current clipboard and
automatically returns it regardless of insertion confirmation. Unconfirmed text
stays in the existing banner with Insert. Explicit Insert captures the newly
selected external cursor and makes a new attempt. Confirmed insertion closes the
banner; X hides it while protected text remains in History.

There is no clipboard recovery window, inspection checkbox, Resolve action in
the banner, Settings or tray, or manual restoration step in the new client.

## Implementation

ClipboardTransaction always restores on Dispose, independent of acknowledgement.
A one-second deadline is armed immediately after clipboard replacement, so a
stalled pre-dispatch UIA call cannot retain the temporary clipboard. Dispatch
shares the lease state lock with restoration, refuses expired leases and renews
the full one-second consumption window after native dispatch. Versioned timers
ignore queued callbacks belonging to older windows. Success/failure returns the
clipboard sooner when the operation completes before the deadline.

Actual Windows restoration failures retain the complete saved formats and retry
automatically, independently of UIA. Newer clipboard owners take priority. The
registry remains occupied until old-lease cleanup finishes, preventing a return
timer from restoring over a newly reserved attempt.

Read-only late exact-text/caret acknowledgement no longer depends on clipboard
ownership. It retains its original input/focus/provider/age guards. A fresh
explicit Insert invalidates the earlier attempt and drains retained confirmed
receipts before replacing its destination. Double-click reservations and durable
confirmed receipts still prevent duplicate dispatch after known success.

Read-only late acknowledgement does not block normal Exit. Exit invalidates
targets under their publication lock and drains receipts before saving/closing.
An active clipboard replacement or actual restoration failure still protects its
in-memory backup until automatic return or a newer clipboard copy completes.

## Verification

- 443/443 Core tests passed, zero skipped.
- 175/175 server unit tests passed in the existing candidate environment. The
  existing Starlette/httpx deprecation warning remains; no server dependency
  change was made. Model activation messages came from synthetic test doubles.
- 90 windowless WPF/controller/lifetime checks passed. No product startup,
  native input, visible native window, microphone, playback or real clipboard
  access was part of these checks.
- Release build and candidate publication passed with zero warnings/errors.
- Failing regressions were observed for the old unconfirmed-lease hold, blocked
  explicit retry, stalled readback, missing pre-dispatch cleanup and stale timer
  callback protection before the corresponding fixes.
- A fresh independent review identified the pre-dispatch lifetime gap. It was
  corrected and the focused re-review found no remaining actionable issue.
- Actual pending banner and narrow main-window error-state renders were visually
  inspected. Insert remains available and no recovery button/window is present.
- Help passed static checks: 20 unique IDs, 12 internal links, no scripts and a
  restrictive CSP. Published Help matches source byte-for-byte.

Commands run from C:\Git\Local-Dictation\repo:

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe test `
  tests\LocalDictation.Core.Tests\LocalDictation.Core.Tests.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-clipboard-return\core
~~~

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe build `
  diagnostics\VerificationBannerRender\VerificationBannerRender.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-clipboard-return\ui

& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
  C:\AI\build\Local-Dictation-clipboard-return\ui\bin\VerificationBannerRender\release\VerificationBannerRender.dll `
  --output C:\AI\build\Local-Dictation-clipboard-return\green
~~~

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

## Candidate and deployment

Candidate:

    C:\Git\Local-Dictation\clipboard-return-candidate-20261113

Version: 0.1.0-clipboard.20261113.1, based on 1236dd8 plus the reviewed uncommitted
patch. The informational-version suffix is the base commit; it does not claim
that the patch has been committed. No commit or push was requested.

Artifact directory:

    C:\AI\build\Local-Dictation-clipboard-return

The candidate manifest records all 17 published files. Validation results are in
validation.json; silent render results and PNGs are in green. Publication does
not install or launch the application. At candidate validation the installed
older client was still PID 13816 and needed normal tray Exit before replacement.

The API listeners remained PID 34080 on 48125 and PID 53440 on 8123; the session
API reported ready=true, busy=false. Neither service was restarted.

## Verification limits

Native insertion into Outlook/Codex, actual Windows clipboard formats/content,
clipboard contention and device audio were not tested in this quiet session.
Windowless lifecycle tests use empty synthetic leases and protected synthetic
Review files; they do not prove native format fidelity.

The user explicitly chose unconditional automatic clipboard return after being
told that an unusually delayed asynchronous paste can consume restored contents.
The bounded attempt does not eliminate that limitation. A new explicit Insert
after an unconfirmed attempt can also duplicate text that actually arrived;
check the destination before retrying. No automatic replay was added.

Older DeliveryProbe/CompactFlowProbe manual-recovery assertions are superseded,
not current acceptance evidence. The Defender-blocked DeliveryProbe was not run,
renamed, moved or repackaged. The disabled Unicode prototype remains disabled.

## Deployment follow-up

Automatic clipboard return shipped together with the later Review/save fixes in
0.1.0-review.20261113.1. See review-transcription-save-20261113.md, Installed update,
for the verified backup, installed hashes, process marker and unchanged data/API
checks. The earlier clipboard-only candidate was not installed separately.
