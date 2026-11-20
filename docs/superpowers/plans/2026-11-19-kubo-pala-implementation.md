# Kubo and Pala integration

**Goal**
Use Pala Note and compatible ESP32-S3 devices with Local Dictation on Windows,
including an offline transcription path and the approved Google destinations.

**Specification**
[Implementation brief](../specs/2026-11-19-kubo-note-integration.md)

## 1. Bridge readiness

- [x] In `devices/kubo-note/tests/test_local_dictation.py`, test that
  `GET /pala/ready` requires a paired-device token. A ready Windows endpoint
  returns 200. Busy, unready, unreachable, malformed or foreign endpoints
  return 503. Requests without valid pairing never reach Local Dictation.
- [x] In `devices/kubo-note/mac-bridge/bridge.py`, add the read-only readiness
  endpoint and reuse the existing check of Local Dictation's identity.
  Probe with a short timeout. Report readiness without sending audio.
- [x] Run the Bridge suite in the existing Bridge Python environment.

## 2. Windows lifecycle and installation

- [x] In the canonical Local Dictation checkout, add **Connections** using
  the existing Settings navigation and search. Move Reconnect and Start
  local API there. Keep the service address in Advanced.
- [ ] Add Bridge status, pairing and Start/Stop to Connections.
- [ ] Run the packaged Bridge as a process owned by Local Dictation.
  Gracefully stop it on explicit Stop or tray Exit. Keep it running when
  the window closes. Test launch races, shutdown during a request, an
  already running standalone Bridge, and a failed or incomplete setup.
- [x] Add and exercise the Bridge side of the ownership protocol.
  `bridge.py --managed` watches redirected standard input. Closing the pipe
  stops discovery and releases listeners.
- [ ] Publish a single installer with the Windows client, Bridge and a
  locally runnable session API. Resolve launchers from installed files.
  Test a clean install with and without the selected model, first-run
  setup, offline dictation, and an upgrade with existing data and pairing.

## 3. Pala connection and Web Companion

- [ ] Pair each PC using the existing TLS identity and device-token model.
  Choose the first ready PC in a priority configured outside Pala.
  Bind reattempts of a possibly accepted upload to the selected PC until
  ownership is resolved. Preserve SD recovery and apply stable request IDs
  to operations that can produce duplicate side effects.
- [ ] Check Pnet3, the A55 hotspot at 2.4 GHz, and Windows public networking.
  Qualify a paired USB transfer path if isolation prevents Wi-Fi transfer.
  Test retransmission, both PCs, and no available endpoint on real devices.
- [ ] Keep Web Companion behind the existing two-button activation. Use
  physical activation to authorize first setup and require authentication
  for all data and settings routes. Keep the existing cross-origin write
  checks. Test password changes from a phone, unauthorized content requests,
  and automatic transfer when the Companion is stopped.
- [ ] Compare flash, static RAM, peak RAM/PSRAM, and power to rc43. Record
  physical checks separately from builds and workstation tests.

## 4. Google destinations

- [x] Prepare the one-PC Tasks pilot and test dedicated-list selection,
  pagination and reruns against a simulated Google client.
- [ ] Pilot Google Tasks against one dedicated test list after Peter
  authorizes the account. Check creation and manual editing on PC and
  Galaxy with synthetic input before routing real dictation.
- [ ] After the pilot, bind the existing **Kubo tasks general** list
  and **Kubo notes general** document through user selection in the
  connected Google account. Persist their IDs so renames do not
  redirect writes. Confirm the selected targets before enabling routing.
- [ ] Add an account-scoped shared allowlist of task-list IDs and one
  designated sorting list. Keep OAuth tokens per installation. Test
  selection changes, disabled or deleted lists, offline clients, and
  a request with unknown delivery status before using both PCs.
- [ ] Use the sorting list for ambiguous routing. Append explicit notes
  to **Kubo notes general** with dates. Keep Calendar confirmation
  before writes and verify it separately. Keep remains manual.

## Development verification

The canonical Windows checkout is `C:\Git\Local-Dictation\repo`.
Use an ordinary branch there per repository instructions. Build and test
against candidate files and synthetic data. Record physical acceptance with
the specific device, network and build used.

The first candidate uses the local branch `codex/kubo-pala-integration`,
based on `06cc5dd`. The Connections UI passed 244 detached WPF checks.
The Bridge suite passed 17 tests, including simulated Tasks responses.
The readiness probe also returned true against the configured local API.

The candidate builds and tests are separate from the installed Windows app
and Pala firmware.

A synthetic Google Tasks item was created and read back with the authorized
Google account. The physical Pala audio path still needs verification.

The temporary Pala pilot uses the existing pairing, Local Dictation and
one explicitly named list. On an approved network, with Bridge port 8443
available, run the following command from the repository root using
the Bridge Python environment.

~~~powershell
py -B devices/kubo-note/mac-bridge/pala_task_pilot.py `
    --list 'Kubo notes general' `
    --title 'Skontrolovať modrý zápisník'
~~~

Wait until Bridge reports that it is listening. Record
“Úloha, skontrolovať modrý zápisník” on Pala Note. The pilot waits for
a fresh matching transcription and reads the resulting task back.
It closes by itself after verification or after four minutes.
