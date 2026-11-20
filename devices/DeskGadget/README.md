# Desk gadget v0.5.1

Firmware for the monochrome Waveshare ESP32-S3-ePaper-1.54 V2 with 8 MB flash.
The display shows local time, date, weather, desk humidity, and an optional note
and timer. The device serves its own control page.

## First use

1. Power the unit over USB. Join the `Desk-XXXX` Wi-Fi network displayed on it.
2. Open `http://192.168.4.1/`. Sign in as `desk` with the password on the
   display. The same password is used to join the device Wi-Fi network.
3. In the **Wi-Fi a heslo** tab, enter the name and password of a permitted 2.4 GHz
   network. Select **Uložiť sieť**. Use the LAN address on the display when
   the desk gadget connects.
4. The prototype starts with the weather location set to Bratislava. Edit the
   latitude and longitude on the control page to use another location.

Short-press BOOT during normal operation to reveal the panel password again.
Hold BOOT for three seconds during normal operation and release it to open
the setup network. The setup network can control the note and timer directly.
Use **Nastaviť podľa PC** on that network to set the clock without internet.
After joining a network with internet, the clock automatically syncs over
SNTP, and **Synchronizovať čas** requests a fresh sync. Time uses Slovakia's
standard and daylight-saving offsets. The RTC holds UTC while powered.

## Saved Wi-Fi and device password

- Save up to five networks from the setup page. Choose **Nastaviť ako
  predvolenú** on the network you want it to try first. The other candidates
  are the last working network and then the remaining saved networks. When a connection
  already exists, select **Pripojiť teraz** if you want to switch immediately.
- To update a Wi-Fi password, enter the same network name with its new
  password. To remove a network, select **Zabudnúť sieť** next to it.
- **Heslo zariadenia** changes both the setup Wi-Fi password and the password
  for the `desk` web account. Use 8 to 16 letters or digits. Rejoin the setup
  Wi-Fi with the new password after saving. If the PC keeps trying the old
  password, forget the setup network and join it again.
- The eye button next to a password field toggles visibility of that field.

### Import a personal Wi-Fi profile from Windows

Use `netsh wlan show profiles` to see the profile names without showing
passwords. Keep USB connected. Hold BOOT on the running gadget for three
seconds, release it, and run the importer within one minute.

```powershell
.\Import-WindowsWiFi.ps1 -ProfileName 'Exact Windows profile name'
```

The script copies the selected personal WPA2 or WPA3 profile over USB if
Windows allows key access. Credentials are kept in memory and are neither
printed nor exported to a file on the PC. The board saves the profile to NVS
and updates the encrypted SD backup when available. An existing connection
stays active. The regular Wi-Fi tab can make the imported network preferred.

The importer does not copy group policy profiles or enterprise credentials.
When Windows denies key access, use the device's setup page to enter a Wi-Fi
password that you are authorized to use. The network must offer 2.4 GHz.

### Encrypted settings on the SD card

Power off the gadget, insert a FAT32 microSD card and power it on. The Wi-Fi
tab reports whether the backup is ready. The firmware writes the saved networks,
preferred network, device password, weather location and pinned note to
`desk-settings.bin` using AES-256-GCM. It verifies the file after writing it.
The firmware does not format the card. Existing `desk-wifi.bin` files are
kept as legacy backups and no longer updated.

The encryption key stays in the board's NVS. A normal Arduino upload preserves
NVS. The gadget can restore missing settings from the card using the existing
key. A full flash erase also removes that key, so the card alone cannot
restore settings after a full erase or on a different board. Use the
normal upload with separate firmware segments when retaining settings. The
merged binary fills the complete flash and is for clean deployment only.

An active timer and the Outlook preview are temporary. Microsoft's sign-in
session remains on the PC.

## Outlook

Open **Outlook** in the web panel for the next meeting and countdown. When
calendar data is fresh, the display shows the start time and time remaining
under the weather and humidity.

On this PC, open PowerShell in the `DeskGadget` folder and run:

```powershell
.\OutlookBridge.ps1
```

Sign in to the intended Microsoft account and grant `Calendars.ReadBasic` if
your organization permits it. This scope allows basic calendar reads. The
script uses the default calendar and needs the USB cable on `COM24` (use
`-Port` if the port changes). It sends the next eligible start time once per
minute. Cancelled and declined events and all-day items are skipped.

By default the gadget shows a generic meeting label. Start the script with
`-ShowTitle` to display titles of ordinary events. The title of a private or
confidential event is replaced with a generic label. Keep PowerShell open while
using the bridge. Ctrl+C stops it. The device expires calendar data after five
minutes without a refresh.

The script uses Microsoft's `Microsoft.Graph.Authentication` 2.41.2 module
from `%LOCALAPPDATA%\PalaDesk\Modules` on this PC. An installed module of the
same name also works. Use `-Mode Demo` for a synthetic meeting without signing
in, or `-Mode Clear` to remove a test meeting.

The note is saved in flash. The display shows up to two lines of it and the
web panel keeps the full text. The countdown clears on reboot. The display
updates the countdown by minute, then shows a finished message. Weather data
comes from [Open-Meteo](https://open-meteo.com/) for this personal prototype.
The e-paper weather percentage is the highest hourly precipitation probability
in the forecast window. An asterisk marks weather older than one hour.

## Build

Use Arduino CLI with Arduino ESP32 core 3.2.0, GxEPD2 1.6.5 and ArduinoJson 7.4.2.
Run this from the `DeskGadget` source directory.

```powershell
arduino-cli compile --fqbn 'esp32:esp32:esp32s3:FlashSize=8M,PartitionScheme=default_8MB,CDCOnBoot=cdc,USBMode=hwcdc,FlashMode=dio,PSRAM=disabled' .
```

The command compiles the firmware locally. It does not write to the device.
Back up the flash before uploading from a different firmware, and verify the
target COM port and ESP32 revision before uploading.
