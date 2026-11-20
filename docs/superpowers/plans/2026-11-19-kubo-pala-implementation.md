# Kubo Note and Local Dictation Implementation Plan

> For implementation, use superpowers:executing-plans and work through the
> checkboxes in order. The first checkbox group is the physical transfer.

**Goal**

Deliver the agreed Kubo Note workflows through Local Dictation, starting
with a proven physical transfer from Pala to the paired Windows Bridge.

**Architecture**

Keep one ESP32-S3 firmware and use the existing Windows Bridge for paired
HTTPS, durable delivery and provider access. Local Dictation owns Bridge's
Windows lifecycle and transcription stays on its loopback API. Keep private
search and answer generation on the PC.

**Tech stack**

ESP32-S3 / Arduino / PlatformIO, Python / SQLite, .NET / WPF, and the
selected Google Tasks, Docs and Calendar APIs.

**Specification**

[Kubo Note integration](../specs/2026-11-19-kubo-note-integration.md)

## Global constraints

- Work in `C:\Git\Local-Dictation\repo` on `codex/kubo-pala-integration`.
  Preserve the specification changes and all existing pairing and SD data.
- Use Peter's selected accounts and destinations. Keep device tokens, TLS
  private keys, audio and OAuth files outside Git and out of diagnostics.
- Derive dates from the recording time in the device's local time zone.
  Require confirmation for calendar writes. A past date requires an explicit
  instruction to write it.
- Prepare work dictations locally for manual transfer. Use an approved list
  or document ID for personal writes. Keep is for manual notes.
- Fix the route on one approved home network before testing automatic PC
  failover. Use the paired USB candidate for Task 1. Record candidate builds
  separately from installed firmware and client versions.

## Review focus

- A device receives no response after a successful write. A retry or restart
  has to recover the same result. See Tasks 2 and 5.
- A target PC may have accepted an upload before it disappeared. The other PC
  may take new or demonstrably unaccepted work. See Task 2.
- A recording crosses midnight, a time-zone transition or its proposed
  appointment. The chosen date must still reflect capture time. See Task 4.
- A user edits the target item or document while Bridge is writing. Preserve
  user text and reconcile uncertain writes. See Task 5.
- The Docs copy is stale or a question mixes public and private context.
  Keep private text local and make the age of the answer clear. See Task 6.

## 1. Verify one physical Pala → USB → Local Dictation transfer

**Files and entry points**

`devices/kubo-note/mac-bridge/usb_sync.py`,
`devices/kubo-note/pala_note/src/app/usb_sync.cpp` and
the [device README](../../../devices/kubo-note/README.md).

- [x] Prepare a paired USB candidate. Use the existing device identity and
  the Local Dictation loopback API. Read a complete WAV and return a
  UTF-8 transcript to the same note. Test with a simulated USB device.
- [x] Identify the connected Pala and flash the paired candidate into the
  application partition. The 2026-11-20 work-PC test found Pala on COM24,
  verified the flashed image and confirmed that the boot, NVS and
  pairing regions matched their pre-flash checksums. The SD was left in place.

~~~powershell
$env:VIRTUAL_ENV = Join-Path $env:LOCALAPPDATA 'PalaBridge/app/.venv'
Invoke-RestMethod http://127.0.0.1:48125/health
py -B -m serial.tools.list_ports -v
~~~

- [x] Check that Local Dictation is ready and idle. Process one pending
  note and require a save acknowledgment for the same note.

~~~powershell
py -B devices/kubo-note/mac-bridge/usb_sync.py --port COM24 --note 20
~~~

  Use the COM port and pending note number for the device under test. The
  first image could not replay the last block after a truncated reply.
  The follow-up image preserved the device configuration and paired
  successfully. The Windows client read the complete 434,220-byte WAV
  for note 20 with 22 block retries. Local Dictation returned a 128-byte
  UTF-8 transcript and Pala confirmed SAVED 20.

- [ ] Open note 20 on Pala and check the displayed text and original WAV.
- [ ] Repeat after unplugging during a synthetic transfer. Verify that the
  WAV stays intact and a partial transcript is unavailable. Diagnose USB
  enumeration, pairing, WAV receipt, transcription and SD save separately.
- [ ] If the cable proves unsuitable, evaluate Bluetooth using the same
  pairing and recovery requirements. Qualify paired HTTPS separately on
  an approved network before using the existing Google Tasks pilot.

## 2. Make recording retries and PC handover safe

**Files**

`devices/kubo-note/pala_note/src/app/notes.cpp`, `notes.h`, `network.cpp`,
`bridge_client.cpp`, `devices/kubo-note/mac-bridge/bridge.py` and a
`test_delivery_replay.py` in the existing Bridge test folder.

- [ ] Pin a regression test for a response lost after processing, the Bridge
  reopening its SQLite database, and a duplicate upload from the device.
  Expect the same result and one application of each destination action.
  Pin two distinct recordings with identical speech as separate requests.
- [ ] Persist a stable `recording_id` with the SD note before upload. Store the
  original capture timestamp with it. Backfill older queued notes once
  without rewriting their recorded timestamp. Derive destination action
  IDs from the recording ID plus action identity. Reuse the existing Bridge
  SQLite database to persist accepted results and uncertain outcomes.
- [ ] Record the selected Bridge identity for an in-flight upload. Retry it
  on the same PC until the outcome is known. Permit the next paired PC
  when the first PC is unavailable before acceptance was possible. Never
  create a second write merely to resolve an unknown result.
- [ ] Run isolated Bridge replay and discovery tests. On the approved setup,
  queue a synthetic recording with Bridge down, restart it, and verify a
  single result. Repeat on a small batch before attempting the existing SD
  backlog. Save the second-PC acceptance for Task 8.

## 3. Integrate Bridge control into Local Dictation

**Files**

`src/LocalDictation.App/AppController.cs`, `AppController.Startup.cs`,
`Views/SettingsWindow.Connections.cs` and a process owner under
`src/LocalDictation.App/Services/`. Extend the detached WPF checks in
`diagnostics/VerificationBannerRender`.

- [ ] Exercise owned startup, double-click restart, closing the main window,
  explicit Exit, missing pairing and discovery of an existing standalone
  Bridge. Use a fake child process in state and layout checks.
- [ ] Build on the existing `--managed` stdin ownership and authenticated
  `GET /pala/ready`. Expose status and **Restart Bridge** in the existing
  tray menu. Add recent errors, pairing and enable/start/stop to
  Connections. Choose the configured startup behavior with Local Dictation.
  Keep the service alive when the main window closes.
- [ ] Release the managed process through its stdin pipe on Exit or Restart.
  Finish or persist in-flight work. Show an existing unmanaged listener
  as such. Defer Codex login validation to features that actually use the
  signed-in CLI so local transcription can run offline.
- [ ] Run the Core and detached WPF checks and verify the actual tray on a
  candidate install. Use fake processes before testing against a real one.

## 4. Resolve appointment dates and confirm on Pala

**Files**

`devices/kubo-note/mac-bridge/bridge.py`, a focused `appointment_dates.py`
and `tests/test_appointment_dates.py` under the Kubo device folder.
Firmware confirmation lives in `pala_note/src/app/network.cpp` and the
related existing UI. Reuse `notes.cpp` and `rtc.cpp` for capture time.

- [ ] Before changing behavior, check whether Martin supplied concrete
  repros or a diff. Reproduce them independently if available. The current
  source already provides a starting case: `Bridge.memories` computes
  yearless dates from the current processing time.
- [ ] Define `appointment_candidates(intent, captured_at, zone, now)`.
  It returns concrete local candidates and a confirmation state from a
  structured date/time intent, the original phrase and explicit past
  intent. Check that the chosen date is still allowed at confirmation.
- [ ] Pin these examples before wiring delivery.

| Input | Result |
| --- | --- |
| Saturday 01:30, “zajtra o 15:00” | Offer Saturday and Sunday at 15:00. |
| Saturday at 04:00, same wording | Offer Sunday at 15:00. |
| Recording on 12 December 2026, “11. decembra o 15:00” | Offer 11 December 2027 at 15:00. |
| Monday “zajtra o 15:00” reaches Bridge on Wednesday | Retain Tuesday in the draft as expired and request a new date. |
| Explicit past date, or confirmation arrives after the proposed time | Require explicit past intent for a past write. Otherwise ask for a replacement. |
| Missing capture time, ambiguous or skipped local time during a clock change | Keep a draft. Resolve the missing date or offset in confirmation before any write. |

- [ ] Keep candidate computation deterministic in the recording's time zone.
  Apply the one-year bound to a date without a year. When no occurrence
  falls in that window, ask for a concrete date. Keep the original WAV and
  transcript if date interpretation needs input.
- [ ] Extend `/pala/event` to carry the chosen date/time for a pending action.
  Keep a valid legacy confirmation for proposals with one unambiguous
  candidate. A new multi-choice or expired draft requires a concrete
  selection. Display the full date including year and show the original
  expired date when requesting a replacement.
- [ ] Test confirmation with a fake provider. No calendar call is allowed
  before a valid confirmation. Protect backdating even when a confirmation
  is replayed after a clock change.

## 5. Qualify Google destinations and local work preparation

**Files**

`devices/kubo-note/mac-bridge/pala_task_pilot.py`, `google_tasks.py`,
`google_store.py`, `bridge.py` and their Bridge tests. Put Docs append and
read operations in a focused `google_docs.py` with its own tests.

- [x] Verify one existing Pala transcript in Google Tasks as a plain
  General entry. The approved application-only flash preserved configuration
  and pairing. The USB export read note 4 and its original capture time.
  The explicit one-note delivery to the trial list was read back with the
  full existing text. No ASR request ran during the test.

  Run the preview from the repository root using the Bridge Python
  environment. It reports the route and number of actions without running
  ASR or contacting Google.

~~~powershell
py -B devices/kubo-note/mac-bridge/pala_google.py --transcript 'C:\path\note_20.txt' --note 20
~~~

  For one General entry, append
  `--general-list 'Kubo notes general' --apply`. For an explicit task use
  `--task-list 'Kubo notes general' --apply`. The command resolves the
  existing trial list to its provider ID and verifies the result after
  writing it. Use `--reconcile` instead of `--apply` to read back an
  uncertain Tasks write.

  Here `Kubo notes general` refers to a **Google Tasks trial list**. The
  production destination for explicit personal notes is the selected
  **Google Docs** document of the same name. That trial needs the approved
  document ID, Docs authorization, and the original capture time.
  Verify the one write before configuring automatic delivery.

- [ ] Bind account and destination type plus provider ID. Keep the approved
  Tasks lists, the sorting list for ambiguity and the selected Docs and
  Calendar IDs. Preserve the same approved list selection on paired PCs.
  Handle renamed, revoked, missing and ambiguous destinations before any
  mutation.
- [ ] Implement personal shopping and tasks in `google_tasks.py`. Test that
  “maslo a rožky” produces two entries and no entry for the command itself.
  On a second distinct request for open “maslo” in the same list, update
  its note to include “Opakovaná požiadavka (x2)” and preserve other text.
  A replay applies neither an additional entry nor a repeat increment.
  Use the action ID and readback to reconcile uncertain creates. Leave
  unresolved outcomes for reconciliation before retrying a write.
  [Google Tasks insert reference](https://developers.google.com/workspace/tasks/reference/rest/v1/tasks/insert)
- [ ] Append explicit personal notes with a date in the selected Docs
  document. Use an action-linked named range and the observed revision in
  the same batch update. Re-read on a revision conflict. Test a concurrent
  manual edit and a retry after the server applied the append.
  [Google Docs named ranges](https://developers.google.com/workspace/docs/api/how-tos/named-ranges)
- [ ] Keep calendar writes behind Task 4. Reuse the stable event ID in
  `google_store.py` and verify the matching existing event after an
  uncertain result or ID conflict before marking delivery complete.
  [Google Calendar event IDs](https://developers.google.com/workspace/calendar/api/guides/create-events)
- [ ] Route explicit work dictations to local preparation before calling
  Codex or an external destination. When one recording contains a work note
  and a separate task, preserve both for manual transfer. Treat “Keep” as
  ordinary text. Cover both cases with outbound adapter checks.
- [ ] Run destination tests with simulated clients. Enable each approved
  Google target only after its authorization and synthetic readback have
  been verified. Validate the actual Google apps on Galaxy.

## 6. Add private local answers and public voice questions

**Files**

`devices/kubo-note/mac-bridge/bridge.py`, `google_docs.py`, a
`local_note_search.py`, `speech.ps1`, and the existing question and
speech paths in `pala_note/src/app/network.cpp` and `speech_stream.cpp`.

- [ ] Read the approved Docs document into a per-account local snapshot with
  document ID, revision and refresh time. Publish the new snapshot only after
  a successful read. Proposed default: use a bounded refresh for questions
  that need the document. On error use the last good snapshot and disclose
  its age. Test a manually added note, a removed note and an offline query.
- [ ] Implement `answer_local_question(question, local_records, docs_snapshot)`.
  Generate a Slovak answer on the PC with the date or source for matching
  text. When local evidence is insufficient, show the limitation. Test that
  private and mixed questions never hand notes to Codex or web search.
- [ ] Route separately identified public questions to the public information
  path. Build the request from the public utterance alone. Test with fake
  public and local adapters before enabling a test query.
- [ ] Reuse the existing speech endpoint and player. Generate Slovak mono
  speech in the format expected by the firmware. Strip Markdown and URLs
  before playback. Verify both visible text and intelligible playback on
  Pala, including when the required Windows voice is missing.

## 7. Update the date, queue count and week strip

**Files**

`devices/kubo-note/pala_note/pala_note.ino`, `src/app/draw.cpp`,
`ui.cpp`, `notes.cpp` and `rtc.cpp` within the firmware folder.

- [ ] Derive the count from the durable queue. Center the number and a
  continuous sound wave together in the top row. Hide both at zero.
  Check an outage, partial delivery,
  final acknowledgment and reboot. Keep the SD record available during
  failed processing.
- [ ] Reuse the device's validated local date. Refresh on wake, and target
  a refresh within one minute of midnight while awake. Vertically center
  the calendar between the status and agenda sections. Place the month,
  weekday and ISO week at the left, center and right of the heading.
  Use one regular-weight date row for Monday–Sunday. Highlight today and
  separate the weekend with a thin divider. Place weather below that row
  when it is added. Align agenda labels left and counts right at the bottom.
- [ ] Check midnight, Sunday/Monday and the New Year week boundary with
  synthetic dates. Build the firmware, then verify fit and readability
  on the physical 200 × 200 display during the device acceptance run.

## 8. Run acceptance and qualify the shared installer

- [ ] Run the complete Bridge suite in the existing Bridge environment,
  Windows tests and a Release build into a separate output directory, and an
  ESP32 candidate build. Use the repo's [build and recovery guide](../../development/rebuild-and-recovery.md).
  Record test results separately from the physical device checks.

~~~powershell
py -B -m unittest discover -s devices/kubo-note/tests -v
.\scripts\Build.ps1 -BuildRoot (Join-Path $env:TEMP 'KuboNote-candidate-build')
py -B -m platformio run -d devices/kubo-note -e pala_note_2
~~~

- [ ] On a qualified network, verify one personal note, a shopping dictation
  and a confirmed event in the actual Google apps on Galaxy. Repeat a
  recording through a Bridge restart and verify one result and a cleared
  queue count. Confirm one private question and one public question,
  Slovenčina on the display and playback. Check the second paired PC for
  available, unavailable and unknown-delivery cases.
- [ ] Build the single Windows installer for the client, Bridge and local
  session API. Validate first setup with and without the model, offline
  transcription, startup and clean shutdown. Upgrade a candidate with
  existing pairing, allowlists, models and history. Confirm preservation
  and verify rollback independently of the installed client.
- [ ] Keep Web Companion's existing two-button activation and authentication
  in the regression run. Run unattended transfer after closing Companion.
  Compare candidate flash and RAM usage against the recorded baseline. Measure
  peak RAM/PSRAM and power on hardware as separate checks.
- [ ] Qualify Galaxy A55 hotspot and any other network profile separately.
  If policy or station isolation blocks Wi-Fi transfer there, record the
  layer and evaluate the previously proposed paired USB path separately.

## Deferred work

The offline aquarium fish, direct OneNote, Planner and Microsoft 365
calendar writes, and location-based shopping reminders are future topics.

## Starting evidence

The installed Bridge has the transcription route. The source candidate
contains `/pala/ready` and `--managed`. The local Google Tasks pilot previously
created and read back a synthetic item. PC/Galaxy editing and the physical
Pala-to-Bridge audio upload are pending. Migration commit `8ff1087` supplies
the baseline sources. Its successful builds and test results establish the
build baseline.

## Implementation checkpoint, 20 November 2026

- The follow-up ESP32-S3 image passed independent flash verification.
  Physical USB transfer, Local Dictation transcription and the save
  acknowledgment passed for note 20 on COM24. The Windows client
  now reads USB data in batches and retries the same block when a
  response is lost or truncated. The Bridge regression suite passed
  37 tests. The on-device text and unplug test are still open.
- The firmware records capture time at recording start, keeps existing
  timestamps and draws the week strip and pending count. Calendar-week
  boundary checks compiled with the ESP32 toolchain.
- The Bridge resolves yearless dates from capture time and holds expired or
  unresolved calendar proposals. The 00:00–03:59 choice and replacement-date
  confirmation on the device remain part of Task 4.
- The automated Bridge suite passed 35 tests. The installed application and
  the flashed firmware are the starting point for the controlled home test.


## Wake-to-sync transport order

- [x] Test USB priority, Wi-Fi readiness and the Bluetooth fallback.
- [x] Reuse the authenticated USB transfer engine for encrypted BLE frames.
- [x] Extend the managed Windows watcher to discover its paired Pala and
  send one queue at a time through Local Dictation.
- [x] Build the paired and stock firmware candidates. Run the Bridge suite.
- [ ] With a paired Pala, test data USB, Wi-Fi without a cable, and BLE with
  Wi-Fi unavailable. Wake with one lower-button press in each case. Interrupt
  an upload once and verify the next attempt saves exactly one transcript.

The Bridge passed 86 tests and the Windows core passed 648 tests. The paired
ESP32-S3 image passed image validation. Windows reported a BLE central
adapter with its radio off. Physical transport testing remains pending.

## Windows configuration and rc44 checkpoint

- Connections now manages Pala sync and the network Bridge, including manual
  start/stop, optional startup, model selection and an activity row. The
  offline Help contents link to the Connections guide.
- The paired rc44 firmware image includes the USB forecast and display-name
  sync. The device stores the name in its preferences. The existing pairing
  identity secures synchronization over USB, Bluetooth and HTTPS.
- The Windows activity build passed the core, detached UI and Bridge tests.
  The offline Help update is installed. The activity build is ready for a
  normal client handover.
- Verify rc44 on the physical Note with an application-partition-only update.
  Use the paired USB name and weather checks, then confirm the display.
  Test Wi-Fi and Bluetooth separately on the approved setup.
