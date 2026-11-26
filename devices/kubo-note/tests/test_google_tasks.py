from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch


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


class GoogleTasksDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.api = Mock()
        self.tasks = self.api.tasks.return_value
        self.rows = {}
        self.list_id = "shopping-list-id"
        self.tasks.list.side_effect = self.list_request
        self.tasks.get.side_effect = self.get_request
        self.tasks.insert.side_effect = self.insert_request
        self.tasks.patch.side_effect = self.patch_request
        self.insert_response_lost = False
        self.insert_rejected = False
        self.patch_response_lost = False
        self.get_response_lost = False

    def list_request(self, *, tasklist, pageToken, **filters):
        self.assertEqual(tasklist, self.list_id)
        self.assertTrue(filters["showCompleted"])
        self.assertTrue(filters["showHidden"])
        request = Mock()
        if pageToken is None:
            request.execute.return_value = {"nextPageToken": "next-page"}
        else:
            request.execute.return_value = {"items": [row.copy() for row in self.rows.values()]}
        return request

    def get_request(self, *, tasklist, task):
        self.assertEqual(tasklist, self.list_id)
        request = Mock()
        if self.get_response_lost:
            request.execute.side_effect = TimeoutError()
        else:
            request.execute.return_value = self.rows[task].copy()
        return request

    def insert_request(self, *, tasklist, body):
        self.assertEqual(tasklist, self.list_id)
        request = Mock()

        def execute():
            if self.insert_rejected:
                raise TimeoutError()
            task_id = f"inserted-{len(self.rows) + 1}"
            self.add_task(task_id, **body)
            if self.insert_response_lost:
                raise TimeoutError()
            return {"id": task_id}

        request.execute.side_effect = execute
        return request

    def patch_request(self, *, tasklist, task, body):
        self.assertEqual(tasklist, self.list_id)
        self.assertEqual(set(body), {"notes"})
        request = Mock()
        request.headers = {}

        def execute():
            self.assertEqual(request.headers["If-Match"], self.rows[task]["etag"])
            self.rows[task].update(body)
            if self.patch_response_lost:
                raise TimeoutError()
            return self.rows[task].copy()

        request.execute.side_effect = execute
        return request

    def add_task(self, task_id, **fields):
        self.rows[task_id] = {"id": task_id, "etag": '"test-etag"', "status": "needsAction", **fields}

    def apply(self, title, key, **kwargs):
        return google_tasks.apply_task(self.api, self.list_id, title, key, **kwargs)

    def test_replay_returns_provider_readback_even_after_edit_and_completion(self):
        created = self.apply("mlieko", "recording-1", coalesce=True)
        task_id = created["id"]
        self.assertEqual(created, self.rows[task_id])
        self.rows[task_id].update(title="polotučné mlieko", status="completed", hidden=True)

        replayed = self.apply("mlieko", "recording-1", coalesce=True)

        self.assertEqual(replayed, self.rows[task_id])
        self.tasks.insert.assert_called_once()
        self.tasks.patch.assert_not_called()
        self.assertEqual(self.tasks.get.call_count, 2)
        self.api.tasklists.assert_not_called()

    def test_read_only_reconciliation_returns_the_saved_action_or_none(self):
        created = self.apply("mlieko", "first")
        self.rows[created["id"]]["title"] = "polotučné mlieko"

        found = google_tasks.read_task_action(self.api, self.list_id, "first")
        missing = google_tasks.read_task_action(self.api, self.list_id, "other")

        self.assertEqual(found, self.rows[created["id"]])
        self.assertIsNone(missing)
        self.tasks.insert.assert_called_once()
        self.tasks.patch.assert_not_called()

    def test_marked_task_without_id_blocks_replay(self):
        created = self.apply("mlieko", "first")
        self.rows[created["id"]]["id"] = None

        with self.assertRaises(ValueError):
            self.apply("mlieko", "first")
        self.tasks.insert.assert_called_once()
        self.tasks.patch.assert_not_called()

    def test_distinct_purchase_requests_increment_one_managed_line(self):
        user_notes = "Obľúbená značka\r\nKupón má kód 123  \n"
        created = self.apply(" Čerstvé   mlieko ", "first", coalesce=True, notes=user_notes)
        repeated = self.apply("C\u030cERSTVÉ mlieko", "second", coalesce=True)
        replayed = self.apply("čerstvé mlieko", "second", coalesce=True)
        third = self.apply("čerstvé mlieko", "third", coalesce=True)

        self.assertEqual(repeated["id"], created["id"])
        self.assertEqual(repeated, replayed)
        self.assertIn("Opakovaná požiadavka (x2)", repeated["notes"].splitlines())
        self.assertEqual(third["id"], created["id"])
        self.assertEqual(third["title"], created["title"])
        self.assertTrue(third["notes"].startswith(user_notes))
        self.assertEqual(
            [line for line in third["notes"].splitlines() if line.startswith("Opakovaná požiadavka")],
            ["Opakovaná požiadavka (x3)"],
        )
        self.tasks.insert.assert_called_once()
        self.assertEqual(self.tasks.patch.call_count, 2)

    def settle_purchase(self, state, title, key="shopping-recording", mode="write"):
        return google_tasks.settle_clean_purchase(state, self.api, self.list_id, title, key, mode)

    def test_clean_purchase_uses_no_internal_note(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            self.assertTrue(self.settle_purchase(state, "šunka"))
            self.assertTrue(self.settle_purchase(state, "šunka", mode="reconcile"))
        saved, = self.rows.values()
        self.assertEqual(saved["title"], "šunka")
        self.assertEqual(saved.get("notes", ""), "")
        self.tasks.insert.assert_called_once_with(tasklist=self.list_id, body={"title": "šunka"})

    def test_clean_purchase_merges_inflected_open_product(self):
        self.add_task("manual", title="Šunku", notes="Od mäsiara")
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            self.assertTrue(self.settle_purchase(state, "šunka"))
            self.assertTrue(self.settle_purchase(state, "šunka", mode="reconcile"))
        saved, = self.rows.values()
        self.assertEqual(saved["id"], "manual")
        self.assertEqual(saved["notes"], "Od mäsiara\n\nOpakovaná požiadavka (x2)")
        self.tasks.insert.assert_not_called()

    def test_clean_purchase_reconciles_lost_readback_without_another_insert(self):
        self.get_response_lost = True
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            with self.assertRaises(google_tasks.TaskWriteUncertain):
                self.settle_purchase(state, "šunka")
            self.get_response_lost = False
            self.assertTrue(self.settle_purchase(state, "šunka", mode="reconcile"))
            self.assertTrue(self.settle_purchase(state, "šunka", mode="reconcile"))
        self.tasks.insert.assert_called_once()
        self.tasks.patch.assert_not_called()

    def test_unverified_clean_insert_is_never_reissued_by_other_bridge(self):
        self.insert_response_lost = True
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            with self.assertRaises(google_tasks.TaskWriteUncertain):
                self.settle_purchase(Path(first), "šunka")
            self.assertFalse(self.settle_purchase(Path(first), "šunka", mode="reconcile"))
            self.assertFalse(self.settle_purchase(Path(second), "šunka", mode="reconcile"))
        self.tasks.insert.assert_called_once()
        self.assertEqual(len(self.rows), 1)

    def test_recorder_syncs_two_ham_forms_as_one_clean_item_after_lost_ack(self):
        import threading
        import usb_sync
        from bridge import Bridge
        from test_recorder_delivery import DEVICE, RecorderNote, TOKEN

        note = RecorderNote("DONE", modern=True)
        note.files[42] = "Daj do nákupu šunka, šunku.".encode()
        self.api.tasklists.return_value.get.return_value.execute.return_value = {"id": "account"}
        profile = {"processing_mode": "kubo", "google_routes": {
            "shopping": {"id": self.list_id, "title": "Nákup"},
        }}
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second, \
                patch("recorder_delivery._api") as connection, patch("recorder_delivery._verify_task_list"):
            connection.return_value.__enter__.return_value = self.api
            a, b = Bridge(Path(first), profile), Bridge(Path(second), profile)
            with self.assertRaises(OSError):
                usb_sync._drain_link(note, TOKEN, a, threading.Event(), device=DEVICE)
            usb_sync._drain_link(note, TOKEN, a, threading.Event(), device=DEVICE)
            usb_sync._drain_link(note, TOKEN, b, threading.Event(), device=DEVICE)
        self.tasks.insert.assert_called_once_with(tasklist=self.list_id, body={"title": "šunka"})
        self.tasks.patch.assert_not_called()
        self.assertEqual(note.delivered, [42])

    def test_existing_manual_purchase_notes_are_preserved(self):
        user_notes = "Peterova poznámka\nOpakovaná požiadavka (x7)"
        self.add_task("manual", title="mlieko", notes=user_notes)

        saved = self.apply("MLIEKO", "first", coalesce=True)

        self.assertEqual(saved["id"], "manual")
        self.assertTrue(saved["notes"].startswith(user_notes + "\n\n"))
        self.assertIn("Opakovaná požiadavka (x2)", saved["notes"].splitlines())
        self.tasks.insert.assert_not_called()

    def test_new_purchase_can_follow_completed_purchase(self):
        self.add_task("completed", title="mlieko", status="completed", hidden=True)

        saved = self.apply("MLIEKO", "first", coalesce=True)

        self.assertNotEqual(saved["id"], "completed")
        self.tasks.patch.assert_not_called()

    def test_normal_tasks_with_equal_titles_remain_separate(self):
        first = self.apply("zavolať", "first")
        second = self.apply("zavolať", "second")
        self.assertNotEqual(first["id"], second["id"])
        self.tasks.patch.assert_not_called()

    def test_refuses_ambiguous_open_purchases(self):
        self.add_task("one", title=" mlieko ")
        self.add_task("two", title="MLIEKO")

        with self.assertRaises(ValueError):
            self.apply("mlieko", "first", coalesce=True)
        self.tasks.insert.assert_not_called()
        self.tasks.patch.assert_not_called()

    def test_refuses_duplicate_action_markers(self):
        first = self.apply("mlieko", "first")
        self.add_task("copied", title="mlieko", notes=first["notes"])

        with self.assertRaises(ValueError):
            self.apply("mlieko", "first")
        self.tasks.insert.assert_called_once()
        self.tasks.patch.assert_not_called()

    def test_changed_managed_count_is_not_overwritten_or_replayed(self):
        self.apply("mlieko", "first", coalesce=True)
        repeat = self.apply("mlieko", "second", coalesce=True)
        row = self.rows[repeat["id"]]
        row["notes"] = row["notes"].replace("Opakovaná požiadavka (x2)", "Opakovaná požiadavka (x9)")

        with self.assertRaises(ValueError):
            self.apply("mlieko", "second", coalesce=True)
        self.tasks.insert.assert_called_once()
        self.tasks.patch.assert_called_once()

    def test_rejects_default_alias_or_oversized_payload_before_mutation(self):
        with self.assertRaises(ValueError):
            google_tasks.apply_task(self.api, "@default", "mlieko", "first")
        with self.assertRaises(ValueError):
            self.apply("a" * 1025, "first")
        with self.assertRaises(ValueError):
            self.apply("mlieko", "first", notes="a" * 8192)

        self.add_task("full", title="mlieko", notes="a" * 8192)
        with self.assertRaises(ValueError):
            self.apply("mlieko", "first", coalesce=True)
        self.tasks.insert.assert_not_called()
        self.tasks.patch.assert_not_called()

    def test_lost_insert_response_is_reconciled_by_readback(self):
        self.insert_response_lost = True

        created = self.apply("mlieko", "first")

        self.assertEqual(created, self.rows[created["id"]])
        self.tasks.insert.assert_called_once()
        self.tasks.get.assert_called_once_with(tasklist=self.list_id, task=created["id"])

    def test_unobserved_insert_remains_uncertain(self):
        self.insert_rejected = True

        with self.assertRaises(google_tasks.TaskWriteUncertain):
            self.apply("mlieko", "first")
        self.tasks.insert.assert_called_once()
        self.assertEqual(self.rows, {})

    def test_unavailable_readback_remains_uncertain(self):
        self.get_response_lost = True

        with self.assertRaises(google_tasks.TaskWriteUncertain):
            self.apply("mlieko", "first")
        self.tasks.insert.assert_called_once()
        self.assertEqual(len(self.rows), 1)

    def test_lost_patch_response_is_reconciled_by_readback(self):
        self.add_task("manual", title="mlieko", notes="Poznámka")
        self.patch_response_lost = True

        saved = self.apply("mlieko", "first", coalesce=True)

        self.assertEqual(saved, self.rows["manual"])
        self.assertIn("Opakovaná požiadavka (x2)", saved["notes"].splitlines())
        self.tasks.patch.assert_called_once()
