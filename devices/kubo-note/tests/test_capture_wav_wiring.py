from pathlib import Path
import unittest

SKETCH = Path(__file__).resolve().parents[1] / "pala_note"
APP = SKETCH / "src" / "app"


class InterruptedCaptureWiring(unittest.TestCase):
    def test_capture_time_uses_the_rec_edge_on_both_paths(self):
        rec = (APP / "record.cpp").read_text(encoding="utf8")
        main = (SKETCH / "pala_note.ino").read_text(encoding="utf8")
        select = rec.split("bool selectSpoolEdge(", 1)[1].split("void spoolWriter(", 1)[0]
        self.assertIn("epoch = captureEpochAtRec(edge);", select)
        self.assertIn("epoch = captures[i].epoch;", select)
        self.assertIn("captureEpochAtRec(edge)", main)
        self.assertIn("const uint32_t edge = recordingEdgeForSave(recordingPressTime());", main)
        isr = main.split("void ARDUINO_ISR_ATTR latchButtonEdge(", 1)[1].split("void ARDUINO_ISR_ATTR latchUpperEdge", 1)[0]
        self.assertIn("if (down) {\n      button.lastDownAt = now;", isr)
        self.assertIn("button.releasedDownAt = button.lastDownAt;", isr)
        self.assertIn("button.releasedAt = now;", isr)
        self.assertIn("upperButton.releasedDownAt == edge ? upperButton.releasedAt : 0", main)
        self.assertIn("return upperButton.lastDownAt;", main)
        self.assertIn("recordHeldToPath(BTN_REC, true, edge,", rec)

    def test_direct_carries_timestamp_from_button_to_journal(self):
        main = (SKETCH / "pala_note.ino").read_text(encoding="utf8")
        self.assertIn("record(capturedUtc, edge)", main)
        rec = (APP / "record.cpp").read_text(encoding="utf8")
        entry = rec.split("bool record(const String& capturedUtc, uint32_t edge) {",1)[1].split("bool recordQuestion",1)[0]
        self.assertLess(entry.index("beginNoteRecording(num, capturedUtc)"), entry.index("return String(path)"))
        writer = rec.split("static bool recordHeldToPath(", 1)[1]
        self.assertLess(writer.index("preparePath(noteNum, timing)"), writer.index("SD_MMC.open("))
        notes = (APP / "notes.cpp").read_text(encoding="utf8")
        self.assertIn("note_catalog::reserveRecording(",notes)
        recovery = notes.split("bool recoverCaptureIntent() {",1)[1].split("bool loadCatalogBase",1)[0]
        self.assertLess(recovery.index("capture_wav::recover("), recovery.index("repairCatalogRecording("))
        self.assertIn("capture_wav::restoreTimestamp(meta.c_str(), utc",recovery)
        self.assertIn('writeNoteMeta(note, "Inbox", captured)',recovery)

    def test_monthly_wav_has_one_resolver_for_capture_and_transport(self):
        rec = (APP / "record.cpp").read_text(encoding="utf-8")
        notes = (APP / "notes.cpp").read_text(encoding="utf-8")
        usb = (APP / "usb_sync.cpp").read_text(encoding="utf-8")
        wifi = (APP / "network.cpp").read_text(encoding="utf-8")
        for source in (rec, notes, usb, wifi):
            self.assertIn("monthly_recordings::path(", source)
        direct = rec.split("bool record(const String& capturedUtc, uint32_t edge)", 1)[1].split("bool recordQuestion", 1)[0]
        self.assertIn("nextNoteNumber(capturedUtc)", direct)
        priority = rec.split("bool adopt(String name,", 1)[1].split("bool readSpoolNames", 1)[0]
        self.assertIn("nextNoteNumber(rec_spool::captureUtc(seconds).c_str())", priority)

    def test_spool_repairs_before_catalog_ack(self):
        rec = (APP / "record.cpp").read_text(encoding="utf8")
        self.assertIn("rec_spool::promotePartial(",rec)
        entry=rec.split("bool adopt(String name,",1)[1].split("bool readSpoolNames",1)[0]
        self.assertIn("beginNoteRecording(note, utc)",entry)
        self.assertIn("utc = rec_spool::captureUtc(seconds)",entry)
        self.assertLess(entry.index("capture_wav::recover("),entry.index("saveTag("))


if __name__ == "__main__":
    unittest.main()
