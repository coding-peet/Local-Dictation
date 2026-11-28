from datetime import date, datetime, timedelta, timezone
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
from zoneinfo import ZoneInfo
import sys
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from bridge import Handler
from provider_task_views import TaskViewSnapshot, ViewItem
from workspace_calendar_feed import WorkspaceCalendarFeed, calendar_payload


class CalendarPayloadTest(unittest.TestCase):
    def test_note_receives_only_display_fields_and_source_status(self):
        day = date(2026, 12, 4)
        updated = datetime(2026, 11, 27, 8, 52, tzinfo=timezone.utc)
        event = ViewItem("calendar-id", "", "event-id", " Lunch\nwith friends ", day, False,
                         kind="event", ends=day + timedelta(days=1))
        task = ViewItem("list-id", "list name", "task-id", "Call",
                        datetime(2026, 12, 4, 14, tzinfo=timezone(timedelta(hours=1))), False)
        snapshot = TaskViewSnapshot("offline", updated, (), (event, task))
        self.assertEqual(calendar_payload(snapshot), {
            "status": "offline",
            "updated": int(updated.timestamp()),
            "truncated": False,
            "rows": [{"kind": "event", "title": "Lunch with friends", "start": "2026-12-04",
                      "end": "2026-12-05"},
                     {"kind": "task", "title": "Call", "start": "2026-12-04T14:00"}],
        })

    def test_v2_sends_selected_list_in_provider_order_without_google_identifiers(self):
        updated = datetime(2026, 11, 27, 8, 52, tzinfo=timezone.utc)
        items = (
            ViewItem("list-id", "Fixture list", "first", " Zulu\nentry ", None, False),
            ViewItem("list-id", "Fixture list", "second", "Alpha", date(2026, 12, 4), True),
        )
        snapshot = TaskViewSnapshot("offline", updated, items, (), saved_items_truncated=True,
                                    saved_list_title="Fixture list")
        payload = calendar_payload(snapshot, include_saved=True)
        self.assertEqual(payload["status"], "offline")
        self.assertEqual(payload["saved"], {
            "title": "Fixture list", "truncated": True,
            "rows": [{"title": "Zulu entry", "completed": False}],
        })
        self.assertNotIn("saved", calendar_payload(snapshot))

    def test_v2_keeps_empty_selected_list_and_clears_unavailable_snapshot(self):
        updated = datetime(2026, 11, 27, 8, 52, tzinfo=timezone.utc)
        snapshot = TaskViewSnapshot("online", updated, (), (), saved_list_title="Empty list")
        self.assertEqual(calendar_payload(snapshot, include_saved=True)["saved"],
                         {"title": "Empty list", "truncated": False, "rows": []})
        self.assertEqual(calendar_payload(TaskViewSnapshot("unavailable"), include_saved=True),
                         {"status": "unavailable"})

    def test_v2_stays_below_device_body_limit_with_maximum_utf8_rows(self):
        day = date(2026, 12, 4)
        now = datetime.now(timezone.utc)
        text = "\U0001f642" * 100
        events = tuple(ViewItem("cal", "", str(i), text, day, False, kind="event",
                                ends=day + timedelta(days=1)) for i in range(32))
        tasks = tuple(ViewItem("selected", "", str(i), text, None, False) for i in range(33))
        payload = calendar_payload(TaskViewSnapshot("online", now, tasks, events, saved_list_title=text),
                                   include_saved=True)
        self.assertLessEqual(len(json.dumps(payload, ensure_ascii=False).encode()), 32768)
        self.assertEqual(len(payload["saved"]["rows"]), 32)
        self.assertTrue(payload["saved"]["truncated"])

    def test_cached_saved_items_expire_and_never_survive_grant_replacement(self):
        with TemporaryDirectory() as temporary:
            grant = Path(temporary) / "synthetic-grant"
            grant.touch()
            feed = WorkspaceCalendarFeed("paired", "list", None, "Europe/Bratislava", grant_path=grant)
            now = datetime.now(ZoneInfo("Europe/Bratislava"))
            feed._day = now.date()
            items = (ViewItem("list", "", "t", "Cached title", None, False),)
            with patch.object(feed, "_schedule"):
                feed._current = TaskViewSnapshot("online", now - timedelta(minutes=5), items, ())
                cached = feed.payload_for("paired", include_saved=True)
                self.assertEqual((cached["status"], cached["saved"]["rows"][0]["title"]),
                                 ("offline", "Cached title"))
                feed._current = TaskViewSnapshot("offline", now - timedelta(hours=24), items, ())
                self.assertEqual(feed.payload_for("paired", include_saved=True), {"status": "unavailable"})
                feed._current = TaskViewSnapshot("online", now, items, ())
                grant.unlink()
                self.assertEqual(feed.payload_for("paired", include_saved=True), {"status": "unavailable"})

    def test_other_paired_notes_cannot_receive_payload_or_start_provider_read(self):
        feed = WorkspaceCalendarFeed("the-note", "the-list", None, "Europe/Bratislava")
        with patch.object(feed, "_schedule") as schedule:
            self.assertIsNone(feed.payload_for("another-note"))
            schedule.assert_not_called()
            self.assertEqual(feed.payload_for("the-note"), {"status": "unavailable"})
            schedule.assert_called_once()

    def test_http_route_requires_the_configured_note_token(self):
        feed = WorkspaceCalendarFeed("selected", "the-list", None, "Europe/Bratislava")
        bridge = SimpleNamespace(workspace_calendar=feed, config={"device_tokens": {
            "selected": "selected-token", "second": "second-token",
        }})
        for path in ("/pala/workspace-calendar", "/pala/workspace-calendar?v=2"):
            self._assert_paired_route(bridge, feed, path)

    def _assert_paired_route(self, bridge, feed, path):
        for token, status in (("selected-token", 200), ("second-token", 401)):
            with self.subTest(status=status, path=path), patch.object(feed, "_schedule"):
                handler = SimpleNamespace(
                    path=path,
                    server=SimpleNamespace(bridge=bridge),
                    headers={"Authorization": "Bearer " + token},
                    send_json=Mock(),
                    send_error=Mock(),
                )
                Handler.do_GET(handler)
                if status == 200:
                    handler.send_json.assert_called_once_with(200, {"status": "unavailable"})
                else:
                    handler.send_error.assert_called_once_with(401)
                    handler.send_json.assert_not_called()


if __name__ == "__main__":
    unittest.main()
