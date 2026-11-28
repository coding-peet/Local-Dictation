# Pala Note recorder-first implementation plan

> Use Superpowers executing-plans to work in task order. Record the red and
> green results and any implementation decisions in this file.

**Goal:** Make capture independent of archive size and make delivery recoverable
across power loss, cable changes and paired computers.

**Architecture:** Pair a bounded, prioritized audio pipeline with monthly
WAVs and per-record receipts. Bridge performs transcription, classification,
provider calls and voice questions.

**Tech stack:** ESP32-S3, Arduino/ESP-IDF FreeRTOS and SD_MMC, existing
paired Bridge in Python, official Workspace MCP.

**Spec:** [Pala Note recorder-first design](../specs/2026-11-25-pala-note-recorder-design.md).

## Global constraints

- Use the existing checkout and branch `codex/pala-note-recorder-rework`.
- Protect each WAV, capture time, transcript and approved delivery target.
- Physical validation uses the second Note and a separate SD. Target
  deployment follows hardware qualification.
- Archive and front-end work follow the capture and sync stages.
- Hardware timings require physical measurements. Host tests provide
  transition and fault-injection coverage.

## Review focus

- REC held and released while Home is still drawing. Verify Task 3 on the Note.
- A month rolls over or the RTC is invalid during capture. Verify Task 4.
- A second Bridge reconnects after a successful provider write with a lost
  reply. Verify Task 5 with both matching and different accounts.
- A delete dialog is open while the pending counts change. Verify Task 6.
- A cached account view expires or access is revoked. Verify Task 7.

## Stage 1 · Capture and transport commit boundaries

### Task 1 · Begin microphone input before capture preparation

**Files:** `pala_note/src/app/record.cpp`, `pala_note/pala_note.ino`,
`tests/test_rec_priority_wiring.py`, `tests/test_capture_wav_wiring.py` under `devices/kubo-note`.

- [x] Add a failing wiring test requiring both direct and priority captures to
  defer storage path preparation until the first codec read. Assert that
  audio initialization and the boot recording window precede display setup.
- [x] Make `recordHeldToPath` acquire its first PCM block before invoking a
  path-preparation callback. Preserve the first samples and prevent overwrite.
  Start the boot recorder before display initialization on the normal boot path.
- [x] Retain first-PCM and first successful write timestamps. Confirm
  frame layout, power-cut tests and the host wiring test.

### Task 2 · Keep route, transcript and queue update in one commit

**Files:** `pala_note/src/app/usb_sync.cpp`, `pala_note/src/app/notes.cpp`,
`pala_note/src/app/notes.h` and `tests/test_sync_replay_wiring.py` under
`devices/kubo-note`. Regression suite includes `tests/test_sync_replay.py`.

- [x] Add a failing wiring test for the USB/BLE COMMIT path. Require recovery
  of any older journal before the queue scope and persistence before SAVED.
- [x] Enclose the route and transcript writes under one existing queue-change
  scope. Retain a fail-closed receipt and existing replay behavior. This is
  the compatibility path while developing the replacement store.
- [x] Run the targeted tests and the Bridge regressions with a disposable
  state. Build paired firmware with PlatformIO when local tools are available.

## Stage 2 · Replace the recording store and complete reliable sync

### Task 3 · Capture continuously into a bounded PCM buffer

- [x] Test first sample retained through a stalled writer, sticky overflow
  reporting, retrigger after abort, and chronological samples across wrap.
- [x] Give the Core 1 mic producer its own lifecycle and preallocated buffer.
  Let a separate SD task consume blocks and publish WAV checkpoints. Keep
  playback, UI and transport from sharing the microphone session.
- [ ] Instrument wake/REC/first PCM/first write. Measure the same three
  capture sequences on empty and populated trial cards and tune from evidence.

### Task 4 · Publish monthly recordings and durable pending state

- [x] Test interruptions before file creation, after the first PCM blocks,
  before publish and after WAV publish. Inject SD read and write faults.
- [x] Replace the normal legacy catalog path with direct recording IDs,
  monthly WAV/TXT paths and an independent work store for unfinished items.
  Move repair of old or inconsistent archive content into explicit maintenance.
- [ ] Test a midnight rollover, invalid clock, full card, collision and a
  previously synchronized recording. Verify WAV and capture time on restart.

### Task 5 · Transport and second-Bridge resume

- [ ] Add loss-of-reply and process-exit tests for USB, BLE and Wi-Fi with
  the same receipt state machine. Include A interrupted by B and one
  accepted result for each ID.
- [x] Negotiate a versioned receipt protocol across existing authenticated
  links. Return a transcript acknowledgment only after persistence.
  Publish pending delivery for a second authorized Bridge and replay text
  without sending WAV again. Keep WAV transient on Bridge.
- [ ] Exercise simultaneous peers, a mismatched account, changed routes,
  provider success with missing acknowledgment, and unsupported reconciliation.
  Validate provider-specific identity recovery and prevent blind retries.
- [ ] Confirm on the second Note through all three real transports and a
  power interruption with a separate SD. Record latency and order.

### Stage 2 hardware acceptance before main Note deployment (26 November 2026)

Run on the second Note and its separate FAT32 test media. Keep the firmware
hash and matching Bridge revision with each result. Use a dedicated Google
Tasks test list. Record each run as passed, failed or pending, with its raw
trace. After a fix, repeat the failing sequence on the same card.

**Evidence already collected**

See the [Test 4 bench results](../../development/pala-note-recorder-test4-bench.md) for the measured times.

- Task 5, Test 4 USB. Three recordings produced three distinct tasks in an
  independent Google readback. C interrupted A during recognition. A recovered
  after its first delivery acknowledgment was deliberately withheld. The bench
  reopened its USB reader. This covers one Bridge and one transport.
- Task 3, original Test 4. After deep sleep the first PCM arrived 355 ms after
  REC, the first WAV write at 1,029 ms. The saved WAV included both ends of
  the spoken phrase. Its UTC and archive month were wrong.
- Task 4, clock candidate. USB corrected the stale August 2025 RTC. After
  deep sleep the RTC and PC agreed to the second before a further clock sync.
  The candidate's Bridge suite passed all 279 tests.
- Task 3, clock candidate, failed. REC to PCM was 353–356 ms, but the first
  WAV writes took 5,612 and 6,500 ms. The second recording overflowed its
  2.048-second PCM buffer.
- Task 4, card readback. Both WAVs were present in /recordings/202611.
  Their capture UTC matched the serial trace. Each decoded to 33,024
  mono frames at 16 kHz (2.064 s). The recording inbox was empty.

**Qualification preparation, 26 November 2026**

The qualification profile captures normal SD timing and microphone CRC,
and has opt-in one-shot windows for the five power-cut boundaries. The
host bench checks the paired development Note, reads the two clocks and
verifies SD PCM against the boot trace. The Wi-Fi writer sends a single
apply attempt per grant. Backup-only plans, claims and receipts can be
reconciled on restart. See the [hardware procedure and acceptance checks](../../development/pala-note-stage2-qualification.md).
The following boxes track physical evidence.

**Hardware acceptance runs**

1. [x] **Clock and archive (Task 4).** Keep the original files. Restart the
   Note, read RTC and system UTC before another sync and compare with the PC. Cover midnight into a new UTC month, an
   invalid clock and recovery on restart. An old recording must retain its
   original capture time and path after correcting the clock.
2. [ ] **Recording response (Task 3).** Separate the time spent preparing the
   path, exclusively opening the file, syncing the header and writing PCM.
   Compare wake while Home draws, idle REC, and REC during active sync on
   empty and populated test media with equal pending counts. Exercise each
   transport, plus archive reading and playback. Record physical wake and REC
   edges where measurable, first PCM, first successful SD write, and WAV
   length with a timed sound. Require intact leading and final samples and
   zero overflows on the previously failing sequence.
3. [x] **Wi-Fi end to end (Task 5).** Confirm the transfer used Wi-Fi.
   Follow a new ID from its SD WAV through transcription, saved text and
   delivery receipt. Independently read the Google task and count matches
   after retry. Test 3 proved association and Web Companion access only.
4. [ ] **Physical power cuts (Tasks 4 and 5).** On the test unit, cut actual
   power at WAV creation, after PCM before a checkpoint, and during publish.
   Also cut after transcript storage before queue publication, and after
   provider success before the Note persists its acknowledgment. Correlate
   each cut with its trace. After reboot inspect readable PCM, capture time,
   pending state, transcript bytes and Google task count. Compare existing
   WAVs and committed text by hash. Repeat recovery once for idempotence.
   Include a full card, a colliding filename and an already delivered ID.
5. [ ] **Two-Bridge recovery (Task 5).** Let Google accept Bridge A's write,
   drop the reply and stop A. Resume on a separately paired Bridge B with
   independent local state. Use the saved transcript, reconcile the approved
   account and target, and verify one Google task by fresh readback. Repeat
   with simultaneous peers, a mismatched account, a changed route and a
   provider result that cannot be reconciled. In ambiguous cases retain the
   pending item and prevent an additional provider write.
6. [ ] **Transport interruption matrix (Task 5).** For USB, BLE and Wi-Fi,
   record coverage of reply loss and Bridge exit around transcript storage
   and delivery confirmation. Interrupt A with REC for B. Verify automatic
   A then B completion, unchanged WAV and committed transcript, recognition
   call counts and exactly one Google task per delivered ID. Reuse the
   existing USB evidence for the boundaries it actually reached.

Hardware records are in the build directories named
pala-note-recorder-test4-hil-20261126 (hardware-report.md and google-verified.json),
pala-note-recorder-clock-20261126 (clock-hil.jsonl), and
pala-note-recorder-test3-20261125 (test3-hardware-report.md).
### Stage 2 acceptance close and main Note candidate (28 November 2026)

Peter closed development acceptance for the first main Note installation.
The checks below record the exercised cases. The broader matrix above
retains its remaining unchecked cases.

- Recording and storage. The development Note passed wake and Home activity
  recording, the forced 6.5-second write delay, overlapping save and capture,
  UTC month rollover, unavailable RTC, and physical power cuts at WAV open,
  PCM and after close before publication. Card readback also passed low-space
  rejection and same-hold filename collision recovery.
- Transport. Physical USB and BLE read and recording trials retained the
  verified microphone samples. The paced BLE run measured an overlapping
  device-side block and a partial radio transfer. The complete Wi-Fi test
  reached the dedicated Google list once for recording 1073796179 and matched
  the WAV, SD transcript and delivery receipt.
- Two Bridges. For recording 1073797203, A held the only write grant and the
  bench withheld the SD receipt. B used independent state and the exact SD
  transcript, reconciled the existing Google task, and completed the receipt.
  ASR calls were A=1 and B=0. All 45 temporarily held dev jobs passed checksum
  verification after restoration.

The new evidence is under
`build/pala-note-stage2-qualification-20261126/`, in
`usb-msc-retry-dev/wifi-stage2-dev/wifi-transfer-8443/`,
`handover-usb-20261128-fixture/`, and `powercut-20261127/`.
The physical handover covers a general Google task over USB. Separate
provider-route conflicts, simultaneous peers, remaining interruption points
and the full per-transport matrix retain their existing coverage status.

#### Main Note preparation

The release candidate is the `pala_note_recorder` profile, without the
qualification-only hooks or Stage 3. The checked application SHA-256 is
`a092c13756f71ba28cc071934e394f023798bc88095515a0976c8fd704e3455b`.
See `build/pala-note-stage2-qualification-20261126/main-note-candidate-20261128/`
for the candidate checks and the installation helper. The Python suite
reported 407 passed, one skipped, and two static audio-wiring checks that
flag the Stage 3 trial-only serial prints. The production image excludes
those prints.

The prepared FAT32 NOTES image has 61 archived recordings with 183 verified
WAV, transcript and metadata files, and an empty pending queue. The archive
path check passed against the current recorder source. The physical card's
first boot with the main Note is pending.

Identify the main Note by its USB serial and ROM MAC. Keep its card removed
while taking an independently verified 8 MiB flash backup. After Peter
authorizes the write, update only the active app0 image and verify the
entire flash against the expected image and preserved regions. Confirm
card insertion only after that readback. On first start with the prepared
card, verify archive visibility and an empty pending queue before a new
test recording. Record the first available capture sequence, expected to
start at 82 on this card unless the retained device ledger is already higher.

Main Note status at this close: candidate verified on PC and installation
pending.

## Stage 3 · Companion, MCP and voice

### Task 6 · Ship Settings and Archive views

- [ ] Add cached SD utilization to Settings. Load month lists and details
  on navigation. Stream playback, WAV download, UTF-8 transcript export
  and log view with bounded memory and prompt REC preemption.
- [ ] Test confirmation with nonzero pending counts, changing counts,
  in-flight delivery and interruption during deletion. Confirm the
  complete month's removal without changing delivered provider items.

### Task 7 · Deliver and read from provider adapters

- [ ] Add Bridge adapter coverage for typed write receipts and read-only
  queries. Implement Google Tasks/Calendar/Docs MCP where applicable.
  Validate OAuth and provider operation semantics with a test account.
- [ ] Test the chosen list for Saved items and cross-list scheduled dates
  for Calendar, including local time/DST, offline freshness, expiry and
  revoked access. Connect the small views to Note.

### Task 8 · Restore Ask Kubo as a read-only voice feature

- [ ] Test routing between public search and explicit private task reads,
  interrupted TTS, unavailable Bridge and rejection of account edits.
- [ ] Retain the lower hold and voice answer. Remove local archive
  searching from the active Note and the daily/weekly shortcut bindings.
  Verify new behavior from the normal gesture and record release evidence.

## After Stage 3

- [ ] Review how Local Dictation uses speech synthesis and identify where
  to improve its integration in the desktop app.

## Execution record

- 2026-11-25. Approved concept and the first capture/sync tranche.
  Start with Tasks 1 and 2. The published rc93 is the comparison baseline.

- Tasks 1 and 2 implemented and verified on PC. The capture retains its
  initial 256 stereo frames before opening the WAV, writes the first PCM
  block ahead of panel work, and closes the WAV before waiting for its UI.
  A legacy journal can reserve an ID and capture time before the file opens.
  The normal boot recording window now starts before display initialization.
- USB/BLE publishes the route and transcript under one queue change. An older
  transcript journal is replayed first, before the new note's queue scope.
  The existing Wi-Fi save call uses the same storage function.
- 246 Python tests passed, including the two-Bridge regression. Four native
  tests passed for REC priority, WAV recovery, transcript recovery and
  ownership. The ESP32-S3 paired image built and passed checksum validation.
  Firmware SHA-256: `0d81d904c5ccad3ad3836a9692037bae8eb09a0fe08c05dc244d5172b01024fd`.
- Next: Task 3. Add a bounded PCM producer and independent SD writer. The
  initial block in Stage 1 protects the first 16 ms only. Durability,
  continuous input during SD stalls, timing and actual link recovery still
  need the later implementation and physical trials. The current build is
  a development artifact.

- Task 3 implementation and focused tests added. The Core 1 I2S task
  uses a preallocated 128-block PSRAM ring. The WAV writer batches
  consumed PCM and finalizes the contiguous prefix on overflow.
- Files in this step: record.cpp, record.h, pcm_capture_buffer.h,
  pala_note.ino, test_pcm_capture_buffer.cpp, test_rec_priority_wiring.py.
- The new wiring regression failed with the one-block recorder as expected.
  Full host tests and an isolated paired image build are next. Actual SD
  stalls, wake and button timing still need measurement on the second Note.

- The first buffered build exposed an Xtensa-wide lock-free assertion that
  this toolchain cannot satisfy. The ring now keeps its atomic indices in
  internal RAM and only PCM blocks in PSRAM. The paired image built and
  passed esptool validation after the correction. The host suite passed 247
  Python tests and five native tests, including 8,000 ordered blocks across
  two threads. The buffered development image SHA-256 is
  9e3c08c170e60a3b00e5dedae2b0e178ced87ac02d0c5ebc76b32dcee9a9eb76.
- Next: arm the boot capture before SD mount and spool inspection. Keep the
  writer behind a separate readiness signal and allow the producer to buffer
  PCM while the storage setup runs. Keep explicit full-storage audit exempt.

- The boot-order test failed on the SD-first implementation and passed after
  the change. Setup now arms REC before SD mount. The spool writer waits for
  mount and existing partial inspection while the producer can fill the ring.
  Mount failure releases the waiter and an expressly requested full audit
  leaves REC disarmed until the audit finishes. Changed files: pala_note.ino,
  record.cpp, record.h and test_rec_priority_wiring.py.
- Next: rebuild the final buffered image and repeat the complete host suite.
  Hardware timing remains open. The boot log records an MCU clock timestamp,
  rather than the physical edge that wakes a powered-down MCU.

- The final buffered image passed the ESP32-S3 build, image checksum and
  embedded validation hash. SHA-256 is
  070a4d18d5273e5e14bd16bf3865c4268a79692448116763dac387c68b2da238.
  The final host suite passed 248 Python tests. All five native regressions
  passed, including the threaded ring test. Static build usage is 99,020
  bytes of internal RAM and 2,736,562 bytes of flash. The PCM blocks add
  66,560 bytes of PSRAM at runtime. The build log and binary are in
  C:/Git/Local-Dictation/build/pala-note-buffered-capture-20261125.
- Task 3 code is ready for hardware qualification. No device was flashed.
  Open measurements include lower-button wake, REC to first PCM and first
  write, SD stalls, capture while Home draws and capture during each link.
- Next: Task 4. Test the new store's publish order at every power-cut boundary.
  Persist one capture identity and its captured UTC time before accepting a
  recoverable WAV. Derive its monthly path from that captured timestamp and
  recover from outstanding work records without enumerating archive months.

- Task 4, first part. Monthly WAV and transcript paths now work through
  direct and priority REC, capture and transcript recovery, USB/BLE, Wi-Fi,
  and existing archive readers. The failing wiring test passed after routing
  callers through the shared resolver.
- Ruling. Derive the month in UTC from the original capture time. Keep
  captures with an unset clock under 000000 and retain the unknown date.
  Store the next sequence in device NVS before opening the WAV so IDs
  remain ordered even if the clock moves back or a month is deleted.
  The encoded ID fits the existing signed 32-bit transport format.
- The legacy catalog and sync projection still provide compatibility in
  this tranche. A dedicated pending-work source and independent startup
  recovery are the next part of Task 4.
- Next checks. Run the path, collision and power-cut regressions on PC
  and build the paired ESP32-S3 image. Physical FAT durability is open.

- Monthly archive checkpoint verified. Passed 249 Python tests, eight
  executable native tests and the compile-time trash policy checks.
  The native recovery test cuts both direct and priority captures after
  the monthly WAV exists. It injects a read fault, retries the reservation
  and verifies capture UTC, playback-equivalent PCM, original bytes and
  that a separate month is intact.
- The paired ESP32-S3 image passed build, checksum and image validation.
  SHA-256: 32f342092f8345c0431f1df0385bc71a1a1feba3f7b8cf4ff448ba8bb8878ba4.
  Artifact root: C:/Git/Local-Dictation/build/pala-note-monthly-archive-20261125.
  No device was flashed. Test FAT32 durability, monthly path creation,
  exclusive create and start latency on the second Note. Include NVS
  commit latency because sequence reservation touches internal flash.
- Next: make pending-work records authoritative. Reserve a single active
  audio job before opening its WAV. Recover only jobs plus the live
  capture journal on normal startup. Close the audio stage only after
  the immutable text is committed, and retain a delivery job until a
  destination receipt is accepted. Remove the normal dependency on the
  compatibility CSV and verify startup with a populated passive archive.

- Continue through a device-testable recorder and sync candidate. Keep a
  distinct build target for the fresh-card recorder format. Finish the
  pending store first, then transport and delivery receipt tests, then
  build and prepare the physical test protocol. The provider views and
  Archive UI have their own acceptance criteria in Stage 3.

- The fresh-card recorder target now reserves capture jobs before writing
  WAV, loads jobs in slices after Home, and serves sync from active records.
  Closing the delivery stage requires a separately durable receipt.
- WORK1 runs over the existing paired USB and BLE sessions. Wi-Fi uses
  the same Note-owned write claim and receipt with the paired HTTPS Bridge.
  On a lost grant or uncertain provider response, the next attempt is
  read-only. A changed binding reports conflict and preserves the source.
- The first provider adapter is Google Tasks with recoverable action markers.
  The binding covers the authenticated Task account's default list identity,
  the exact selected list, and the immutable transcript. Unavailable routes
  stay pending. Google Docs, Calendar, provider views, and Ask Kubo remain
  separate work for the wider rework.
- Earlier build of the new target linked successfully. Initial legacy test
  doubles expected the old exact command sequence. They now model the legacy
  firmware's ERROR COMMAND response to WORK-PROTOCOL. Complete real-worker
  interruption tests and native storage verification before packaging.

- Recorder Test 1 is packaged for the second Note. The running firmware
  separates recording, pending transcription and confirmed delivery. Setup
  only scans the active work directory. Companion opens at Settings and
  has a read-only monthly Archive with WAV playback, download and text
  export. Settings includes pending counts, SD usage, battery and RAM.
- Verified on PC. 258 Python tests, ten native executable suites and a
  Node syntax check on the Settings asset passed. The USB and encrypted
  BLE worker tests cover disconnect before and after transcript publication,
  plus second-Bridge recovery after a successful provider write. The Wi-Fi
  dispatch test covers lost provider confirmation and digest conflict.
- ESP32-S3 build, checksum, embedded version and package verification all
  passed. Firmware SHA-256
  c58f1c59189431dbfff5495aaf64bad805e2aa7235bfd901907aebf884ea5344.
  ZIP SHA-256
  27ef6bc95e21b825f93b1accc73f01c758f08b87fba42586b89fd3ceaba24017.
  Package and build evidence are under
  C:/Git/Local-Dictation/build/pala-note-recorder-test1-20261125.
- Physical verification remains open. Use the second Note, an empty FAT32
  card and a dedicated Google Tasks list. Start with the short test in
  docs/development/pala-note-recorder-test1.md, then compare archive sizes.
  No hardware or installed Bridge was modified for this build.

### Recorder Test 2 storage hot path

- The same-card baseline attributed the delay to FAT lookups through the
  populated legacy root. The trace recorded 360 ms to PCM, 15.953 s to the
  first WAV write, and 7.749 s / 20.199 s / 15.509 s for USB SELECT,
  COMMIT and EXPORT. The preceding stock trial measured 18.068 s to the
  first WAV write.
- New WAVs and their archive sidecars use /recordings/YYYYMM. An on-card
  ordinal boundary preserves Test 1 paths without trying both roots.
  Direct capture reserves its pending job after exclusive file creation
  and before writing a recoverable WAV. Empty moment sidecars are skipped.
- The focused wiring tests reproduced the missing behavior before the
  patch and now pass. Native regressions, ESP32-S3 build and the same-card
  second-Note REC and USB trial are in progress.

- Verified on PC. All 260 Python tests and five focused native suites
  passed. The Recorder Test 2 build and image checks passed. The second
  Note received app0 only and its existing pairing authenticated.
- Verified on the same populated FAT32 card. REC edge to PCM was 356 ms
  and to the first WAV data write was 897 ms. Bridge parsed 68,864 frames
  and read the stored transcript back byte-for-byte. USB SELECT, COMMIT
  and EXPORT took 16, 452 and 30 ms respectively. One AUTH failure after
  local ASR recovered on the next attempt using its cached transcript.
  An independent 18 s idle control passed. The next USB trial should log
  the cause of any session reset if the AUTH failure recurs.
- Evidence and measurements are in
  C:/Git/Local-Dictation/build/pala-note-recorder-test2-20261125.

### USB session loss while Bridge transcribes

- Unmodified application behavior reproduced twice on the same test card.
  The controlled 150 ms SOF-indicator gap and an unforced 24 ms gap during
  local ASR both discarded the active session. The first TEXT command
  returned AUTH. The original event log had recorded only the reply.
- The paired firmware now confirms SOF absence for 250 ms before treating
  it as a detach. The hardware CDC bus reset event still clears the
  session and invalidates the USB pairing attachment before queued serial
  input is handled. Real REC cancellation retains its existing priority.
- Native assertions cover first attach, transient gaps, sustained loss,
  recovery and wraparound. The firmware wiring test failed before the
  patch and passes after it. All 261 Python tests and the release build
  passed. Same-card hardware confirmation is in progress.

- Hardware verification completed without button input. The fixed policy
  preserved the same selected test recording across the 150 ms injected
  gap and read back the exact saved text. It revoked the session after
  250 ms of a 400 ms injected gap. The independently verified clean
  release is on the second Note. Ordinary pre-sleep USB sync did fresh
  local ASR in 16.409 s and saved and re-read the exact text on the first
  attempt. Existing pairing and the protected flash prefix verified.
  Evidence is in C:/Git/Local-Dictation/build/pala-note-recorder-usb-20261125.

### Recorder Test 3 Settings

- The Note card now leads the shared Settings page. Recorder capability
  reaches USB and BLE and recognizes earlier Recorder Test 2 responses.
  Archive navigation stays on the device portal. Recorder's Bridge action
  is transcript delivery to Google Tasks. Legacy destinations retain
  their own controls and the existing persisted processing preference.
- The factory name is Note. The connected device can retain its saved
  name. Windows Connection labels use Note, Bridge and Processing.
- Original idle values confirmed against rc43: Home 60 seconds, queued
  grace 180 seconds, Companion 300 seconds. The firmware already uses
  those values for Default.
- Password protection remains on when no policy is stored. Paired USB can
  change it. Disable and re-enable preserve the password hash and
  invalidate the old Web and BLE password sessions. Existing pairing
  remains required.
- Verification: 264 Python tests and 30 Web Settings tests passed. The
  native sleep-policy test passed. The ESP32-S3 release image built and
  passed image checksum and hash validation. The Windows app built
  with no warnings. Test 3 has not been flashed. Password policy
  persistence and sign-in still need a hardware check.
- Home Wi-Fi association passed on Test 2. The isolated HTTPS test on
  port 18443 received no audio. Windows denied the temporary inbound
  rule. End-to-end Wi-Fi sync is still unverified.

### Recorder Test 4

- Reproduce shopping normalization, clean provider notes and the empty Home
  refresh gap with focused regressions before implementation.
- Add a device-owned delivery plan for a stable AI title. Use clean shopping
  writes, persist successful receipts, and reconcile uncertain writes without
  resending them. Preserve pending claims created by the previous protocol.
- Run explicit Home refresh independently of the recording queue and ASR.
  Reuse authenticated USB, BLE and Wi-Fi metadata paths.
- Run focused regressions, the host suite and firmware build. Package matching
  firmware and Bridge sources without touching the installed runtime or either
  device.
- Build and host verification passed. The ESP32-S3 image fits app0 and passed
  the image checksum and hash checks. The partition table matches Test 3.
  All 276 Python and 30 Web Settings tests passed. Native recorder work,
  transport and sleep-policy checks passed.
- The exact ham regression produced one clean task and replayed after a lost
  Note acknowledgement from both original and second Bridge states. Provider
  readback loss recovered using the durable task ID. Indeterminate inserts
  without an ID stayed pending without another write.
- Verified bundle: C:/Git/Local-Dictation/build/pala-note-recorder-test4-20261125/Pala-Note-Recorder-Test4.zip.
  Hardware acceptance and live provider or title generation remain open.
