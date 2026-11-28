from datetime import date, datetime, timezone
import json
from pathlib import Path
import sys
import unittest
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from provider_task_views import TaskViews, ViewSelection
from workspace_tasks_mcp import GoogleTasksMcp, MCP_URL, McpReadError


class Response:
    def __init__(self, body, status=200, *, stream=False):
        kind = "text/event-stream" if stream else "application/json"
        self.headers = {"Content-Type": kind}
        self.status_code = status
        payload = json.dumps(body).encode("utf-8")
        self.body = b": comment\r\ndata: " + payload + b"\r\n\r\n" if stream else payload

    def __enter__(self):
        return self

    def __exit__(self, *_):
        pass

    def iter_content(self, chunk_size):
        for start in range(0, len(self.body), 11):
            yield self.body[start:start + 11]


class Session:
    def __init__(self, replies=()):
        self.replies = iter(replies)
        self.calls = []

    def request(self, method, url, **options):
        self.calls.append((method, url, options))
        item = next(self.replies)
        if isinstance(item, Response):
            return item
        request_id = options["json"]["id"]
        if isinstance(item, tuple):
            return Response({"jsonrpc": "2.0", "id": request_id,
                             "result": {"content": [{"type": "text", "text": json.dumps(item[0])}]}}, stream=True)
        return Response({"jsonrpc": "2.0", "id": request_id, "result": {"structuredContent": item}})


LISTS = {"taskLists": [{"id": "saved", "title": "Shared title"}, {"id": "other", "title": "Shared title"}]}


def task(task_id, list_id, **fields):
    return {"id": task_id, "title": task_id, "taskListId": list_id, **fields}


class WorkspaceTasksMcpTest(unittest.TestCase):
    def setUp(self):
        self.views = TaskViews(clock=lambda: datetime(2026, 11, 27, 12, tzinfo=timezone.utc))

    def selection(self, reader):
        day = date(2026, 11, 27)
        return ViewSelection(reader.account_id(), "saved", ZoneInfo("Europe/Bratislava"), day, day)

    def test_views_from_grant_mcp_lists_and_paginated_tasks(self):
        session = Session([
            LISTS,
            {"tasks": [task("child", "saved", parentTaskId="parent", position=0),
                       task("last", "saved", position=2, deadlineDate="2026-11-27")],
             "nextPageToken": "second"},
            ({"tasks": [task("parent", "saved", position=0),
                        task("dated", "saved", position=1, scheduledDate="2026-11-27", completed=True)]},),
            {"tasks": [task("time", "other", scheduledDate="2026-11-26",
                            scheduledTime="23:45:00", timeZone="UTC"),
                       task("deleted", "other", deleted=True, scheduledDate="2026-11-27")]},
        ])
        reader = GoogleTasksMcp(session)
        result = reader.refresh(self.views, self.selection(reader))
        self.assertEqual(result.status, "online")
        self.assertEqual([row.task_id for row in result.saved_items], ["parent", "child", "last"])
        self.assertEqual([row.task_id for row in result.calendar], ["dated", "time"])
        self.assertTrue(result.calendar[0].completed)
        self.assertEqual(result.calendar[1].scheduled.isoformat(), "2026-11-27T00:45:00+01:00")
        for method, url, options in session.calls:
            self.assertEqual((method, url), ("POST", MCP_URL))
            self.assertFalse(options["allow_redirects"])
            self.assertEqual(options["json"]["method"], "tools/call")
        requests = [options["json"]["params"] for _, _, options in session.calls]
        self.assertEqual([r["name"] for r in requests],
                         ["list_task_lists", "list_tasks", "list_tasks", "list_tasks"])
        first = requests[1]["arguments"]
        self.assertEqual(requests[2]["arguments"], {**first, "pageToken": "second"})
        self.assertEqual(first["filter"], {"taskListId": "saved", "state": "ALL"})
        self.assertEqual(requests[3]["arguments"]["filter"]["timeFilter"],
                         {"property": "SCHEDULED_TIME", "startDate": "2026-11-25", "endDate": "2026-11-29"})

    def test_new_grant_cannot_read_or_reuse_a_previous_grants_selection(self):
        previous = GoogleTasksMcp(Session([LISTS, {"tasks": [task("note", "saved")]}, {}]))
        selection = self.selection(previous)
        self.assertEqual(previous.refresh(self.views, selection).status, "online")
        session = Session()
        reader = GoogleTasksMcp(session)
        self.assertEqual(reader.refresh(self.views, selection).status, "unavailable")
        self.assertEqual(session.calls, [])
        self.assertEqual(self.views.offline().status, "unavailable")

    def test_revoked_grant_purges_existing_cache_and_temporary_failure_keeps_it(self):
        session = Session([LISTS, {"tasks": [task("note", "saved")]}, {},
                           Response({}, 503), Response({}, 403)])
        reader = GoogleTasksMcp(session)
        selection = self.selection(reader)
        self.assertEqual(reader.refresh(self.views, selection).status, "online")
        self.assertEqual(reader.refresh(self.views, selection).status, "offline")
        self.assertEqual(reader.refresh(self.views, selection).status, "unavailable")
        self.assertEqual(self.views.offline().status, "unavailable")

    def test_incomplete_or_wrong_scope_responses_fail_closed(self):
        for responses in (
            [{**LISTS, "hasMoreItems": True}],
            [LISTS, {"tasks": [task("wrong-list", "other")]}],
            [LISTS, {"nextPageToken": "same"}, {"nextPageToken": "same"}],
            [Response({"jsonrpc": "2.0", "id": 999, "result": {"structuredContent": LISTS}})],
        ):
            with self.subTest(responses=responses), self.assertRaises(McpReadError):
                reader = GoogleTasksMcp(Session(responses))
                reader.refresh(self.views, self.selection(reader))


if __name__ == "__main__":
    unittest.main()
