"""Guard coverage of the long production paths, the physical gate and durable final frame."""
from pathlib import Path
import unittest


SKETCH = Path(__file__).resolve().parents[1] / "pala_note"
APP = SKETCH / "src" / "app"


class AuditLifecycleWiring(unittest.TestCase):
    def test_claim_precedes_storage_and_confirmation_is_physical(self):
        sketch = (SKETCH / "pala_note.ino").read_text(encoding="utf-8")
        setup = sketch.split("void setup() {", 1)[1].split("void loop() {", 1)[0]
        self.assertLess(setup.index("claimNoteStorageAuditForBoot()"), setup.index("initializeStorage()"))
        self.assertEqual(setup.count("runRequestedNoteStorageAudit("), 1)
        self.assertIn("if (fullCheck) {\n    runRequestedNoteStorageAudit", setup)
        prompt = sketch.split("bool pollRestartConfirmation() {", 1)[1].split("bool pollPairingOverlay", 1)[0]
        self.assertIn("showNoteRestartPrompt(request.mode == NoteRestartMode::Audit)", prompt)
        self.assertLess(prompt.index("armed = armReleasedPrompt()"), prompt.index("answerNoteRestart(shown, !input.upper)"))
        ui = (APP / "ui.cpp").read_text(encoding="utf-8")
        restart_screen = ui.split("void showNoteRestartPrompt(bool audit) {", 1)[1].split("void showNoteRestartExpired()", 1)[0]
        self.assertIn('drawSideButtonChoices(sk ? "NIE" : "NO", sk ? "ANO" : "YES")', restart_screen)
        maintenance = (APP / "restart_maintenance.cpp").read_text(encoding="utf-8")
        claim = maintenance.split("bool claimNoteStorageAuditForBoot() {", 1)[1].split("bool runRequested", 1)[0]
        self.assertLess(claim.index("auditCookie = 0;"), claim.index("claimAuditBoot("))
        self.assertIn("visibleAuditState(report.state, activeDisplay != nullptr)", maintenance)

    def test_long_steps_report_their_real_units(self):
        expected = {
            "notes.cpp": ["LoadCatalog"],
            "note_trash_storage.cpp": ["Trash"],
            "sync_queue.cpp": ["LoadQueue", "QueueSources", "BuildQueue", "VerifyQueue"],
            "agenda_index.cpp": ["AgendaStates", "BuildAgenda", "SortAgenda", "SortStates", "AgendaSources",
                                 "AgendaKeys", "VerifyAgenda", "VerifyKeys"],
            "restart_maintenance.cpp": ["CheckCatalog", "Discovering", "Checking", "ScanOrphans", "TaskState"],
        }
        for name, phases in expected.items():
            source = (APP / name).read_text(encoding="utf-8")
            for phase in phases:
                with self.subTest(file=name, phase=phase):
                    self.assertIn("NoteStorageAuditPhase::" + phase, source)
                    self.assertIn("noteAuditWork(", source)

    def test_result_is_stored_and_read_back_before_final_frame(self):
        source = (APP / "restart_maintenance.cpp").read_text(encoding="utf-8")
        self.assertIn("prefs.getBytes(kReportKey, &readback, sizeof(readback))", source)
        run = source.split("bool runRequestedNoteStorageAudit(", 1)[1]
        self.assertLess(run.index("activeDisplay = &display"), run.index("initializeNoteCatalog(false)"))
        self.assertLess(run.index("repairDerived(report)"), run.index("finishAudit("))
        self.assertLess(run.index("finishAudit("), run.index("saved ? NoteStorageAuditPhase::Finished"))
        self.assertIn("verifyPublishedAgenda(true)", source)
        self.assertIn("verifySyncQueueSources(true)", source)
