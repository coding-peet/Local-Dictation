# Recorder Test 4 bench results, 26 November 2026

Deployment on the main Note is blocked. These measurements come from the
second Note with its separate FAT32 card. Two firmware versions were tested.

## Original Test 4

Three USB recordings reached a dedicated Google Tasks list. A fresh provider
read found exactly three distinct tasks. Recording C interrupted recognition
of A. Bridge reused the result for A after the USB session reopened and
reconciled its delivery after a deliberately withheld acknowledgment.

A separate wake recording contained the entire spoken phrase. The first PCM
block arrived 355 ms after REC, and the first SD write took 1,029 ms.
The Note dated that recording 28 August 2025. The test took place on
26 November 2026 in Bratislava.

## Test 4 with the USB clock fix

Previously USB did not set the hardware clock, and startup accepted its
stale date. The updated Bridge sends UTC during paired USB sync. The Note
checks the hardware clock before confirming the new time.

| Readback | Hardware RTC (UTC) | PC (UTC) |
| --- | --- | --- |
| Before first sync | 2025-08-28 23:22:50 | 2026-11-25 23:51:27 |
| After paired USB sync | 2026-11-25 23:53:16 | 2026-11-25 23:53:15.795 |
| After deep sleep | 2026-11-25 23:57:24 | 2026-11-25 23:57:24.198 |

The last read was taken before a further clock sync. At that point it was
already 26 November locally.

| Wake capture | REC to first PCM | REC to first SD write | PCM bytes reported |
| --- | ---: | ---: | ---: |
| First | 356 ms | 5,612 ms | 66,048 |
| Second | 353 ms | 6,500 ms | 66,048 |

The second capture overflowed the 2.048-second microphone buffer. The
timings include path preparation, WAV creation, header sync and the first
data write. The individual operation times are still unknown.

## Read-only inspection of the test card

The known SHA-256 of `note_1073756228.wav` identified the FAT32 NOTES card.
Both new WAVs were in `/recordings/202611`, and `/.rec-inbox` was empty.
Each sidecar's `created_utc` matched the time encoded in its spool name.
Bratislava local time was one hour ahead of UTC on this date.

| Archived WAV | Capture UTC on 25 November | Readable frames | Audio duration |
| --- | --- | ---: | ---: |
| note_1073757267.wav | 23:55:22 | 33,024 | 2.064 s |
| note_1073758291.wav | 23:55:33 | 33,024 | 2.064 s |

Both RIFF and data sizes agreed with each 66,092-byte file. Decoding
returned 66,048 bytes of mono PCM16 at 16 kHz per file. The preserved
length equals the initial 256-frame block plus all 128 buffered blocks.
Only the second capture logged overflow. Whether the first contains the
complete utterance remains unverified.

Tested clock firmware SHA-256:
`7fcf0596caebdf047a6f5e86c1240e094692b99dd189a8a9836b2373d7b056b3`.

## First-write timing trial on the development Note

Use the same 2 GB FAT32 NOTES card as the failing trial. Windows reported
1,996,488,704 bytes. Media age is a test condition. Retain the populated
card for the comparison.

Build the `pala_note_recorder_timing` PlatformIO environment. Its firmware
version is `2.0.0-recorder-test4-timing`. The USB listener selects the
development unit by serial number `70:04:1D:D8:16:A8`. Keep USB attached
and hold the upper REC button for eight seconds, counting aloud. Release,
wait for Home, then repeat. The device-specific sleep setting is 30 minutes.

The firmware stores the timestamps in RAM and prints the `[RecTiming]`
lines when the microphone session has ended. Each `ms` is elapsed wall
time since the preceding mark. The last `elapsed` must equal the original
`write_latency_ms`. For early returns, retain the last completed stage
and the existing recorder error. An incomplete trace cannot identify the
slowest operation with confidence.

- `pcm` ends at the producer's first block. `dequeue` ends when the writer
  has received it.
- Priority capture measures storage readiness and each check for an
  existing spool file. Direct capture measures inbox recovery, ID
  allocation, monthly directory preparation and work reservation.
- Both routes measure the original serial output, exclusive open, header
  positioning and write, flush, fsync and the first 512-byte PCM write.

Compare trials that used the same capture route. A long FAT call can
include a wait for another task holding the filesystem. The elapsed time
alone does not distinguish that wait from access to the card itself.

The timing2 build also aligns the two confirmation choices with the buttons
on the right of the case. The upper choice is centered at approximately
row 143 of the 200-pixel display, level with the upper button. The lower
choice sits at the bottom right because the lower button is below the
active panel. The original English restart labels exceeded the available
row width by 36 pixels. The geometry follows the manufacturer's
[enclosure drawing](https://docs.waveshare.com/ESP32-S3-ePaper-1.54#dimensions).

The development Note's app0 and protected ranges verified after flashing
timing2. The paired settings read after reboot returned 30 minutes.
Timing2 firmware SHA-256:
`2cee20213cf3f62b9125751ccae104f95ee385ae20b3366e57521c9753ab188a`.

### Timing from Home

The direct capture `note_1073759315.wav` reached the first write in
1,250 ms. PCM arrived at 356 ms. The next 894 ms comprised 598 ms for
the pending work record, 278 ms for the header fsync, 6 ms for exclusive
open, 3 ms for the first PCM write and 9 ms for the remaining operations.
The recorder reported 110,592 PCM bytes. All 14 timing intervals sum to
the independently logged REC-to-write interval.

The two slow overnight captures used the priority inbox. That route
defers work reservation until after capture, so the 598 ms measured here
cannot explain their 5.6–6.5 s latency.

The next USB listener waited 15 minutes for a ready-state recording to
trigger a fresh restart prompt. It closed without a REC trace. The detailed
first-write timing for the boot inbox is still outstanding.

### Timing3 boot-inbox trial

Firmware `2.0.0-recorder-test4-timing3` was written to app0 on the development
Note. SHA-256 `1f42e93b615dac543c94c803ef764480180f3ff01c24b8dcc9f6df83fa0c1782`. The 8 MiB
pre-flash backup and the written image were independently verified. The
protected prefix and tail matched. Paired USB confirmed the test card and
the 30-minute sleep setting after reboot.

1. Start the boot logger on the development Note's USB serial.
2. From Home, tap the lower button once. This shortcut is diagnostic-only.
3. At the restart question, briefly press and release the upper button.
4. At the first display refresh after restart, hold upper REC for about
   eight seconds and count aloud. Release and leave USB connected.

The logger matches the software reset, the inbox-specific WAV operations,
the REC edge during setup and the separate REC-to-first-write total. A late
Home recording is saved as a control and the listener continues. The
post-capture `[RecBoot]` records timestamp SD mount, directory/layout and
spool checks, display, catalog, queue and remaining Home initialization.
Intervals overlapping a boot phase are evidence of concurrent activity.
The timings alone cannot identify a filesystem lock owner.

The first timing3 recording was a late control on the inbox path. REC was
19,915 ms after reboot, while setup finished at 2,623 ms. The first PCM
write completed 4,188 ms after REC. Checking for a pre-existing partial
file took 1,540 ms and exclusively creating the WAV took 1,569 ms (74% of
the total together). Storage readiness took 0 ms, the header fsync 281 ms
and the first PCM write 135 ms. The PCM buffer overflowed. The writer
reported 66,048 PCM bytes (2.064 s), without an independent card readback.

The USB log lost the early REC banner. Subtracting the raw latency from
the raw first-write timestamp gives exactly the reported timing edge
(24,103 − 4,188 = 19,915 ms). All 13 stage intervals independently sum
to 4,188 ms. The listener now uses those two values to correlate the
records when a banner is missing. A separate early-boot repeat was armed.

### Repeat at the end of boot, 26 November at 10:28 CET

After a software reset, REC was pressed at 2,631 ms uptime. Setup finished
at 2,635 ms, four milliseconds later. The capture used `/.rec-inbox`.
The microphone produced the first PCM block at 2,993 ms and the first audio
write completed at 3,550 ms. The independent first-write message and final
cumulative stage timestamp both give 919 ms from the REC edge.

| Interval | Elapsed |
| --- | ---: |
| REC to first microphone block | 362 ms |
| Sync WAV header to card (fsync) | 414 ms |
| Write first audio block | 134 ms |
| All remaining operations | 9 ms |

The path checks and path preparation together took 3 ms, and opening the
file took 2 ms. They accounted for most of the delay in the previous
4,188 ms trial. The multi-second stall did not recur in this recording.
The writer reported 265,728 PCM bytes (8.304 s at 16 kHz mono PCM16),
with no overflow message. The archived duration was not independently read back.

USB delivered 10 of the 13 stage lines intact. This prevented automatic
acceptance. The raw inbox path, reset flag, REC edge, embedded setup-end
timestamp, and both first-write timestamps directly establish the boot
timing and the three largest intervals.

The [Stage 2 plan](../superpowers/plans/2026-11-25-pala-note-recorder-rework.md)
lists the outstanding restart, recording, transport and recovery checks.

### Slow-card capture buffer, timing4

The native PCM regression reproduced the old failure at 33,024 frames
(2.064 s), the exact length of the truncated recording on the 2 GB card.
The SD writer held one 256-frame block and the microphone filled all 128
remaining slots before the first write finished.

The recorder now reserves 1,024 blocks in PSRAM at boot. At 16 kHz the
queue holds 16.384 s of mono audio. The inbox tests for an existing WAV
and exclusively creates its partial file. This removes one FAT lookup
before capture reaches disk. The WAV header and checkpoint syncs remain
part of the measurement.

The same native test replayed 6.5 s and 12 s without any writer progress.
After each pause it drained the queue and compared every sample, timestamp
and marker against the input, including samples buffered at button release.
The native test passed. The Python suite ran 281 tests with one skip.

The development Note received `2.0.0-recorder-test4-timing4`, SHA-256
`db2ead4cd210fe171c28e42c6d61fbb53a8758240d164b57d7c2d1573675f58e`.
The 8 MiB pre-flash backup, installed application and protected flash
ranges were independently verified. After reboot, paired USB confirmed
the same test-card fingerprint and the 30-minute sleep setting.

The repeat uses the timing3 restart procedure above and the same 2 GB card,
with its natural write latency. The USB listener was armed at 10:56 CET.
Each completed take reports the queued blocks at the first PCM write, total
captured and written frames, overflow and WAV close status. Check the REC
edge against setup completion and compare both frame counts. A fast card
write in a boot trial does not exercise the original multi-second stall.

### Timing4 boot recording, 26 November at 11:12 CET

The recorder detected REC at 2,102 ms after software reset. Home finished
drawing at 2,618 ms and setup finished at 3,094 ms. The recording started
on the boot inbox path and reached the first SD write before setup ended.

| Interval from REC through first PCM write | Time |
| --- | ---: |
| First microphone block | 359 ms |
| Sync WAV header to SD | 282 ms |
| Write first PCM block | 135 ms |
| Other operations combined (including 1 ms to open) | 8 ms |
| Total | 784 ms |

At the first write, 27 microphone blocks were waiting. The capture counter
reported 172,800 mono frames. The writer reported 345,600 PCM bytes,
exactly 10.800 s at 16 kHz PCM16. The recorder then verified and archived
`/recordings/202611/note_1073762387.wav`.

The USB trace lost the tail of the PCM summary and some stage lines.
The automatic parser therefore did not mark the trial complete. The raw
REC timestamp, boot markers, inbox path, independent first-write total,
captured-frame count, written bytes and archive line survived. The card
did not reproduce its earlier multi-second delay in this run.

Independent readback used the paired USB connection during the sync window
and selected only note 1073762387. Its capture metadata matched the inbox
timestamp (10:11:56 UTC). Reading the file returned 345,644 bytes. The
RIFF size, data size and decoded length agreed. The mono PCM16 stream at
16 kHz contained exactly the 172,800 frames reported by the microphone.
WAV SHA-256: `4bcebb51e1d51dcaa8a50a102d6ea472556d6b2b79bf4ee81426a3f81c8a2714`.

### Forced 6.5-second writer stall

The separate `pala_note_recorder_stall6500` image adds a delay before the
first PCM block reaches the SD writer, after the WAV header has been synced.
Only the first boot-inbox recording whose REC edge fell during setup after
a software reset can trigger it. The microphone continues sampling.
The diagnostic image SHA-256 is
`ac3daddd465c9f5165e362858349a57b38a2eef821de3d4eba19d2aa9c96a45f`.

On the development Note with the same 2 GB card, use the lower-button
restart shortcut from Home. Briefly press and release the upper button to
confirm. About one second later press upper REC again and hold it for ten
seconds. Silence is sufficient. Leave USB connected.

The bench listener measures the actual pause and microphone blocks produced
during it. The expected count is about 406 at 16 kHz. For a full-duration
test, require at least 400 blocks during the pause, a REC edge during boot,
no overflow and a successful save. Read the matching archive over paired
USB and compare decoded frame count and PCM CRC32 with the microphone.
The timestamp in the archive metadata must match the inbox name.

Host tests cover software reset, early and late presses, the single-use
guard, and early timer wakeup. The readback check also rejects a PCM byte
change that preserves WAV length. These are preparation checks; record the
physical result separately.

The image was deployed to the development Note on 26 November 2026.
The full 8 MiB backup, flashed image and protected flash ranges passed
independent verification. Paired USB confirmed the test card and the
30-minute sleep setting after boot. The USB collector opened COM25 at
13:00:46 CET. The trace file is `boot-stall-20261126T120046Z.jsonl`.

#### Hardware result, 26 November at 13:18 CET

Passed on the development Note with the same 2 GB card. The REC edge was
at 1,950 ms after software reset. Setup ended at 2,858 ms, giving a 908 ms
margin. The microphone delivered its first PCM block 355 ms after REC.

| First-write timing | Time |
| --- | ---: |
| REC to the start of the injected delay (including 538 ms header fsync) | 901 ms |
| Measured writer delay | 6,500 ms |
| First PCM block write after the delay | 131 ms |
| REC to completion of the first PCM write | 7,532 ms |

The microphone produced 406 blocks during the delay. It reported 209,408
frames for the entire recording, and the writer reported the same count,
with no overflow. Paired USB matched the archive timestamp to the inbox
recording and returned `note_1073763411.wav` from the card. The RIFF and
data sizes matched the 418,860-byte file. Decoding yielded 209,408 mono
PCM16 frames at 16 kHz (13.088 s). CRC32 over the decoded PCM was
`54912889`, identical to the CRC32 computed at the microphone.
File SHA-256: `bcf4423d2f1f9976cb376b945ca049fe872359289451a0916c2cd506a58e90b7`.
