from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "pala_note/src/app"
FIRMWARE = ROOT / "pala_note/pala_note.ino"


class RecorderFirstWiring(unittest.TestCase):
    def test_recorder_build_uses_pending_store(self):
        notes = (APP / "notes.cpp").read_text(encoding="utf-8")
        sync = (APP / "sync_queue.cpp").read_text(encoding="utf-8")
        self.assertIn("startRecorderWork()", notes)
        self.assertIn("scanRecorderWorkSlice()", notes)
        self.assertIn("reserveRecorderWork(num, capturedUtc)", notes)
        self.assertIn("visitRecorderWork(newestFirst, visit)", sync)

    def test_transport_capability_guards_external_write_protocol(self):
        usb = (APP / "usb_sync.cpp").read_text(encoding="utf-8")
        self.assertIn('reply("WORK2")', usb)
        self.assertIn('command == "WORK-CLAIM"', usb)
        self.assertIn('command == "WORK-DONE"', usb)

    def test_manual_home_refresh_runs_independently_of_queue_and_asr(self):
        firmware = FIRMWARE.read_text(encoding="utf-8")
        begin = firmware.split("void beginRecordingSync(SyncTiming event) {", 1)[1].split("void pollUsbBacklog()", 1)[0]
        self.assertLess(begin.index("requestManualHomeRefresh()"), begin.index("syncQueueCounts()"))
        self.assertIn("pollManualHomeRefresh(sleep.homeMs)", firmware)
        bridge = (ROOT / "mac-bridge" / "usb_sync.py").read_text(encoding="utf-8")
        watch = bridge.split("def watch_paired(", 1)[1].split("def _open_link(", 1)[0]
        self.assertLess(watch.index("refresh_attachment(key[0], token)"), watch.index("if not is_ready()"))

    def test_usb_idle_poll_stays_armed_for_later_manual_sync(self):
        source = (APP / "usb_sync.cpp").read_text(encoding="utf-8")
        work = source.split('if (command == "WORK-NEXT")', 1)[1].split("SyncQueueEntry selected;", 1)[0]
        self.assertIn('reply(owner == Link::Usb ? "EMPTY RECHECK" : "EMPTY")', work)

    def test_archive_only_enumerates_the_requested_month(self):
        source = (APP / "network.cpp").read_text(encoding="utf-8")
        archive = source.split("static void handleRecorderArchive()", 1)[1].split("static bool positiveFormValue", 1)[0]
        self.assertIn('if (monthView) path += "/" + month', archive)
        self.assertIn('transferServer.setContentLength(CONTENT_LENGTH_UNKNOWN)', archive)
        self.assertNotIn("noteIndex", archive)
        self.assertIn("pendingPriorityRecording()", archive)

    def test_archive_completion_updates_the_cached_sd_usage_outside_settings(self):
        archive = (APP / "archive_month.cpp").read_text(encoding="utf-8")
        self.assertIn("capacity.observe(progress)", archive)
        self.assertIn("capacity.service(storageBusy, refreshRecordingSpace)", archive)
        settings = (APP / "note_config.cpp").read_text(encoding="utf-8")
        self.assertIn('device["sdUsedMiB"] = recordingUsedMiB()', settings)
        self.assertIn('device["sdTotalMiB"] = recordingTotalMiB()', settings)
        self.assertNotIn("refreshRecordingSpace()", settings)

    def test_saved_items_uses_the_normal_home_gesture_and_keeps_rec_priority(self):
        firmware = FIRMWARE.read_text(encoding="utf-8")
        home = firmware.split("void handleIdle()", 1)[1].split("void handleAgenda()", 1)[0]
        self.assertIn("displayPageIndex = kWorkspaceSavedItemsPage", home)
        agenda = firmware.split("void handleAgenda()", 1)[1].split("bool yieldBacklogToUsb()", 1)[0]
        saved = agenda.split("if (displayPageIndex == kWorkspaceSavedItemsPage)", 1)[1]
        self.assertLess(saved.index("captureMemory()"), saved.index("workspaceSavedItemsPages()"))
        source = (APP / "workspace_calendar.cpp").read_text(encoding="utf-8")
        self.assertIn('fetchWorkspaceCalendar(client, "/pala/workspace-calendar?v=2")', source)
        self.assertIn("workspace_view::readSavedItems", source)

    def test_recorder_creates_final_wav_exclusively_without_looking_up_the_name(self):
        source = (APP / "record.cpp").read_text(encoding="utf-8")
        capture = source.split("static bool recordHeldToPath", 1)[1].split("constexpr char mountedSpool", 1)[0]
        opener = source.split("static FILE* openExclusiveWav", 1)[1].split("static bool writeWavHeader", 1)[0]
        self.assertIn("O_WRONLY | O_CREAT | O_EXCL", opener)
        self.assertIn("openExclusiveWav", capture)
        self.assertLess(capture.index("openExclusiveWav(path, timing.io, openError)"), capture.index("reserveRecorderWork"))
        self.assertLess(capture.index("reserveRecorderWork"), capture.index("writeWavHeader(f, 0, &timing)"))
        self.assertNotIn("SD_MMC.exists(path)", capture)
        self.assertNotIn("SD_MMC.open(path", capture)
        self.assertIn('captureMoments && !momentsMs.empty() && !saveNoteMoments', capture)

    def test_recorder_skips_occupied_wav_names_in_the_same_microphone_session(self):
        source = (APP / "record.cpp").read_text(encoding="utf-8")
        capture = source.split("static bool recordHeldToPath", 1)[1].split("constexpr char mountedSpool", 1)[0]
        retry = capture.index("openError == EEXIST")
        self.assertLess(capture.index("MicrophoneSession session"), retry)
        self.assertLess(capture.index("session.read(firstBlock)"), retry)
        self.assertLess(retry, capture.index("reserveRecorderWork"))
        self.assertIn("stage2::captureWavCollision", capture)
        self.assertLess(capture.index("reserveRecorderWork"), capture.index("append(firstBlock.samples"))

    def test_capture_layout_is_loaded_before_the_queue_and_persisted_before_new_ids(self):
        setup = (APP.parent.parent / "pala_note.ino").read_text(encoding="utf-8")
        mount = setup.split("bool initializeStorage()", 1)[1].split("bool pollRestartConfirmation", 1)[0]
        self.assertIn("initializeRecordingLayout()", mount)
        create = (APP / "notes.cpp").read_text(encoding="utf-8")
        next_id = create.split("int nextNoteNumber", 1)[1].split("bool saveTag", 1)[0]
        self.assertIn("recording_layout::activate", next_id)
        self.assertIn('"/recordings"', (APP / "monthly_recordings.h").read_text(encoding="utf-8"))

    def test_usb_transport_confirms_sof_loss_and_revokes_sessions_on_bus_reset(self):
        sketch = (APP.parent.parent / "pala_note.ino").read_text(encoding="utf-8")
        poll = sketch.split("void pollUsbBacklog() {", 1)[1].split("void serviceConnectingLinks()", 1)[0]
        self.assertIn("usbDataPresence.update(usb_serial_jtag_is_connected(), millis())", poll)
        self.assertIn("usbBusReset.exchange(false)", poll)
        self.assertIn("|| busReset", poll)
        self.assertLess(poll.index("cancelUsbSync();"), poll.index("handleUsbSetup();"))
        self.assertIn("Serial.onEvent(ARDUINO_HW_CDC_BUS_RESET_EVENT", sketch)
