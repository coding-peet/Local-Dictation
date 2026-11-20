# Kubo Note integration

Kubo Note runs on Pala Note hardware. Its paired Windows Bridge and the Local
Dictation client use the same local speech recognition API. Share one firmware
source. Set pairing, network and account details for each installation.

## Transfer and Windows controls

Discover the paired Bridge by its stable local hostname. Support manual
fallback to a private IPv4 address on the connected Wi-Fi subnet. Verify the
paired TLS certificate and use the device-specific token.

Waking Pala with the lower button starts the queue check. Give a data USB
connection a short window for the paired Local Dictation watcher to
authenticate. Otherwise try the paired Windows Bridge over HTTPS. Check its
authenticated readiness before sending a recording. If it is unreachable,
advertise the recording service over Bluetooth Low Energy for a short window.
An authenticated Bridge that reports a busy local service keeps the work queued.

Bluetooth uses the existing Pala Bridge pairing token and the same recording
transfer and local save acknowledgment as USB. A fresh connection challenge
and authenticated encryption protect the Bluetooth frames. Local Dictation
discovers the paired device in the background, with no further interaction
after pairing. USB and Bluetooth share one device session and keep the SD
recording until its result is saved. Verify the cable, Wi-Fi and Bluetooth
paths on the actual device and the Windows PC.

Keep the recording durable during outages. Give each recording a stable identity
across retries and preserve the result of any accepted write. A retry after a
lost acknowledgment reuses that result. Qualify recovery after restarting the
Bridge and after switching paired PCs.

Use the existing Local Dictation tray menu for Bridge status and restart. Use
Connections for details, recent errors, pairing and local service controls.
When enabled, Bridge starts with Local Dictation. Closing the main window
keeps the process running. The existing tray Exit stops it gracefully while
the recording queue stays recoverable. The shared installer remains part of
the planned integration.

## Recording time, dates, and confirmation

Derive relative dates from the validated recording timestamp in the device's
local time zone. Preserve the recording timestamp across transfer, retry and
clock corrections after capture.

- From 00:00 through 03:59 local recording time, show both candidate calendar
  dates for “zajtra” (tomorrow) in the confirmation. A Saturday recording at
  01:30 that says “zajtra o 15:00” offers Saturday and Sunday at 15:00.
- At 04:00 and later, “zajtra” means the next calendar day.
- When the year is absent, propose the nearest future occurrence within one
  year of the local recording time. Show the full date including the year.
- Confirm the concrete date and time before writing to the calendar. Allow a
  date in the past only on explicit instruction.
- When the intended appointment expired while a recording was queued,
  preserve the transcript and the original proposed date. Mark it as expired
  and ask for a replacement date in the confirmation.

Cover midnight, year and week boundaries, a daylight-saving transition, and a
delayed transfer in date verification. Settle the confirmation behavior for
missing or invalid recording timestamps during implementation design.

## Personal and work routes

Send each ordinary personal recording to the configured Google General list
or document, one entry per recording with the full saved transcript. Pala
is the recording device and Kubo handles the processing in the background.
The user finds results in the Google apps. A clear spoken destination
selects the corresponding approved route.

Use the approved Google Tasks lists for personal tasks and shopping. Keep the
existing sorting list for ambiguous routing. Bind selected destinations by ID.
Complete the existing synthetic Google Tasks pilot after physical transfer.

A shopping dictation such as “maslo a rožky” creates two independently
checkable entries. A second distinct request for an existing open item in the
same list keeps one item and adds “Opakovaná požiadavka (x2)” to its note.
Preserve existing note text. A retry of the same recording applies its effect
once. Verify the result in Google Tasks on Galaxy.

Append explicit personal notes with a date to the selected Google Docs
document **Kubo notes general**. Put confirmed personal appointments in
Google Calendar. Keep is for manual notes. Treat “Keep” as ordinary text
in dictation. Prepare work dictations locally for manual transfer to OneNote,
Planner or the work calendar.

## Voice questions

Include Slovak answers on the display and through the device speaker in the
next implementation scope. Verify the Slovak Windows voice and strip URLs
and Markdown from speech playback.

Search local Kubo records and a local PC copy of the selected Google Docs
document, including entries Peter adds by hand. Generate answers about stored
notes locally. Refresh the document copy as its own sync operation. Define
refresh timing and behavior when the copy is stale before end-to-end testing.

Use web search only for public information. Send the public query alone.
Verify a public query separately from a question about a locally stored note.

## Home screen and later additions

Keep the status row at the top, with the queued recording count and a
continuous sound wave centered as a group. Hide both when the queue is
empty. Show the sleep lock to the left of the battery with a clear gap.
Put Tasks and Events in a compact two-column strip at the bottom, each
with a centered label and count. Refresh the date on wake and within one
minute after midnight
while the device is awake.

Center the calendar vertically between the status and agenda sections.
Use a heading such as `NOV FRI CW47`. Center the weekday in larger bold
type and keep the month and week number smaller in their side positions.
Shorten CW to W when more room is needed for the large weekday. Use one
regular-weight Monday–Sunday date row with today highlighted and a thin
divider before Saturday. Verify the layout on the 200 × 200 display.

Below the week, show the forecast icon on the left, daily low and high for
Trnava in the center, and the current Moon phase on the right. The paired
Bridge fetches the public daily forecast. Cache the seven-calendar-day
window on Pala's SD card and only display the entry for the local date.
Calculate the approximate eight-phase Moon icon from the device clock.
The offline aquarium fish, direct work integrations and location-based
shopping reminders are future topics.

## Acceptance sequence

1. Confirm one physical transfer, then repeat with Bridge unavailable and
   check that eventual delivery creates one result and clears the count.
2. Verify calendar rules with concrete local dates before enabling live writes.
3. Verify one note, one shopping dictation and one confirmed calendar event
   in their actual Google applications on Galaxy.
4. Check a local note question, a public question and Slovak speech on the
   device. Verify the date, week strip and queue count across midnight and wake.

Use the [implementation plan](../plans/2026-11-19-kubo-pala-implementation.md)
for the implementation and acceptance sequence. See the
[device setup instructions](../../../devices/kubo-note/README.md) for setup
and verification entry points.