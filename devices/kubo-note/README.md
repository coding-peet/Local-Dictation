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
