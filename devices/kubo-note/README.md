# Kubo Note on Pala Note 2.0

Pair each Pala once with its owner's Bridge. It can then find that Bridge
after a DHCP address change on an approved private Wi-Fi network.

## Open the shared settings page

On Windows, open **Connections → Note Configuration → Open Note
settings** in Local Dictation. The local Bridge uses the paired USB data
connection or a Bluetooth connection. To make an awake Note available by
Bluetooth with an empty recording queue, briefly press and release both
buttons on its Home screen, then refresh the page.

For first pairing, use **Pair over USB** or **Pair over Bluetooth**. Over
USB, check the computer name on the Note and follow the button prompt.
Over Bluetooth, compare the six-digit codes on both devices before
confirming. The Note labels the upper button for confirmation and the
lower button for cancellation.

Hold both buttons on the Note for three seconds to open Web Companion.
The display shows **WEB** in the center, battery on the right, and the
current Wi-Fi network and browser address below. Tap the upper or lower
button to adjust speaker volume. Five bars appear briefly. When the
Note opens its hotspot, the display shows the hotspot name, password and
address. Open **Setup** for the same Note settings page in the phone
browser. Web Companion and the shared settings show the saved Note name. Both entry points use
**Wi-Fi networks** below the Note card. If the Note is unavailable,
the section shows how to reconnect and disables the form.

The page manages the Note's name, language, Wi-Fi profiles, paired
clients and optional Bridge IPv4 fallback. Bridge name and weather location
are editable when the page is opened from that Bridge. An added Bridge has its own credential. The Note keeps at most
five pairings and shows the list in the page. When Bridge is unavailable,
the Note searches the remaining eligible bridges in order after trying
the last one that worked.

## Codex usage on Home

When Kubo processing is enabled, Home shows the remaining Codex allowance
next to the Note name. In **Note settings → Note**, choose **5 hours
(weekly fallback)** or **Weekly** and save the Note settings. Bridge reads
both available windows from the signed-in Codex CLI account status.
On Windows, Bridge can also find the registered Codex desktop package when
its CLI is absent from PATH. It starts its own background CLI process to
read the quota as soon as Bridge starts in Kubo processing mode.

The percentage is the remaining allowance. The small blocks underneath show
the time until reset in approximate hours or days. A partially remaining hour
or day counts as one block. In the weekly view, blocks become low marks from
left to right as the week elapses. The rightmost block stays tall until the
reset. The blocks appear when Codex reports a reset time.
The pending recording count and waveform sit just left of the connection
and battery indicators. The percentage and blocks stay visible as long as
the name and recording count leave enough room in the header.

## Note password

Open the paired Note over local data USB in **Local Dictation → Connections →
Open Note settings**. Set the password in **Note password** before first using
Web Companion over Wi-Fi or Note settings over Bluetooth. Use 8 to 48
printable ASCII characters. The Note has one password and no username.
Use the eye beside a password field to show or hide its current entry.
The strength meter for new passwords is informational. Save accepts
supported passwords at every rating.

Password protection defaults to on. From the paired USB settings page,
use **Require password** under **Note password** to change the policy.
Turning it off opens Web Companion without password sign-in. Bluetooth
settings still need their existing pairing. Turning it back on uses the
saved Note password, or requests setup over USB if no password was set.

With protection on, sign in on Wi-Fi before opening Web Companion, reading notes,
playing audio or using settings and Trash. On Bluetooth, sign in from the
Local Dictation settings page after the connection is paired. The locked
page shows the password form. Sign in to open settings, or sign out later
in **Note password**. A session expires after 15 minutes without a request.

To change a password over Wi-Fi or Bluetooth, enter the current password
and enter the new password twice. For initial setup or password recovery,
connect by local data USB and open the same form. Enter the new password
twice and leave the current password blank.

Settings forms show an unsaved reminder after an edit. Note settings use the
saved device name in that reminder. Save each form after reviewing its changes.

The Note's Wi-Fi Web Companion uses HTTP. Traffic is unencrypted on the
network. Use the Note's password-protected hotspot or a trusted private
network for Wi-Fi browser access. SD-card recordings remain readable
from the card. Paired Bridge transcription uses its machine authentication.

## Configure display pages

Open **Display pages** in the shared Note settings. Set each checkbox,
use **Move up** or **Move down**, then choose **Save display pages**. The
trial starts with **Tasks**, **Events**, **Current month**, and **Next month**
in that order. **Month after next** is available independently and starts
disabled. **Home** is fixed at the bottom of the settings list. Read upward
from Home to see the order used by the upper button. A disabled page retains
its position in the saved order.

On the awake Home screen, tap the upper button to open the first enabled
page. Tap it again to page through Tasks or Events and continue to the
following enabled views. **Return to Home after the top page** controls the
next upper tap at the end. Clear it to stay on the last enabled page.
On any display page, the lower button returns Home and holding the upper
button starts a recording. With every page disabled, a short upper tap on
Home leaves Home visible.

The month grids show Monday through Sunday and ISO week numbers. The
current month highlights today's date. All three use the Note's local
clock and can be viewed offline.

## Sleep timeout

In shared Note settings, set **Sleep timeout** from 1 to 99 minutes and
choose **Save timeout**. This is the idle limit on Home and in Web
Companion. In-flight processing can finish and save its result.

Choose **Default** to restore the original behavior: 1 minute on Home,
up to 3 minutes when work is queued, and 5 minutes in Web Companion.

## Trash

Use **Move to Trash** in Web Companion to remove a memory from the active
list. The Note keeps the recording, transcript, and related files on the
SD card. Restoring returns the memory to its original place.

On Web Companion Home, choose **Trash** beside **Export** under **Memories**.
Trash uses the same card layout and paging as Memories. Use **Restore**
on an item to return it to the active list. Shared Note settings also
supports restoring items and changing retention over USB or Bluetooth.

The default retention is 90 days. Change it to any value from 1 to 3650 days. Expiry is
checked while the Note is awake with a valid clock. **Empty Trash**
permanently deletes the items in Trash and requires explicit confirmation.

## Storage indexes and manual sync

The Note keeps its recordings, transcripts, and task state on the SD card.
Tasks and Events use a rebuildable disk index with a limited page buffer.
The sync queue is separate and retains recordings that are still pending.

Home and recording are available during routine recovery. A manual sync
requested during recovery starts when the queue is ready. Rebooting clears
that request and retains any recordings still waiting for a new sync.

You can hold REC again as soon as Saving appears. A small disk and caption
on the new recording indicate that the previous recording is still being
saved. The indicator clears on the next available panel refresh after that
save finishes. Each of the two microphone buffers holds 16.384 seconds at
16 kHz if the card stops accepting data. The writer reuses space as it
drains each buffer, so normal recordings can be longer.

To record during sync, hold the upper button as usual. The Note releases
the active transport at a safe boundary. After saving the recording, it
resumes the interrupted item before continuing through the queue, including
the recording just saved.

## Restart and storage checks

In **Note settings**, choose **Restart** or **Restart and check storage**.
Press the lower button (Yes) to restart or the upper button (No) to cancel.

The full check can take several minutes. It verifies the catalog, queue,
metadata, and WAV headers and sizes. The screen shows check progress.
Recording is available after completion. The Note reports source-file
problems and can rebuild derived indexes. It leaves audio payloads alone.

After a power loss during the check, the Note starts normally and Settings
reports that the check is incomplete. When a previous run has supplied a
timing measurement, Settings shows an estimate for the current archive.

Companion shows current and peak internal RAM and PSRAM under the battery.
Note settings show the same values and battery percentage. The peaks
survive normal deep sleep and reset on a restart or power cycle.

## Aquarium

From the awake Home screen, press the lower button three times quickly.
The clownfish peeks out of the anemone and swims beside it.

- Press the upper button once to drop a flake. Separate single presses
  each request a new flake.
- Press the lower button once for a current. The fish turns into the flow
  and circles the anemone as the water settles. The anemone polyps bend
  with the current and straighten as it fades.
- Press the lower button twice to swim to the torch coral. Repeat
  the double press to return to the anemone. Both sites support the same
  feeding, current, and closing actions.
- Press the upper button twice to end the visit. After inactivity the fish
  performs the same closing sequence and returns Home.

The aquarium keeps up to eight recognized commands while an animation plays.
It starts them in the order pressed. Once the closing animation begins, it
clears any later commands. The aquarium runs independently of recording, transcription, and
Internet access.

To regenerate both reefs' 200 × 200 frames and a preview, run the
exporter from the repository root with the local Microsoft Edge executable
and a build output directory.

~~~powershell
node devices/kubo-note/art/export_aquarium_reef.mjs $edgePath $outputDir
~~~

The exporter renders locally and writes both atlases to the output directory.
Copy the verified aquarium_frames.inc and aquarium_reef_frames.inc into
pala_note/src/app to use them in a firmware build.

## Bridge processing

In Local Dictation under **Connections**, choose the Codex model used
for Kubo processing and the model's reasoning effort. High is the
default. Changes apply to the next request.

The **Note transcripts** window in Connections shows the original text
retained by Bridge and the related routing and delivery decisions. See
[Bridge routing rules](BRIDGE_ROUTING_RULES.md) for the current rules and
observed failures.

Open Note settings from Local Dictation and select **Bridge**. With **Kubo
processing** off, Bridge runs local transcription and returns the text to
Note. With it on, specify an existing default Google Tasks list and the ID
of your personal calendar. Use `primary` for the personal primary calendar.

Run `codex login` and check the sign-in from Bridge settings. Run the
following in the Bridge Python environment to sign in to Google separately:

~~~powershell
py -B .\mac-bridge\bridge.py --tasks-login
py -B .\mac-bridge\bridge.py --google-login
~~~

These commands save the corresponding authorization on the Bridge computer.
They do not create tasks or appointments. An existing Google OAuth client
configuration is required by these Google sign-in commands.

### Windows voice for Ask Kubo

Bridge can synthesize answers locally with Piper. For Slovak, use the
[Piper Lili voice](https://huggingface.co/rhasspy/piper-voices/tree/main/sk/sk_SK/lili/medium).
Keep its ONNX model and adjacent `.onnx.json` file outside the repository.
Install `piper-tts==1.8.0` and `scipy` in a dedicated Python environment.

Save `speech-voice.json` in `%LOCALAPPDATA%\PalaBridge` with the absolute
paths to that environment's Python executable and the voice model.

~~~json
{
  "python": "C:/AI/LocalDictation/tts/piper-lili/.venv/Scripts/python.exe",
  "model": "C:/AI/LocalDictation/tts/piper-lili/sk_SK-lili-medium.onnx"
}
~~~

Bridge reads the profile for each answer and produces 24 kHz mono PCM for
the Note. Profiles without this file use the installed Windows speech voice.

## Build and run checks

Run these commands from this directory. Use a Python environment with the
packages in `mac-bridge/requirements.txt` installed.

~~~powershell
py -B -m unittest discover -s tests -v
py -B -m platformio run -e pala_note_bridge
~~~

The first command exercises Bridge with isolated test identities. The second
compiles firmware for the 8 MB ESP32-S3 board. PlatformIO downloads the
versions pinned in platformio.ini if they are missing. Use the Bridge build
target for a fresh Note and enroll it from the shared settings page. Include
the previous private pairing material when migrating a Note that already
uses the older compiled-in pairing. The pala_note_2 target with no pairing
header keeps the original standalone provider setup.

The Android companion is under `samsung`. Its Gradle wrapper uses JDK 17
and Android SDK 35. Keep the APK and Gradle build output outside Git.

## Synchronize recordings

Keep Local Dictation running on the paired Windows PC. Press the lower
button to wake Pala. On the awake **Home** screen, the lower button has
four actions.

| Gesture | Action |
| --- | --- |
| One tap | Open Events if enabled in Display pages |
| Two quick taps | Play the weekly briefing |
| Three quick taps | Open the aquarium |
| Hold | Ask Kubo. Keep holding while you speak and release to finish the question. |

The Note waits briefly after the first or second tap to distinguish the
gestures. On a display page, the lower button returns to Home. On Home,
tap the upper button three times to synchronize queued recordings.

In **Note settings → Recording sync**, choose when automatic recording
sync starts and select **Save sync timing**.

- **Immediately on wake** starts processing the existing queue on wake.
- **After recording** starts processing the queue when a recording is saved.
- **Before sleep** is the default. When Home has been idle for the configured
  timeout, the Note allows up to two more minutes to start and drain queued work.
  A stage already running can finish safely.
- **Manual only** leaves recordings queued until you trigger sync.

The upper-button triple press works in every mode.

Open **Wireless connections** to enable or disable Wi-Fi and Bluetooth
independently. Existing Notes start with both enabled. Disabling a radio
keeps its saved networks or pairings. It also closes any settings session
using that radio. Connect by USB to turn either radio back on.

Open **Sync connection priority** and select **Wi-Fi first** or
**Bluetooth first** for recording sync. USB always has priority. A
Bluetooth-first attempt gives a paired PC a short window to pick up audio,
then tries Wi-Fi. Processing that requires a Wi-Fi Bridge still uses Wi-Fi.

1. When an active USB data connection is present, Pala first gives the
   paired Windows client time to authenticate over USB.
2. Otherwise Pala tries its paired HTTPS Bridge over Wi-Fi. Bridge must
   support the authenticated readiness endpoint below.
3. When the Wi-Fi Bridge is unreachable, Pala briefly advertises the
   Bluetooth LE transfer service. Keep Bluetooth enabled in Windows.
   Look for the configured display name plus `-Note`, such as `Kubo-Note`,
   during this short discovery window. Bridge discovers and authenticates
   it through the existing pairing. Its Windows environment needs the
   BLE dependencies listed in `mac-bridge/requirements.txt`.

BLE v2 requires matching firmware and Bridge code. Each side contributes
a fresh challenge. Both verify the peer's proof using the existing pairing
before transferring encrypted recording data. Older BLE peers leave the
recording queued.

When both BLE v2 peers support binary audio, the Note sends the original
WAV in encrypted blocks through the existing notification characteristic.
The Bridge verifies each block before transcription. Older v2 firmware
continues to use the original block reads over the paired connection.

The client returns the local transcript to the same SD note over USB or
Bluetooth. The watcher releases the COM port between queue checks. An
empty USB check leaves the device's usual sleep deadline in effect.
If all routes fail, the original recording stays queued.

The paired Bluetooth connection also updates the Codex quota and the
open General count on Home. Open Note settings over Bluetooth to refresh
them when there are no recordings queued. While Google delivery is pending,
the Note also opens a short Bluetooth window automatically to update Home.
The Bridge sends the latest Home data over the paired link without fetching
recordings. The Note briefly delays sleep for these checks and resumes them
on the next wake if delivery is still pending.

### Manual USB check

For a manual one-recording check, run the following from the Local Dictation
repository root while the automated watcher is idle.

~~~powershell
$env:VIRTUAL_ENV = Join-Path $env:LOCALAPPDATA 'PalaBridge/app/.venv'
Invoke-RestMethod http://127.0.0.1:48125/health
py -B -m serial.tools.list_ports -v
py -B devices/kubo-note/mac-bridge/usb_sync.py --port COM24
~~~

Use the COM port found for Pala and optionally add `--note` followed by its
pending note number. The command uses existing pairing, transcribes one
stored WAV through Local Dictation and saves the result on Pala. This USB
path keeps the transcript local and preserves the WAV on the SD card.
A disconnected session expires or can be cancelled with a device button.

To read the stored text, wake Pala and hold both buttons for three seconds
from Home to open the Web Companion. Use the address on the display from
the same private network, or join Pala's hotspot if the display offers it.
In **Memories**, locate the note by number and use **Download TXT** for
the full transcript.

USB transcription stores text on the note's SD card and uses the Local
Dictation API. It does not add a desktop History entry. Google routing is
available when the private Bridge has approved destination bindings.

### Process saved transcripts over Bluetooth

With Kubo processing enabled in Bridge, recording sync over a paired
Bluetooth link also picks up saved transcripts waiting for Kubo. Wi-Fi
and USB can both be off. Start sync from Home with three presses of the
upper button.

Note shows the date, local start time, and duration on one line, for
example '25.11.2026 15:00 / 30 min'. If there are two date candidates,
select the intended date and then confirm the displayed event. Use
the lower button to decline it.
Leaving the prompt unanswered keeps it pending for a later sync.
Bridge adds an event to Google Calendar after that event is confirmed.

The counter shows the items eligible for the current Bluetooth session.
In Kubo mode, a recording counts when the structured result is saved.
In transcription-only mode, it counts when the transcript is saved.
An interrupted item stays in the queue.

An explicit list name is looked up in the connected Google Tasks account.
For a shopping command without a named list, Bridge uses the configured
shopping list or looks for an existing list called Nákup. A missing or
ambiguous target keeps the delivery pending. A spoken calendar command can
appear before or after the appointment details.

## Verify one saved transcript in Google

Export one existing TXT from Pala and keep its original note number. The
command below identifies the route locally. It neither invokes ASR nor
opens any other recording.

~~~powershell
py -B devices/kubo-note/mac-bridge/pala_google.py --transcript 'C:\path\note_20.txt' --note 20
~~~

Use the Bridge Python environment above. With the authorized Tasks
account and the agreed trial list, a clearly dictated task can be tested
with `--task-list 'Kubo notes general' --apply`. Use
`--general-list 'Kubo notes general' --apply` for an ordinary personal
transcript. Short dictations fit in the Google Tasks title alone. A longer
dictation uses up to three topic words from across the transcript as its
title. The complete transcript stays in details. Title selection runs
locally and excludes common Slovak filler words.
For a Docs trial use the
approved document ID with `--docs-id`, an authorized Docs token, and the
original recording time with `--captured`. Do not substitute the import
date for the recording date. The export candidate also accepts `--usb-port`
in place of `--transcript` and reads the existing text and capture time
directly from the paired device.

Automatic USB delivery uses explicit `google_routes` bindings in the
private Bridge configuration. Each Tasks, shopping, sorting, General
or Docs binding contains the verified provider `id` and `title`.
A General binding can set `provider` to `docs` for a document. A named list
or document uses a binding such as `list:moj nakup` or `document:kubo napady`.
Say the title followed by a pause that transcribes as a colon or comma,
or use a title in quotation marks. An unresolved named destination stays
pending.

With the bindings in place, the USB client stages a delivery before saving
its text to Pala and makes the delivery eligible after Pala acknowledges the
save. The local receipt keeps a verified delivery complete after Google
edits or deletions. If Google returns no task ID and the write cannot be
verified, keep it uncertain to prevent a duplicate. Work instructions
and appointment proposals remain local. Appointments still require
confirmation before any Google Calendar write.

## Device name

In Recorder Web Companion, use **Note → Name** to name the device (up to
12 characters). For example, **Kubo Note** opens at http://kubo-note.local
and **kubo-backup** at http://kubo-backup.local on the same Wi-Fi network.
Reopen Companion after saving a new name. Its e-paper screen shows the
active address and the fallback IP. Hotspot mode shows the IP.

### Legacy Bridge display name

Open **Connections → Pala Note Configuration** in Local Dictation and set
**Note display name**. The default is **Note**. Use 1–10 letters, digits,
spaces or hyphens, starting with a letter or digit. The paired Note stores
the name in its settings and uses it after a reboot. Local Dictation sends
the name on paired USB and Bluetooth sync; the Note can also fetch it
from the paired HTTPS Bridge when it connects over Wi-Fi.

To verify the name on a paired USB port without processing recordings:

~~~powershell
py -B devices/kubo-note/mac-bridge/usb_sync.py --port COM24 --name-only
~~~

Use the actual paired port. A successful response verifies that the Note
saved the name. The existing pairing and DNS identity stay in use.

## Home delivery status

**General** shows the number of unfinished items in the configured Google
Tasks General list. Zero means the list has no unfinished items. A dash
means the count is unavailable. The Note keeps its latest snapshot offline.

A stopwatch beside **Google** shows how many dictations are waiting for
confirmation. Once the queue is empty, a check mark beside **Delivered**
(**Doručené** in Slovak) appears above the local time of the last confirmed
delivery. A plain **Google** label with a dash means there is no confirmed
time to display.

After submitting a note over Wi-Fi, the Note fetches an update on the same
connection and briefly checks again while delivery is pending. Bluetooth
checks also run automatically while Home shows a pending delivery.

Connect the paired Note by USB to transfer and verify only the counters:

~~~powershell
py -B devices/kubo-note/mac-bridge/usb_sync.py --port COM24 --delivery-only
~~~

This check leaves recordings and Google destinations untouched.

## Home weather and Moon

Set **Weather location** in the shared page opened from **Connections → Pala Note Configuration**.
Enter a town. Add a two-letter country code for a narrower search, for
example `Bratislava, SK`. Clearing the field disables weather updates.

The paired Bridge resolves the saved town with Open-Meteo and supplies up
to seven days of Celsius forecasts. Start Bridge for Wi-Fi weather updates.
The device checks when idle, after queued recording transfers. It caches
the forecast on SD for offline use and displays today's local entry. The
Moon icon uses the device clock.

The current Windows watcher also sends the available forecast on paired
USB attachment and during a paired Bluetooth sync. This check can run when
the transcription service is busy. The watcher uses the cached provider
response when it is fresh. To send and verify weather over USB without
touching the recording queue, run this from the repository root in the
Bridge Python environment:

~~~powershell
py -B devices/kubo-note/mac-bridge/usb_sync.py --port COM24 --weather-only
~~~

Use the actual paired port. This mode reads back the committed SD cache
and verifies the values selected for the device's local date.

Weather data by [Open-Meteo](https://open-meteo.com/). The Moon phase
estimate uses the January 2000 epoch from
[NASA's Moon phase tables](https://eclipse.gsfc.nasa.gov/phase/phases1901.html).

## Stage 3 candidate

Build `pala_note_stage3` for Calendar, Saved items, Ask Kubo and Archive.
From Home, an upper triple tap refreshes Calendar and Saved items over
paired Wi-Fi. An upper single tap opens Calendar, with the next 14 days
and up to 32 rows from Google Tasks and the primary Google calendar.
A lower single tap opens Saved items, with the selected list's first 32
open items in provider order. The last Calendar page also leads to Saved items.
Each view shows when the last successful update is being used offline.

Hold the lower button on Home to ask a public question or read open scheduled
Tasks for today, tomorrow or the next seven days. The paired Bridge keeps
Tasks reads local to that route. Calendar and Tasks changes use dictation
and delivery. The existing voice player can stop an answer with REC.

Under Settings, open Archive recordings and select a month. View logs shows
local recording and transcript status and streams any log files in that
month as plain text. Delete month shows the two pending counts before a
destructive confirmation. Files and work approved in that confirmation
are journaled on the card. A reboot resumes the journal. A new recording
is outside the approved set. Google items stay in the account. The Note
resamples SD usage after deletion once recording and sync are idle. Settings
reads the cached totals.

The smaller `pala_note_workspace_calendar` environment is available for
Calendar-only qualification.

Use a dedicated candidate Bridge profile. Set `workspace_calendar.note_id` to
the selected Note's paired device ID and `workspace_calendar.saved_list_id` to
the stable Tasks list ID. The optional `workspace_calendar.zone` defaults to
`Europe/Bratislava`.

Run the existing `bridge.py --workspace-task-views` verification with
`--calendar-id primary`. Adding `--save-bridge-grant` requests renewable
read access. After a successful read it creates `workspace-read-token.json`
in the local Bridge profile. The profile must stay private. A read without
that option is temporary. Restart the candidate Bridge to load a new grant.
Google's external testing mode expires renewable grants after seven days.

## Calendar holidays

In the shared Pala Note configuration page, set **Holiday country** next to
**Weather location** in the Bridge section. Slovakia is the default.
Czechia is also available. The setting applies to the paired Bridge.

During USB, Wi-Fi, or Bluetooth sync, Bridge sends the selected country's
nationwide statutory days of rest for the current and following calendar
year. It also includes dates in the preceding December 26–31 so the Home
week can cross New Year. The Note saves the calendar locally and displays
holiday day numbers in bold on Home and in each monthly view. Today keeps
its black background.

The Slovak calendar follows [Act 241/1993 Coll., sections 2 and 4b](https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/1993/241/20261029).
May 8 and September 15 are excluded for 2026 and resume in 2027 under the
current law. The Czech calendar follows [Act 245/2000 Coll., sections 1–3](https://e-sbirka.gov.cz/sb/2000/245).
Bridge computes Easter-related dates locally. Future changes to holiday laws
require updating the Bridge rules.

## Transcription on Windows

Run the Local Dictation API on the same computer as Pala Bridge.
In the Windows candidate with the Connections page, manage the address
under **Connections → Advanced**. In earlier builds, use
**General → Advanced → Local transcription service**. Bridge reads the
saved address and verifies the service before sending audio.
The default address is http://127.0.0.1:48125.

Paired clients can use the authenticated `GET /pala/ready` endpoint to check
Windows transcription readiness before uploading. Bridge returns HTTP 200
when the Local Dictation API is ready and idle, or HTTP 503 when it cannot
accept the work. The response contains only the `ready` boolean.

Use `bridge.py --managed` when starting Bridge as an owned child process with
redirected standard input. Close that pipe to stop discovery and release the
listeners. Pala retains a recording on SD while its upload needs to be retried.

## Google Tasks pilot

Run the pilot from one PC with the Bridge Python environment. Register a
Desktop app OAuth client in the personal Google Cloud project and enable
Google Tasks API first.

~~~powershell
py -B mac-bridge/google_tasks.py pilot --client-secrets 'C:\path\to\oauth-client.json'
~~~

The JSON file is the credential downloaded from Google Cloud Console.
Keep it in private local storage. The browser asks you to sign in and
approve Tasks access. The pilot stores a separate Tasks token in the
private Pala Bridge state directory.

The command creates or reuses **Kubo pilot**, adds the synthetic item
**mlieko**, and reads it back. Repeating the command reuses the marked item if
its pilot marker in the Notes field is still present. After a successful
run, open Google Tasks with the same account on PC and Galaxy to check
the item and confirm a manual edit.

## Use the manual address

1. Open the Pala Web Companion and visit /settings at the displayed address. Use the legacy **Pala Bridge** form for its manual fallback address.
2. Enter the PC's private IPv4 address under **Fallback IPv4 address**.
3. Save the address. Leave the field empty to clear it.

Pala tries automatic discovery first. It tries the fallback address only
on the connected Wi-Fi subnet. Both paths require the paired Bridge's
TLS certificate before sending the device credential or a recording.

## Update an existing installation

1. Stop the existing Bridge. Back up its private state and the current
   firmware before changing pairing or flashing. On Windows, the state
   is under `%LOCALAPPDATA%\PalaBridge`. Keep the backup off shared storage.
2. Install the dependencies from `mac-bridge/requirements.txt` into the
   Bridge environment. Keep downloaded wheels outside the source tree.
3. Use the updated `mac-bridge/setup_window.py`. Select the Pala's USB
   port and the PC's IPv4 address on an approved private network. Preserve
   the existing model, destination and calendar settings. The installer
   adds the selected address's current subnet and accepts additional
   approved private networks in CIDR notation, such as `192.168.1.0/24`.
4. Pair and upload once. Start the updated Bridge on the PC.

The updated Bridge retains the original IP certificate and token for
existing clients at the original address. Its discovery identity, key and
per-device credentials belong to the local Bridge state. Use fresh state
for each colleague's Bridge. Share source files only.

On a network that blocks multicast, use the fallback IPv4 address if the
peer can still reach TCP 8443. Discovery needs mDNS on UDP 5353. On a
managed PC, have IT authorize the applicable network before enabling
either service. If the PC moves to an unlisted subnet, stop the Bridge
and add that approved CIDR to the `discovery_networks` list in its local
`config.json`. Then start the Bridge again. The device pairing requires
no change.

## Verification

Run the isolated Windows tests with the two mDNS dependencies available:

~~~powershell
py -B -m unittest discover -s tests -v
~~~

The tests use temporary identities and localhost. They cover distinct
device credentials, lookup of the exact mDNS name, address changes,
rejection of a second Bridge, and the original IP-based TLS connection.

For device acceptance, pair two Palas to separate Bridge installations
on the same authorized network. Check that each device uses its own
Bridge, that a DHCP change needs no new pairing, and that a manual IP
works for the paired Bridge when mDNS is unavailable. Point the fallback
at the other Bridge to verify that the recording stays queued.
