# Pala Note Recorder Test 1

## Test setup

Use the second ESP32-S3 Note and a separate, empty FAT32 card. Have the
existing Note password, USB pairing and Wi-Fi pairing available if those
links will be tested. Keep the main Note powered off during the pilot. Use a dedicated Google Tasks test list for delivery.
Set the Bridge to **Transcription only** for tests that should stop at
**Waiting for delivery**.

The application image is for the Pala Note 8 MB partition table with app0
at 0x10000. A second device already on that layout can receive the application
image alone. Verify the device, layout, active slot and backup before writing.
An app0 write preserves the other partitions. Match the recorder test
version shown in Settings after restart.

The bundle includes the candidate Bridge source. It uses the existing
PalaBridge environment and paired profile. Run it in place of the regular
Bridge during the test so two processes do not claim the same USB, BLE or
HTTPS listener. The bundle contains no tokens or private profile.

## Build and Bridge entry points

Firmware environment from the repository root:

~~~powershell
$env:VIRTUAL_ENV = Join-Path $env:LOCALAPPDATA 'PalaBridge/app/.venv'
$env:PLATFORMIO_CORE_DIR = Join-Path $env:LOCALAPPDATA 'PalaBridge/platformio'
& "$env:VIRTUAL_ENV/Scripts/platformio.exe" run -d devices/kubo-note -e pala_note_recorder
~~~

To use the candidate Bridge from the extracted bundle on Windows, open
PowerShell in that folder. The commands use the current private Bridge
profile. Close the ordinary Bridge normally before starting the candidate.
Stop the candidate with Ctrl+C after the test.

~~~powershell
$env:VIRTUAL_ENV = Join-Path $env:LOCALAPPDATA 'PalaBridge/app/.venv'
$env:PYTHONPATH = "$env:VIRTUAL_ENV/Lib/site-packages"
py -B bridge/usb_sync.py --watch
~~~

For Wi-Fi, the paired Note connects to the candidate HTTPS listener in a
second PowerShell window:

~~~powershell
$env:VIRTUAL_ENV = Join-Path $env:LOCALAPPDATA 'PalaBridge/app/.venv'
$env:PYTHONPATH = "$env:VIRTUAL_ENV/Lib/site-packages"
py -B bridge/bridge.py
~~~

The USB watcher also provides the Windows BLE path. To test Wi-Fi and BLE
separately, unplug USB and select the desired link preference on the Note.

## First test

1. Wake from deep sleep. Hold REC as early as possible. Speak at the start
   and check the first words in the monthly archive. Repeat once during
   Home refresh and once while a transfer runs.
2. With manual sync and the cable already attached, record A. Triple-tap,
   wait for the transfer, then record B. Check A completes before B, with
   both WAV files in Archive and one transcript per recording.
3. On the test list, drop the link after a transcript is visible in Archive.
   Reconnect with the same or another individually paired Bridge on the same
   Tasks account and list. Check that the Note uses the saved text and the
   list contains a single item for the recording.
4. Repeat with BLE and Wi-Fi. Power-cycle the second Note with a pending item.
   Home and REC should be available while pending work loads. In manual
   sync mode the new run starts with a new triple-tap.
5. Open Settings for SD, RAM, battery and both pending counts. In Archive,
   play and download a WAV and download a month's transcript text.
6. Once the short smoke test passes, compare a blank archive with a populated
   archive while keeping the same pending count. Use the serial REC and
   first-write timestamps and an audible marker at the actual button edge.

## Scope of this image

This is the recorder and synchronization test milestone. Bridge delivers
WORK1 recordings to a configured Google Tasks list and reads back an
action marker before accepting completion. Unsupported destinations and
ambiguous external writes remain in Waiting for delivery. A changed account
or list cannot redirect an already claimed recording.

The capped active-store projection can hold 4,096 pending jobs. Test its
startup cost on physical FAT32 at representative queue sizes. Archive reads
use the selected directory. Month deletion, the full-storage audit, online
provider views and Ask Kubo are later milestones. The old local pages are disabled in this image. The archive supports playback,
WAV download and monthly transcript export.

### Measurements to retain

- Hardware and SD identity, firmware and Bridge hashes, queue and archive counts.
- Wake edge to PCM, REC edge to PCM, and REC edge to the first successful file write.
- WAV prefix/duration, active job state, and provider readback after each cut.
- USB, BLE and Wi-Fi results and the second-Bridge account/list configuration.
