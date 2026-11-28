"""Read-only Saved items and Calendar projections for a bound Bridge account.

The reader must verify the active account and supply normalized tasks. Only
explicitly transient failures permit offline reuse. Signal disconnect or a
change of authorization with select(None) before binding a new selection.
"""
from collections.abc import Callable, Iterable
from dataclasses import dataclass, replace
from datetime import date, datetime, time, timedelta, timezone
from heapq import nsmallest
from itertools import chain
from threading import Lock
from typing import Literal
from zoneinfo import ZoneInfo


_MAX_OFFLINE_AGE = timedelta(hours=24)


class ProviderOffline(ConnectionError):
    """The active grant remains valid but the provider cannot be reached."""


class AccessRevoked(PermissionError):
    """The Bridge no longer has permission to read the active account."""


@dataclass(frozen=True)
class Task:
    task_id: str
    title: str
    scheduled: date | datetime | None = None
    completed: bool = False


@dataclass(frozen=True)
class TaskList:
    list_id: str
    title: str
    tasks: Iterable[Task]


@dataclass(frozen=True)
class CalendarEvent:
    calendar_id: str
    event_id: str
    title: str
    start: date | datetime
    end: date | datetime


@dataclass(frozen=True)
class AccountTasks:
    account_id: str
    lists: Iterable[TaskList]
    events: Iterable[CalendarEvent] = ()


@dataclass(frozen=True)
class ViewSelection:
    account_id: str
    saved_list_id: str
    zone: ZoneInfo
    calendar_start: date
    calendar_end: date
    calendar_id: str | None = None
    include_completed: bool = True

    def __post_init__(self):
        if not self.account_id or not self.saved_list_id:
            raise ValueError("Select the verified account and a stable Saved items list ID.")
        if self.calendar_start > self.calendar_end:
            raise ValueError("The Calendar date range is reversed.")
        if self.calendar_id is not None and (not self.calendar_id or self.calendar_end == date.max):
            raise ValueError("Calendar MCP requires a calendar and an exclusive upper date bound.")


@dataclass(frozen=True)
class ViewItem:
    list_id: str
    list_title: str
    task_id: str
    title: str
    scheduled: date | datetime | None
    completed: bool
    kind: Literal["task", "event"] = "task"
    ends: date | datetime | None = None


@dataclass(frozen=True)
class TaskViewSnapshot:
    status: Literal["online", "offline", "unavailable"]
    updated_at: datetime | None = None
    # None marks an unavailable view. An empty tuple is a successful empty read.
    saved_items: tuple[ViewItem, ...] | None = None
    calendar: tuple[ViewItem, ...] | None = None
    saved_items_truncated: bool = False
    calendar_truncated: bool = False
    saved_list_title: str = ""


_UNAVAILABLE = TaskViewSnapshot("unavailable")


def _day(scheduled: date | datetime) -> date:
    return scheduled.date() if isinstance(scheduled, datetime) else scheduled


def _calendar_order(item: ViewItem):
    scheduled = item.scheduled
    assert scheduled is not None
    # The offset can change within a single date. Sort timed items by instant,
    # including the two occurrences of an hour at the end of daylight saving.
    instant = scheduled.astimezone(timezone.utc) if isinstance(scheduled, datetime) else None
    return (_day(scheduled), instant is not None, instant, item.list_id, item.task_id)


def _events_in_view(events: Iterable[CalendarEvent], selection: ViewSelection):
    if not selection.calendar_id:
        return
    until = selection.calendar_end + timedelta(days=1)
    for event in events:
        if event.calendar_id != selection.calendar_id:
            raise AccessRevoked("The provider returned a different calendar.")
        start, end = event.start, event.end
        if isinstance(start, datetime) and isinstance(end, datetime):
            if start.utcoffset() is None or end.utcoffset() is None:
                raise ValueError("Calendar event times must include offsets.")
            start, end = start.astimezone(selection.zone), end.astimezone(selection.zone)
            lower = datetime.combine(selection.calendar_start, time.min, selection.zone)
            upper = datetime.combine(until, time.min, selection.zone)
            overlaps = start.timestamp() < upper.timestamp() and end.timestamp() > lower.timestamp()
        elif type(start) is date and type(end) is date:
            overlaps = start < until and end > selection.calendar_start
        else:
            raise ValueError("Calendar event boundaries must use matching time types.")
        if overlaps:
            yield ViewItem(event.calendar_id, "", event.event_id, event.title, start,
                           False, kind="event", ends=end)


def _project(account: AccountTasks, selection: ViewSelection, limit: int) -> TaskViewSnapshot:
    saved = None
    saved_list_title = ""

    def scheduled_items():
        nonlocal saved, saved_list_title
        for task_list in account.lists:
            selected = task_list.list_id == selection.saved_list_id
            if selected and saved is None:
                saved = []
                saved_list_title = task_list.title
            for task in task_list.tasks:
                if task.completed and not selection.include_completed:
                    continue
                scheduled = task.scheduled
                if isinstance(scheduled, datetime):
                    if scheduled.utcoffset() is None:
                        raise ValueError("A scheduled time must include a UTC offset.")
                    scheduled = scheduled.astimezone(selection.zone)
                elif scheduled is not None and not isinstance(scheduled, date):
                    raise ValueError("Expected a date, an offset-aware time or an unscheduled task.")
                item = ViewItem(task_list.list_id, task_list.title, task.task_id, task.title,
                                scheduled, task.completed)
                if selected and not task.completed and len(saved) <= limit:
                    saved.append(item)
                if scheduled is not None and selection.calendar_start <= _day(scheduled) <= selection.calendar_end:
                    yield item

    # Consume each list only once, while keeping at most limit + 1 rows per view.
    calendar_items = chain(scheduled_items(), _events_in_view(account.events, selection))
    calendar = nsmallest(limit + 1, calendar_items, key=_calendar_order)
    return TaskViewSnapshot(
        "online",
        saved_items=tuple(saved[:limit]) if saved is not None else None,
        calendar=tuple(calendar[:limit]),
        saved_items_truncated=saved is not None and len(saved) > limit,
        calendar_truncated=len(calendar) > limit,
        saved_list_title=saved_list_title,
    )


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


class TaskViews:
    """Keep one bounded snapshot in Bridge memory for the current selection."""

    def __init__(self, *, limit: int = 32, clock: Callable[[], datetime] = _utc_now):
        if type(limit) is not int or limit < 1:
            raise ValueError("The view size must be a positive integer.")
        self._limit = limit
        self._clock = clock
        self._selection = None
        self._snapshot = None
        self._generation = 0
        self._state = Lock()
        self._read = Lock()

    def select(self, selection: ViewSelection | None) -> None:
        """Apply the active selection. None also invalidates in-flight results."""
        with self._state:
            if selection != self._selection:
                self._set_selection(selection)

    def _set_selection(self, selection):
        self._selection = selection
        self._snapshot = None
        self._generation += 1

    def _now(self):
        now = self._clock()
        if now.utcoffset() is None:
            raise ValueError("The Bridge clock must provide a UTC offset.")
        return now.astimezone(timezone.utc)

    def offline(self) -> TaskViewSnapshot:
        with self._state:
            return self._offline()

    def _offline(self):
        cached = self._snapshot
        if cached is None:
            return _UNAVAILABLE
        age = self._now() - cached.updated_at
        if not timedelta(0) <= age < _MAX_OFFLINE_AGE:
            self._snapshot = None
            return _UNAVAILABLE
        return replace(cached, status="offline")

    def refresh(self, read_account: Callable[[str], AccountTasks], *,
                expected_selection: ViewSelection | None = None) -> TaskViewSnapshot:
        """Publish a complete read or reuse the same selection's unexpired cache."""
        # Serialize provider reads. Keep selection changes independent of provider I/O.
        with self._read:
            with self._state:
                selection = self._selection
                generation = self._generation
            if selection is None or (expected_selection is not None and selection != expected_selection):
                return _UNAVAILABLE
            try:
                account = read_account(selection.account_id)
                if account.account_id != selection.account_id:
                    raise AccessRevoked("The provider returned a different account.")
                projected = replace(_project(account, selection, self._limit), updated_at=self._now())
            except AccessRevoked:
                with self._state:
                    if generation == self._generation:
                        self._set_selection(None)
                return _UNAVAILABLE
            except ProviderOffline:
                with self._state:
                    return self._offline() if generation == self._generation else _UNAVAILABLE
            with self._state:
                if generation != self._generation:
                    return _UNAVAILABLE
                self._snapshot = projected
                return projected
