"""Recording-based date calculations shared by the Bridge's date routes."""
import datetime as dt


def recording_time(value, zone):
    """Read an aware device timestamp. Preserve the local day of capture."""
    if not isinstance(value, str):
        return None
    try:
        recorded = dt.datetime.fromisoformat(value)
    except ValueError:
        return None
    return recorded.astimezone(zone) if recorded.tzinfo is not None else None


def _local_value(wall, zone):
    # A gap has no matching instant. A repeated wall time requires a choice.
    candidates = {}
    for fold in (0, 1):
        value = wall.replace(tzinfo=zone, fold=fold)
        utc = value.astimezone(dt.timezone.utc)
        if utc.astimezone(zone).replace(tzinfo=None) == wall:
            candidates.setdefault(utc, value)
    return next(iter(candidates.values())) if len(candidates) == 1 else None


def resolve_yearless(value, captured, *, timed):
    """Find a month/day within the year after capture, or leave it unresolved."""
    if captured is None:
        return None
    reference = captured.replace(tzinfo=None)
    if reference.year == dt.MAXYEAR:
        return None
    try:
        end = reference.replace(year=reference.year + 1)
    except ValueError:
        end = reference.replace(year=reference.year + 1, day=28)  # Leap day.

    for year in (reference.year, end.year):
        try:
            wall = value.replace(year=year, tzinfo=None)
        except ValueError:
            continue
        if timed:
            if not reference < wall <= end:
                continue
            candidate = _local_value(wall, captured.tzinfo)
            if (candidate is not None
                    and candidate.astimezone(dt.timezone.utc) <= captured.astimezone(dt.timezone.utc)):
                continue
            return candidate
        if reference.date() <= wall.date() <= end.date():
            return _local_value(wall, captured.tzinfo)
    return None


def valid_future_start(value, zone, now):
    """Accept only a concrete local appointment start that is still ahead."""
    try:
        wall = dt.datetime.fromisoformat(value)
    except (TypeError, ValueError):
        return False
    if wall.tzinfo is not None:
        return False
    start = _local_value(wall, zone)
    return start is not None and start.astimezone(dt.timezone.utc) > now
