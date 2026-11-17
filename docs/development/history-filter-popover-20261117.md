# Compact History filter follow-up

Date: 2026-11-17
Baseline HEAD: 586ed67d4e0e1947c70c01d55c708b456e42c098, feature/native-windows.
Status: implemented, independently reviewed, verified and deployed.
The corrected .2 client is installed and running; no filter finding remains open.

## Behavior

The always-present filter card is replaced by a small outlined Filters trigger
in the search/import row. Conditions live in a separate floating Material-themed
form. Only applied conditions have chips; inactive filtering reserves no row.
One date range is one chip. A single scrolling chip row retains Clear filters.

The form edits a draft. Apply validates and commits; Cancel/Escape/outside click
or navigation discards it. Text search remains immediate. Clear filters and chip
removal preserve the search text and original selected-identity behavior. No
index, transcript persistence or recognition setting changes are introduced.

## Verification

Initial detached RED reproduced the missing compact trigger/permanent-card
behavior. Focused checks passed six behavior/layout groups across Light/Dark:
apply/draft/cancel/Escape, invalid ranges, typed dates, independent search,
applied-only chips, selected identity, compact widths432/640/1040 and separate
form layout. Native popup/window/input/audio was not used.

Fixtures use synthetic text and zero PCM buffers in isolated protected stores.
Playback output remains absent; no microphone capture or real speech was used.
All build/test artifacts are outside Git.

Evidence:

    C:\AI\build\Local-Dictation-personal-tools\filter-red-20261117
    C:\AI\build\Local-Dictation-personal-tools\filter-behavior3-20261117
    C:\AI\build\Local-Dictation-personal-tools\filter-full-20261117
    C:\AI\build\Local-Dictation-personal-tools\filter-core-results
    C:\AI\build\Local-Dictation-personal-tools\filter-backend-tests-20261117.log

## Scope and runtime preservation

All earlier uncommitted import/private-selector/General/startup work remains in
the same canonical checkout. No commit/push is requested. Session API48125/PID
54728 and batch API8123/PID51740 remain untouched. The user's own enabled Run
startup value still points to the installed EXE with --startup; do not modify it.
No client process was observed at the initial passive check. Recheck immediately
before deployment; if a client is running, normal tray Exit is required.

Actual native popup focus, outside mouse dismissal and nested calendar/dropdown
interaction remain distinct from detached verification. No real logon/audio or
model-quality acceptance is claimed. Deployment and final results follow below.


## Historical pause checkpoint (resolved during resume)

Work stopped on Peter's explicit request to save progress. All source, tests,
requirements, Help and plan changes are already saved in the canonical checkout.
Nothing was committed or pushed. Do not discard earlier dirty/untracked work.

Completed verification before the new review regression:

- Core640/640 passed.
- Detached WPF215/215 passed.
- Backend185/185 passed.
- Focused filter checks passed six Light/Dark behavior/layout groups.

Independent review then found P2: typing an invalid From/To date and leaving
its DatePicker textbox lets WPF restore the previous date or blank. The current
DateValidationError handler only displays text. Apply then rereads the reverted
DatePicker.Text, commits other draft conditions and closes, hiding the invalid
attempt. Also validate raw PART_TextBox text for Enter before focus loss.

The following real-control regression was added and observed RED before any
fix. It sets DatePicker PART_TextBox.Text to an invalid value, raises managed
LostFocus, then asserts Apply refuses and retains the applied HistoryQuery.
It currently fails with:

    A DatePicker-reverted invalid date silently applied other draft filters.

Evidence:

    C:\AI\build\Local-Dictation-personal-tools\filter-invalid-date-red-20261117\results.json

Next source changes should retain per-field invalid/raw draft state until an
explicit correction or cancellation, validate uncommitted textbox input, and
clear error state when a valid date is selected/edited. Do not merely suppress
the failing assertion or trust DatePicker.Text after focus loss. Confirm a
keyboard correction and calendar selection can recover without trapping focus.
Then rerun focused checks and the full WPF suite, update this report and rebuild
the candidate. Core/backend need not be repeated unless affected by new changes.

Relevant files:

    C:\Git\Local-Dictation\repo\src\LocalDictation.App\Views\HistoryPanel.Filters.cs
    C:\Git\Local-Dictation\repo\src\LocalDictation.App\Views\HistoryPanel.Search.cs
    C:\Git\Local-Dictation\repo\diagnostics\VerificationBannerRender\HistoryFilterPopoverChecks.cs
    C:\Git\Local-Dictation\repo\diagnostics\VerificationBannerRender\HistorySearchChecks.cs

Build and focused check (no native window, input, audio or API):

~~~powershell
Set-Location C:\Git\Local-Dictation\repo

& C:\AI\tools\dotnet-10.0.403\dotnet.exe build `
    diagnostics\VerificationBannerRender\VerificationBannerRender.csproj `
    -c Release --no-restore --nologo `
    --artifacts-path C:\AI\build\Local-Dictation-personal-tools\filter-ui

& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
    C:\AI\build\Local-Dictation-personal-tools\filter-ui\bin\VerificationBannerRender\release\VerificationBannerRender.dll `
    --output C:\AI\build\Local-Dictation-personal-tools\filter-date-fix-20261117 `
    --only HistoryFilterPopoverChecks
~~~

Use a fresh output directory if the named one already exists. For the full
WPF suite, omit --only and use a separate fresh output directory.

## Historical installed/candidate state at pause

The installed client is still the last verified General/startup build:

    C:\Git\Local-Dictation\app
    0.1.0-general.20261117.1+working.586ed67.586ed67d4e0e1947c70c01d55c708b456e42c098

The candidate below contains the known-unfixed filter issue and MUST NOT be
deployed until corrected, retested and republished:

    C:\Git\Local-Dictation\filter-candidate-20261117

No client process was found at the last passive check. Both existing API
listeners were left running unchanged (last observed 48125/PID54728 and
8123/PID51740). These PIDs are historical: recheck after Windows restart/logon
before any future rollout. Never restart/stop an API as incidental setup.
The user's enabled Start with Windows entry was preserved and matched the
installed EXE with --startup. Do not disable it to facilitate testing.

No background build/test process from this task remains. No desktop control
session was used. Native popup focus, outside mouse dismissal, nested calendar
and dropdown behavior remain unverified. No voice tests or model inference ran.


## Resume: invalid-date correction

The paused P2 was reproduced again before editing:

    filter-date-resume-red-20261117

DateValidationError now retains a per-field invalid state even when WPF restores
the old displayed value. Apply refuses that state, preserving the applied query.
Explicit text input, deletion/paste, a valid calendar selection or cancellation
can recover. Apply also reads the DatePicker PART_TextBox before focus loss so
Enter cannot ignore uncommitted raw input. Closing the draft resets validation.

Each date picker explicitly uses the current UI date culture; parsing follows
that picker's culture. Focused checks verify keyboard and calendar correction,
cancel recovery, the reverted-invalid case, raw invalid input and date
round-tripping in sk-SK, en-US and en-GB. All seven focused groups passed with
zero build warnings/errors and no native input/audio/API activity.

Focused evidence:

    C:\AI\build\Local-Dictation-personal-tools\filter-date-resume-green-20261117
    C:\AI\build\Local-Dictation-personal-tools\filter-date-cultures-20261117

On resume, passive checks found no LocalDictation process and no listeners on
48125 or 8123. The own Start with Windows value remained enabled and matched
the installed EXE. Do not reuse historical PIDs or restart services as setup.
Normal client launch automatically starts its session API when absent, so the
rollout must distinguish file replacement from that service-start side effect.


## Final verification before deployment

- Focused7/7 groups passed, including reverted/raw invalid dates, explicit text
  correction, calendar correction, cancel recovery and sk-SK/en-US/en-GB formats.
- Full detached WPF216/216 passed after the fix. No native HWND/input/audio.
- Release publish completed with zero warnings/errors. Corrected package version:

    0.1.0-filters.20261117.2+working.586ed67.586ed67d4e0e1947c70c01d55c708b456e42c098

- Earlier Core640/640 and backend185/185 results still cover unchanged sources;
  no new Core/backend change justified repeating them during this resume.
- Independent review's only material P2 is resolved by an observed RED-to-GREEN
  real-DatePicker regression. No further review finding is deferred.

Evidence:

    C:\AI\build\Local-Dictation-personal-tools\filter-resume-full-20261117
    C:\AI\build\Local-Dictation-personal-tools\filter-resume-deployment-20261117

A later passive check found clientPID35024 and new API listeners48125/PID49972,
8123/PID33624. They appeared externally; this task launched none. The deployment
safety guard stopped at its first running-client check before any backup/copy.
A normal tray Exit was requested once for this newly observed client. Preserve
current listener identities, not the obsolete pre-pause IDs, during rollout.


## Completed rollout

Peter confirmed normal tray Exit for client35024. Process absence was verified
before replacing files. The previous 17-file client was backed up and all 17
installed files matched the corrected .2 candidate. All107 protected data files
matched their pre-deployment hashes before launch. The redundant candidate was
removed after final installed/candidate hash comparison; the previous-app backup
and deployment manifests are retained outside Git.

Client53908 now responds from C:\Git\Local-Dictation\app. Its error log grew
by zero bytes. Both existing API identities stayed unchanged across copy and
launch:48125/PID49972 and8123/PID33624. The enabled own Windows startup entry
retained its original kind and value hash. No API or registry mutation was
performed by the agent.

After launch, the data check found only settings.json modified. Peter explicitly
confirmed he switched the microphone. That live user change was preserved; all
other106 original data files were hash-identical. Do not restore the earlier
settings.json or treat this confirmed user edit as an installation regression.

Verification evidence, payload/backup manifests and startup/readback fingerprints:

    C:\AI\build\Local-Dictation-personal-tools\filter-resume-deployment-20261117

Final verification: focused7/7 and full detached WPF216/216 pass; earlier
Core640/640 and backend185/185 still cover their unchanged source. The corrected
invalid-date regression was observed RED, then GREEN. Native popup focus/mouse
and nested calendar/dropdown interaction remain outside this silent automated
verification. No voice, recording, playback or real model inference was tested.

All source remains in the canonical checkout on feature/native-windows at
HEAD586ed67d4e0e1947c70c01d55c708b456e42c098 plus the reviewed working changes.
No commit or push was performed during that rollout. Peter subsequently
authorized commit and push of the complete change set on feature/native-windows.


## Git publication authorization

On 2026-11-17, Peter requested commit and push of all reviewed changes to
feature/native-windows and asked to include current Help. This report belongs
to that publication change set. The Help troubleshooting paths and tray-menu
names were checked against the current General/Models/Dictation/History UI.
No runtime data, recordings, models, logs or generated binaries belong in Git.


## Publication validation

Before the authorized commit/push, the complete final source was verified again:
Core640/640, detached WPF216/216 and backend185/185 passed. The build reported
zero warnings/errors. Evidence is under:

    C:\AI\build\Local-Dictation-personal-tools\publication-check-20261117
    C:\AI\build\Local-Dictation-personal-tools\publication-backend-tests-20261117.log

Help now also corrects the troubleshooting routes to General / Advanced, the
retention route to General / History, and the tray action to Open Local Dictation.
Its HTML parsed with balanced tags,26 unique IDs and15 resolved local links.
The History guidance was split into shorter paragraphs for readability.
Updated Help.html was copied to the installed app with a prior-file backup and
matching source/installed SHA256. No client binary replacement, runtime setting
change or API restart was needed for this static documentation update.

The reviewed publication scope is54 .cs/.md/.html files covering import errors,
private transcript selection, General/startup navigation, compact History filters,
their regression checks and documentation. No private/runtime/generated data is
included. Remote feature/native-windows matched the baseline before publication;
push must remain non-force and final remote HEAD/clean status must be verified.
