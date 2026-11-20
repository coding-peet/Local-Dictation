# Kubo Note on Pala Note 2.0

Pair each Pala once with its owner's Bridge. It can then find that Bridge
after a DHCP address change on an approved private Wi-Fi network.

## Build and run checks

Run these commands from this directory. Use a Python environment with the
packages in `mac-bridge/requirements.txt` installed.

~~~powershell
py -B -m unittest discover -s tests -v
py -B -m platformio run -e pala_note_2
~~~

The first command exercises Bridge with isolated test identities. The second
compiles firmware for the 8 MB ESP32-S3 board. PlatformIO downloads the
versions pinned in `platformio.ini` if they are missing. Generate device-specific
pairing material locally with the setup tool before a paired deployment.

The Android companion is under `samsung`. Its Gradle wrapper uses JDK 17
and Android SDK 35. Keep the APK and Gradle build output outside Git.

## Synchronize recordings after waking Pala

Keep Local Dictation running on the paired Windows PC. Press the lower
button to wake Pala. When Pala is already awake on Home, the lower button
also starts a check if recordings are waiting.

1. When an active USB data connection is present, Pala first gives the
   paired Windows client time to authenticate over USB.
2. Otherwise Pala tries its paired HTTPS Bridge over Wi-Fi. Bridge must
   support the authenticated readiness endpoint below.
3. When the Wi-Fi Bridge is unreachable, Pala briefly advertises the
   Bluetooth LE transfer service. Keep Bluetooth enabled in Windows.
   The watcher uses the existing Pala Bridge pairing token and the
   BLE dependencies in `mac-bridge/requirements.txt`.

The client returns the local transcript to the same SD note over USB or
Bluetooth. The watcher releases the COM port between queue checks. An
empty USB check leaves the device's usual sleep deadline in effect.
If all routes fail, the original recording stays queued.

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

Open **Connections → Pala Note Configuration** in Local Dictation and set
**Note display name**. The default is **Kubo**. Use 1–10 letters, digits,
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

## Home weather and Moon

The paired Bridge's authenticated GET /pala/weather endpoint serves the
seven-day Open-Meteo forecast for Trnava in Celsius. Start the current
Bridge to make the forecast available. The device checks when idle,
after queued recording transfers. It keeps the last valid forecast
on SD and uses only the entry for today's local date. The Moon icon is
an approximate phase calculated from the device clock.

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

1. Open the Pala Web Companion and go to **Settings → Pala Bridge**.
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
