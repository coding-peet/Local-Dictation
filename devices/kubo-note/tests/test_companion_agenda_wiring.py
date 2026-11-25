"""Guard the transfer-mode call site used by the Companion HTTP session regression."""
from pathlib import Path
import unittest


class CompanionAgendaWiring(unittest.TestCase):
    def test_one_maintenance_slice_after_web_and_controls_in_transfer_mode(self):
        sketch = (Path(__file__).resolve().parents[1] / "pala_note/pala_note.ino").read_text(encoding="utf-8")
        companion = sketch.split("void handleCompanion() {", 1)[1].split("\n}\n", 1)[0]
        self.assertLess(companion.index("transferServer.handleClient()"), companion.index("maintainCompanionAgenda()"))
        self.assertLess(companion.index("pollBufferedCompanionControls()"), companion.index("maintainCompanionAgenda()"))
        self.assertIn("!pendingPriorityRecording()) maintainCompanionAgenda();", companion)
        self.assertEqual(companion.count("maintainCompanionAgenda();"), 1)
        loop = sketch.split("void loop() {", 1)[1]
        self.assertIn("if (state == STATE_TRANSFER) {\n    handleCompanion();", loop)
