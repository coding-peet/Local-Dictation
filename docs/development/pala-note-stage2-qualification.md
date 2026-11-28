# Stage 2 recorder qualification on the development Note

Use the second Note (USB / base MAC 70:04:1D:D8:16:A8). The qualification
profile is `pala_note_recorder_qualification`. Version qual1 measured the
clock and WAV timings below. Version qual2 adds REC display timing. Both
measure natural SD timing. The earlier
6.5-second injection has its own profile and already has a matching SD
readback of 209,408 frames and PCM CRC32 54912889.

## Verified candidate, 26 November 2026

Application: 2,571,584 bytes, ESP32-S3, DIO, 8 MiB flash.
File SHA-256: `5ca081a9510286f1db3c229f84641e731727b6cda0a229b4cedac1788cb16878`.
The image checksum and embedded validation hash passed. Both recorder
profiles built. The regular recorder image contains no qualification
command strings or cut markers.

On the PC, all 293 Python tests and eight native test programs passed.
The native recording-owner helper was enabled for the cross-Bridge replay
test. Feeding the previous 6.5-second trial's original USB lines through
the new trace filter recovered the same 209,408 frames and PCM CRC32
54912889 as the archived WAV. The new candidate still requires the
physical trials below.

## Physical month rollover, 26 November 2026

The development Note on the populated 2 GB FAT32 NOTES card ran the
qualification image above. An independent 8 MiB flash readback matched
the entire application and the original bytes outside its erase sectors.
The original pairing and 30-minute idle setting also matched after boot.

Before changing the clock, RTC and system UTC both read 13:12:57. The PC
read 13:12:59. With the Note on Home, the bench then set the test clock to
30 November at 23:59:25 UTC. The first press crossed midnight while the
same WAV stayed open. A second press followed in December.

| Archived ID | Capture UTC from SD metadata | Folder | Frames at 16 kHz | Duration | PCM CRC32 |
| --- | --- | --- | ---: | ---: | --- |
| 1073764435 | 2026-11-30 23:59:53 | 202611 | 442,624 | 27.664 s | 6cf8014f |
| 1073765460 | 2026-12-01 00:00:37 | 202612 | 189,184 | 11.824 s | 33a9294e |

Both files were opened directly in the PC card reader after restoring
real UTC. Their decoded PCM lengths and CRCs matched their respective
microphone traces, with zero reported overflow. The previous trial WAV
1073763411 still matched its original SHA-256. On restore, the RTC, system
clock and host all read 26 November at 13:16:39 UTC.

This covers direct recording on an idle dev Note across a UTC month
boundary and archive readback after clock correction. The startup inbox
has a separate test below.
Raw trace and readback results are under
`pala-note-stage2-qualification-20261126/month-20261126T130833Z/trial-131510`.

## Month rollover through the startup inbox

The same dev Note and 2 GB FAT32 card recorded ID 1073768531 through
`/.rec-inbox` after a software restart. Home finished drawing at 2,615 ms
and setup completed at 2,623 ms. The REC edge arrived at 3,691 ms uptime,
1,068 ms after setup. This run exercised the startup inbox after Home had
finished drawing. The earlier unavailable-RTC run exercised REC during
the Home redraw.

The capture metadata reported 30 November 2026 at 23:59:47 UTC. The
32.896-second WAV crossed midnight and was published under
`/recordings/202611/note_1073768531.wav`. After the real clock was
restored, the bench downloaded the entire file over authenticated USB
and validated the 16 kHz mono PCM header. The microphone, writer and
download each yielded 526,336 frames. The microphone and downloaded PCM
had CRC32 `8082ffc4`, with zero reported buffer overflows. The WAV
SHA-256 was `dca1cbb6d7457ce57d2c1c0274ce251472a221bdbb4c2b3f973aa15a057032ed`.
The original settings were verified after the run.

The first microphone block was ready 359 ms after REC. The first PCM
write finished 6,246 ms after REC without an injected stall. The logged
seek took 1,572 ms, header fsync 280 ms and first PCM write 1,703 ms.
The operator reported that the recording indicator appeared late. The
inbox writer buffers audio independently of the foreground display path.
This trace has no timestamps for the REC screen request or panel refresh
completion. Both timestamps are needed to quantify that delay.

Evidence is in
`acceptance-20261126T145648Z/operator-month-161513/evidence.jsonl`
under the Stage 2 qualification build directory.

## REC display timing on the startup inbox path

Build `2.0.0-recorder-stage2-qual2` uses the same recording and display
sequence as qual1. It buffers timestamps in RAM and emits one
`[RecDisplay]` line after the recording and display tasks have joined.
The line and microphone trace must have the same REC edge.

All six fields are milliseconds from the physical REC press:

| Field | Event |
| --- | --- |
| request | The foreground requests the initial REC screen. |
| draw | The UI task starts drawing the REC frame. |
| refresh | The completed frame is handed to the refresh routine. |
| wave | The driver is about to issue the image activation command (0x20). |
| wait_end | The driver returns from waiting on the panel BUSY pin. |
| done | The initial REC screen routine has returned. |

Only `status=idle` confirms the BUSY pin was observed high and then low.
`timeout` records the end of an unsuccessful wait. `no_busy` means an
active BUSY signal was never sampled. `missing` denotes an incomplete
measurement. `full` and `partial` identify the display waveform.
Controller completion is measured at the driver's polling resolution.

Repeat the lower-button November-to-December test on the populated 2 GB
card. Compare the display fields, first microphone block and first PCM
write for that edge. Preserve the WAV frame count, PCM CRC and capture
UTC read back from the card.

The dev Note's full-flash readback verified application SHA-256
`117a93977b8218fbbe16438735a334a513dad677a9bf2b2b649ded7e318623de`. The verified backup and test evidence are under
`display-qual2-162955` in the Stage 2 build directory.

## Display overlap with a forced 6.5 s first write

Use the `pala_note_recorder_display_stall6500` profile on the development
Note and the identified 2 GB card. The normal recorder profile has the
same display pipeline and no forced SD delay. This trial uses the actual
RTC time.

1. Verify the application and the full flash backup, the paired device and
   the existing reference recording on the card. Check the qualification
   build identifies itself as `2.0.0-recorder-display3-stall6500`.
2. Arm the single-port USB reader before touching the buttons. The arming
   check requires a completed full Home refresh, an observed BUSY pulse,
   the prepared REC image and the configured 6,500 ms delay. Record this
   boot identity. The operator starts the sequence from Home at will.
3. Tap the lower button, wait for the restart question, then briefly press
   and release the upper button to confirm. At the **first visible flash**
   of the new Home refresh, immediately hold upper REC for 10–12 seconds
   and release it. Silence is sufficient for this data-integrity test.
4. Read the retained measurement over paired USB after release. It survives
   a host disconnect during the same boot. Read it before another take or
   reset. The existing hardware bench also downloads the complete WAV and
   independently computes its PCM CRC and frame count.

For a direct diagnostic read with the lab Bridge pairing state:

~~~powershell
py -B devices/kubo-note/tools/stage2_bench.py display-trace --port COM25 --state $pairedState
~~~

Set `$pairedState` to the enrolled lab Bridge state directory. This read
returns the first Home screen, the latest capture and the initial WAV-write
stages. Values are milliseconds of device uptime except for counts, IDs,
flags and CRC. A zero timestamp denotes a missing event. Compare timestamps
only within a consistent boot and revision.

Check the physical REC edge inside Home BUSY, and first PCM and REC image
ready in RAM **before Home BUSY releases**. Check that the old frame's
transaction finished before REC upload, that the first PCM write followed
a measured wait of at least 6,500 ms, and that physical activation of REC
occurred during that wait. If activation was even earlier, report that
ordering separately. Zero overflow and exact frame count and CRC against
the stored WAV complete the test. An injected sleep gives CPU time back to
other tasks. A natural FAT stall is a separate scheduling case.

On the first boot after installation, the dev Note prepared the REC image
at 441 ms, requested Home at 499 ms and finished setup at 515 ms. The full
Home waveform held BUSY from 636 to 2,391 ms (1,755 ms). Its controller
transaction completed at 2,603 ms. These times describe the initial boot
before the operator trial. The 8 MiB flash readback matched the installed
application and all bytes outside its erase range. The receipt, initial
trace and operator trial share the build folder
`pala-note-stage2-qualification-20261126/display3-20261126191226`.

### Measured display3 recordings on the original 2 GB fixture

Both recordings used the software-restart inbox and the injected 6,500 ms
wait. Milliseconds in this table are measured from the upper REC press.

| Note | REC edge vs Home BUSY release | First PCM and REC image ready | REC activation | REC BUSY release | First WAV write | Mono samples |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1073770579 | 115 ms after release | 359 | 361 | 933 | 11,399 | 148,480 |
| 1073771603 | 219 ms before release | 358 | 432 | 1,005 | 11,741 | 66,048 |

The full WAV files read back from the card matched their respective microphone
CRCs, `5bc6259d` for the first recording and `4dd071e2` for the second.
The second press landed inside the physical waveform. Its first PCM and
ready image came 139 ms after BUSY released and 73 ms before Home's full
controller transaction finished. On both takes REC activated before the
injected delay began. Neither take demonstrates an image ready before BUSY
released. Neither measured button release or the visible end of REC.
The earlier restart confirmation has no pre-reset timing on display3.

### Measuring release on periph1

The dev Note verified application SHA-256
`f926e4ec8ccaa201f10a0bf72608676cfe7a07b8766db641c08f0803085eab12` in
a complete 8 MiB readback. The same card, original settings and 30-minute
sleep override were checked after boot. One pre-arm settings read returned
a background battery reading of 92 percent. The run is stored under
`peripherals1-20261126T2037Z/home-run-20261126T210846Z` in the qualification
build folder.

On the same card and qualification profile, start a fresh software reboot
from the local restart question. Press upper REC at the earliest inversion of
Home. Hold for about six seconds, then release. The two display3 traces
entered the injected wait 4,767 and 5,109 ms after REC. This hold aims to
land inside the 6,500 ms wait even if the faster initial header moves the
start earlier. Confirm the overlap from device timestamps and check that
Saving or the eventual committed result replaces REC.
An ordinary take can also measure release without the artificial wait.

Use the authenticated `display-trace` command. The additional `release`
page reports the matched GPIO release and the microphone stop, queuing and
preparation of Saving, its waveform and BUSY, admission to the numbered
catalog, and the Saved request and panel acknowledgement. A
superseded pending Saving frame has its own timestamp. Compare physical
button to panel activation and BUSY release. A missing release timestamp or
a missing BUSY acknowledgement is inconclusive. The `audio` page reports DMA
loss, read interruptions and ring high water. Compare those results with the
independent PCM count and CRC from the full WAV. All times from the
`[RestartPrompt]` record belong to the boot that handled the question.

### Measured Home, release and WAV on periph1

At 21:27 UTC on 26 November, note `1073772627` used the startup inbox on the
2 GB card. The trace boot ID was `616511740`, revision 50. The timestamps
below use the ESP32 monotonic clock. Panel completion is the observed BUSY
falling after the controller acknowledged the waveform.

The physical press was 1,590 ms after restart, inside the Home BUSY interval
of 568 to 2,323 ms. The hold was accepted 323 ms after the press. First PCM
and the REC frame in RAM followed at 358 ms, with Home still BUSY for 375 ms.
REC activated at 946 ms after the press and finished at 1,519 ms.

| Event | Milliseconds after physical release |
| --- | ---: |
| Microphone stopped and Saving was queued | 9 |
| Saving pixels ready | 10 |
| Partial waveform activated and BUSY asserted | 11 |
| Saving complete according to BUSY | 584 |
| First PCM write returned | 1,504 |
| Saved requested | 2,633 |
| Saved complete according to BUSY | 3,210 |
| Numbered catalog entry published | 27,860 |

The operator held REC for 9,184 ms. The injected wait ran from 5,645 to
12,145 ms of uptime. Both release and the entire Saving waveform landed
inside this exact 6,500 ms interval. REC was already on the panel when this
wait began. The original qualification flag requiring initial REC activation
inside the wait therefore stays false.

The complete WAV retrieved over paired USB contained 141,824 mono samples
at 16 kHz (8.864 seconds). Its PCM CRC32 `67dba80a` matched the microphone.
The driver reported zero DMA overruns, zero lost bytes and zero read errors.
The application ring had zero overflows and peaked at 553 of 1,024 blocks.

The original PC evaluator flagged the catalog timestamp as inconsistent.
The firmware can acknowledge a finished, renamed startup-inbox WAV before
catalog replay publishes the numbered entry. That rename has no separate
timestamp in this build. Replaying the retained trace gives Saving as the
first acknowledged screen after release at 584 ms. The original result and
USB evidence remain in the run folder alongside `verified-result-replay.json`.

### Tracing exclusive WAV creation and the first header write

The preceding periph1 trace spent 1,544 ms in the open stage and 1,566 ms
in the header stage. Those intervals were measured between adjacent recorder
markers. They did not identify a lock or an SD transaction.

The development profile `pala_note_recorder_storage_stall6500` keeps the
startup gesture, audio, display timeline and 6,500 ms first-PCM stall. Its
firmware version is `2.0.0-recorder-sdtrace1-stall6500`. Start from Home
with the lower button, confirm the restart with a short upper press, then
hold upper REC for about six seconds at the first Home flash and release it.

After capture, the authenticated `display-trace` reader retrieves three
additional pages for the same boot, revision and physical press.

- `io_open` covers exclusive creation, stream binding and configuration.
- `io_header` covers the initial unbuffered 44-byte fwrite. The subsequent
  fflush, fsync, forced wait and header checkpoints have separate boundaries.
- `io_calls` gives consecutive times for path construction, POSIX open,
  fdopen, setvbuf and descriptor cleanup on failure. It also reports open
  and header outcomes. Compare the subcalls against the total open time.
  Adding them to the lock or controller buckets would count time twice.

Each phase reports wall time in microseconds and its exclusive breakdown.
Inspect measured contention on the FatFS volume, VFS/newlib locks and the
SDMMC host mutex. The SD event timer starts when FreeRTOS places the WAV
writer on the controller's receive wait list. It ends when the receive
returns to the writer, so it includes wake-up and scheduling time. Immediate
queue reads, driver setup and DAT0 busy polling stay in the remaining
command time. Electrical card activity and internal programming time
require independent signals. Other elapsed time stays unattributed.

Use the original storage stage markers to spot delays outside the narrow
call scopes. Report a slow phase only when observed in that take. A fast
open or header cannot identify the cause of the earlier 1.5-second interval.
Retain the trace before any maintenance reset, then match the independent
card WAV sample count and PCM CRC to the microphone.

The native harness checked contended and immediate locks, positive-time
event receives with and without blocking, foreign tasks and interrupts.
The final ESP32-S3 ELF passed 19 call-site checks including the Arduino
SD_MMC host callback and both FreeRTOS wait paths. The regular recorder
build contains none of the ten diagnostic wrappers. All 325 Note/Bridge
host tests passed.

The development Note booted the 2,587,440-byte application with SHA-256
`4eb8fe99f3f077b7037c74efc6facd257b2796fac8430230da0bb66e936a69e2`.
Full 8 MiB readback exactly matched the application overlaid on the latest
verified backup. The card matched reference recording 1073750084.
The passive USB logger armed in `sdtrace1-20261126/home-run-20261126T225018Z`
with all three new pages at zero, boot 3353886863 and revision 12.

### Two physical recordings on the same 2 GB card

The software restart generated boot 2742465014. The panel's Home waveform
was BUSY from 569 through 2,324 ms. The Home operation returned at 2,536 ms.
The first REC edge landed at 2,361 ms, 37 ms after BUSY cleared and 175 ms
before Home returned.

| Recording | REC uptime | Injected first PCM wait | Microphone and card frames | Microphone and card CRC32 | REC to first PCM write |
| --- | ---: | ---: | ---: | --- | ---: |
| 1073773651 | 2,361 ms | 6,500 ms | 102,656 (6.416 s) | `3cc133ab` | 11,368 ms |
| 1073774675 | 22,414 ms | 0 ms | 71,936 (4.496 s) | `3c09522f` | 4,688 ms |

The startup trace records 1,576 ms for the initial header stage. From the
end of the WAV existence check to the marker preceding the header write,
1,549 ms elapsed. This interval includes exclusive creation and intervening
work. The second press replaced the first press's per-call RAM counters
before USB read them. An exact exclusive-open time or a lock breakdown for
the first recording cannot be recovered from this trace.

The second recording retained both per-call measurements at revision 88.

| Operation | Total | Waiting for VFS/newlib mutex | Blocked on SDMMC event | Other SDMMC command time |
| --- | ---: | ---: | ---: | ---: |
| Create WAV and attach stream | 1,550.691 ms | 1,547.760 ms (99.81%) | 1.419 ms | 0.220 ms |
| Initial 44-byte header write | 1,576.746 ms | 1,575.690 ms (99.93%) | 0.358 ms | 0.153 ms |

Each operation blocked on a VFS/newlib mutex once. The FatFS volume mutex
and the SDMMC host request mutex both recorded zero blocked time. POSIX open
accounted for 1,550.415 ms of the first row. The probe identifies the mutex
class. Identifying the holder would require a per-mutex task trace.

Both full WAVs were read independently from the card. Match each file to its
own REC edge and CRC using `two-take-reconciliation.json`. The retained
`verified-result.json` and `retained-trace.json` refer to the second take.

## VFS mutex owner on the first two recordings

Use `pala_note_recorder_vfsowner_stall6500` on the development Note with
the same 2 GB card. The runtime version is
`2.0.0-recorder-vfsowner2-stall6500`. Start a passive USB capture with
an authenticated boot baseline before pressing either button.

1. From Home, tap the lower button. Confirm the restart with a short upper
   press and release.
2. At the first Home flash, hold upper REC for about six seconds, then release.
   The software-restart bench holds the first PCM write for 6,500 ms.
3. When Home returns after saving, repeat the six-second REC press once.
   Both takes belong to the same boot. The second is a control without the
   injected delay. Silence is sufficient for a sample-count and CRC check.

The firmware pins the first two accepted REC edges in RAM until the next
reset. Later takes update a separate latest slot. A delayed inbox adoption
maps its archived WAV ID back to the original take by its spool basename.
Read the history before another restart. Wait for both files to be archived
before using their IDs for an independent WAV and PCM CRC check.

On 27 November the paired dev Note `70041dd816a8` booted this image with
the populated 2 GB card inserted (reference note `1073750084`). The
application at `0x10000` contained 2,597,328 bytes with SHA-256
`e5407a99e088611f149241722e80d30c4e4a7c0885e17f662e267849dcd54513`.
The independent 8 MiB flash readback matched the predicted image byte for
byte. The new runtime verified the two existing WAVs and original settings.
The passive two-take collection started at 00:48:38 CET on COM25 with an
empty retained history on boot `3116488929`, revision 12. The run is
`vfsowner2-20261127/two-takes-20261126T234838Z` under the qualification
build directory.

### Two microphone buffers during a delayed save

Use the same environment and the same enrolled development Note and 2 GB
card for the follow-up build `2.0.0-recorder-overlap1-stall6500`. Arm
the passive collector before the restart. From Home, tap the lower button,
then briefly press and release the upper button to confirm the restart.

1. On the first Home flash, hold upper REC for about two seconds and release.
2. When Saving appears, hold upper REC again for 12–15 seconds. Keep it
   held long enough to see the previous-save disk disappear, then release.
   Silence is sufficient. The 6.5 s injected delay applies to the first take.
3. Read both pinned traces and both archived WAVs before another restart.
   Compare the second edge, hold qualification (320 ms), first PCM and the
   completion of the first save. A valid overlap has actual second PCM
   before completion. Compare the indicator's BUSY-low timestamps separately.
   A coalesced image has no physical visibility evidence.
4. For each WAV, check its own sample count, PCM CRC32, DMA loss counters
   and ring high-water reading. Distinguish a missed Home interval or an
   unobserved indicator from missing audio. The first take's Saved feedback
   is suppressed when a newer recording is already buffered.

The paired Note `70041dd816a8` verified two buffers at boot `3538719581`.
Each reserved 532,480 bytes, including 524,288 bytes of PCM. The device
reported 7,257,400 of 8,388,608 PSRAM bytes free at that measurement.
The 2,602,720-byte image matched an independent full-flash readback. The
baseline and USB capture are in `overlap-rec-20261127/two-takes-20261127T081054Z`.

### Measured lock owner and a second press during Saving, 27 November

The two pinned takes were recovered from the paired firmware at revision 90
of boot `2427359130`. The raw USB banner was unavailable. The boot ID
differed from the armed baseline and both takes reported a software restart.
The retained ELF digest matched the file used to resolve the call sites.

The first button edge was at 1,983 ms in the Home panel BUSY interval
569–2,324 ms. The first microphone block arrived at 2,342 ms, 18 ms after
BUSY ended. The injected stall ran from 7,383 to 13,883 ms. The first PCM
write completed at 14,016 ms.

| First take | Writer total | Held from writer block to matched give | Holder operation | Holder SD host | Included host event |
| --- | ---: | ---: | --- | ---: | ---: |
| WAV open | 1,630.442 ms | 1,627.546 ms | Catalog `f_open` | 1,496.949 ms | 1,266.807 ms |
| Initial header | 1,631.099 ms | 1,630.072 ms | Catalog `f_stat` | 1,495.911 ms | 1,264.564 ms |

Both operations encountered the same FAT mount mutex `0x3fcc9a58`, held by
`loopTask`. The writer resumed 20 and 14 microseconds after the matched
give, respectively. The second WAV opened in 1.884 ms and wrote its header
in 3.881 ms with zero observed VFS mutex blocks and no injected stall.

The second REC edge at 13,515 ms occurred while Saving was visible and
368 ms of the first injected stall remained. The device accepted REC at
15,802 ms and delivered the first PCM at 15,837 ms, 2,322 ms after the
press. The first take's Saved frame completed at 15,436 ms. A matching
WAV CRC covers the samples delivered by the microphone after capture
starts. It cannot verify speech before that point.

| WAV ID | Frames at 16 kHz | PCM CRC32 | Release to microphone stop | Release to Saving BUSY end |
| --- | ---: | --- | ---: | ---: |
| 1073775699 | 99,584 | `e8e4370c` | 13 ms | 588 ms |
| 1073776723 | 114,688 | `6ce10add` | 14 ms | 589 ms |

Independent SD downloads matched both microphone frame counts and CRCs.
DMA overrun, lost-byte and ring overflow counters were zero for both.
The retained pages, complete WAVs and decoded operations are in the run
folder above.

### Read or replay the evidence

Use a single owner of the paired development USB port. Stop its passive
capture before running the standalone reader. Substitute the actual port
and use the Bridge Python environment. The first command reads both takes in
one measurement revision. The second works offline with the matching ELF
and the Xtensa addr2line from the installed toolchain.

~~~powershell
py -B devices/kubo-note/tools/stage2_bench.py owner-trace --port COM25 |
    Set-Content -Encoding utf8 owner-history.json

py -B devices/kubo-note/tools/stage2_bench.py owner-report owner-history.json `
    --elf $firmwareElf --addr2line $xtensaAddr2line
~~~

Read `wav_open` and `header_write` independently for each REC edge. Each
phase retains the two longest VFS/newlib mutex waits plus the total number
of waits, omitted shorter waits and any task-context registry drops.

- The FreeRTOS contention hook records the holder's task handle and name,
  the mutex queue and the writer's lock-acquisition call site.
- A matched kernel give for that same queue and holder bounds the observed
  hold from writer block to give. The rest of the writer's wait is reported
  separately. A missing or ambiguous give leaves the hold durations unknown.
- The holder's firmware work, deepest observed file operation and its caller
  can identify a catalog, free-space or other file operation. Flags show
  which context existed when blocking began and which changed later.
- SD host time includes the controller-event interval. A positive task delay
  can also overlap the host interval. These are wall times. Report unknown
  context and uninstrumented time as such.
- Resolve addresses only with the SHA-256 of the ELF returned by the
  running image. Compare each stored WAV's actual frames and PCM CRC32 to
  that take's microphone counts, CRC and upstream DMA counters.

### Lock sites in the 27 November build

The linked ELF SHA-256 is
`9174f378df1c6ec05c3b3b97c170e079fddd80466e23fba8320fd53c3503f2e8`.
Compare the symbolized waiter location to these call instructions. Use
the queue address and the RTOS holder record from the actual take.

| Caller and call instruction | Lock identified in this ELF |
| --- | --- |
| `_open_r`, `0x42088eea` | Global VFS descriptor table |
| `vfs_fat_open`, `0x421031f9` | The FAT mount context lock at offset 28 |
| `vfs_fat_write`, `0x42103148` | The same mount context lock for that mount |
| `_fdopen_r` at `0x42172b21`, `setvbuf` at `0x421739ea`, `_fwrite_r` at `0x421736c6` | Recursive newlib lock; compare the measured queue to identify the instance |

The firmware exports Xtensa return PCs. The decoder resolves the byte
immediately before each return address, inside the corresponding call.
These addresses describe possible waits. The physical trace identifies
which call and task actually occurred.

## Reboot after UTC correction, 26 November 2026

With both radios off, the bench verified the application flash digest and
reset the dev Note over USB. At 15:14:51 UTC the first authenticated USB
read returned 1795706091 for the RTC, system and PC clocks. Neither NTP nor a USB clock
write supplied the boot time during this trial.

After the restart, the bench downloaded both complete WAV files over USB.
The SHA-256 of each file, the capture UTC from its metadata, the frame
count and the PCM CRC32 matched the earlier independent SD card read.
Several USB sessions had to be retried before both downloads completed.
The original radio and sync settings, peer and network configuration hashes
and 30-minute sleep setting matched after restoration.

Evidence: `acceptance-20261126T145648Z/post-correction-151442/evidence.jsonl`
under the Stage 2 qualification build directory. This verifies the restart
after correction for IDs 1073764435 and 1073765460.

## Unavailable RTC during software restart

The qualification hook skipped the initial RTC read on one software
restart. REC went low 2,348 ms after reset, while Home was drawing. The
first PCM arrived 360 ms after the button edge and the first successful WAV
write followed at 1,042 ms. The microphone and writer both counted 95,744
frames (5.984 s), with zero overflow and PCM CRC32 d54d3ed0.

The startup inbox published `/recordings/000000/note_1073767424.wav`.
After signed clock correction and another hardware reset, an independent
USB download returned the same frame count and CRC, SHA-256
`27f9f227dbfa946459bcf840ecd42d35ec22ae8544f8b30afe0d31f8bbb8c9a0`
and the USB metadata value `-` for unknown capture time. The post-reset
RTC and system clock were each one second behind the PC. Original
settings matched on completion.

Evidence: `acceptance-20261126T145648Z/rtc-operator-152613/evidence.jsonl`.
A separate recording from 15:25:00 UTC was also recovered on the card.
It had a valid date and fell outside the unknown-clock trial.

## Delivered items after reboot and interrupted USB and BLE reads

On two consecutive hardware resets, the active USB work queue omitted
all three previously delivered test IDs (1073753156, 1073754180 and
1073755204). For each ID the next offered item was 1073756228. All three
SD transcripts and their capture times matched the earlier card snapshot
on both boots. This checks device recovery. The earlier Test 4 report
contains the independent Google readback from the original delivery.

In a separate trial the USB reader process exited after receiving 32,768
of 378,412 bytes for December ID 1073765460, without closing its session.
A fresh process downloaded the entire WAV. Both the 32,768-byte prefix
and the full file matched. The recovered file had 189,184 frames and PCM
CRC32 33a9294e. The interruption occurred before transcription.

Both traces are in `acceptance-20261126T145648Z` in the qualification build
directory, under `delivered-reboot-152154` and `usb-worker-exit-152401`.

The same interruption was exercised over paired BLE on the existing
137,772-byte test WAV 1073751108. The first process exited after 16,384
bytes. A second process reconnected without rebooting the Note, received
all 68,864 frames and matched the first process's prefix hash. The full
SHA-256 matched the earlier card read. The capture UTC matched. The BLE recovery finished
about 95 seconds after the first process exited. The isolated run wrote
no transcript or delivery receipt. Evidence is under
`acceptance-20261126T145648Z/ble-worker-exit-153802`.

## Prepare the bench

1. Identify the second Note by USB serial. Verify its full 8 MiB backup
   and the candidate application image before the separate upload. Preserve
   pairing, NVS and the current settings. Verify the application flash by
   independent readback after upload.
2. Start with the populated 1,996,488,704-byte FAT32 NOTES card. Identify it
   by the previously recorded WAV hashes. Use separate disposable media
   for deliberate cuts, full-card tests and collisions. Back up first.
3. Run the commands from the repository root in PowerShell 7, with the
   Bridge Python dependencies available. Store evidence on the PC. Substitute
   the verified Windows drive and port. The device commands check both the
   USB identity and the authenticated build. A second serial reader cannot
   share the port with a USB Bridge worker.
4. Keep automatic clock correction and automatic sync workers out of the
   controlled clock trial. Select a dedicated Google Tasks list and a short,
   unique general task for delivery trials. Use one known note ID per trial.

```powershell
$bench = "devices/kubo-note/tools/stage2_bench.py"
$port = "COM25"
$card = "E:/"
py -B $bench snapshot $card | Set-Content -Encoding utf8 before.json
py -B $bench clock --port $port
```

The first command hashes existing archive WAVs, committed text, moments and
receipts. It records the original capture UTC from metadata while allowing
normal sync fields to change. Keep the baseline and its file count.
The second command reads chip UTC, system UTC and the host comparison time.

## Clock and month boundary

1. Restart, then read the clocks before a Bridge or NTP correction. Record
   the host time before and after the read. Repeat after normal deep sleep.
2. Move the test clock to 23:59:45 UTC. Hold REC shortly before midnight,
   continue across midnight and release. Make another short recording
   after midnight. Record their IDs, capture UTCs and folders. The first
   take belongs to November and the second to December, including after
   reboot. Repeat on the inbox path during startup. The host boundary test
   additionally covers the case where servicing the November press begins
   in December.
3. Use `clock-unknown` for one take. Expect an empty `created_utc` and
   `/recordings/000000/`. It models an unavailable capture clock in RAM.
   Restart clears the override. To exercise real startup recovery, arm
   clock-fail-next-boot and perform a software restart. It skips the initial
   RTC read exactly once. Read CLOCK before any sync, record an unknown-time
   take, restore through the signed USB clock command, then reboot. The RTC
   calendar registers remain available for an independent read. A physically
   stopped RTC oscillator is a separate check.
4. Restore the actual time, restart once more, read both clocks, compare the
   baseline and check that both the November and unknown-time takes stayed
   at their original path and capture UTC. Restore after an interrupted run.

```powershell
py -B $bench set-utc --port $port 2026-11-30T23:59:45Z
py -B $bench clock-unknown --port $port
# Before the separate software-restart trial:
py -B $bench clock-fail-next-boot --port $port
# After making the unknown-time take:
py -B $bench restore --port $port
py -B $bench compare $card before.json
```

## REC latency and sample integrity

Start a fresh trace per boot so repeated uptime values refer to one run.
Use the same timed sound with identifiable first and last syllables.
Keep separate columns for physical wake (when measured), REC edge, first
microphone block, first PCM write, queued blocks, overflow, sample count
and CRC. Take the stage durations from the trace to identify slow FAT calls.

```powershell
py -B $bench trace --port $port --seconds 120 | Tee-Object take.log
# After reading the card, substitute the note ID, expected UTC and REC edge.
py -B $bench take $card $noteId $expectedUtc $edge take.log
```

The take check reads the RIFF header and actual PCM bytes and compares
them with the microphone count and CRC for that edge. Use `unknown` for
the expected UTC of the unknown-clock take. Listen to the saved file and
check both syllables for the acoustic test. An incomplete trace requires
a repeat or independent evidence.

Run on empty and populated media with equal pending counts. For each card,
cover startup during Home drawing, idle, active USB, BLE and Wi-Fi sync,
and reading the archive and playing an older take. Record card model,
capacity and pending count for each row. Ideally use the same card model.
For USB, run the single-port worker below. It reads the serial trace
while processing the specified recording. One invocation can transcribe,
save text and deliver to the configured provider. Use the dedicated test
list. Keep the same log on retry and record the stage_completed result. The standalone
trace command suits BLE, Wi-Fi and recording-only runs.

```powershell
py -B $bench usb-step --port $port --note $noteId | Tee-Object usb-trial.log -Append
```

## Delivery, interruption and the second Bridge

- First establish a working paired Wi-Fi Bridge. Record the actual
  transcription request and the recorder plan and apply requests. The Note
  emits `[BenchLink] link=wifi` with the note ID after transcription and
  at delivery. Compare SD text with the received transcription. A first
  successful delivery has a matching receipt and a unique provider task.
- For each of USB, BLE and Wi-Fi: start A, press REC for B during active
  processing, then allow delivery to resume. Count recognizer calls per ID.
  Exercise disconnect/worker exit before transcript storage, after storage
  before its reply, after Google success and after delivery confirmation.
  Verify both IDs finish once, then run the same worker again and repeat the
  provider and card reads. A previously committed transcript is reused.
- For a deterministic lost completion, arm `lose-receipt` on an otherwise
  idle dev Note with a single pending test item. It consumes one completed
  delivery attempt before the Note persists its receipt. Stop A and resume
  on separately paired B using independently initialized state. Match
  the same account and destination. A had the write grant. B must only
  reconcile. Keep A offline until B's trace and readback are complete.
- Repeat with overlapping peers, a changed Google account, changed list,
  and unavailable or ambiguous provider evidence. The conflicting item
  stays pending. A subsequent identifiable item must still progress.
  An already delivered ID must survive two reboots without another grant.
- For a short, ordinary one-task recording, the command below scans the
  specified provider list for its stable action marker, requires uniqueness
  across all pages, re-reads the task and checks the expected title.
  Obtain the approved account's default Tasks list ID before starting.
  Archive the SD receipt, queue state and independent provider result
  before and after retry. For pending work, a copy of its SD claim can be
  used to look for an existing provider write.

```powershell
py -B $bench arm --port $port lose-receipt --note $noteId
py -B $bench provider $receipt --account $accountId --list-id $listId --title $expectedTitle
```

## Power interruption and storage faults

Arm one point, start the appropriate operation, and cut actual board power
only after the matching `[BenchCut]` marker. Each point opens a ten-second
window once. For text, receipt and lose-receipt, pass --note with the intended ID. Check the marker ID or pre-allocation REC edge before cutting. If it expires, mark the run missed. A USB disconnection on a
battery-powered board is a link interruption. Use a separately powered
serial logger or the single-owner HIL collector to correlate the cut.

| Argument to arm | Cut boundary |
| --- | --- |
| open | Exclusive WAV creation succeeded, before its header |
| pcm | First batch of PCM was written through stdio, before the periodic checkpoint |
| publish | Direct WAV closed or inbox WAV renamed, before publishing the note |
| text | Transcript bytes committed, before refreshing the work index |
| receipt | Provider reported success, before the Note saves the delivery receipt |

```powershell
py -B $bench arm --port $port pcm
py -B $bench trace --port $port --seconds 90
# Cancel a point before it is reached.
py -B $bench arm --port $port off
```

After each cut inspect the archive, inbox, `.pala-work`, capture UTC and
saved transcript. An empty newly created file may contain no recoverable
audio. For interrupted PCM, check the recovered header against the actual
readable complete frames. Compare all pre-existing archive hashes. Repeat
recovery and provider readback after a second reboot. Record any changes.

Use a disposable FAT32 copy for deliberate storage faults. Fill only its
free space with a separately named test file, verify available bytes on
the Note and exercise REC below the low-space threshold. To test collision,
place known bytes at the predicted next WAV name on the copy, retain its
hash and trigger allocation. Verify exclusive creation preserved the
sentinel and recovery kept other jobs. Repeat on a copy containing a
completed receipt. Never format the populated 2 GB reference card.

Record each run with firmware SHA-256, device serial, card identity, boot
reason, transport, Bridge identities, note IDs and evidence paths. Only
observed hardware rows may close the corresponding Stage 2 checklist.

### Low-space rejection and USB mass-storage handoff, 27 November 2026

The development Note (70041dd816a8) received one REC hold 8.716 s after
boot on the 2 GB FAT32 NOTES fixture. Windows measured 8 MiB free against
the recorder's 10 MiB reserve. The overlap1 recorder accepted 512 microphone
samples into RAM and stopped before opening a WAV. Its queue stayed at 37.
The retained trace did not include the error text shown on the display.

The paired USB service on build 2.0.0-recorder-msc1 then exposed the same
inserted card to Windows. SHA-256 matched the pre-trial backup for all
20,107 files in notes, recordings, .rec-inbox and .pala-work, including
4,041 WAVs and 37 queued jobs. Directory names and file sets also matched.
Deleting the fixture filler restored exactly 1,537,937,408 free bytes.

After Windows flushed, dismounted and ejected the volume, the Note returned
to recorder mode on COM25. A fresh paired session confirmed a different boot
ID, the installed ELF, original settings, fixture identity and 37 work items.
This run exercised immediate entry and safe eject.

Evidence under `build/pala-note-stage2-qualification-20261126/`:

- `full-card-20261127T1030Z/early-rec.json`
- `full-card-20261127T1030Z/msc-check-20261127T111053505124Z/verified-archive.json`
- `full-card-20261127T1030Z/msc-check-20261127T111053505124Z/cleanup.json`
- `usb-msc-dev/installed.json`
- `usb-msc-dev/status-20261127T111613953250Z.json`

### Cold USB service boot before recorder recovery, 27 November 2026

The paired development Note acknowledged `usb-storage-next` on recorder boot
396254000. With the battery disconnected and the 2 GB card still in the
Note, the operator removed and reinserted USB power. Windows observed the
device disappear, its startup serial/JTAG interface appear briefly, and the
MSC interface appear 3.259 s after the first returning USB interface.
The verified ELF branches to the dedicated USB service before the recorder's
FAT mount. Windows confirmed the same USB parent and FAT32 volume.

Read-only SHA-256 comparison matched all 20,107 archive and queue files,
including 4,041 WAVs and 37 jobs. All nine directories matched. After safe
eject, the paired recorder returned on COM25 with boot ID 1698815808,
the same runtime ELF, settings, fixture identity and 37 jobs.

Evidence in `usb-msc-dev/cold-entry-20261127/` includes `cycle.jsonl`,
`firmware-boot-path.json`, `card.json` and `result.json`.

### WAV filename collision on the 2 GB fixture, 27 November 2026

On development firmware 2.0.0-recorder-msc1, the live allocation ledger
predicted ordinal 43. With no valid RTC time, the predicted path was
`recordings/000000/note_1073785856.wav`. A synthetic WAV was placed there
over USB Mass Storage. Its SHA-256 and all 20,107 baseline archive and
queue files matched after the two REC attempts.

USB logged EEXIST (17) and RECORD AGAIN on the first hold. It wrote zero
samples. The second hold saved ordinal 44 with ID 1073786880. The WAV's
207,616 PCM samples and CRC32 6C0CC95E matched the retained microphone
trace. The recorder required a second hold to save. After removal of the
synthetic fixture and safe eject, the paired recorder returned on COM25
with 38 queued items, including the new recording.

Evidence in `usb-msc-dev/wav-collision-20261127/` includes `events.jsonl`,
`rec-trace.json`, `verified.json` and `recorder-returned.json`.

### Same-hold WAV collision retry on the 2 GB card, 27 November 2026

On development firmware 2.0.0-recorder-msc2, one physical REC hold
encountered the seeded WAV at ID 1073787904 and saved to the next free
ID, 1073788928. Raw USB showed the same button edge and first PCM time
for both filenames. The serial collector disconnected before completion.
After reconnecting, the authenticated trace on the same boot showed one
capture, one collision, zero DMA overruns and zero capture-buffer overflows.

The verified 16 kHz mono WAV contained 210,176 samples (13.136 s).
Its PCM CRC32, 00A76A50, matched the microphone. The occupied WAV matched
its original bytes, and all 20,110 baseline archive and queue files matched
their SHA-256 reference. The only additions beyond the synthetic WAV were
the new WAV and its single job. The job contained its own ID and empty UTC.
This direct recording without a valid clock produced no metadata sidecar.

The host removed the verified synthetic WAV and safely ejected the card.
The paired recorder returned on COM25 with 39 queued items, up from 38.

Evidence in `usb-msc-retry-dev/trial/` includes `events.jsonl`,
`rec-trace.json`, `verified.json`, `fixture-removed.json` and
`recorder-returned.json`.

### REC between USB WAV downloads, 27 November 2026

On the populated 2 GB card, the USB reader finished receiving the last
44 bytes of WAV 1073788928 at 13:10:33.820 UTC. The host then tried to
select the WAV again. The next diagnostic lines came from the recorder.
The button edge maps to about 13:10:39.182 UTC using the host receipt of
the first-write line and the firmware's measured edge-to-write interval.
The last confirmed payload preceded the edge by approximately 5.36 s.
This records capture during a USB session. Overlap with an active WAV
data block remains open.

On the same boot the hold was accepted at +323 ms, first microphone PCM
arrived at +358 ms, the header write returned at +697 ms, header fsync
at +1238 ms and the first PCM write at +1370 ms. One inbox recording was
archived as 1073789952. Independent USB readback matched all 123,904
microphone samples (7.744 s at 16 kHz) and PCM CRC32 6AF0AED1. DMA
overruns, capture-buffer overflows and microphone read errors were zero.
The source WAV matched its previous SHA-256, and the original device
settings were read back after restoration.

Evidence in `usb-msc-retry-dev/rec-during-usb-read/` includes
`events.jsonl`, `rec-trace.json`, `verified.json` and
`settings-restored.json`.

### REC during continuous USB WAV reads, 27 November 2026

On the same populated 2 GB card and development build
2.0.0-recorder-msc2, the host selected a temporary 24,000,044-byte WAV
and issued consecutive 512-byte reads in one paired session. The log
recorded 5,028 complete blocks before the next READ stopped receiving
WAV data. Across the approximately 1.3 s around the estimated button
edge, 305 blocks completed and the largest host gap between successive
blocks was 0.116 ms. There was no file re-selection in this interval.

The estimated edge falls in the request and response interval at
offset 2,536,448. This estimate derives from host receipt of the
first-write line and the firmware's edge-to-write interval. Serial
transport and millisecond firmware timestamps leave uncertainty about
the exact block or byte at the edge. Payload continued until about
321 ms after the estimated edge, near hold acceptance at +322 ms.

First microphone PCM arrived at +357 ms. The first WAV header write
returned at +672 ms, header fsync at +1,205 ms and the first PCM write
at +1,337 ms. The delay hook did not run. One new WAV, 1073790976, was
read directly from the card via USB Mass Storage. Its 169,216 samples
(10.576 s) and PCM CRC32 9BD7E14C matched the microphone trace.
No DMA overruns, audio-buffer overflows or microphone read errors
were recorded. The previously recorded WAV matched its prior SHA-256.

Evidence in `usb-msc-retry-dev/rec-during-long-usb-read/` includes
`blocks.jsonl`, `rec-trace.json`, `verified.json` and
`fixture-removed.json`.

### BLE WAV read and REC on battery power, 27 November 2026

With USB disconnected, the paired host read 387 native BLE audio blocks
of 4,096 bytes from a temporary 24,000,044-byte WAV on the same 2 GB card.
The last complete block arrived at 13:41:20.191 UTC, after 1,585,152 bytes.
The next request timed out after 8.015 s. The operator recorded once
during the trial window. The host logs have no common clock with the
button edge, and the retained trace came from a different boot ID than
the USB setup checkpoint. The precise overlap with a BLE data block is
unverified.

The retained trace put hold acceptance at +323 ms and first microphone
PCM at +358 ms. The WAV header write returned at +673 ms, header fsync
at +1,080 ms and the first PCM write at +1,211 ms. The delay hook did
not run. The WAV for ID 1073792000 read from the card contained 192,000
samples at 16 kHz (12.000 s) and CRC32 7D3E6CD2, matching the microphone
trace. DMA and capture-buffer overflows and microphone read errors were
zero. The synthetic source and its job were removed after verification.

Evidence in `usb-msc-retry-dev/rec-during-long-ble-read/` includes
`blocks.jsonl`, `rec-trace.json`, `verified.json` and
`fixture-removed.json`.

### BLE and REC on the same firmware clock, 27 November 2026

Build 2.0.0-recorder-msc3 retains the last 32 BLE audio block requests and
the first and last notifications accepted by the BLE stack. These and
the REC GPIO edge use the same millisecond clock. On the battery-only
trial the PC received 44 complete 4,096-byte blocks. The next request at
offset 180,224 timed out on the PC. The last notification for that block
was accepted by the BLE stack 20,141 ms before the REC edge. The trace
and the host offset agree. This run did not exercise REC during a block.

First PCM arrived at +361 ms, the WAV header write returned at +678 ms,
header fsync at +1,214 ms and first PCM write at +1,346 ms. The WAV read
from the card for ID 1073793024 contained 146,944 microphone samples
(9.184 s) with CRC32 6457A918. No DMA overruns, capture-buffer overflows or
microphone read errors were recorded. The synthetic WAV and its job were
removed and the card was safely ejected.

Evidence in `usb-msc-retry-dev/ble-clock-dev/` includes
`retry-live/blocks.jsonl`, `ble-clock-trace.json`, `rec-trace.json`
and `verified.json`.

### REC during a sustained BLE session, 27 November 2026

The host read 1,828 complete 4,096-byte blocks from a temporary 24 MB
WAV on the same 2 GB card. The host received all 17 fragments of
block 45. The run used development build 2.0.0-recorder-msc3.

On the same Note clock, the last notification for block 1,826 was
accepted by the BLE stack 27 ms before REC. Request 1,827 was processed
79 ms after the edge, and request 1,828 at +197 ms. Hold acceptance
followed at +327 ms. The edge was between the measured BLE stack
submissions within the active session. The trace measures submission to
the BLE stack. The exact radio transmission time is unavailable.

First PCM arrived at +362 ms. The header write completed at +680 ms,
the header fsync at +1,224 ms and the first PCM write at +1,359 ms.
The WAV for ID 1073794131 contained 175,104 samples (10.944 s at
16 kHz), matching the microphone CRC32 2403A751. The recorder observed
zero DMA overruns, capture-buffer overflows and microphone read errors.

Evidence in `usb-msc-retry-dev/ble-clock-repeat-dev/` includes
`blocks.jsonl`, `ble-clock-trace.json`, `rec-trace.json` and
`verified.json`.

### REC within a paced BLE block, 27 November 2026

Development build 2.0.0-recorder-msc4-ble-paced spaces BLE notifications
by at least 180 ms without blocking the recorder loop. With USB detached,
the PC received eight 4,096-byte blocks from the temporary 24 MB WAV on
the same 2 GB card. Complete blocks took approximately three seconds.

The same Note clock placed the first and last accepted BLE notifications
of block 9 at -472 ms and +253 ms relative to the REC edge. The host
received five of its 17 fragments. The block did not complete. Hold
acceptance followed at +325 ms, first microphone PCM at +360 ms, WAV
header write completion at +675 ms, header fsync at +1,211 ms and first
PCM write completion at +1,346 ms.

WAV 1073795155 read directly from the card contained 335,616 samples
(20.976 s at 16 kHz) and CRC32 F30363EA, matching the microphone exactly.
DMA and capture-buffer overflows and microphone read errors were zero.
The temporary WAV and test job were removed after verification.

Evidence in `usb-msc-retry-dev/ble-clock-paced-dev/` includes
`blocks.jsonl`, `ble-clock-trace.json`, `rec-trace.json`
and `verified.json`.
