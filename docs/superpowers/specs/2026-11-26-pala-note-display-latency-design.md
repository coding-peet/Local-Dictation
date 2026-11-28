# Pala Note display response during recording

Design proposal for the 200 × 200 monochrome development Note, 26 November 2026.

The microphone can be collecting sound while the panel still shows Home. As
soon as the first block of microphone samples is in RAM, prepare the recording
screen. If the panel is free, start its refresh. If it is refreshing Home,
prepare REC during that refresh and present REC at the next safe opportunity.

## What the development Note measured

On the populated 2 GB card, build `2.0.0-recorder-stage2-qual2` recorded the
following times from the physical REC edge for note `1073769555`.

| Milestone | Time from REC | Time since previous row |
| --- | ---: | ---: |
| Display request | 3,203 ms | 3,203 ms |
| Drawing started | 3,204 ms | 1 ms |
| Drawing reached the refresh call | 3,885 ms | 681 ms |
| Panel activation command | 3,887 ms | 2 ms |
| Panel BUSY released | 4,462 ms | 575 ms |
| Display call returned | 4,463 ms | 1 ms |

The press came 6,870 ms after setup completed. The 3,203 ms interval includes
gesture recognition. The trace could not apportion the remainder to individual
operations. The 681 ms is elapsed time, including any scheduling and settings
reads. The WAV matched the microphone's 613,376 samples and CRC32 `78d1b19d`.

Today the ordinary REC path starts its display task after the first PCM write.
The startup inbox path requests the screen when the foreground takes over the
recording. Both can produce late feedback. Drawing uses one mutable image, and
each partial refresh reads it twice, on opposite sides of the panel BUSY wait.
See [capture](../../../devices/kubo-note/pala_note/src/app/record.cpp),
[drawing](../../../devices/kubo-note/pala_note/src/app/draw.cpp) and the
[panel driver](../../../devices/kubo-note/pala_note/src/display/epaper_driver_bsp.cpp).

## Recommended design

Moving the REC request earlier is the smallest change. It would still leave
Home and REC competing for one image when their work overlaps. Use early REC
notification **and** two working images shared by a renderer and a panel task.

1. **Confirm capture at the source.** Keep the existing 320 ms hold and gesture
   routing. Give each recording an identity before the producer starts. After
   its first successful PCM block enters the ring, the producer publishes one
   small event with that identity and the original edge time. Use a latched
   state and a nonblocking notification for both the direct and inbox paths.
   It works even if the panel task has not started. Audio never allocates,
   draws, logs over USB or waits for the display on this path. Keep Ask Kubo
   capture distinct.
2. **Render into a separate image.** The renderer alone owns the writable
   buffer. It receives a screen description with a generation number and owned
   data. A newer valid request supersedes an unfinished or queued Home,
   animation or progress frame. Check for replacement between bounded drawing
   steps and immediately before handing an image to the panel. Invalidate a
   slow Home or agenda result if its generation has expired. Resolve SD data,
   language, device name and sensor readings outside the rendering path.
3. **Keep REC ready.** Cache the initial localized recording image once the
   device name and language are available. Invalidate it when either changes.
   A built-in REC symbol and label cover early boot or a cache miss. The audio
   event only selects the state. The renderer copies the prepared image into
   the free buffer and wakes the panel task.
4. **Give the panel one owner.** Only the panel task sends SPI commands, waits
   for BUSY, updates the previous-image plane and tracks the clean-refresh
   count. Immediately before upload, the panel task atomically claims the newest
   complete image for the current generation. This freezes the submitted
   image. Once claimed, finish that transaction. For a partial refresh send the frozen image to
   `0x24`, activate with `0x20`, wait for BUSY to release, then send the
   **same image** to `0x26`. Only then recycle its buffer. The renderer can
   compose the next complete image throughout that wait. A lock around a
   pointer exchange suffices for ownership. Release it before drawing or I/O.

For example, if Home is half drawn in ESP32 RAM when REC starts, discard the
unfinished Home and draw REC. If the panel has already started showing Home,
finish its waveform and previous-image write while REC is prepared beside it.
When the panel becomes available it receives the most recent valid screen.

Each image occupies exactly 5,000 bytes (25 bytes per row × 200). Two working
images use 10,000 bytes, an increase of 5,000 over today. The optional cached
REC image brings the pixel budget to 15,000 bytes. Allocate at startup after
reserving the audio ring. Keep that ring's 16.384 s capacity. Budget task
stacks, descriptors and any internal DMA staging separately. Measure the
lowest free internal DMA-capable memory and PSRAM under radio and SD load.
Keep the Core 1 microphone producer ahead of display work and verify that the
Core 0 SD writer can still drain the ring. If display memory is short, drop
the cache first, then degrade visual feedback while retaining capture. [Waveshare board specifications](https://www.waveshare.com/product/displays/e-paper/esp32-s3-epaper-1.54.htm)

## Screen transitions that must survive the change

- REC owns the presentation while its session is recording. Moment presses
  still enter the audio timeline even when several counter frames collapse
  into one. Retire REC on capture end and show Saved only after the WAV is
  committed. If recording finishes before REC reaches the panel, the newest
  valid result wins. A delayed Home, marker or old session cannot revive it.
- For restart, pairing and calendar choices, treat the matching frame as
  presented when its activation has asserted BUSY and BUSY has returned to
  idle successfully. Start its visible dwell then. Arm the choices only
  after observing both buttons released and accept a fresh press. A timeout
  leaves the prompt unarmed. Keep ticket expiry independent and discard
  expired prompts.
- Give incremental views a known base. For example, render the lock over the
  matching Home image before sleep. Route direct framebuffer users such as
  the aquarium through the renderer. Keep USB/BLE polling on its owning task
  when retiring panel wait callbacks.
- Preserve a full base refresh at power-up and the existing full/partial
  cleaning policy. If REC arrives before the first upload, it can itself
  establish the base. A BUSY timeout or uncertain completion invalidates the
  base. Recover with a bounded full reinitialization at a safe opportunity.
  At sleep, drain the final display transaction or its bounded timeout before
  removing panel power, rechecking input and capture activity.

## Delivery and acceptance on the development Note

First route the common drawing and refresh primitives through the two owners,
including full refresh and direct pixel users. During migration, callers that
depend on presentation may wait for their own frame in the foreground. Move
REC, Home and progress to nonblocking publication, replace the temporary REC
display task, and remove display joins from recording and WAV finalization.
Migrate prompt arming and sleep before enabling asynchronous drawing for those
screens. Keep the existing layouts for pixel comparisons.

In the qualification build, retain a per-take timing record in RAM, readable
over paired USB once the take finishes. Tag it with boot, capture identity,
direct/inbox path, screen generation, refresh mode and cache hit. Timestamp
the physical edge, accepted hold, first PCM, UI request, ready image, prior
panel availability, both SPI plane writes, activation, BUSY and first WAV
write. Include the existing storage stage markers, frame count, overflow and
microphone CRC. This permits readback after the live serial listener misses a
line. A timeout is a separate outcome.

Proposed targets for a warm, partial-ready panel on the same card:

| Measurement | Proposed target |
| --- | --- |
| First PCM in RAM to recording notification | At most 20 ms |
| First PCM to REC panel activation with an idle panel | At most 100 ms |
| Physical press to activation on idle Home with a ready panel | Aim for 500 ms including the 320 ms hold |
| REC activation if a previous refresh is in flight | Within 100 ms of the later of first PCM and release of the previous frame after its plane write |
| Audio integrity | Exactly matching WAV and microphone frame count and CRC32, zero overflow |

Report edge-to-first-PCM, edge-to-activation and edge-to-BUSY-release for
every take. Compare first-PCM latency against the current build in the same
scenario. If activation occurs at 500 ms and the previously measured 575 ms
partial waveform repeats, the panel would finish around 1.1 s after REC.
Report cold full initialization separately.

Test idle, REC during the first Home draw after a restart, REC during an
active partial and an active full waveform, and a forced 6.5 s first WAV
write stall. Require the early-boot edge inside the instrumented Home-draw
interval. In both the full and partial overlap tests, trigger capture early
in the previous waveform and require the trace to show **first PCM → REC
frame ready in RAM → previous BUSY released**, in that order. Compare the
WAV count and CRC on those same takes. A late-arriving REC is a separate
activation-latency case. The current stall hook applies to the inbox path.
Require its marker and prove REC starts refreshing during the injected stall
when the panel is available.
Include a release before the panel is free, a second REC, an active sync,
a stale button press on a new prompt, a missing BUSY acknowledgement and a
sleep/wake cycle. Check the visible sequence and ghosting on the device as
well as timing. [Existing qualification procedure](../../development/pala-note-stage2-qualification.md)
