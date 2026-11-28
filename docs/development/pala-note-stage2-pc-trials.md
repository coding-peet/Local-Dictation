# Stage 2 PC trial runner

The runner collects USB evidence and verifies individual acceptance cases.
Use the Stage 2 qualification procedure for the hardware sequence and the
acceptance matrix. Keep a separate directory per case with the firmware
SHA-256, Bridge revision, test-card identity, scenario and resulting JSON.

Run from the repository root in PowerShell. Set the variables to the current
bench values and use a fresh output path for each trace.

~~~powershell
$trial = 'devices/kubo-note/tools/stage2_trial.py'
$bench = 'devices/kubo-note/tools/stage2_bench.py'
~~~

## Record a restart and REC

Run while the development Note is awake and the port is free. The selected
Bridge state must contain its existing pairing. Set the build to the value
returned by BENCH status, without the boot banner's added v prefix.

~~~powershell
py -B $trial restart-trace --state $state --build $build --port $port --log $trace
~~~

The PC starts reading before authentication and sends one restart
confirmation request. Wait for the ticket, confirm on the Note and hold REC
at the intended point. If the reply is lost, check the Note for the prompt.
The reader keeps logging and reopens USB by device identity after a detach.

Use passive capture for a restart initiated with the lower button or for
a Wi-Fi, BLE or power-cut test. Start the capture before triggering the
trial. Wait for the usb_attached line before pressing a button.

~~~powershell
py -B $trial trace --state $state --build $build --log $trace --seconds 180
~~~

Configure or arm the test before the collector opens the port. For a
concurrent USB delivery trial, use stage2_bench usb-step as the single port
owner. It prints its diagnostic lines during the protocol exchange.

The trace logs allowlisted diagnostics and firmware boot banners. Replies,
pairing tokens and settings are filtered. A reset can still prevent capture
of bytes emitted before Windows re-enumerates the Note. The runner reports
a missing restart banner as a failed acquisition and retains the trace.
Use the firmware's retained measurement where supported and record its
provenance in the trial notes.

## Verify the recording

After mounting the test card read-only, enter the expected capture UTC, note
ID and REC edge. A boot index is one-based within the trace. An earlier boot
with the same edge cannot contribute evidence to the selected boot.

~~~powershell
py -B $trial recording $card $note $utc $edge $trace --boot 1 --build $build --software-restart
~~~

For a clock-controlled test add --crosses with the required UTC boundary.
The recording check compares the archive path, UTC metadata, RIFF length,
sample count and microphone CRC. It reports firmware timing marks and panel
timing when present. Establish and restore a simulated clock using the
qualification procedure.

Use traces with one measured take per case for startup, idle, active sync,
archive reading and playback. Record card identity and queue depth before
each trial. Use a
timed spoken phrase and listen to the saved file to verify the capture
starts and ends where expected.

## Verify delivery and Bridge handover

Save the Bridge's transcript as UTF-8 bytes without reformatting. Use the
approved test account and dedicated test list. The provider check reads
Google Tasks and searches the selected list for the receipt's task marker.

~~~powershell
py -B $trial delivery $card $note $trace --transport wifi --transcript $textFile --state $state --account $account --list-id $listId --title $title
~~~

The verifier requires the selected transport's firmware sequence, an exact
transcript match, the SD receipt and one independently read task. For a
lost-receipt handover add --previous with Bridge A's log and use Bridge B's
log as the main trace. A must have one write grant and a withheld receipt.
B must reconcile and finish with just that grant. Record each Bridge's
identity and recognition counts. Test overlapping peers, destination
conflicts and ambiguous provider results as separate cases.

## Access the development card over USB

Use the paired recorder port and the development build
`2.0.0-recorder-msc1`. Let the current recording finish saving and release
both buttons. Run this command to hand the test card to Windows now.

~~~powershell
py -B $bench usb-storage --state $state --port $port
~~~

The recorder restarts into a dedicated USB mass-storage boot. If the
acknowledgment is lost, check Windows for `Pala Note SD`, VID/PID
`303A:0002`, serial `70041DD816A8`, before repeating the request.
Windows has write access to the card. Finish file operations, then use
**Eject** on its volume. The Note restarts into recorder mode after ejection.

For a power-cut trial, arm one storage boot before the cut. The recorder
continues running until the next boot, when the PC can inspect the card
before the recorder opens it for recovery.

~~~powershell
py -B $bench usb-storage-next --state $state --port $port
~~~

Run the following command to cancel the armed boot while the recorder is still running.

~~~powershell
py -B $bench usb-storage-cancel --state $state --port $port
~~~

## Verify preservation after a power cut

On disposable media, take a baseline with stage2_bench snapshot. Arm the
intended cut point. Start the passive trace, perform the trial and record
the physical power cut. Run snapshot after recovery and again after the
second boot without new work.

~~~powershell
py -B $bench snapshot $card | Set-Content -Encoding utf8 $baseline
py -B $trial recovery $baseline $after $afterSecondBoot $trace pcm $edge
~~~

Use the REC edge for open and pcm. Use the note ID for publish, text and
receipt. The verifier requires a matching cut marker followed by detach
and boot, preserved originals and a stable archive on the second boot.
Confirm that board power was physically interrupted. Check recovered PCM,
pending work in the inbox and .pala-work, and provider state as specified in
the qualification procedure. Use stage2_bench compare with the full-card
and filename-collision baselines. Keep the REC rejection, free space and
sentinel evidence for those cases.

## PC regression tests

~~~powershell
py -B -m unittest discover -s devices/kubo-note/tests -p 'test_stage2*.py' -v
~~~

The tests use simulated USB, temporary WAVs and fixture provider responses.
Summarize physical Stage 2 results in the qualification matrix.
