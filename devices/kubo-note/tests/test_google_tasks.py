from pathlib import Path
import sys
import unittest
from unittest.mock import Mock


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
import google_tasks


class GoogleTasksPilotTests(unittest.TestCase):
    def setUp(self):
        self.api = Mock()
        self.lists = self.api.tasklists.return_value
        self.tasks = self.api.tasks.return_value
        self.tasks.list.return_value.execute.return_value = {}

    def test_inserts_synthetic_task_into_new_pilot_list_only(self):
        self.lists.list.return_value.execute.return_value = {
            "items": [{"id": "real-list", "title": "Nákup"}]
        }
        self.lists.insert.return_value.execute.return_value = {"id": "pilot"}
        self.tasks.insert.return_value.execute.return_value = {"id": "new-task"}
        self.tasks.get.return_value.execute.return_value = {
            "title": "mlieko", "notes": google_tasks.PILOT_MARKER,
        }

        self.assertTrue(google_tasks.run_pilot(self.api))

        self.lists.insert.assert_called_once_with(body={"title": "Kubo pilot"})
        self.tasks.insert.assert_called_once_with(tasklist="pilot", body={
            "title": "mlieko",
            "notes": google_tasks.PILOT_MARKER,
        })
        self.tasks.get.assert_called_once_with(tasklist="pilot", task="new-task")

    def test_readback_mismatch_cannot_report_success(self):
        self.lists.list.return_value.execute.return_value = {
            "items": [{"id": "pilot", "title": "Kubo pilot"}],
        }
        self.tasks.insert.return_value.execute.return_value = {"id": "new-task"}
        self.tasks.get.return_value.execute.return_value = {"title": "other"}

        with self.assertRaisesRegex(RuntimeError, "read back"):
            google_tasks.run_pilot(self.api)
        self.tasks.insert.assert_called_once()

    def test_reuses_edited_task_and_pilot_list_across_pages(self):
        self.lists.list.return_value.execute.side_effect = [
            {"items": [{"id": "real-list", "title": "Nákup"}], "nextPageToken": "next"},
            {"items": [{"id": "pilot", "title": "Kubo pilot"}]},
        ]
        self.tasks.list.return_value.execute.side_effect = [
            {"nextPageToken": "next-task"},
            {"items": [{"title": "Edited in the phone app", "notes": google_tasks.PILOT_MARKER}]},
        ]

        self.assertFalse(google_tasks.run_pilot(self.api))

        self.lists.insert.assert_not_called()
        self.tasks.insert.assert_not_called()
        self.tasks.list.assert_called_with(
            tasklist="pilot", pageToken="next-task", showCompleted=True, showHidden=True,
        )

    def test_refuses_when_pilot_list_title_is_ambiguous(self):
        self.lists.list.return_value.execute.return_value = {
            "items": [{"id": "one", "title": "Kubo pilot"}, {"id": "two", "title": "Kubo pilot"}],
        }
        with self.assertRaises(ValueError):
            google_tasks.run_pilot(self.api)
        self.lists.insert.assert_not_called()
        self.api.tasks.assert_not_called()
        self.tasks.insert.assert_not_called()
