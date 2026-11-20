import json
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from pala_task_pilot import PalaTaskPilot, find_task_list


class PalaTaskPilotTests(unittest.TestCase):
    def setUp(self):
        self.api = Mock()
        self.tasks = self.api.tasks.return_value
        self.tasks.list.return_value.execute.return_value = {}
        self.title = "Skontrolovať modrý zápisník"
        self.transcript = "Úloha, skontrolovať modrý zápisník."
        self.pilot = PalaTaskPilot(Path("."), {}, self.tasks, "general-list", self.title)
        self.doc = {
            "pala_note": 23,
            "pala_created": "2026-11-20T00:00:00Z",
            "pala_transcript": self.transcript,
            "text": {"format": {"name": "pala_memory", "schema": {
                "properties": {"language": {"enum": ["en"]}}
            }}},
        }

    def memory(self):
        result = self.pilot.dispatch("/v1/responses", json.dumps(self.doc).encode(), "application/json")
        return json.loads(result["output"][0]["content"][0]["text"])

    def test_saved_transcript_without_a_matching_wav_cannot_write(self):
        with self.assertRaises(ValueError):
            self.memory()
        self.tasks.insert.assert_not_called()

    def test_only_the_confirmed_dictation_is_routed_and_replay_does_not_duplicate_it(self):
        with patch.object(self.pilot, "transcribe", return_value={"text": self.transcript}) as transcribe:
            self.pilot.dispatch("/v1/audio/transcriptions", b"test audio", "test content type")
        transcribe.assert_called_once_with(b"test audio", "test content type")
        self.doc["pala_transcript"] = "Úloha, poslať faktúru."
        with self.assertRaises(ValueError):
            self.memory()
        self.tasks.insert.assert_not_called()
        self.doc["pala_transcript"] = self.transcript

        marker = self.pilot.marker(self.doc)
        self.tasks.insert.return_value.execute.return_value = {"id": "new-task"}
        self.tasks.get.return_value.execute.return_value = {"title": self.title, "notes": marker}
        memory = self.memory()
        self.assertEqual(memory["tasks"][0]["title"], self.title)
        self.assertEqual(memory["language"], "en")
        self.assertTrue(self.pilot.verified.is_set())
        self.tasks.list.return_value.execute.return_value = {"items": [{"id": "new-task", "notes": marker}]}
        self.memory()
        self.tasks.insert.assert_called_once_with(tasklist="general-list",
                                                  body={"title": self.title, "notes": marker})
        self.tasks.get.assert_called_with(tasklist="general-list", task="new-task")

    def test_missing_or_ambiguous_general_list_never_creates_another_list(self):
        lists = self.api.tasklists.return_value
        for entries in ([], [{"title": "Kubo tasks general", "id": "a"},
                             {"title": "Kubo tasks general", "id": "b"}]):
            with self.subTest(entries=entries):
                lists.list.return_value.execute.return_value = {"items": entries}
                with self.assertRaises(ValueError):
                    find_task_list(self.api, "Kubo tasks general")
        lists.insert.assert_not_called()

    def test_selects_only_the_exact_list_chosen_for_this_run(self):
        self.api.tasklists.return_value.list.return_value.execute.return_value = {"items": [
            {"title": "Kubo notes general", "id": "notes"},
            {"title": "Kubo tasks general", "id": "tasks"},
        ]}
        self.assertEqual(find_task_list(self.api, "Kubo notes general"), "notes")
