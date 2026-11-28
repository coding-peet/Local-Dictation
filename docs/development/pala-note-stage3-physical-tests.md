# Stage 3 physical acceptance on the dev Note

Run these trials when the dev Note is released from Stage 2. The four sections
cover REC lock and rollover, spoken continuity, archive deletion and REC
during an Ask Kubo voice answer. Physical acceptance remains open.

## Prepare the bench

- Use the dev Note with USB identity `70041DD816A8` and the existing paired
  dev Bridge state. Use one process at a time on the USB recorder port.
- Use `pala_note_stage3_trial` and a Bridge from the same reviewed source
  revision. The trial runner requires the version `2.0.0-stage3-trial1`.
  Rebuild from the reviewed Stage 2 and Stage 3 sources before handing the
  device over. Record the application and ELF hashes and verify the installed
  firmware on the identified dev Note. Keep a verified flash backup.
- The PC-built reference image is under the results directory below at
  `firmware/pala_note_stage3_trial/firmware.bin`. Its SHA-256 is
  `a207d06974279c4ccb443f9520435ae532b3092cf6f2672d34075543edfed42a`.
- Back up the existing 2 GB card and check it is the test card. Capture and
  playback trials can use its normal dev contents. The archive deletion trial
  requires a verified disposable fixture, so run it separately or last.
- Use a dedicated Bridge with multipart recording support. For long and
  interrupted REC, save a local STT transcript on the card. Use the
  transcription-only Wi-Fi route, or the USB `sync_one` entry point for the
  specific note ID with the Bridge's multipart callbacks. When using the USB
  entry point, perform sync after the collector closes and before mounting
  the card. Keep Google writes disabled for these four trials. Keep
  automatic sync idle for the archive fixture.
- Sign in to Web Companion using the device's existing password. Open it
  with both buttons held for three seconds on Home. Use the address shown
  on the Note. Leave Companion and return to Home before sending any USB
  maintenance request.
- Arrange a live view of the local PC console. The speech cues are
  1.5 seconds apart. Start REC only after the bench operator sees USB READY.

Run commands from the canonical repository root. Set the actual dev Bridge
state folder and select a fresh output directory for each run. The scripts
refuse to overwrite trace and snapshot files. Update the card drive letter
each time Windows mounts Pala Note SD.

~~~powershell
Set-Location 'C:/Git/Local-Dictation/repo'
$trialBuild = 'C:/Git/Local-Dictation/build/pala-note-stage3-four-trials-20261128'
$trialEvidence = Join-Path $trialBuild 'physical-run-01'
New-Item -ItemType Directory -Path $trialEvidence | Out-Null
$env:VIRTUAL_ENV = Join-Path $env:LOCALAPPDATA 'PalaBridge/app/.venv'
$bridgeState = 'C:/path/to/paired-dev-bridge-state'
$capture = 'devices/kubo-note/tools/stage3_capture_trial.py'
$archive = 'devices/kubo-note/tools/stage3_archive_trial.py'
~~~

## 1. Lock REC, cross five minutes and stop

~~~powershell
py -B -X utf8 $capture watch long $bridgeState "$trialEvidence/long.jsonl"
~~~

1. Hold upper REC. Check the REC dot and the battery icon when the display
   updates. Keep recording until the display offers LOCK near the lower
   button.
2. Press lower while upper is still held. Wait for ZAMKNUTE, then let go of
   both buttons. Check that REC and the sample-based timer continue.
3. Stay quiet until the countdown. Speak **Na námestí** on the SPEAK cue at
   about 4:59, then **stojí modrý bicykel** on CONTINUE at about 5:00.5.
   Speak naturally through the boundary. Stop with a new upper press on
   the STOP cue at about 5:15.
4. Repeat the lock sequence with the short trial. Stop with a new lower
   press at the cue near 12 seconds. Run this one from Home.

~~~powershell
py -B -X utf8 $capture watch short $bridgeState "$trialEvidence/short.jsonl"
~~~

The countdown projects the Note's first buffered PCM time onto the PC
monotonic clock using the two timestamps in the USB event. USB transit,
console latency, reaction time and mic sampling drift still affect the cue
heard or seen by the operator. Confirm spoken placement using the recorded
boundary excerpt. Treat the simulated schedule checks as PC evidence.

Wait for the long take's transcript to be committed. Open the archived
month in Web Companion and save the listing as UTF-8 HTML. Use Download WAV
on the measured ID and save that response outside the card. Return the Note
to Home, then mount its card over USB.

~~~powershell
py -B $archive mount $bridgeState "$trialEvidence/capture-settings.json"
# Example drive letter. Verify the dev Note USB volume before using it.
$testCard = 'D:/'
py -B -X utf8 $capture verify "$trialEvidence/long.jsonl" $testCard --listing "$trialEvidence/listing.html" --download "$trialEvidence/download.wav" --boundary "$trialEvidence/boundary.wav"
py -B -X utf8 $capture verify "$trialEvidence/short.jsonl" $testCard
~~~

Pass when both STOP buttons work in their respective trials, the long
capture has exactly one archive item and download link, and the first
WAV has 4,800,000 frames. The second part follows it without any loss or
duplication. The sealed part index, both WAV checksums, combined frame count
and microphone CRC agree. The joined Companion download and complete card
transcript also agree. Listen to the six-second excerpt for both halves of
the phrase on their respective sides of the boundary. An optional
`--model-root` with the ASR environment independently transcribes those
sides and flags disagreement for listening. Eject the USB volume safely
and verify that the Note returns to Home.

## 2. Check long dictation against noise on PC

This is the repeatable control for the physical rollover. It uses the
actual multipart Bridge and local Slovak Whisper with a fixed synthetic
reference, low-level noise and attenuated words. A fresh results directory
keeps the previous evidence available.

~~~powershell
$env:VIRTUAL_ENV = 'C:/AI/PalaNote-ASR/.venv'
py -B -X utf8 devices/kubo-note/tools/stage3_quiet_voice.py "$trialBuild/spoken.wav" "$trialEvidence/voice-controls" --model-root 'C:/AI/PalaNote-ASR/models'
$env:VIRTUAL_ENV = Join-Path $env:LOCALAPPDATA 'PalaBridge/app/.venv'
~~~

Read each case JSON. The target is both sentences in order and zero added
words. Noise alone should produce an empty transcript. The 28 November
2026 PC run reproduced 11 inserted words in each speech-plus-noise case
and 13 words from noise alone. The clean control matched exactly. Exit
code 1 records an acceptance mismatch and retains the results.

## 3. Verify archive deletion over USB

Use `C:/Git/Local-Dictation/build/pala-note-stage3-mcp-card-20261127`.
Keep its manifest on the PC. Copy only the contents of `card-root` to the
empty disposable FAT32 test volume in the dev Note service mode. Keep a
verified backup of any prior contents before staging the fixture. Leave
the card in the Note and eject the volume. Advance only after confirming
that the device has loaded the fixture and the pending jobs are idle.

1. With Companion open, check the November log and open its Delete
   confirmation. It must identify November 2026 with two pending audio
   records and one pending delivery. Save the confirmation HTML on the
   PC. Keep its session data private. Cancel and return to Home.
2. Mount and collect the before snapshot. Check the USB VID/PID
   `303A:0002`, dev serial and FAT32 volume. Eject and return to Companion.
3. Reopen the confirmation after ejecting. Check the same month and counts
   before approving. The reboot invalidates the previous confirmation.
   Wait for deletion and the Settings SD usage refresh, then return Home.
4. Mount the card again. Verify the after snapshot and eject safely.

~~~powershell
$fixture = 'C:/Git/Local-Dictation/build/pala-note-stage3-mcp-card-20261127'
$manifest = Join-Path $fixture 'manifest.json'
py -B $archive mount $bridgeState "$trialEvidence/archive-settings-before.json"
# Set $testCard to the current Pala Note SD drive.
py -B $archive before $testCard $manifest "$trialEvidence/archive-settings-before.json" "$trialEvidence/archive-before.json" "$trialEvidence/confirmation.html"
# After ejecting, confirming deletion and waiting for completion.
py -B $archive mount $bridgeState "$trialEvidence/archive-settings-after.json"
# Check the drive letter again before running the after comparison.
py -B $archive after-delete $testCard $manifest "$trialEvidence/archive-settings-after.json" "$trialEvidence/archive-after.json" "$trialEvidence/archive-before.json"
~~~

Pass when all November fixture files and work entries disappear, December
and the other baseline files keep their checksums, and Settings and the
FAT32 volume both report more free space. The fixture removes 4,544,738
logical bytes. FAT32 allocates in clusters. Record the actual disk delta.

For REC during deletion, restore the fixture and take a fresh baseline.
With the Note at Home, start `watch during-delete` in its own console,
then enter Companion, confirm deletion and take one short REC during the
operation. The recorder must accept it at a safe opportunity. After
completion, pass `--survivor-trace` with that run's trace to the after
comparison. It verifies the new recording's CRC and pending job. Confirm
actual overlap independently. A good after snapshot alone establishes
preservation and cannot place REC inside the delete operation. Use the
cases in [Stage 3 PC acceptance](pala-note-stage3-pc-tests.md#archive-log-view-and-month-deletion-cases)
for confirmation changes, active delivery and recovery. A physical power
cut requires an observed durable journal and incomplete deletion. Keep
those cases open when the timing evidence is unavailable.

## 4. Interrupt Ask Kubo during speech

Use an isolated paired Bridge that can give a spoken answer through the
normal Ask Kubo gesture. Stage the public question and a long response
before starting the measurement. Use the Note speaker as the cue.

~~~powershell
py -B -X utf8 $capture watch interrupt $bridgeState "$trialEvidence/interrupt.jsonl"
~~~

1. Hold the lower button to ask a public question with a long answer, such
   as asking Kubo to describe the route of the Danube in five sentences.
2. Once speech is audible, immediately hold upper REC and say
   **Modrý bicykel stojí pri bráne.** Release after speaking. Check the
   playback stops and does not restart. Check that the answer stays readable
   when returning from REC.
3. Save the transcript of that REC, return to Home and read the card over
   USB. Eject safely after the check.

~~~powershell
py -B $archive mount $bridgeState "$trialEvidence/interrupt-settings.json"
# Set $testCard to the current Pala Note SD drive.
py -B -X utf8 $capture verify "$trialEvidence/interrupt.jsonl" $testCard
~~~

The trace reports REC edge to playback shutdown, shutdown to first PCM,
and REC edge to first PCM, all from the Note clock. Playback shutdown is
measured in firmware after releasing audio I/O. Record the speaker and
screen observations separately. Pass when the trace shows a single
interrupted playback, valid microphone frame count and CRC, and the card
transcript contains the complete test sentence including the first word.

The rest of the release matrix, including live Workspace views and
provider checks, is in [Stage 3 PC acceptance](pala-note-stage3-pc-tests.md).
