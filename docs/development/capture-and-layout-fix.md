# Capture continuity and native layout

## Confirmed recording failure

On 2026-11-11, Jabra Link 380 opened successfully but the client immediately
reported Unavailable. The capture callback emitted this sequence:

~~~text
Bytes  Position  Flags
248    0         DataDiscontinuity
0      0         None
320    160       None
0      0         None
320    320       None
~~~

The client treated an empty callback's position as valid and inferred a dropped
packet by adding the previous buffer size. This incorrectly aborted a healthy
capture stream. Startup format conversion also made that inferred position
unsuitable as a strict continuity check.

The fix ignores empty callbacks and uses the native WASAPI discontinuity flag
after the first nonempty packet. The first packet can mark stream startup.
Real subsequent discontinuities still stop capture and preserve received text.

References:

- https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer
- https://learn.microsoft.com/en-us/windows/win32/api/audioclient/ne-audioclient-_audclnt_bufferflags

The original capture code reproduced the failure. The corrected production
capture class recorded 11,768 bytes from Jabra without a fault in a short probe.
The probe discards audio and outputs only the device name, byte count and error.

## Error reporting

Capture failures keep their specific reason. Other failures identify their
phase and exception code. logs/client-errors.jsonl contains phase, exception
type, HRESULT and stack only; it does not log audio, transcript or exception
messages. The log rotates at approximately 1 MiB.

## Layout direction

The recording banner follows docs/prototypes/recording-review.html: 580-pixel
inner panel, 17-pixel corners, #202631 background, 39-pixel status tile, level
bars, Finish action and metadata/shortcut row. Level bars follow microphone
amplitude. The window retains the native no-activation and monitor-placement
behavior needed for guarded cursor insertion.

The main window groups Recording separately from Text and verification.
Appearance, shortcut and service controls are under Settings. Record to Review
is the primary action and forces Review delivery for that recording.

## Verification

47 Core tests passed, including the recorded Jabra startup sequence and a real
subsequent discontinuity. Microphone probe succeeded on the actual default
device. Application-level banner, stop, transcription and history checks are
reported separately from these checks.

The main Windows window was visually inspected after grouping its settings.
Record to Review started and stopped without the original immediate capture
failure; that test returned no transcript. It does not establish recognition
quality or retention with a spoken recording.

The banner was rendered from the actual WPF controls with a synthetic Listening
state and no microphone capture. docs/prototypes/native-banner-preview.png is
that layout preview, not a screenshot of a real dictation or evidence of insertion.
The diagnostic renderer is invoked with --render-banner followed by a PNG path.
