# Pala Note rc93 hardware validation

Hardware runs took place on 24 November 2026. This branch failed hardware
acceptance. Recording started several seconds late in one trial, and the
standard USB Bridge did not complete a ten-recording backlog on either tested
archive. The branch is preserved for reference. Deployment is blocked.

## Setup

- Second Pala Note with stock firmware `2.0.0-rc93`, a dedicated 2 GB FAT32
  card, and the updated Bridge code in an isolated test state.
- The 100-note and 4,000-note archives started with the same ten synthetic
  pending recordings. The card contents passed PC readback before each run.
- The manual upper-button triple press triggered the USB tests. The Bridge
  used its standard six-second command deadline and a deterministic local
  transcript result. External delivery was disabled.
- Stock firmware measured the button edge to the first buffer captured from
  the microphone. The timestamp of the first SD write was unavailable.

## Physical results

| Archive / pending | USB result | Recording and display |
| --- | --- | --- |
| 100 / 10 | Device reported 100 notes. First selection 1.261 s. The first WAV reached the Bridge. `COMMIT` timed out in 6.063 s and the immediate recovery `EXPORT` timed out in 6.030 s. The attempt ended after 17.071 s with zero confirmed receipts. | First microphone sample arrived after 345 ms, 344 ms and 7,146 ms. Four WAVs passed card readback. One recording had no usable edge trace. The screen stayed on Recording and recovered slowly. |
| 4,000 / 10 | The verified card contained all 19,973 files. The running catalog count could not be confirmed. After the reported triple press, one authenticated `NEXT` returned `EMPTY` in 0.006 s. The next `HELLO` timed out in 6.100 s. No audio was recognized or acknowledged. | No REC or page-opening timing was captured for the large archive. |

For 100 / 10, subsequent card readback verified that the first transcript
matched the Bridge result byte for byte and preserved its capture time. The
completed item left the audio queue. The other nine original items and all
four new recordings remained queued. The four WAVs contained 67,585, 36,865,
38,913 and 8,193 mono PCM16 samples at 16 kHz. The 7,146 ms capture contained
approximately 0.512 s of audio.

The 4,000 / 10 run ended before a final card readback. The `EMPTY` response
alone does not establish whether the manual request was active or the queue
was drained.

## Prior checks

- On 4,000 / 1,000, individual selections took 7.24–18.62 s. A diagnostic
  attempt waited up to 35 s without a `COMMIT` receipt. Later readback
  returned the exact synthetic transcript and original capture time.
- On 1,000 / 1,000, two `NEXT` requests offered audio in 3.59 s and 1.47 s.
  `COMMIT` timed out at 6.19 s. Later readback matched the transcript and
  capture time. Two further `NEXT` requests timed out at 6.12 s each.
- The 10,000 / 1,000 fixture required at least 119,007 directory entries in
  the firmware's flat `/notes` directory. This exceeded the FatFs limit of
  65,536 entries per directory in this build. A larger FAT32 card leaves that
  directory limit in place.
- Before hardware validation, the host runs passed 242 Python and 28 Node
  tests. MSVC compiled 28 test files and all 12 test executables passed. The
  regression runs establish host behavior for their fixtures.

## Interpretation

A first transcript currently rewrites the entire catalog CSV before the
device acknowledges the save. The two short-queue fixtures require 100 or
4,000 file-write calls for the catalog alone. The filesystem can buffer these
calls. This behavior also exists in rc89. The recorded traces leave the
precise causes of the USB timeout and the 7.146 s REC delay unresolved. The
small archive still had an unfinished first-use agenda index during testing.

Tasks and Calendar opening times, first SD-write latency, physical BLE/Wi-Fi
resume and physical power-cut recovery remain unverified. The instrumented
build stayed on the PC when testing stopped. The diagnostic unit retained
stock rc93 throughout the short-queue comparison.
