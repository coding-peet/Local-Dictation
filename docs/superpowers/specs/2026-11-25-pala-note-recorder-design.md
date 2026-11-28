# Pala Note recorder-first design

Approved for the start of implementation on 2026-11-25.
Working branch: `codex/pala-note-recorder-rework`. Reference: `86840b2`.

## Product behavior

The lower button wakes the Note. The upper REC hold starts a recording even
while the device is starting. Keep the physical Home screen and existing
non-archive controls. Recording works offline and takes precedence over sync,
storage activity, display refresh, question processing and playback.

The Note retains WAV files on its 32 GB SD. New recordings and their UTF-8
transcripts belong to folders named `YYYYMM`. Bridge receives audio for
transcription without retaining an audio backup. The Note starts with a fresh
archive. Peter completes the old device's sync and confirms his Google items
before cutover.

USB, Bluetooth LE, Wi-Fi, pairing and the configured sync timing carry forward.
A manual request runs when the queue is ready. Restart clears the manual
request and preserves queued work. A current run resumes the interrupted
recording before including a new one.

## Manual sync and Home status

The upper-button triple tap asks an available, paired Bridge to refresh the
Home status over USB, Bluetooth LE or Wi-Fi. This request works with no
audio or delivery items queued and with transcript delivery disabled. The
refresh does not depend on transcription service readiness.

Refresh the remaining Codex account allowance, including the selected
five-hour or weekly window, the open-task count for the configured default
task list, and the weather forecast. Also refresh other Home data that
Bridge supplies, such as public holidays and the delivery summary, when
available. These reads use their own account access and configuration.
Update Home as data arrives. When a source or the Bridge is unavailable,
apply the existing freshness rules to the last valid values.

Keep the refresh within the normal idle budget and give recording priority.
The Home refresh itself performs no audio upload, archive scan or provider
write. Any queued recordings and pending deliveries use the ordinary sync
flow.

## Architecture

The previous design used the same flat catalog as an archive and part of the
recording lifecycle. A disk index could accelerate individual views, but its
updates and source recovery still compete with recording and sync. The chosen
architecture keeps the monthly archive passive and gives capture and delivery
their own state. The earlier rc93 evidence stays useful as a comparison.

Reserve Core 1 for the time-sensitive microphone task and its I2S input. Start
with the existing ESP-IDF radio affinity on Core 0. Use a bounded PCM buffer and
a separate writer so short SD and application stalls cannot discard beginning
samples. The writer prioritizes capture over archive reads, storage reporting
and sync. Keep the buffer bounded and report an interrupted recording if its
capacity is exceeded. Measure the final affinities, buffer size and battery
impact on the second Note.

The recording identity travels with its original capture time. Start the mic
before SD preparation, status refresh and network work. Finalize the WAV and
publish its work record with a recoverable sequence. Incomplete writes retain
recoverable PCM. Recover by following active work references into the archive.
A full archive scan belongs to explicitly requested maintenance.

## Sync state and durability

| Durable state | Behavior |
| --- | --- |
| Audio pending | Send the WAV when an authenticated Bridge is ready. |
| Pending delivery | The complete identical transcript and state are durable on the Note. Remove the item from the audio queue. |
| Delivered | Confirm all selected destination writes. The WAV and local transcript remain in the archive. |

Store the approved delivery target, account and operation identity before an
external write. Two peers can read recovery state. Only one peer can make a
particular external attempt. Transfer an approved delivery to another paired
Bridge with matching account authorization and the same transcript and
operation identity. After a lost response, reconcile with the provider first.
A provider without a conclusive recovery check leaves the operation pending
for review while independent work continues. Confirmation must reflect durable
state. An identical retry returns the existing result. A conflicting retry
preserves the original and reports the conflict.

The per-record state and a bounded outstanding queue are sufficient for
recording and normal sync. An audio write, transcript or delivery receipt
must not rebuild the archive or read unrelated months.

## Titles for saved notes

Bridge uses the configured AI processing model and the entire note body to
generate a short, specific list title for a long note. The title captures the
main subject or requested action in the note's language. Omit spoken filler and
routing phrases. A short dictation that already works as a title can be used
as written. Keep the full transcript available in the note details and the
local archive.

Save the selected title with the pending delivery before the provider write.
Retries and another paired Bridge use the saved title. If the model is
unavailable or returns an unusable title, keep the transcript and mark the
delivery for retry while other items continue.

For example, a long request to improve how titles are generated in the list
can use the Slovak title "Oprava názvu poznámky v zozname". Verify that a note
with a long introduction gets a title based on the main point, that all details
remain available, and that a retry uses the same title.

## Shopping list entries

A single shopping request without user details displays only the product name,
for example "ananas". Preserve user-entered details and the readable repeat
count for separate requests that add to the same open purchase. Never put
delivery hashes or other internal identifiers in the visible title or notes.

Keep durable delivery receipts outside those visible fields and make the
receipt available for recovery by another paired Bridge. If an uncertain
provider write cannot be verified, keep it pending for review. Remove a
legacy marker only after its receipt is recoverable and preserve the other
details on that item.

Verify the visible shopping entry on first delivery, a separate request for
the same product and a replay after a lost provider response. Replaying from
another Bridge must leave one open item and must not increment its count.

## Web, provider views and voice

Open Companion at the existing Settings layout. Put used and total SD capacity
with battery and RAM. An **Archive recordings** navigation row opens the month
list. Load archived data only when opened. The selected month supports WAV
playback and download, log preview and a single UTF-8 transcript export.
An explicit delete confirmation names the month and reports both pending
counts. Confirming can discard the complete month including pending work.
Provider items already delivered stay in the destination. Pause competing
operations at their safe boundary, revalidate counts and persist delete intent.

On Note, **Saved items** shows the selected destination list. **Calendar**
combines scheduled tasks across the synchronized account and events from the
primary Google calendar. Each row shows the date, applicable time, title and
whether it is a task or an event. A compact cached view can be shown for up
to 24 hours with the Offline label and the last successful update time. Expiry shows
Unavailable. Account disconnection and revoked access invalidate the view.

Bridge owns provider authentication. Use official Google Workspace MCP for
AI-driven access and keep provider interfaces on Bridge. Google Tasks MCP
provides the task view. Google Calendar MCP reads events from the primary
calendar. The Stage 3 read grant requests only `tasks.readonly` and
`calendar.events.readonly`. The grant stays on the Bridge PC. Docs uses
its own tools where applicable. The local archive contains independently captured audio
and transcripts. Keep provider snapshots subject to their own lifetime.

The lower hold opens **Ask Kubo**. Bridge can search the public web and read
the user's tasks in response to explicit questions. Voice answers have only
read operations. Dedicated daily and weekly briefing shortcuts are replaced
by asking Kubo. All account writes originate in the dictation and sync flow.

## Hardware acceptance

Measure wake edge to usable PCM, REC edge to usable PCM and first SD write.
Check the actual start of each WAV with a timed sound. Compare an empty
archive with a populated archive at the same pending count and use the
second Note and a separate SD. Include startup, ordinary idle, active
transport, archive read and playback. Measure practical sleep current.

At each persistence boundary, verify power-cut recovery, original PCM and
capture time, lost acknowledgments, A then B after a REC interruption, and
handover to a second Bridge. Validate all three links, provider ambiguity,
expiry of snapshots and deletion interrupted before completion. Record
implementation decisions and measurements before considering deployment.

With an empty recording and delivery queue, change the Bridge's test values
for allowance, open tasks and weather. Verify that the upper triple tap updates
Home over each supported transport with transcript delivery disabled and the
transcription service unavailable. Repeat with one data source unavailable.
Verify that no audio or delivery request was made and that REC can interrupt a
refresh in progress.

## References

- [rc93 hardware report](../../development/pala-note-rc93-hardware-validation-20261124.md)
- [ESP32-S3-PICO-1 datasheet](https://documentation.espressif.com/esp32-s3-pico-1_datasheet_en.pdf)
- [ESP-IDF task priorities](https://docs.espressif.com/projects/esp-idf/en/latest/esp32s3/api-guides/performance/speed.html)
- [ESP-IDF I2S and DMA](https://docs.espressif.com/projects/esp-idf/en/v5.4.2/esp32s3/api-reference/peripherals/i2s.html)
- [Google Workspace AI Tools Addendum](https://workspace.google.com/terms/ai-tools/)
- [Google Tasks MCP](https://developers.google.com/workspace/tasks/reference/mcp/tools_list/list_tasks)
- [Apple HIG lists and tables](https://developer.apple.com/design/human-interface-guidelines/lists-and-tables)
