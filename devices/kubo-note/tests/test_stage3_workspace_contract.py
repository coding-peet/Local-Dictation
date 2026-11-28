from datetime import date, datetime, timezone
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock
from urllib.parse import parse_qs
from zoneinfo import ZoneInfo

import requests
from google.auth.transport.requests import AuthorizedSession, Request

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from provider_task_views import TaskViews, ViewSelection
from test_workspace_tasks_mcp import LISTS, Response, Session, task
from workspace_tasks_mcp import GoogleTasksMcp, McpReadError, _scopes, load_read_grant


class ScriptedHttp(requests.adapters.BaseAdapter):
    """Answer inside requests. No socket, browser or real grant is needed."""

    def __init__(self, handler):
        self.handler = handler
        self.requests = []

    def send(self, request, **kwargs):
        self.requests.append(request)
        status, payload = self.handler(request)
        reply = requests.Response()
        reply.status_code = status
        reply.url = request.url
        reply.headers["Content-Type"] = "application/json"
        reply._content = json.dumps(payload).encode()
        reply._content_consumed = True
        return reply

    def close(self):
        pass


def grant(scopes):
    source = Mock()
    source.read_bytes.return_value = json.dumps({
        "client_id": "fixture-client",
        "client_secret": "fixture-secret",
        "refresh_token": "fixture-refresh",
        "scopes": list(scopes),
    }).encode()
    return source


class WorkspaceContractTest(unittest.TestCase):
    def selection(self, reader, day=date(2026, 10, 25), *, calendar="primary"):
        return ViewSelection(reader.account_id(), "saved", ZoneInfo("Europe/Bratislava"),
                             day, day, calendar)

    def test_real_authorized_session_refreshes_and_invalid_grant_clears_both_views(self):
        scopes = _scopes("primary")
        credentials = load_read_grant(grant(scopes), "primary")
        token_replies = iter([
            (200, {"access_token": "fixture-access", "expires_in": 3600, "scope": " ".join(scopes)}),
            (400, {"error": "invalid_grant"}),
        ])

        def refresh(request):
            self.assertEqual(request.url, "https://oauth2.googleapis.com/token")
            raw = request.body.decode() if isinstance(request.body, bytes) else request.body
            form = parse_qs(raw)
            self.assertEqual(form["grant_type"], ["refresh_token"])
            self.assertEqual(form["refresh_token"], ["fixture-refresh"])
            self.assertEqual(form["scope"], [" ".join(scopes)])
            return next(token_replies)

        def read(request):
            self.assertEqual(request.headers["Authorization"], "Bearer fixture-access")
            rpc = json.loads(request.body)
            name = rpc["params"]["name"]
            if name == "list_events":
                self.assertEqual(rpc["params"]["arguments"]["calendarId"], "primary")
            data = {"list_task_lists": LISTS, "list_tasks": {}, "list_events": {}}[name]
            return 200, {"jsonrpc": "2.0", "id": rpc["id"], "result": {"structuredContent": data}}

        tokens, provider = ScriptedHttp(refresh), ScriptedHttp(read)
        with requests.Session() as oauth:
            oauth.mount("https://", tokens)
            with AuthorizedSession(credentials, auth_request=Request(session=oauth)) as session:
                session.mount("https://", provider)
                reader = GoogleTasksMcp(session)
                views = TaskViews()
                selection = self.selection(reader)
                self.assertEqual(reader.refresh(views, selection).status, "online")
                self.assertEqual(len(provider.requests), 4)
                self.assertEqual(len(tokens.requests), 1)

                credentials.expiry = datetime(2000, 1, 1)
                result = reader.refresh(views, selection)
                self.assertEqual(result.status, "unavailable")
                self.assertIsNone(result.saved_items)
                self.assertIsNone(result.calendar)
                self.assertEqual(views.offline().status, "unavailable")
                self.assertEqual(len(tokens.requests), 2)
                self.assertEqual(len(provider.requests), 4)

    def test_excess_grant_scopes_writes_and_secondary_calendar_fail_before_io(self):
        scopes = _scopes("primary")
        for extra in ("openid", "https://www.googleapis.com/auth/calendar.events",
                      "https://www.googleapis.com/auth/tasks"):
            with self.subTest(extra=extra), self.assertRaises(McpReadError):
                load_read_grant(grant((*scopes, extra)), "primary")
        session = Session()
        reader = GoogleTasksMcp(session)
        for name in ("create_event", "update_event", "delete_event", "create_task", "list_calendars"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                reader._tool(name, {})
        with self.assertRaises(ValueError):
            reader.refresh(TaskViews(), self.selection(reader, calendar="secondary"))
        self.assertEqual(session.calls, [])

    def test_dst_folds_remain_distinct_and_all_day_end_stays_exclusive(self):
        def timed(ident, start, end):
            return {"id": ident, "summary": ident,
                    "start": {"dateTime": start}, "end": {"dateTime": end}}

        events = [
            timed("late", "2026-10-25T02:10:00+01:00", "2026-10-25T02:20:00+01:00"),
            {"id": "ended", "start": {"date": "2026-10-24"}, "end": {"date": "2026-10-25"}},
            timed("early", "2026-10-25T02:40:00+02:00", "2026-10-25T02:50:00+02:00"),
            {"id": "all-day", "start": {"date": "2026-10-25"}, "end": {"date": "2026-10-26"}},
        ]
        reader = GoogleTasksMcp(Session([LISTS, {}, {}, {"events": events}]))
        rows = reader.refresh(TaskViews(), self.selection(reader)).calendar
        self.assertEqual([row.task_id for row in rows], ["all-day", "early", "late"])
        self.assertEqual([row.scheduled.fold for row in rows[1:]], [0, 1])
        self.assertEqual(rows[0].ends, date(2026, 10, 26))

    def test_lost_calendar_page_reuses_only_the_previous_complete_view(self):
        event = {"id": "previous", "start": {"date": "2026-10-25"}, "end": {"date": "2026-10-26"}}
        session = Session([
            LISTS, {"tasks": [task("old", "saved")]}, {}, {"events": [event]},
            LISTS, {"tasks": [task("changed", "saved")]}, {},
            {"events": [{**event, "id": "partial"}], "nextPageToken": "retry"}, Response({}, 503),
        ])
        reader = GoogleTasksMcp(session)
        views = TaskViews(clock=lambda: datetime(2026, 10, 25, 12, tzinfo=timezone.utc))
        selection = self.selection(reader)
        previous = reader.refresh(views, selection)
        offline = reader.refresh(views, selection)
        self.assertEqual(offline.status, "offline")
        self.assertEqual(offline.updated_at, previous.updated_at)
        self.assertEqual(offline.calendar, previous.calendar)
        self.assertEqual(offline.saved_items, previous.saved_items)
        self.assertEqual([row.task_id for row in offline.calendar], ["previous"])
        self.assertEqual([row.task_id for row in offline.saved_items], ["old"])


if __name__ == "__main__":
    unittest.main()
