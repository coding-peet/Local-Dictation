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

    def test_storage_probe_covers_only_the_initial_wav_header(self):
        source = (APP / "record.cpp").read_text(encoding="utf-8")
        capture = block(source, "static bool recordHeldToPath(", "constexpr char spool[]")
        checkpoint = block(source, "static bool checkpointWavHeader(", "template<class PreparePath>")
        header = block(source, "static bool writeWavHeader(", "static bool checkpointWavHeader(")
        self.assertIn("writeWavHeader(f, 0, &timing)", capture)
        self.assertIn("writeWavHeader(file, dataBytes)", checkpoint)
        self.assertLess(header.index("if (timing) timing->io.begin()"), header.index("std::fwrite("))
        self.assertLess(header.index("timing->io.finishHeader("), header.index("std::fflush("))

    def test_recorder_acquires_pcm_before_preparing_its_file(self):
        source = (APP / "record.cpp").read_text(encoding="utf8")
        capture = block(source, "static bool recordHeldToPath(", 'constexpr char spool[]')
        direct = block(source, "bool record(const String& capturedUtc, uint32_t edge)", "bool recordQuestion(")
        priority = block(source, "void spoolWriter(", "bool adopt(")
        self.assertIn('preparePath(noteNum, timing)', capture)
        self.assertLess(capture.index("session.read(firstBlock)"), capture.index("preparePath(noteNum, timing)"))
        self.assertLess(capture.index("preparePath(noteNum, timing)"), capture.index("openExclusiveWav(path, timing.io, openError)"))
        self.assertLess(direct.index("recordHeldToPath("), direct.index("recoverSpooledRecordings()"))
        self.assertLess(direct.index("recordHeldToPath("), direct.index("beginNoteRecording("))
        self.assertLess(priority.index("recordHeldToPath("), priority.index("present("))
        self.assertNotIn("waitForRecordingUi", capture)
        producer = block(source, "void microphoneProducer(", "class MicrophoneSession")
        self.assertLess(producer.index("pushStereo("), producer.index("note_display::firstPcm("))
        self.assertNotIn("note_display::firstPcm(", capture)
        self.assertIn("first_write=", capture)

    def test_timing_image_measures_first_write_before_reporting(self):
        source = (APP / "record.cpp").read_text(encoding="utf8")
        writer = block(source, "static bool recordHeldToPath(", 'constexpr char spool[]')
        marks = ('timing.at("pcm"', 'timing.mark("dequeue")', 'preparePath(',
                 'timing.mark("path_end")', 'timing.mark("serial")', "openExclusiveWav(path, timing.io, openError)",
                 'timing.mark("open")', "reserveRecorderWork(", 'timing.mark("reserve")',
                 "writeWavHeader(f, 0", "append(firstBlock.samples", 'timing.at(audioOk')
        positions = [writer.index(mark) for mark in marks]
        self.assertEqual(positions, sorted(positions))
        header = block(source, "static bool writeWavHeader(", "static bool checkpointWavHeader(")
        self.assertNotIn("std::ftell(", header)
        self.assertNotIn("std::fseek(", header)
        for call, stage in (("std::fwrite(", "header_write"), ("std::fflush(", "flush"),
                            ("::fsync(", "fsync")):
            self.assertLess(header.index(call), header.index(f'mark("{stage}")'))
        self.assertEqual(writer.count("checkpointWavHeader(f, writer.partSize)"), 2)
        self.assertIn("openPart,checkpointWavHeader,esp_rom_crc32_le", writer)
        priority = block(source, "void spoolWriter(", "bool adopt(")
        self.assertNotIn('present(String("/sdcard") + temp)', priority)
        self.assertIn("O_WRONLY | O_CREAT | O_EXCL", source)
        for end in ("ready", "stat_wav"):
            self.assertIn(f'timing.mark("{end}")', priority)

    def test_forced_stall_is_opt_in_and_precedes_only_the_first_pcm_write(self):
        ini = (SKETCH.parent / "platformio.ini").read_text(encoding="utf8")
        normal, forced = ini.split("[env:pala_note_recorder_stall6500]", 1)
        self.assertNotIn("PALA_TEST_FIRST_WRITE_DELAY_MS", normal)
        self.assertIn("PALA_TEST_FIRST_WRITE_DELAY_MS=6500", forced)
        source = (APP / "record.cpp").read_text(encoding="utf8")
        writer = block(source, "static bool recordHeldToPath(", 'constexpr char spool[]')
        ordered = ("session.read(firstBlock)", "writeWavHeader(f, 0", "if (stagedMoments)",
                   "timing.stallFirstWrite()", "append(firstBlock.samples")
        pos = [writer.index(item) for item in ordered]
        self.assertEqual(pos, sorted(pos))
        stall = block(source, "void stallFirstWrite()", "void reportStall")
        for condition in ("ESP_RST_SW", "setup_done", "used", "delay(", "PALA_TEST_FIRST_WRITE_DELAY_MS"):
            self.assertIn(condition, stall)
        self.assertNotIn("session.stop", stall)

    def test_mic_producer_can_keep_sampling_while_the_sd_writer_is_blocked(self):
        source = (APP / "record.cpp").read_text(encoding="utf8")
        producer = block(source, "void microphoneProducer(", "class MicrophoneSession")
        writer = block(source, "static bool recordHeldToPath(", 'constexpr char spool[]')
        self.assertIn("audio_playback_read(", producer)
        self.assertIn("pushStereo(", producer)
        self.assertIn("session.read(", writer)
        self.assertNotIn("audio_playback_read(", writer)
        sampling = producer.split("audio_idle_suspend()", 1)[0]
        self.assertNotIn("Serial", sampling)
        for dependency in ("SD_MMC", "startRecordingUi(", "malloc", "preparePath"):
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
