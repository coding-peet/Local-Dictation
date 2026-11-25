"""Check that both production audit readers also guard replacement publication."""
from pathlib import Path
import unittest

APP = Path(__file__).resolve().parents[1] / "pala_note" / "src" / "app"


class AuditSourceWiring(unittest.TestCase):
    def test_confirmed_audit_and_rebuild_check_the_same_source_projection(self):
        audit = (APP / "restart_maintenance.cpp").read_text(encoding="utf-8")
        for call in ("verifyAgendaSources()", "verifySyncQueueSources()",
                     "requestAgendaIndexRebuild(true)", "requestSyncQueueRebuild(true)"):
            self.assertIn(call, audit)
        agenda = (APP / "agenda_index.cpp").read_text(encoding="utf-8")
        self.assertIn("verifyAgendaSourceRows(active)", agenda)
        self.assertIn("verifyReplacement && !verifyAgendaSourceRows(work.target)", agenda)
        self.assertIn("loadAgendaTasksForNote(id, rows) && applyStoredStates(rows)", agenda)
        self.assertIn("if (!loadAgendaTasksForNote(note->num, tasks)) return false;", agenda)
        queue = (APP / "sync_queue.cpp").read_text(encoding="utf-8")
        self.assertIn("ready && verifyQueueFile(queuePath, finalPass)", queue)
        start = queue.index("bool publishCandidate()")
        self.assertLess(queue.index("verifyQueueFile(candidatePath)", start),
                        queue.index("commitStoredFile(", start))
        self.assertIn("return classify(note, kind);", queue)

    def test_edit_and_migration_propagate_source_read_failures(self):
        tasks = (APP / "tasks.cpp").read_text(encoding="utf-8")
        self.assertIn("task_storage::edit(kMountedTaskState", tasks)
        self.assertIn("if (!readBrainMemoryChecked(noteNum, memory)) return false;", tasks)
        self.assertIn("if (!file.line(line) || line.empty()) return false;", tasks)
