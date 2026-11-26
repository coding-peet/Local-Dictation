"""Guard the blocking entry points which must arm the independent microphone."""
from pathlib import Path
import unittest

SKETCH = Path(__file__).resolve().parents[1] / "pala_note"
APP = SKETCH / "src" / "app"


def block(source, start, end):
    return source.split(start, 1)[1].split(end, 1)[0]


class RecordingWiring(unittest.TestCase):
    def test_home_and_microphone_before_recovery(self):
        source = (SKETCH / "pala_note.ino").read_text(encoding="utf8")
        setup = block(source, "void setup() {", "void loop() {")
        self.assertLess(setup.index("attachInterrupt("), setup.index("palaSettingsBegin()"))
        self.assertLess(setup.index("RecordingWindow bootMicrophone"), setup.index("renderReadyHome()"))
        self.assertLess(setup.index("renderReadyHome()"), setup.index("initializeNoteCatalog(true)"))
        self.assertNotIn("clearLatchedPresses()", setup)
        self.assertIn("armBootRecording = recordingReady && !fullCheck && !setupRequired", setup)
        self.assertIn("bootMicrophone(armBootRecording)", setup)

    def test_recorder_acquires_pcm_before_preparing_its_file(self):
        source = (APP / "record.cpp").read_text(encoding="utf8")
        capture = block(source, "static bool recordHeldToPath(", 'constexpr char spool[]')
        direct = block(source, "bool record(const String& capturedUtc)", "bool recordQuestion(")
        priority = block(source, "void spoolWriter(", "bool adopt(")
        self.assertIn('preparePath(noteNum)', capture)
        self.assertLess(capture.index("session.read(firstBlock)"), capture.index("preparePath(noteNum)"))
        self.assertLess(capture.index("preparePath(noteNum)"), capture.index("openExclusiveWav(path)"))
        self.assertLess(direct.index("recordHeldToPath("), direct.index("recoverSpooledRecordings()"))
        self.assertLess(direct.index("recordHeldToPath("), direct.index("beginNoteRecording("))
        self.assertLess(priority.index("recordHeldToPath("), priority.index("present("))
        self.assertLess(capture.index("const bool headerOk"), capture.index("waitForRecordingUi();"))
        self.assertIn("first_write=", capture)

    def test_mic_producer_can_keep_sampling_while_the_sd_writer_is_blocked(self):
        source = (APP / "record.cpp").read_text(encoding="utf8")
        producer = block(source, "void microphoneProducer(", "class MicrophoneSession")
        writer = block(source, "static bool recordHeldToPath(", 'constexpr char spool[]')
        self.assertIn("audio_playback_read(", producer)
        self.assertIn("pushStereo(", producer)
        self.assertIn("session.read(", writer)
        self.assertNotIn("audio_playback_read(", writer)
        for dependency in ("SD_MMC", "Serial", "startRecordingUi(", "malloc", "preparePath"):
            self.assertNotIn(dependency, producer)
        self.assertIn('microphoneProducer, "pcm-capture"', source)

    def test_boot_arms_capture_before_initializing_the_display(self):
        source = (SKETCH / "pala_note.ino").read_text(encoding="utf8")
        setup = block(source, "void setup() {", "void loop() {")
        self.assertLess(setup.index("audio_play_init()"), setup.index("initializeRecordingAudio()"))
        self.assertLess(setup.index("initializeRecordingAudio()"), setup.index("RecordingWindow bootMicrophone"))
        self.assertLess(setup.index("RecordingWindow bootMicrophone"), setup.index("initializeDisplay()"))
    def test_boot_capture_does_not_wait_for_sd_mount(self):
        main = (SKETCH / "pala_note.ino").read_text(encoding="utf8")
        setup = block(main, "void setup() {", "void loop() {")
        self.assertLess(setup.index("RecordingWindow bootMicrophone"), setup.index("initializeStorage()"))
        self.assertLess(setup.index("initializeStorage()"), setup.index("completeRecordingStorageInit("))

        source = (APP / "record.cpp").read_text(encoding="utf8")
        priority = block(source, "void spoolWriter(", "bool adopt(")
        self.assertLess(priority.index("recordHeldToPath("), priority.index("waitForRecordingSpool()"))
        self.assertLess(priority.index("waitForRecordingSpool()"), priority.index("present("))

    def test_csv_queue_ble_and_wifi_cover_blocking_calls(self):
        main = (SKETCH / "pala_note.ino").read_text(encoding="utf8")
        maintenance = block(main, "bool agendaMaintenanceActive", "if (waitingManualSync")
        self.assertLess(maintenance.index("RecordingWindow "), maintenance.index("maintainNoteCatalog()"))
        self.assertLess(maintenance.index("RecordingWindow "), maintenance.index("maintainSyncQueue()"))
        wifi = block(main, "void handleBackgroundBrain(const IdleSleepPolicy& sleep) {", "void refreshHomeMetadataWhenIdle")
        self.assertLess(wifi.index("RecordingWindow "), wifi.index("connectConfiguredWifi("))
        self.assertLess(wifi.index("RecordingWindow "), wifi.index("processPendingMemories("))
        usb = (APP / "usb_sync.cpp").read_text(encoding="utf8")
        batch = block(usb, "void planBluetoothBacklog(", "void nextBluetoothWork")
        self.assertLess(batch.index("RecordingWindow "), batch.index("visitSyncQueue("))
        self.assertIn("if (recordingRequestedDuringSync()) return false", batch)
        self.assertIn("if (!read || recordingRequestedDuringSync())", batch)

    def test_calendar_interrupt_returns_before_decision_is_delivered(self):
        network = (APP / "network.cpp").read_text(encoding="utf8")
        choice = block(network, "const int result = confirmCalendarEvent(", "String memoryJson")
        self.assertLess(choice.index("if (result < 0)"), choice.index('post("/pala/event"'))
        self.assertIn("return false;", block(choice, "if (result < 0)", "decision["))
        ui = (APP / "ui.cpp").read_text(encoding="utf8")
        common = block(ui, "static int calendarButtonChoice(", "static bool parseCalendarMinute")
        self.assertIn("!recordingRequestedDuringSync() && (!serviceConnection || serviceConnection())", common)


if __name__ == "__main__":
    unittest.main()
