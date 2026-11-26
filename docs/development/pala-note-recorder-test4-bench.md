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

The second capture overflowed the 2.048-second microphone buffer. The trace
shows both temporary files. The final archive path and readable audio length
still require a read-only check of the card. The timings include path
preparation, WAV creation, header sync and the first data write. They do
not identify which operation caused the delay.

Tested clock firmware SHA-256:
`7fcf0596caebdf047a6f5e86c1240e094692b99dd189a8a9836b2373d7b056b3`.

The [Stage 2 plan](../superpowers/plans/2026-11-25-pala-note-recorder-rework.md)
lists the outstanding restart, recording, transport and recovery checks.
