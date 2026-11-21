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
authenticated readiness before sending a recording. If the Bridge, its local
transcription service, or its readiness reply is unavailable, advertise the
recording service over Bluetooth Low Energy for a short window. A validated
busy reply keeps the work queued. A deferred Wi-Fi retry still allows Bluetooth
discovery. Recheck the queue promptly after a USB session loses its cable.

Bluetooth uses the existing per-device Pala Bridge pairing token and the same
recording transfer and local save acknowledgment as USB. A settings request
on a regular Bluetooth sync connection hands the link back to the recording
queue as soon as the request completes. The dedicated settings window does
not start a recording transfer. Fetch the forecast after attempting the
recording queue so a slow forecast cannot delay the transfer.

For each BLE connection, Pala and Bridge each contribute a fresh 16-byte
random challenge.
They derive the session keys with HKDF-SHA256 and exchange role-specific
proofs before allowing AES-256-GCM application traffic. BLE requires the v2
handshake on both sides. An older or unauthenticated peer leaves the recording
queued.

Local Dictation discovers its paired device in the background. During the
fallback window, advertise the saved display
name plus `-Note` as the Bluetooth local name. For example, Kubo advertises
as `Kubo-Note`. Bridge selects the device using its existing pairing identity.
Show the current transport as USB, Wi-Fi or BT while connecting.
USB and Bluetooth share one device session and keep the SD recording until
its result is saved. Verify the cable, Wi-Fi and Bluetooth
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

## Shared Note settings and enrollment

Use the same settings page asset from Local Dictation's loopback Bridge and
the Note's Web Companion. From the PC, apply Note settings over authenticated
USB or BLE. From an Android phone with the PC off, open the Note's Companion
over Wi-Fi or the Note's hotspot in a browser.

Configure the Note's name, language and saved Wi-Fi profiles. Show only the
SSID for stored networks. Configure the Bridge name and weather location
when the page is hosted by that Bridge.

The Note stores up to five independently revocable pairings. List and remove
them through the settings page. The Note's screen provides the physical
confirmation prompts during enrollment. New BLE enrollments use Secure
Connections Numeric Comparison. The user checks matching codes and the Note
shows which button confirms or cancels. USB enrollment requires a physical
confirmation as well. A brief press and release of both buttons on Home
opens the Bluetooth setup window. The longer hold opens Web Companion.

Check the last working Bridge first over Wi-Fi, followed by the saved
priority. Authenticate each candidate using its separate hostname,
certificate and credential. Pick a ready Bridge before sending new work.
If an organization request may already have made an external write, keep
the retry bound to the original Bridge.

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

Keep the status row at the top. Center the count of recordings awaiting
transcription and the sound-wave mark as a group. Use seven separated
vertical bars with relative heights 3, 8, 15, 7, 12, 5, and 3.
Use the same centered silhouette as the Local Dictation application icon
and recording banner. The Note's mark is static and monochrome.
Hide the mark and count when the queue is empty.

Show the sleep lock to the left of the battery with a clear gap. During a
wake, use the same slot for the latest verified Bridge link over Wi-Fi, USB,
or Bluetooth. Start each wake with an empty slot and retain a verified
transport after routine radio shutdown within that wake. Update the icon
when the verified transport changes.
Refresh the date on wake and within one minute after midnight while the
device is awake.

Use the compact two-column strip at the bottom for Google delivery.
Center each count beneath its label.

- **Doručené dnes** (left) counts dictations whose required Google Tasks
  or Google Docs writes were all confirmed by Bridge today. Use the day
  of the final confirmation in the device's local time zone.
- **Čaká na Google** (right) counts transcribed dictations with at least
  one configured Google Tasks or Google Docs write awaiting confirmation.

Count each dictation once, including when one audio recording produces
several entries or the same transfer is retried. The bottom counters use
Bridge delivery records for Google Tasks and Docs.

Show the forecast icon, daily low and high, and current Moon phase above
the calendar heading. Calculate the approximate eight-phase Moon icon from
the device clock.

Use a heading such as `NOV FRI CW47`. Keep the weekday at a consistent large
bold size in the center. Keep the month and ISO week in the side positions.
Shorten CW to W when the weekday needs more room. Place the regular-weight
Monday–Sunday strip directly above the lower block's divider, with today
highlighted and a thin separator before Saturday. Verify the layout on the
200 × 200 display.

Open Pala Note Configuration from Connections and set the forecast town in the web settings. Bridge
reads the saved setting and fetches the public daily forecast. A two-letter
country code can narrow the town search. Clearing the town disables updates.
Cache up to seven days for the configured town on Pala's SD card and select
the entry for the device's local date. Automatic location selection and its
source remain open decisions.

Direct work integrations and location-based shopping reminders are future
topics.

## Offline aquarium

Open the aquarium with three short presses of the upper button from Home.
The existing upper-button shortcuts still open the agenda on one short
press, read the daily briefing on two, and record on a hold.

The ocellaris clownfish peeks out of its anemone and then swims beside it.
While the fish is swimming calmly, the upper button drops one flake. The
fish notices the falling flake, swims to it, swallows it, and returns to its
usual place. The lower button starts a current. Tentacles bend downstream,
the fish turns upstream, holds position, then circles the anemone and
returns beside it. Use a sparse sequence of monochrome frames on e-paper.

Two short upper presses or inactivity start the same closing sequence.
The fish hides headfirst, its tail disappears last, and it peeks out once
more before Home returns. Opening the aquarium is a deliberate button
gesture, independent of transcription and Google delivery.

## Acceptance sequence

1. Transfer a test recording over USB, then repeat while disconnecting USB
   during download. Verify the same recording receives a single saved
   transcript once a paired transport becomes available.
2. On Wi-Fi, test with the local ASR stopped and then with a confirmed busy
   ASR. Verify Bluetooth fallback in the first case and deferred processing in
   the second. Open settings during a normal Bluetooth sync and verify the
   recording can continue on that connection.
3. Verify calendar rules with concrete local dates before enabling live writes.
4. Verify one note, one shopping dictation and one confirmed calendar event
   in their actual Google applications on Galaxy.
5. Check a local note question, a public question and Slovak speech on the
   device. Verify the date, week strip and queue count across midnight and wake.

Use the [implementation plan](../plans/2026-11-19-kubo-pala-implementation.md)
for the implementation and acceptance sequence. See the
[device setup instructions](../../../devices/kubo-note/README.md) for setup
and verification entry points.