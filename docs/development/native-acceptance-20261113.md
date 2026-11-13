# Native acceptance follow-up: Outlook and pause test

Date: 13 November 2026

## Result

PASS: a silent, prepared Review text was placed in a new Outlook draft through
Choose destination and Ctrl+Shift. The complete synthetic sentence appeared at
the selected caret, with the existing signature retained. The draft had no
recipient and was not sent.

INCONCLUSIVE: live continuation across a controlled speech pause. The microphone
test encountered a stream discontinuity and captured ambient speech rather than
an isolated synthetic input. Repeated live native acknowledgements were observed
in Notepad, but they do not establish the requested controlled pause scenario.
Playback and microphone testing stopped when Peter explained the shared office
needed quiet. No further sound or microphone recording was started.

This is not acceptance of the complete automatic Outlook Finish pipeline after
recording. The successful Outlook check used an explicitly edited Review draft
and placement shortcut; it did not finish a fresh recording or run verification.

## Exact Outlook evidence

Installed build:

    0.1.0-recovery.20261113.2+b494dca1845394af04d0d1e18eec06568ce1292a

Client PID: 46672

Native metadata timestamp: 2026-11-13T10:37:43.6535956+01:00

    Final: true
    QueuedInputEvents: 4
    BeforeLength: 241
    RequestedLength: 51
    ExpectedLength / ObservedLength: 292 / 292
    ObservedCaret: 79
    ExactText / ExactCaret: true / true
    ClipboardRestored: true
    ClipboardRecoveryPending: false
    Conflict: false
    Milliseconds: 346.8303

The whole observed document matched exactly, including surrounding signature
text. No Outlook paragraph projection or readback retry was required. Native
metadata excludes transcript, clipboard values and document content. Screenshots
of the actual Outlook draft independently showed the complete synthetic sentence.

## Interrupted microphone attempts

The app's microphone was temporarily changed from System default to the built-in
Microphone Array. Existing local Microsoft Filip synthesis generated two short
Slovak WAV clips, with a twelve-second pause. Playback selected an existing Realtek
speaker output without changing Windows default devices or volume.

The first attempt logged an audio/channel interruption before a controlled
synthetic-input baseline could be established. A single retry used a silent
audio pre-roll, then was stopped. The second attempt logged a complete four-event
live paste followed by an input-generation interruption. Its pending clipboard
backup was retained; this is an unresolved observation, not a proven pause bug
or a successful pause test. No further input was replayed to obtain confirmation.

The known Notepad destination was inspected. After recording stopped, the new
recovery window was used with its inspection checkbox; the UI reported that the
previous clipboard was restored. This also verified the exceptional recovery UI
in actual use. Ambient transcript/audio contents are not reproduced in this record.

## Cleanup and control release

- System default microphone was restored through the app. All saved settings
  are byte-identical to the pre-test snapshot.
- The Outlook insertion restored the previous clipboard automatically.
- API reports ready=true, busy=false; no recording remains active.
- No software, driver, virtual microphone or security configuration was installed
  or changed. The previously blocked DeliveryProbe was not executed or rehosted.
- Computer control ended immediately after observing the Outlook result. Final
  checks used metadata, saved-setting hashes and the local API only.
- The new unsent Outlook test draft and test Notepad tab were left in place.
  Existing messages/documents were not deleted or sent.

Canonical source: C:\Git\Local-Dictation\repo

Branch: feature/native-windows

Base HEAD: b494dca1845394af04d0d1e18eec06568ce1292a

No application source or installed binary changed during these acceptance checks.
