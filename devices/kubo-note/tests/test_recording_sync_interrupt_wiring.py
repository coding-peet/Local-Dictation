from pathlib import Path
import unittest

SKETCH = Path(__file__).resolve().parents[1] / "pala_note"
MAIN = (SKETCH / "pala_note.ino").read_text(encoding="utf8")
USB = (SKETCH / "src/app/usb_sync.cpp").read_text(encoding="utf8")


def between(text, start, end):
    return text.split(start, 1)[1].split(end, 1)[0]


class RecordingSyncInterruptWiring(unittest.TestCase):
    def test_disconnects_status_and_advertising_without_fabricating_a_request(self):
        interrupt = between(MAIN, "bool recordBeforeResumingSync() {", "void requestBacklogSync() {")
        self.assertIn("!recordingSync.running()", interrupt)
        self.assertIn("!bluetoothHomeDeliveryOpen()", interrupt)
        self.assertIn("if (recordingSync.running() &&", interrupt)
        self.assertIn("interruptedSync.interrupted(transportNote, activeSyncNote)", interrupt)
        for close in ("cancelUsbSync();", "stopBluetoothSync();"):
            self.assertLess(interrupt.index("latchPriorityRecording("), interrupt.index(close))
            self.assertLess(interrupt.index(close), interrupt.index("captureMemory();"))
        self.assertNotIn("requestBacklogSync();", interrupt)
        self.assertNotIn("SyncTiming::Manual", interrupt)
        command = between(USB, 'if (!authenticated) { reply("ERROR AUTH"); return; }', "bool syncActive(")
        self.assertLess(command.index("RecordingWindow microphone"), command.index('command == "DELIVERY"'))
        advertise = between(MAIN, "bool startBacklogBluetooth(", "void handleBackgroundBrain(")
        self.assertNotIn("recordingSync.finish()", advertise)
        loop = MAIN.split("void loop() {", 1)[1]
        self.assertLess(loop.index("recordBeforeResumingSync()"), loop.index("pollUsbBacklog()"))
        self.assertIn("(bothButtonsUp() && upperButton.pressLatched))) stopBluetoothSync();", loop)

    def test_only_empty_work_replies_close_a_drained_request(self):
        close = between(MAIN, "void finishDrainedRecordingSync() {", "void rememberRecordingSyncNote(")
        for guard in ("!noteCatalogReady()", "!syncQueueReady()", "recordingRequestedDuringSync()",
                      "queue.audio || queue.organization"):
            self.assertLess(close.index(guard), close.index("recordingSync.finish()"))
        ble = between(USB, "void nextBluetoothWork()", "bool offerRecording(")
        usb = between(USB, "void selectRecording(", "void exportTranscript(")
        self.assertIn("finishDrainedRecordingSync();", ble)
        self.assertIn("finishDrainedRecordingSync();", usb)
        self.assertEqual(USB.count("finishDrainedRecordingSync();"), 2)

    def test_common_record_entry_resumes_the_actual_schedule_only(self):
        capture = between(MAIN, "void captureMemory() {", "void askBrain() {")
        self.assertLess(capture.index("recordingSync.pauseForRecording()"), capture.index("saveHeldRecording()"))
        self.assertIn("if (saveHeldRecording()) afterRecordingSyncPending = true;", capture)
        self.assertIn("if (recordingSync.resumeAfterRecording(palaSyncTiming(), lastActivityMs))", capture)
        self.assertNotIn("SyncTiming::Manual", capture)
        loop = MAIN.split("void loop() {", 1)[1]
        self.assertLess(loop.index("handleIdle()"), loop.index("beginRecordingSync(SyncTiming::AfterRecording)"))
        self.assertIn("beginRecordingSync(SyncTiming::OnWake)", between(MAIN, "void setup() {", "void loop() {"))
