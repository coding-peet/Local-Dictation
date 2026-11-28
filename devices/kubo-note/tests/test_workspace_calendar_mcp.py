from datetime import date
import unittest
from zoneinfo import ZoneInfo

from test_workspace_tasks_mcp import LISTS, Session, task
from provider_task_views import TaskViews, ViewSelection
from workspace_tasks_mcp import CALENDAR_MCP_URL, GoogleTasksMcp, _scopes


class WorkspaceCalendarMcpTest(unittest.TestCase):
    def test_combines_timed_and_overlapping_events_with_scheduled_tasks(self):
        session = Session([
            LISTS,
            {"tasks": [task("saved-item", "saved"), task("timed-task", "saved",
                scheduledDate="2026-12-04", scheduledTime="08:30:00", timeZone="Europe/Bratislava")]},
            {},
            {"events": [
                {"id": "long", "summary": "All day", "start": {"date": "2026-12-03"},
                 "end": {"date": "2026-12-05"}},
                {"id": "old", "start": {"date": "2026-12-03"}, "end": {"date": "2026-12-04"}},
                {"id": "cancelled", "status": "cancelled"},
            ], "nextPageToken": "more"},
            {"events": [
                {"id": "meeting", "summary": "Test meeting",
                 "start": {"dateTime": "2026-12-04T08:15:00Z"},
                 "end": {"dateTime": "2026-12-04T08:45:00Z"}},
            ]},
        ])
        reader = GoogleTasksMcp(session)
        day = date(2026, 12, 4)
        selection = ViewSelection(reader.account_id(), "saved", ZoneInfo("Europe/Bratislava"),
                                  day, day, "primary")
        result = reader.refresh(TaskViews(), selection)
        self.assertEqual([item.task_id for item in result.saved_items], ["saved-item", "timed-task"])
        self.assertEqual([(item.kind, item.task_id) for item in result.calendar],
                         [("event", "long"), ("task", "timed-task"), ("event", "meeting")])
        self.assertEqual(result.calendar[-1].scheduled.isoformat(), "2026-12-04T09:15:00+01:00")
        self.assertEqual(result.calendar[-1].ends.isoformat(), "2026-12-04T09:45:00+01:00")
        calls = [options["json"]["params"] for method, url, options in session.calls
                 if url == CALENDAR_MCP_URL and method == "POST"]
        expected = {"calendarId": "primary", "startTime": "2026-12-04T00:00:00",
                    "endTime": "2026-12-05T00:00:00", "timeZone": "Europe/Bratislava",
                    "pageSize": 10, "orderBy": "startTime"}
        self.assertEqual(calls, [{"name": "list_events", "arguments": expected},
                                 {"name": "list_events", "arguments": {**expected, "pageToken": "more"}}])

    def test_combined_grant_only_requests_data_read_scopes(self):
        self.assertEqual(_scopes("primary"), (
            "https://www.googleapis.com/auth/tasks.readonly",
            "https://www.googleapis.com/auth/calendar.events.readonly",
        ))
        self.assertEqual(_scopes(None), ("https://www.googleapis.com/auth/tasks.readonly",))


if __name__ == "__main__":
    unittest.main()
