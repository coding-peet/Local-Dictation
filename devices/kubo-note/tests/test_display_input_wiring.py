"""Verify input ownership across asynchronous boot, modal presentation and sleep."""
from pathlib import Path
import unittest


SKETCH = Path(__file__).resolve().parents[1] / "pala_note"


class DisplayInputWiring(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = (SKETCH / "pala_note.ino").read_text(encoding="utf-8")
        cls.sleep = (SKETCH / "src/app/sleep.cpp").read_text(encoding="utf-8")

    def test_first_home_has_a_capture_window_after_setup(self):
        setup, loop = self.source.split("void setup() {", 1)[1].split("void loop() {", 1)
        self.assertLess(setup.index("RecordingWindow bootMicrophone"), setup.index("renderReadyHome();"))
        self.assertIn("bootPanelMicrophone.emplace()", setup)
        self.assertIn('stage2::homeEvent("setup_done"', setup)
        self.assertIn("!note_display::bootHomeOutstanding()", loop)
        self.assertIn("note_display::inBootHome(", loop)
        self.assertIn("bootPanelMicrophone.reset()", loop)

    def test_restart_and_pairing_use_the_presented_frame_and_gesture_queue(self):
        restart, pairing = self.source.split("bool pollRestartConfirmation() {", 1)[1].split(
            "bool pollPairingOverlay() {", 1)
        pairing = pairing.split("}  // namespace", 1)[0]
        for owner in (restart, pairing):
            self.assertIn("note_display::lastPresentationOk()", owner)
            self.assertIn("pollBufferedButtons(1, 1)", owner)
        self.assertNotIn("takeLatchedPress(", pairing)
        self.assertIn("pairingDisplayedPeer", pairing)
        self.assertIn("routeHoldToPrompt(true)", restart)
        self.assertIn("routeHoldToPrompt(true)", pairing)
        press = self.source.split("bool physicalRecordingPress(uint32_t& edgeMs) {", 1)[1]
        self.assertIn("!promptOwnsHold", press.split("bool physicalRecordingHold", 1)[0])
        self.assertIn("physicalRecordingPress(edgeMs) && millis() - edgeMs >= ButtonGesture::kHoldMs", press)

    def test_restart_uses_its_own_physical_visibility_timestamp(self):
        modal = self.source.split("bool pollRestartConfirmation() {", 1)[1].split("bool pollPairingOverlay()", 1)[0]
        self.assertIn("button_prompt::retryDue(millis(), lastAttemptEnd)", modal)
        self.assertIn("armPresentedPrompt(note_display::lastPresentationVisibleAt())", modal)
        pipeline = (SKETCH / "src/app/display_pipeline.cpp").read_text(encoding="utf-8")
        self.assertIn('if (strcmp(name, "idle") == 0) transferVisibleAt = at;', pipeline)
        self.assertIn("lastOk ? lastVisibleAt : 0", pipeline)

    def test_audio_during_the_sleep_frame_prevents_poweroff(self):
        entry = self.sleep.split("void enterUltraSleep() {", 1)[1]
        self.assertLess(entry.index("RecordingWindow microphone"), entry.index("showSleepLocked()"))
        after_frame = entry.split("showSleepLocked();", 1)[1]
        self.assertLess(after_frame.index("sleepInputPending()"), after_frame.index("stopTransferMode()"))
        self.assertNotIn("showIdle(", after_frame)
        self.assertIn("cancelSleepFrame()", after_frame)
        after_radio = after_frame.split("WiFi.mode(WIFI_OFF)", 1)[1]
        self.assertLess(after_radio.index("sleepInputPending()"), after_radio.index("audio_prepare_sleep()"))

    def test_archival_trace_follows_publication(self):
        save = self.source.split("bool saveHeldRecording() {", 1)[1].split("void captureMemory() {", 1)[0]
        self.assertLess(save.index("saveTag(lastRecNum"), save.index("stage2::captureArchived("))
        self.assertIn("lastRecNum > 0", save)
