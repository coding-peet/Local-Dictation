# Live preview and delivery at the cursor

## Using cursor insertion

1. Save the desired model and delivery settings.
2. Place an empty cursor in the destination field.
3. Use the configured shortcut to start and stop recording.

The Settings button named Record in banner starts a preview-only recording.
It has no external destination, so use Choose destination after recording.
Starting with the shortcut in an editable field remembers that destination,
even with Insert live text at the cursor disabled. That option controls live
updates; the completed text still returns to the remembered field.

The original field must expose editable text and its selection through Windows
accessibility. Protected, elevated, unsupported or changed destinations fall
back to Review. The app does not follow a click into another field.

## Preview and focus changes

The banner now shows a read-only transcript panel during recording. Hide text
collapses it; Show text restores it. Clicking these controls or Finish does
not invalidate the original destination because the banner does not activate.

Typing, clicking outside the banner, or changing focus pauses insertion.
Recording and transcription continue. Return to the original unchanged field
to resume at the owned draft's end. Manual changes to that field require a
new explicit destination; unrelated text is never overwritten.

Drag the banner by its microphone tile, title/status area, or waveform. Manual
position survives refreshes and later recordings during the same app run.
When Windows exposes a native caret, initial placement tries to avoid it.

With Verify after recording, the app saves the complete transcript before
removing its owned draft. Differences appear as underlined phrases in the
compact banner. Each choice advances to the next difference; the last choice
validates the original field and inserts the completed text. Agreement inserts
automatically after successful verification. Cancelled or failed sessions keep
their text pending for explicit action.

The large editor opens only through Details. Closing Review keeps the pending
item, including unapplied custom wording. Reviews restored after an app restart
need Choose destination and the shortcut because their old window lease is gone.
Already inserted items remain marked as inserted, including when the receipt
can be recovered from History after a pending-file write failure.

Live updates arrive after speech segments and model inference. They are not
character-by-character output. With live disabled, the preview explicitly
states that transcription starts after Finish.

## Large-v3

Large-v3 live was tested on 91.8 seconds of real audio on 2026-11-11 using
the actual session/engine code and original FP16 weights. It worked without
lost audio, but first partials took 5.347 and 7.790 seconds. Final live output
had 23/86 word errors (26.74%), compared with the earlier batch result of
16/86 (18.60%) on the same reference. The batch result was not rerun.

The original catalog kept Large-v3 in after-recording mode despite that technical
capability. Peter subsequently requested access to the supported live mode.
The built-in Large-v3 catalog now advertises live_supported=true while keeping
live_ready=false: live can be selected, and Settings explains potentially slow
updates. This changes capability policy, not original model weights or inference.
After-recording mode remains available when live speed/quality is unsuitable.
The original experiment was isolated and did not restart either existing listener.

## Earlier live-visibility verification on 2026-11-11

These results describe the original live-visibility change. The later compact
Review and Superpowers audit have their own reports.

- The new input-observation regression initially failed 2 of 3 tests under
  the old invalidate-on-every-click behavior.
- The updated implementation passes all 62 Core tests.
- Release WPF publish succeeds.
- Native WPF renders were inspected for both cursor and Review states. These
  use synthetic text, without microphone capture or inference.
- A review found that a hidden preview concealed the fallback reason. The
  panel now expands automatically when cursor delivery stops.
- Live insertion in Codex still requires Peter's manual acceptance test.
- The native DeliveryProbe passed seven real UIA/delivery checks in its own
  synthetic fields: live insertion, pause, return/resume, owned withdrawal,
  Review reinsertion, manual-edit conflict, and closed target protection.

Automatic insertion currently accepts single-paragraph text. A manual draft
containing line breaks/control characters is rejected before focus or text
changes, and the Review remains open for correction.

No production API, model weights, model precision, or TypeWhisper settings
were changed by this client fix.
