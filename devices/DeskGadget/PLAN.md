# Desk gadget v0.1

- Confirm the Waveshare revision from ESP32-S3 flash capacity and save a complete flash backup before writing.
- Drive the monochrome display and SHTC3. Show local date/time from an SNTP-synchronised clock and restore UTC from the PCF85063 when available.
- Fetch local weather over HTTPS. Show when weather is missing or stale.
- Run a local web panel for Wi-Fi setup, a pinned note, a monotonic countdown, and immediate SNTP synchronisation. Store the note and Wi-Fi setup across reboots.
- Build, verify binary/flash, and check the device over serial and HTTP where available. Outlook is outside this version.
