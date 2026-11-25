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
        self.assertIn("bootMicrophone(!fullCheck && !setupRequired)", setup)

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
