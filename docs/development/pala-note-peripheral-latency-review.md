# Pala Note peripheral latency review

26 November 2026. This review covers the monochrome Waveshare ESP32-S3-ePaper-1.54
development Note, its populated 2 GB FAT32 card and the recorder working tree
based on `4b64b23`. PlatformIO pins PioArduino 54.03.20. The installed framework
is Arduino-ESP32 3.2.0 with ESP-IDF 5.4.1. References below match those versions.
[Board](https://docs.waveshare.com/ESP32-S3-ePaper-1.54) ·
[Build configuration](../../devices/kubo-note/platformio.ini)

The first priorities are catching audio loss before it reaches the application
buffer and shortening the long tail of the first SD write. Sampling the battery
outside the UI is a smaller, independent change with a clear timing baseline.

## What was measured

| Development Note run | Result |
| --- | --- |
| Startup inbox, November rollover, note 1073768531 | First PCM at 359 ms from REC, first PCM write at 6,246 ms. The seek stage took 1,572 ms, header fsync 280 ms and the first PCM write stage 1,703 ms. |
| Boot with the initial RTC read skipped, note 1073767424 | REC landed during the Home redraw. First PCM at 360 ms, first PCM write at 1,042 ms. |
| Later display trial, note 1073769555 | REC arrived after setup. The UI request took 3,203 ms from REC. Panel activation to BUSY release took 575 ms. All 613,376 WAV samples matched the microphone CRC32 of `78d1b19d`. |

The gesture includes a 320 ms hold. On the first two takes that leaves about
39–40 ms for recognition, dispatch and first audio after that threshold. The
latest display take did not capture the breakdown of its 3,203 ms interval.
Each number above belongs to one take. [Hardware qualification](pala-note-stage2-qualification.md) ·
[Display evidence and design](../superpowers/specs/2026-11-26-pala-note-display-latency-design.md)

The new display code passed 21 host runs across 15 scenarios. The GPIO/SPI
simulation held Home BUSY while a synthetic first PCM block requested REC.
It checked all 5,000 bytes of both controller planes and the ordering of the
next activation. Separate runs exercised missing BUSY and failed allocation
of the optional image or drawing task. The same-card trial measures those
timings on the physical display and reads back the WAV.

## Resource ownership

Button IRQs enqueue timestamped edges and the I²S ISR counts DMA overflow.
The microphone producer drains I²S into the existing 16.384 s PSRAM ring.
The SD writer can block while audio and display owners keep running. FAT
transactions, calibrated ADC reads, e-paper SPI, and network requests run in
task context. Input and PCM preserve order. Transient frames and sensor readings
can be superseded. Shared I²C transactions and each pair of e-paper RAM plane
writes still complete under a single owner.

Size each queue from its event rate and the longest measured consumer gap.
Check the largest contiguous internal DMA-capable allocation, PSRAM headroom
and task stacks under simultaneous audio, display, SD and radio load. A
single free-heap figure cannot determine all buffer sizes. Sections 4 and 5
identify the remaining synchronous clock and radio operations to qualify.

## 1. Measure the audio path on both sides of I²S

The producer reads 256 stereo frames at 16 kHz, copies the left channel and
hashes the received samples. The application ring holds 1,024 such blocks,
or 16.384 s. The I²S channels use the IDF default of six DMA descriptors
with 240 frames each. At 16 kHz that is a nominal 15 ms per descriptor and
90 ms across the descriptors. The pinned driver queues up to five completed
buffer pointers (75 ms). The remaining margin depends on the active DMA
and reader phase. [I²S queue allocation](https://github.com/espressif/esp-idf/blob/v5.4.1/components/esp_driver_i2s/i2s_common.c#L313) [Capture ring](../../devices/kubo-note/pala_note/src/app/pcm_capture_buffer.h) ·
[Channel setup](../../devices/kubo-note/pala_note/src/codec_board/codec_init.c) ·
[Espressif I²S driver](https://docs.espressif.com/projects/esp-idf/en/v5.4.1/esp32s3/api-reference/peripherals/i2s.html)

The periph1 recorder counts DMA overruns and discarded stereo DMA bytes in
an IRAM callback. Its authenticated diagnostics also report the maximum
gap between completed reads, peak application ring occupancy, reconfiguration
retries and read errors per take. The producer yields on codec reconfiguration
and bounds the retry period. The old codec could report a successful read of
substituted zeroes. Equal WAV and producer CRCs alone cannot detect a prior
DMA loss or that substitution in an older recording.
Use a known pulsed tone to check the first and last sound and gaps within it.
Size DMA from the observed maximum service gap. Keep codec-on standby as a
later power/latency experiment. [Codec read](../../devices/kubo-note/pala_note/src/esp_codec_dev/platform/audio_codec_data_i2s.c) ·
[Audio power and codec lifecycle](../../devices/kubo-note/pala_note/src/audio/audio_bsp.c)

## 2. Reduce SD work with the same recovery boundary

This board profile configures CLK, CMD and D0 and mounts in one-bit mode.
The recorder exclusively creates an unbuffered file, writes and syncs the
44-byte header, writes the first PCM block, then batches 4 KiB of audio.
It rewrites and syncs the header at roughly five-second checkpoints and at
close. The observed seek interval includes any scheduling or filesystem lock
wait. It cannot identify the card's internal work by itself.
[Recorder](../../devices/kubo-note/pala_note/src/app/record.cpp) ·
[SD wiring and mount](../../devices/kubo-note/pala_note/pala_note.ino)

Periph1 writes the initial header at offset zero after exclusive creation.
Checkpoints and finalization still save the stream position, rewrite and
sync the header, then restore the PCM append position. A host file test
exercised the real header with denied positioning at creation, checkpoint
and append, payload readback and injected sync failure. Compare the same-card
traces to see where the FAT work moves.
Measure open, position, header write, flush, fsync, first PCM write and peak
ring occupancy separately. If steady writes also stall, trial packing batches
to sector boundaries after the header while retaining the existing WAV layout.
Obtain the sector size from the card. Preserve the on-card sync boundaries.
The current repair code treats every complete sample after the header up to
the file's physical length as audio. Pre-extending a WAV would require an
independently recoverable committed length to avoid publishing an unwritten
tail after a power cut. [Recovery logic](../../devices/kubo-note/pala_note/src/app/capture_wav_recovery.h) ·
[FatFs write alignment](https://elm-chan.org/fsw/ff/doc/appnote.html) ·
[FatFs synchronization](https://elm-chan.org/fsw/ff/doc/sync.html)

`refreshRecordingSpace()` calls `totalBytes()` and `usedBytes()`. Each calls
`f_getfree` in this Arduino core. FatFs may use a cached free count. Measure
a cold mount and a repeat before attributing the delay to a FAT scan. A single
idle snapshot can supply both totals. Keep the low-space recheck before
acceptance and the writer's near-full stopping margin. [Arduino SD_MMC implementation](https://github.com/espressif/arduino-esp32/blob/3.2.0/libraries/SD_MMC/src/SD_MMC.cpp) ·
[FatFs free-space accounting](https://elm-chan.org/fsw/ff/doc/getfree.html)

## 3. Button edge capture and prompt choices

The candidate retains one interrupt ring per button. Each ring has 64 slots
and holds 63 timestamped press or release edges. Normal input uses 14 ms
debounce and a 320 ms hold.

The baseline host replay verified edge ordering across timer wrap and
recovery after overflow. It also reproduced two independent input paths.
Releasing the lower key of a chord while holding the upper key could qualify
as priority REC. A 3 ms pulse produced zero debounced edges but set the raw
press latch used by pairing. These observations came from host replay.

The candidate blocks priority REC after a chord until both keys are up.
Restart and pairing questions wait for a successfully presented prompt and a
stable release before accepting a fresh debounced choice. If a key remains
down when a prompt closes, its hold stays suppressed until release.

MSVC evaluated the candidate's sequential transitions at compile time for
overflow, timer wrap, residual chords, prompt release, a fresh debounced
choice and REC rearming. Ten Python tests checked the display/input,
audit-lifecycle and sync-interrupt wiring. Physical verification remains
open for REC/release and lower-button markers during panel BUSY and a slow
SD write. Record queue drops and high-water counts during that trial.

Release feedback and Restart were also replayed with blocked consumers.
With the SD result withheld, display3 kept REC on the panel. Periph1 queues
a lightweight Saving image from the microphone's stop event. A committed
result or a newer session can supersede it before upload. All 31 simulated
panel runs passed, including 10 release scenarios. Restart replay recovered
a fresh choice after BUSY released and retried a failed presentation inside
the original ticket lifetime. The previous physical trace cannot identify
which path caused the intermittent delay at the confirmation.

The sibling build directory `pala-note-input-audit-20261126` contains
`result.txt` and `replay_button_inputs.py` for the baseline and
`result-final-display.txt` and `verify_display_inputs.py` for the candidate.
[Interrupt and prompt paths](../../devices/kubo-note/pala_note/pala_note.ino) ·
[Gesture classifier](../../devices/kubo-note/pala_note/src/app/button_gesture.h)

## 4. Cache battery readings and bound clock access

The display3 baseline took 21 calibrated ADC samples per caller and used
63 ms of explicit delays, plus conversion and scheduling time. In periph1
a low-priority task samples ADC1 about every 50 ms while awake. It averages
up to ten valid readings younger than 500 ms. Home, sync, Companion and
settings copy one coherent snapshot. A partial window supplies startup
readings. A stale window reports unknown, with the last valid sample time.
The task uses a 3,072-byte static stack and 352-byte task control block on
this build. It introduces no deep-sleep timer. Measure battery-only current
and the task stack margin on the board. [Espressif ADC oneshot driver](https://docs.espressif.com/projects/esp-idf/en/v5.4.1/esp32s3/api-reference/peripherals/adc_oneshot.html)
[Battery sampling](../../devices/kubo-note/pala_note/src/app/battery.cpp) ·
[Settings status](../../devices/kubo-note/pala_note/src/app/note_config.cpp)

The RTC and ES8311 share SDA/SCL. Boot reads the clock before arming capture.
The board I²C wrapper supplies tick-converted 5,000 and 1,000 ms values to
APIs that accept milliseconds. The values happen to agree at this build's
1,000 Hz RTOS tick rate. Choose explicit millisecond budgets from observed
healthy transaction times, then bound retries. Keep the existing monotonic
capture-time anchor. Test an RTC-only timeout separately from a stuck bus.
With an isolated RTC failure, recording can use the established unknown-time
archive policy. A shared bus fault may also prevent opening the codec. The
existing qualification hook skipped the RTC call and did not exercise a
driver timeout. [I²C wrapper](../../devices/kubo-note/pala_note/src/i2c_bsp/i2c_bsp.c) ·
[Capture clock](../../devices/kubo-note/pala_note/src/app/rtc.cpp) ·
[Espressif I²C timeouts](https://docs.espressif.com/projects/esp-idf/en/v5.4.1/esp32s3/api-reference/peripherals/i2c.html)

## 5. Bound transport work and measure whole-board power

The Note already latches priority REC before tearing down sync. Wi-Fi still
has separately blocking operations, including mDNS lookup (1,500 ms request)
and the paired TLS handshake (5 s timeout on the main provider path). Measure
REC during discovery, TLS and BLE and compare the foreground delay, first PCM
and return to the interrupted item. Advance one bounded unit of work at a
time and let its owner cancel it at the next safe point. Preserve peer
verification, archive position and delivery receipts. Wi-Fi and BLE share the
S3 radio. Test reconnect under contention as well as with one link active.
[Bridge connection](../../devices/kubo-note/pala_note/src/app/bridge_client.cpp) ·
[Network operations](../../devices/kubo-note/pala_note/src/app/network.cpp) ·
[Espressif RF coexistence](https://docs.espressif.com/projects/esp-idf/en/v5.4.1/esp32s3/api-guides/coexist.html)

The USB reply sender currently ignores the byte count returned by
`Serial.write()`. The pinned HWCDC driver can return zero or a short count
under backpressure. Test a stalled reader and disconnect mid-frame. Track
short writes and preserve framing or restart the authenticated exchange.
Keep qualification timing separate from protocol replies, with a bounded RAM
record identified by boot and take. Include the largest free internal
DMA-capable block around audio, radio and display overlap alongside the
existing heap low-water marks. [USB transport](../../devices/kubo-note/pala_note/src/app/usb_sync.cpp) ·
[Arduino HWCDC](https://github.com/espressif/arduino-esp32/blob/3.2.0/cores/esp32/HWCDC.cpp) ·
[Heap diagnostics](https://docs.espressif.com/projects/esp-idf/en/v5.4.1/esp32s3/api-reference/system/heap_debug.html)

Audio already closes inactive codec directions. Sleep configures lower-button
EXT1 wake, unmounts the SD and cuts display and audio power. Measure on the
battery supply with USB disconnected. Compare energy per completed take,
steady idle and deep-sleep current, and wake-to-first-PCM before changing
power policy. Keep the dev Note's 30-minute override and product defaults.
[Sleep sequence](../../devices/kubo-note/pala_note/src/app/sleep.cpp) ·
[Espressif sleep modes](https://docs.espressif.com/projects/esp-idf/en/v5.4.1/esp32s3/api-reference/system/sleep_modes.html)

## Smallest useful bench sequence

| Run | Evidence to retain |
| --- | --- |
| Warm idle and Home redraw, then repeat with the verified 6.5 s injected write delay on the same 2 GB card | Physical REC, qualified hold, first PCM, ring high-water, DMA overrun count, first write, screen-ready time, panel BUSY edges, WAV sample count and CRC. Require the new frame ready during prior BUSY in the overlap run. |
| Replay chord and bounce on the host, then try REC, release and marker during redraw on the dev Note | Exact input sequence, drop counts, resulting actions, button release and prompt generation. |
| Cold and warm battery/status reads, RTC driver timeout | ADC calls, age and foreground duration. Separate RTC-only failure from codec bus failure. |
| Stalled USB reader or connecting Wi-Fi, then REC and reconnect | First PCM, cancellation latency, which frame or item resumed, one delivered receipt. |
| Power-cycle recovery after a WAV checkpoint, then battery-only sleep | Compare surviving committed PCM and metadata after recovery on a separate expendable card. Measure current and wake latency on battery. |

Use a matched existing build for these comparisons. Record firmware, card,
backlog, cache state and which injection fired for each run. Keep individual
values and ranges for small samples. The single old card is sufficient for
the current overlap test. Destructive recovery qualification belongs on
the separate expendable card.

## Skill discovery

The public skills.sh queries `embedded-systems` and `esp-idf` returned the
following candidates on 26 November 2026. Counts describe that lookup.

- Jeffallan's `embedded-systems` had 7,447 skill installs and 12,529 stars
  for its full repository. Its RTOS, DMA and power checklist fits future
  design reviews. Device timings still come from the pinned driver and the
  Note. [Catalog entry](https://skills.sh/jeffallan/claude-skills/embedded-systems) ·
  [Skill source](https://github.com/Jeffallan/claude-skills/blob/main/plugin/skills/embedded-systems/SKILL.md)
- Pedro Minatel's `esp-idf` had 118 installs and 10 repository stars. Its
  documented focus is native IDF setup, build, flash and debug. The Note
  already has a working PlatformIO deployment path.
  [Skill source](https://github.com/pedrominatel/awesome-esp-ai/blob/main/skills/esp-idf/SKILL.md)
- Espressif Engineering offers vendor workflows, including Wi-Fi diagnostics,
  through an authenticated service. Engineering and Documentation MCP are
  separate connections. This review used the public documentation and catalog
  only. [Espressif Engineer Skills](https://ts-mcp.espressif.com/en/)
