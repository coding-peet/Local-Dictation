# Outlook delivery investigation and recovery design

Date: 12 November 2026.

## Verified input

A physical shortcut/microphone test visibly inserted into Notepad and saved an
insertion receipt with protected audio. A second receipt has an unidentified
destination. A new Outlook test saved text/audio but had no insertion receipt
and reported an unconfirmed paste with clipboard recovery.
These are separate acceptance outcomes; Notepad does not establish Outlook support.

The user also could not hide the recovery banner. The controller requires recovery
visibility unconditionally, so idle Cancel immediately shows it again. A saved
missing microphone warning also survives selecting an available microphone.

The current client was restarted during testing, so the original process-only
diagnostic opt-in no longer identified the running process. The next diagnostic
launch must prove its PID and build before another Outlook attempt.

## Approach

Preserve guarded native paste and gather an attributable failure first. Do not
assume that queued input proves insertion, discard the clipboard backup, loosen
field ownership, or switch input transports without a demonstrated failure cause.
These alternatives would remove existing safety evidence instead of explaining
the Outlook result. This is the coordinator's engineering choice within the
already authorized troubleshooting scope.

Keep the current WPF client and original FP16 model/runtime separation. No new
packages, model changes or production API restart are needed.

## Recovery behavior

Idle X hides the banner and preserves pending Review, the recovery lease and the
running process. Older queued notifications must not undo dismissal. A new recovery
event or explicit Start/Exit/recovery request may make the status visible again.

Expose the existing Restore clipboard action in the idle banner. Keep its existing
inspection confirmation and recheck eligibility after the nested dialog. Finish
retains priority while recording; active operations and insertion block restoration.
Closing the banner must never permit Exit or another paste while a lease is held.

Recovery with compact Review uses the existing text surface, without duplicating
the transcript or increasing the ordinary banner into a second large editor.

## Diagnostics and microphone status

Keep LOCAL_DICTATION_DELIVERY_DIAGNOSTICS=1 as an explicit process opt-in. Record a
startup PID/build marker, additive PID on existing delivery events and fixed capture
stages with timing and exception type/HRESULT. Do not record field text/names,
clipboard content, audio or paths. Journals have one bounded active/previous file.
Logging must add no UIA queries or change insertion guards/native-call ordering.

Clear a microphone-owned stale warning after recovery without erasing unrelated
footer statuses. A fresh enumeration failure remains until successful discovery.
Passive refresh must retain a missing saved ID rather than silently selecting one.

## Acceptance

Run failing regressions before product fixes, then existing and added synthetic
checks. Build into a separate candidate; preserve settings, models and recordings.
Deploy only after normal client exit, with clipboard recovery resolved by its owner.

Verify the new session marker matches the actual process. A further narrow Outlook
test must identify the failing capture/readback stage before any transport change.
Completion requires visible text at the intended cursor and a durable receipt;
Review approval, focus switching and clipboard restoration remain distinct checks.
