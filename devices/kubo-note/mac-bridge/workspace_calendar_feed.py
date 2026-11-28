"""Read-only Workspace views for a single opted-in paired Note."""
from dataclasses import replace
from datetime import datetime, timedelta
from pathlib import Path
from threading import Lock, Thread
from time import monotonic
from zoneinfo import ZoneInfo

from provider_task_views import TaskViews, TaskViewSnapshot, ViewSelection


_REFRESH_SECONDS = 120
_CALENDAR_DAYS = 14
_MAX_ROWS = 32


def _display_text(text):
    printable = "".join(ch if ch.isprintable() else " " for ch in text)
    return " ".join(printable.split())[:100]


def calendar_payload(snapshot, *, include_saved=False):
    if snapshot.status == "unavailable":
        return {"status": "unavailable"}
    rows = []
    for item in snapshot.calendar:
        start = item.scheduled
        row = {
            "kind": item.kind,
            "title": _display_text(item.title),
            "start": start.isoformat(timespec="minutes")[:16] if isinstance(start, datetime) else start.isoformat(),
        }
        if item.ends:
            row["end"] = (item.ends.isoformat(timespec="minutes")[:16]
                          if isinstance(item.ends, datetime) else item.ends.isoformat())
        rows.append(row)
    payload = {
        "status": snapshot.status,
        "updated": int(snapshot.updated_at.timestamp()),
        "truncated": snapshot.calendar_truncated,
        "rows": rows,
    }
    if include_saved and snapshot.saved_items is not None:
        open_items = [item for item in snapshot.saved_items if not item.completed]
        payload["saved"] = {
            "title": _display_text(snapshot.saved_list_title),
            "truncated": snapshot.saved_items_truncated or len(open_items) > _MAX_ROWS,
            "rows": [{"title": _display_text(item.title), "completed": False}
                     for item in open_items[:_MAX_ROWS]],
        }
    return payload


class WorkspaceCalendarFeed:
    def __init__(self, note_id, saved_list_id, reader, zone, *, grant_path=None):
        self.note_id = note_id
        self._saved_list_id = saved_list_id
        self._reader = reader
        self._grant_path = grant_path
        self._grant_revision = grant_path.stat().st_mtime_ns if grant_path else None
        self._zone = ZoneInfo(zone)
        self._views = TaskViews()
        self._current = TaskViewSnapshot("unavailable")
        self._lock = Lock()
        self._next = 0
        self._reading = False
        self._day = None

    def _schedule(self, day):
        if self._reading or (day == self._day and monotonic() < self._next):
            return
        if day != self._day:
            self._current = TaskViewSnapshot("unavailable")
        self._day = day
        self._reading = True
        Thread(target=self._refresh, args=(day,), name="workspace-calendar", daemon=True).start()

    def _refresh(self, day):
        from workspace_tasks_mcp import McpReadError

        selection = ViewSelection(self._reader.account_id(), self._saved_list_id, self._zone, day,
                                  day + timedelta(days=_CALENDAR_DAYS - 1), "primary")
        snapshot = TaskViewSnapshot("unavailable")
        try:
            snapshot = self._reader.refresh(self._views, selection)
            if snapshot.saved_items is None:
                self._views.select(None)
                snapshot = TaskViewSnapshot("unavailable")
        except (McpReadError, OSError, ValueError):
            self._views.select(None)
        finally:
            with self._lock:
                self._current = snapshot
                self._next = monotonic() + _REFRESH_SECONDS
                self._reading = False

    def payload_for(self, note_id, *, include_saved=False):
        if note_id != self.note_id:
            return None
        now = datetime.now(self._zone)
        if self._grant_path:
            try:
                if self._grant_path.stat().st_mtime_ns != self._grant_revision:
                    return {"status": "unavailable"}
            except OSError:
                return {"status": "unavailable"}
        with self._lock:
            self._schedule(now.date())
            snapshot = self._current
            if snapshot.updated_at is None or self._day != now.date():
                return {"status": "unavailable"}
            age = now - snapshot.updated_at
            if not timedelta(0) <= age < timedelta(hours=24):
                return {"status": "unavailable"}
            if age >= timedelta(seconds=2 * _REFRESH_SECONDS):
                snapshot = replace(snapshot, status="offline")
            return calendar_payload(snapshot, include_saved=include_saved)


def configured_calendar_feed(state: Path, config):
    """Load the opt-in Stage 3 profile. The refresh grant stays on the Bridge."""
    from google.auth.transport.requests import AuthorizedSession
    from workspace_tasks_mcp import GoogleTasksMcp, load_read_grant

    selection = config["workspace_calendar"]
    note_id = selection["note_id"]
    list_id = selection["saved_list_id"]
    if not isinstance(note_id, str) or not note_id or not isinstance(list_id, str) or not list_id:
        raise ValueError("The Workspace Calendar profile requires a paired Note and a Saved items list.")
    grant_path = state / "workspace-read-token.json"
    credentials = load_read_grant(grant_path, "primary")
    return WorkspaceCalendarFeed(note_id, list_id, GoogleTasksMcp(AuthorizedSession(credentials)),
                                 selection.get("zone", "Europe/Bratislava"), grant_path=grant_path)
