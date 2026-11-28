"""Synthetic-only MCP write adapter used by the Stage 3 acceptance tests."""

from dataclasses import dataclass
from datetime import datetime
from itertools import count
import json
from zoneinfo import ZoneInfo

from google_docs import _append_batch, _document_tabs, _has_verified_entry
from provider_task_views import AccessRevoked
from recorder_delivery import plan_digest
from workspace_tasks_mcp import McpReadError, _object, _read_response, _string


TOOLS = {
    "tasks": {"create_task", "list_tasks", "get_task"},
    "calendar": {"create_event", "list_events", "get_event"},
    "docs": {"update_doc", "read_doc"},
}
ZONE = "Europe/Bratislava"
START = "2026-12-04T09:30:00"
END = "2026-12-04T10:00:00"
CAPTURED = "2026-11-27T09:00:00Z"


@dataclass(frozen=True)
class Operation:
    provider: str
    target: str
    title: str = "Synthetic Stage 3 item"
    body: str = "Fixture text with Slovak diacritics: \u0161u\u0148ka and \U0001f642."
    account: str = "fixture-owner-a"
    note: int = 1073743955

    def __post_init__(self):
        if self.provider not in TOOLS or not self.target.startswith("fixture-") or not self.account.startswith("fixture-"):
            raise ValueError("The test adapter accepts synthetic destinations only.")

    @property
    def digest(self):
        immutable = json.dumps([self.title, self.body, START, END, ZONE], ensure_ascii=False)
        return plan_digest("fixture-note", self.note, CAPTURED, immutable, self.account,
                           self.target, f"mcp-{self.provider}")

    @property
    def marker(self):
        return "pala-fixture-" + self.digest

    @property
    def description(self):
        return self.body + "\n\n" + self.marker

    @property
    def entry(self):
        return CAPTURED[:10] + "\n" + self.body


@dataclass(frozen=True)
class Receipt:
    provider: str
    account: str
    target: str
    operation_digest: str
    remote_id: str


class ScriptedMcp:
    """Use the Bridge decoder with an injected transport and no network adapter."""

    def __init__(self, provider, send):
        self.provider = provider
        self.send = send
        self.ids = count(1)

    def call(self, name, **arguments):
        if name not in TOOLS[self.provider]:
            raise ValueError(name)
        request_id = next(self.ids)
        response = self.send(self.provider, {
            "jsonrpc": "2.0", "id": request_id, "method": "tools/call",
            "params": {"name": name, "arguments": arguments},
        })
        if response.status_code != 200:
            raise McpReadError(f"Synthetic HTTP status {response.status_code}")
        body = _read_response(response, request_id)
        if body.get("jsonrpc") != "2.0" or body.get("id") != request_id or "error" in body:
            raise McpReadError("No successful response matching this request.")
        result = _object(body.get("result"))
        if result.get("isError"):
            raise McpReadError("The tool rejected the operation.")
        return _object(result.get("structuredContent"))


class Delivery:
    def __init__(self, operation, send):
        self.operation = operation
        self.client = ScriptedMcp(operation.provider, send)
        self.receipt = None

    @property
    def digest(self):
        return self.operation.digest

    def settle(self, mode):
        if mode not in ("write", "reconcile"):
            raise ValueError("A recorder claim or read-only reconciliation is required.")
        try:
            if mode == "write":
                self.write()
            remote_id = self.readback()
        except (TimeoutError, AccessRevoked, McpReadError, RuntimeError, KeyError, TypeError, ValueError):
            return False
        if not isinstance(remote_id, str) or not remote_id:
            return False
        plan = self.operation
        self.receipt = Receipt(plan.provider, plan.account, plan.target, plan.digest, remote_id)
        return True

    def read_document(self):
        plan = self.operation
        content = _object(self.client.call("read_doc", documentId=plan.target).get("content"))
        if content.get("documentId") != plan.target or content.get("title") != plan.title:
            raise McpReadError("Document binding did not match.")
        return content.get("revisionId"), list(_document_tabs(content.get("tabs", [])))

    def write(self):
        plan = self.operation
        call = self.client.call
        if plan.provider == "tasks":
            call("create_task", taskListId=plan.target, title=plan.title, description=plan.description)
        elif plan.provider == "calendar":
            call("create_event", calendarId=plan.target, summary=plan.title, description=plan.description,
                 startTime=START, endTime=END, timeZone=ZONE, notificationLevel="NONE", useDefaultReminders=False)
        else:
            revision, tabs = self.read_document()
            if _has_verified_entry(tabs, plan.marker, plan.entry):
                return
            if not revision or not tabs:
                raise McpReadError("Document revision or tab data is unavailable.")
            call("update_doc", documentId=plan.target, **_append_batch(revision, tabs[0], plan.marker, plan.entry))

    def read_pages(self, name, key, arguments):
        token = None
        seen = set()
        for _ in range(20):
            page = self.client.call(name, **arguments, **({"pageToken": token} if token else {}))
            rows = page.get(key, [])
            if not isinstance(rows, list):
                raise McpReadError("Invalid paginated data.")
            yield from map(_object, rows)
            token = page.get("nextPageToken")
            if not token:
                return
            if not isinstance(token, str) or token in seen:
                raise McpReadError("Pagination did not advance.")
            seen.add(token)
        raise McpReadError("Pagination exceeded the fixture limit.")

    def readback(self):
        plan = self.operation
        call = self.client.call
        if plan.provider == "docs":
            _, tabs = self.read_document()
            if not _has_verified_entry(tabs, plan.marker, plan.entry):
                return None
            return next(tab["namedRanges"][plan.marker]["namedRanges"][0]["namedRangeId"]
                        for _, tab in tabs if plan.marker in tab.get("namedRanges", {}))

        if plan.provider == "tasks":
            candidates = self.read_pages("list_tasks", "tasks", {
                "filter": {"taskListId": plan.target, "state": "ALL"}, "pageSize": 100,
            })
        else:
            candidates = self.read_pages("list_events", "events", {
                "calendarId": plan.target, "startTime": START, "endTime": END, "timeZone": ZONE, "pageSize": 10,
            })
        matches = [row for row in candidates if plan.marker in _string(row, "description").splitlines()]
        if len(matches) != 1 or not isinstance(matches[0].get("id"), str) or not matches[0]["id"]:
            return None
        ident = matches[0]["id"]
        if plan.provider == "tasks":
            row = call("get_task", taskId=ident)
            valid = row.get("taskListId") == plan.target and row.get("title") == plan.title and not row.get("deleted")
        else:
            row = call("get_event", calendarId=plan.target, eventId=ident)
            valid = row.get("summary") == plan.title and row.get("status") == "confirmed" and event_times_match(row)
        return ident if valid and row.get("id") == ident and row.get("description") == plan.description else None


def event_times_match(row):
    zone = ZoneInfo(ZONE)
    for key, expected in (("start", START), ("end", END)):
        returned = datetime.fromisoformat(row[key]["dateTime"])
        if returned.utcoffset() is None:
            return False
        if returned.astimezone(zone).replace(tzinfo=None) != datetime.fromisoformat(expected):
            return False
    return True
