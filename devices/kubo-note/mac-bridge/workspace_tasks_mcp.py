"""Read Google Tasks and Calendar MCP into the Bridge views."""
import argparse
from datetime import date, datetime, time, timedelta, timezone
from itertools import count
import json
from pathlib import Path
import sys
import unicodedata
from uuid import uuid4
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from google.auth.exceptions import RefreshError, TransportError
from requests.exceptions import RequestException

from provider_task_views import (
    AccessRevoked, AccountTasks, CalendarEvent, ProviderOffline, Task, TaskList, TaskViews, ViewSelection,
)


MCP_URL = "https://tasksmcp.googleapis.com/mcp/v1self"
CALENDAR_MCP_URL = "https://calendarmcp.googleapis.com/mcp/v1self"
_TASKS_READ = "https://www.googleapis.com/auth/tasks.readonly"
_EVENTS_READ = "https://www.googleapis.com/auth/calendar.events.readonly"
_READ_TOOLS = {"list_task_lists": MCP_URL, "list_tasks": MCP_URL, "list_events": CALENDAR_MCP_URL}
_MAX_BODY = 2 * 1024 * 1024
_MAX_PAGES = 100
_PAGE_SIZE = 100


class McpReadError(RuntimeError):
    """The provider result was invalid or did not cover the requested view."""


def _object(value):
    if not isinstance(value, dict):
        raise McpReadError("Google returned a non-object response.")
    return value


def _json(raw):
    try:
        return _object(json.loads(raw))
    except (ValueError, UnicodeError):
        raise McpReadError("Google returned an invalid JSON response.") from None


def _array(payload, field):
    value = payload.get(field, [])
    if not isinstance(value, list):
        raise McpReadError("Invalid Google MCP array: " + field)
    return map(_object, value)


def _string(payload, field, *, required=False):
    value = payload.get(field, "")
    if not isinstance(value, str) or (required and not value):
        raise McpReadError("Invalid Google MCP string: " + field)
    return value


def _read_response(response, request_id):
    media = response.headers.get("Content-Type", "").split(";", 1)[0].strip().lower()
    body = bytearray()
    event = []
    consumed = 0
    if media not in ("application/json", "text/event-stream"):
        raise McpReadError("Unexpected Google response media type.")
    for chunk in response.iter_content(chunk_size=16384):
        consumed += len(chunk)
        if consumed > _MAX_BODY:
            raise McpReadError("Google response exceeds the Bridge read limit.")
        body.extend(chunk)
        if media == "application/json":
            continue
        while (end := body.find(b"\n")) >= 0:
            line = bytes(body[:end]).removesuffix(b"\r")
            del body[:end + 1]
            if line.startswith(b"data:"):
                event.append(line[5:].removeprefix(b" "))
            elif not line and event:
                message = _json(b"\n".join(event))
                event.clear()
                if message.get("id") == request_id and "method" not in message:
                    return message
                if "method" in message and "id" in message:
                    raise AccessRevoked("The Google request requires additional approval.")
    if media == "text/event-stream":
        raise McpReadError("Google MCP stream ended without a matching reply.")
    return _json(body)


def _scheduled(task):
    day_text = _string(task, "scheduledDate")
    time_text = _string(task, "scheduledTime")
    if not day_text and not time_text:
        return None
    try:
        day = date.fromisoformat(day_text)
        if not time_text:
            return day
        clock = time.fromisoformat(time_text)
        if clock.tzinfo is not None:
            return datetime.combine(day, clock)
        zone = ZoneInfo(_string(task, "timeZone") or "UTC")
        # On a repeated wall-clock time, the response supplies a zone and no fold.
        # Use its first occurrence. A nonexistent wall-clock time is invalid.
        local = datetime.combine(day, clock, tzinfo=zone)
        if local.astimezone(timezone.utc).astimezone(zone).replace(tzinfo=None) != local.replace(tzinfo=None):
            raise ValueError("Nonexistent local time")
        return local
    except (ValueError, ZoneInfoNotFoundError):
        raise McpReadError("Google Tasks returned an invalid scheduled date or time.") from None


def _event_boundary(row, field, default_zone):
    value = _object(row.get(field))
    day = _string(value, "date")
    timestamp = _string(value, "dateTime")
    try:
        if day and not timestamp:
            return date.fromisoformat(day)
        if timestamp and not day:
            instant = datetime.fromisoformat(timestamp)
            if instant.utcoffset() is None:
                zone_name = _string(value, "timeZone")
                instant = instant.replace(tzinfo=ZoneInfo(zone_name) if zone_name else default_zone)
            return instant
    except (ValueError, ZoneInfoNotFoundError):
        pass
    raise McpReadError("Google Calendar returned an invalid event boundary.")


def _provider_order(rows):
    """Flatten each sibling group in Google's custom order, following its parent."""
    ids = {item.task_id for item, _, _ in rows}
    children = {}
    for item, parent, position in rows:
        children.setdefault(parent if parent in ids else "", []).append((position, item))
    for siblings in children.values():
        siblings.sort(key=lambda pair: (pair[0], pair[1].task_id))
    stack = list(reversed(children.get("", ())))
    output = []
    while stack:
        _, item = stack.pop()
        output.append(item)
        stack.extend(reversed(children.get(item.task_id, ())))
    if len(output) != len(rows):
        raise McpReadError("Google returned an invalid hierarchy of tasks.")
    return output


class GoogleTasksMcp:
    """Read Google Workspace MCP with one immutable authorization session."""

    def __init__(self, session):
        self._session = session
        # A new login creates a new reader and cannot reuse a previous reader's
        # cached data. No Google profile or OpenID scope is needed.
        self._grant_id = uuid4().hex
        self._ids = count(1)

    def _request(self, method, url, request_id=None, **options):
        try:
            with self._session.request(method, url, timeout=15, max_allowed_time=30,
                                      stream=True, allow_redirects=False, **options) as response:
                code = response.status_code
                if code in (401, 403):
                    raise AccessRevoked(f"Google denied read access (HTTP {code}).")
                if code in (408, 429) or code >= 500:
                    raise ProviderOffline(f"Google temporarily unavailable (HTTP {code}).")
                if code != 200:
                    raise McpReadError(f"Unexpected Google HTTP status {code}.")
                return _read_response(response, request_id)
        except RefreshError as error:
            if error.retryable:
                raise ProviderOffline("Google token refresh temporarily failed.") from None
            raise AccessRevoked("The Google OAuth grant could not be refreshed.") from None
        except (TransportError, RequestException):
            raise ProviderOffline("Google could not be reached.") from None

    def account_id(self):
        """Return the local identifier of this authorization session."""
        return self._grant_id

    def _tool(self, name, arguments):
        url = _READ_TOOLS.get(name)
        if url is None:
            raise ValueError("Only configured Google Workspace read tools may be called.")
        request_id = next(self._ids)
        message = self._request(
            "POST", url, request_id,
            headers={"Accept": "application/json, text/event-stream"},
            json={"jsonrpc": "2.0", "id": request_id, "method": "tools/call",
                  "params": {"name": name, "arguments": arguments}},
        )
        if message.get("id") != request_id or message.get("jsonrpc") != "2.0":
            raise McpReadError("Google MCP reply does not match the request.")
        if "error" in message:
            code = _object(message["error"]).get("code")
            if code in (401, 403, -32001):
                raise AccessRevoked("Google MCP rejected authorization.")
            raise McpReadError(f"Google MCP read error {code if type(code) is int else 'unknown'}.")
        result = _object(message.get("result"))
        if result.get("isError") is True:
            raise McpReadError("Google MCP reported a tool error.")
        if "structuredContent" in result:
            return _object(result["structuredContent"])
        content = list(_array(result, "content"))
        if len(content) == 1 and content[0].get("type") == "text":
            return _json(_string(content[0], "text", required=True))
        raise McpReadError("Google MCP returned no structured read data.")

    def _lists(self):
        page = self._tool("list_task_lists", {"pageSize": _PAGE_SIZE})
        if page.get("hasMoreItems", False) is not False:
            raise McpReadError("Google did not return all task lists and exposes no list page token.")
        known = set()
        for row in _array(page, "taskLists"):
            list_id = _string(row, "id", required=True)
            if list_id in known:
                raise McpReadError("Google returned a duplicate task list.")
            known.add(list_id)
            yield list_id, _string(row, "title")

    def find_list(self, account_id, title):
        if self.account_id() != account_id:
            raise AccessRevoked("The selection belongs to a different Google authorization session.")
        found = [ident for ident, name in self._lists() if name == title]
        if len(found) != 1:
            raise McpReadError("Google must return exactly one list with the selected title.")
        return found[0]

    def _tasks(self, list_id, selection):
        filters = {"taskListId": list_id, "state": "ALL"}
        selected = list_id == selection.saved_list_id
        if not selected:
            # Google filters on the task's own scheduled date. Allow the largest
            # zone shift before the Bridge applies the actual display dates.
            start = date.fromordinal(max(date.min.toordinal(), selection.calendar_start.toordinal() - 2))
            end = date.fromordinal(min(date.max.toordinal(), selection.calendar_end.toordinal() + 2))
            filters["timeFilter"] = {"property": "SCHEDULED_TIME",
                                     "startDate": start.isoformat(), "endDate": end.isoformat()}
        args = {"filter": filters, "pageSize": _PAGE_SIZE}
        token = ""
        seen_tokens = set()
        seen_ids = set()
        ordered = []
        for _ in range(_MAX_PAGES):
            page = self._tool("list_tasks", {**args, **({"pageToken": token} if token else {})})
            for row in _array(page, "tasks"):
                if _string(row, "taskListId") not in ("", list_id):
                    raise McpReadError("Google returned a task from a different list.")
                if row.get("deleted") is True:
                    continue
                ident = _string(row, "id", required=True)
                if ident in seen_ids:
                    raise McpReadError("Google returned the same task on more than one page.")
                seen_ids.add(ident)
                item = Task(ident, _string(row, "title"), _scheduled(row), row.get("completed") is True)
                if selected:
                    position = row.get("position", sys.maxsize)
                    if type(position) is not int or position < 0:
                        raise McpReadError("Google returned an invalid task position.")
                    ordered.append((item, _string(row, "parentTaskId"), position))
                else:
                    yield item
            token = _string(page, "nextPageToken")
            if not token:
                yield from _provider_order(ordered)
                return
            if token in seen_tokens or len(token) > 4096:
                raise McpReadError("Google Tasks pagination did not advance.")
            seen_tokens.add(token)
        raise McpReadError("Google Tasks pagination exceeded the Bridge read limit.")

    def _events(self, selection):
        args = {
            "calendarId": "primary",
            "startTime": f"{selection.calendar_start.isoformat()}T00:00:00",
            "endTime": f"{selection.calendar_end + timedelta(days=1):%Y-%m-%d}T00:00:00",
            "timeZone": selection.zone.key,
            "pageSize": 10,
            "orderBy": "startTime",
        }
        token = ""
        seen_tokens = set()
        seen_ids = set()
        for _ in range(_MAX_PAGES):
            page = self._tool("list_events", {**args, **({"pageToken": token} if token else {})})
            for row in _array(page, "events"):
                if _string(row, "status") == "cancelled":
                    continue
                ident = _string(row, "id", required=True)
                if ident in seen_ids:
                    raise McpReadError("Google returned the same event on more than one Calendar page.")
                seen_ids.add(ident)
                start = _event_boundary(row, "start", selection.zone)
                end = _event_boundary(row, "end", selection.zone)
                if type(start) is not type(end):
                    raise McpReadError("Google Calendar returned incompatible event times.")
                if isinstance(start, datetime):
                    ordered = start.timestamp() < end.timestamp()
                else:
                    ordered = start < end
                if not ordered:
                    raise McpReadError("Google Calendar returned an event with an invalid duration.")
                yield CalendarEvent(selection.calendar_id, ident, _string(row, "summary"), start, end)
            token = _string(page, "nextPageToken")
            if not token:
                return
            if token in seen_tokens or len(token) > 4096:
                raise McpReadError("Google Calendar pagination did not advance.")
            seen_tokens.add(token)
        raise McpReadError("Google Calendar pagination exceeded the Bridge read limit.")

    def refresh(self, views, selection):
        if selection.calendar_id not in (None, "primary"):
            raise ValueError("This Bridge only reads the signed-in account's primary calendar.")

        def read(_):
            if self.account_id() != selection.account_id:
                raise AccessRevoked("The selection belongs to a different Google authorization session.")
            lists = (TaskList(ident, title, self._tasks(ident, selection)) for ident, title in self._lists())
            events = self._events(selection) if selection.calendar_id is not None else ()
            return AccountTasks(selection.account_id, lists, events)

        views.select(selection)
        return views.refresh(read, expected_selection=selection)


def _scopes(calendar_id):
    return (_TASKS_READ, _EVENTS_READ) if calendar_id else (_TASKS_READ,)


def load_read_grant(path, calendar_id):
    from google.oauth2.credentials import Credentials

    scopes = _scopes(calendar_id)
    saved = _json(path.read_bytes())
    if set(saved.get("scopes", ())) != set(scopes):
        raise McpReadError("Use an isolated Google grant with exactly the requested read-only scopes.")
    return Credentials.from_authorized_user_info(saved, scopes)


def _read_grant(args):
    if args.token_file:
        return load_read_grant(args.token_file, args.calendar_id)
    scopes = _scopes(args.calendar_id)

    from google_auth_oauthlib.flow import InstalledAppFlow

    flow = InstalledAppFlow.from_client_secrets_file(str(args.oauth_client), scopes,
                                                      autogenerate_code_verifier=True)
    options = ({"access_type": "offline", "prompt": "consent"}
               if args.save_bridge_grant else {"access_type": "online"})
    credentials = flow.run_local_server(
        host="127.0.0.1", port=0, timeout_seconds=180, login_hint=args.test_account,
        include_granted_scopes="false", authorization_prompt_message=None, **options,
    )
    if set(credentials.granted_scopes or ()) != set(scopes):
        raise ValueError("Google granted a different scope set from the requested read-only grant.")
    return credentials


def _event_title_key(title):
    folded = unicodedata.normalize("NFKD", title.casefold())
    return " ".join("".join(c for c in folded if not unicodedata.combining(c)).split())


def _verify_live(argv=None):
    from google.auth.transport.requests import AuthorizedSession

    parser = argparse.ArgumentParser(description="Verify live Bridge reads using a dedicated test account.")
    parser.add_argument("--test-account", required=True, help="Sign-in hint for the selected test account")
    auth = parser.add_mutually_exclusive_group(required=True)
    auth.add_argument("--oauth-client", type=Path, help="Google Desktop OAuth client file for an AI-powered agent")
    auth.add_argument("--token-file", type=Path, help="Existing isolated Google Workspace read grant")
    saved = parser.add_mutually_exclusive_group(required=True)
    saved.add_argument("--list-id", help="Google Tasks MCP list ID")
    saved.add_argument("--list-title", help="Exact test list title")
    parser.add_argument("--start", type=date.fromisoformat, required=True)
    parser.add_argument("--end", type=date.fromisoformat, required=True)
    parser.add_argument("--zone", default="Europe/Bratislava")
    parser.add_argument("--calendar-id", choices=("primary",), help="Read the main Google Calendar")
    parser.add_argument("--expect-event-title", help="Verify an event title in the combined view")
    parser.add_argument("--save-bridge-grant", action="store_true",
                        help="Store the isolated Workspace refresh grant in the local Bridge profile")
    args = parser.parse_args(argv)
    if args.expect_event_title and not args.calendar_id:
        parser.error("--expect-event-title requires --calendar-id")
    grant_path = None
    if args.save_bridge_grant:
        if not args.oauth_client or not args.calendar_id:
            parser.error("--save-bridge-grant requires --oauth-client and --calendar-id primary")
        from pair import STATE

        grant_path = STATE / "workspace-read-token.json"
        if grant_path.exists():
            raise McpReadError("A Workspace refresh grant already exists in the local Bridge profile.")
    credentials = _read_grant(args)
    with AuthorizedSession(credentials) as session:
        reader = GoogleTasksMcp(session)
        account = reader.account_id()
        selected = args.list_id if args.list_id else reader.find_list(account, args.list_title)
        result = reader.refresh(TaskViews(), ViewSelection(account, selected, ZoneInfo(args.zone),
                                                           args.start, args.end, args.calendar_id))
    if result.status != "online" or result.saved_items is None or result.calendar is None:
        raise McpReadError("Both views require a verified online read and an accessible selected list.")
    events = [item for item in result.calendar if item.kind == "event"]
    matches = []
    if args.expect_event_title:
        expected = _event_title_key(args.expect_event_title)
        matches = [item for item in events if _event_title_key(item.title) == expected]
    verified = not args.expect_event_title or (len(matches) == 1 and not result.calendar_truncated)
    if grant_path and verified:
        if not credentials.refresh_token:
            raise McpReadError("Google did not return a refresh grant for local Bridge access.")
        with grant_path.open("x", encoding="utf-8") as grant:
            grant.write(credentials.to_json(strip=("token", "expiry")))
    # Report the requested match's times without printing unrelated event data.
    print(json.dumps({"status": result.status, "updatedAt": result.updated_at.isoformat(),
                      "savedItems": len(result.saved_items), "calendar": len(result.calendar),
                      "scheduledTasks": len(result.calendar) - len(events), "events": len(events),
                      "expectedEvent": [{"title": item.title, "start": item.scheduled.isoformat(),
                                         "end": item.ends.isoformat()}
                                        for item in matches],
                      "savedItemsTruncated": result.saved_items_truncated,
                      "calendarTruncated": result.calendar_truncated}))
    if not verified:
        raise McpReadError("The primary Calendar did not verify one matching event in the requested range.")


def main(argv=None):
    from oauthlib.oauth2 import OAuth2Error

    try:
        _verify_live(argv)
    except Warning:
        sys.exit("Google returned a different OAuth scope set. Use a separate read-only OAuth grant.")
    except OAuth2Error:
        sys.exit("Google did not complete the OAuth authorization.")
    except (AccessRevoked, McpReadError, ProviderOffline, OSError) as error:
        sys.exit(str(error))
    except ValueError:
        sys.exit("Invalid Bridge view selection, OAuth client or scoped token file.")


if __name__ == "__main__":
    main()
