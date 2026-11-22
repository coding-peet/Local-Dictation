"""Route one already transcribed Pala recording to approved Google targets."""
import argparse
import contextlib
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import sqlite3
import sys
import threading
import time
import unicodedata
from typing import Callable
from zoneinfo import ZoneInfo

from appointment_dates import recording_time
from bridge_activity import BridgeActivity
from google_tasks import TASKS_SCOPE, items


SLOVAK_FOLD = str.maketrans(
    "áäčďéíĺľňóôŕšťúýžÁÄČĎÉÍĹĽŇÓÔŔŠŤÚÝŽ",
    "aacdeillnoorstuyzAACDEILLNOORSTUYZ",
)
WORK = re.compile(
    r"\b(?:do\s+(?:onenot(?:e|u)|(?:microsoft\s+)?planner(?:a|u)?|pracovneho\s+kalendara)"
    r"|pracovn[ua]\s+(?:ulohu|uloha|poznamku|poznamka)|pracovn[ye]\s+termin)\b",
    re.IGNORECASE,
)
SHOPPING = re.compile(
    r"^(?:pridaj(?:\s+mi)?\s+na\s+nakupny\s+zoznam|nakup(?:ny zoznam)?)"
    r"(?:\s*[:,–-]\s*|\s+)(.+)$", re.IGNORECASE | re.DOTALL,
)
TASK = re.compile(
    r"^(?:uloha|pridaj(?:\s+mi)?\s+(?:osobnu\s+)?ulohu)"
    r"(?:\s*[:,–-]\s*|\s+)(.+)$", re.IGNORECASE | re.DOTALL,
)
DOC = re.compile(
    r"^(?:uloz|zapis)(?:\s+mi)?\s+do\s+google\s+docs"
    r"(?:\s*[:,–-]\s*|\s+)(.+)$", re.IGNORECASE | re.DOTALL,
)
CALENDAR = re.compile(
    r"^(?:prosim[\s,:]+)?(?:termin\b"
    r"|(?:pridaj|naplanuj|zapis|vytvor|napis|vloz)(?:\s+mi)?\s+(?:osobny\s+)?(?:termin|udalost)\b"
    r"|(?:(?:daj|pridaj|zapis|uloz|napis|vloz|zaznamenaj|naplanuj)(?:\s+mi)?\s+)?"
    r"do\s+(?:google\s+)?kalendara\b)",
    re.IGNORECASE,
)
NAMED_TARGET = re.compile(
    r"^(?:(?:daj|pridaj|zapis|uloz)(?:\s+mi)?\s+)?do\s+(zoznamu|dokumentu)\b\s*(.*)$",
    re.IGNORECASE | re.DOTALL,
)
QUOTED_TARGET = re.compile(r'^(?:"([^"]+)"|„([^“]+)“)\s*(?:[:,–-]\s*)?(.+)$', re.DOTALL)
ANOTHER_COMMAND = re.compile(
    r"(?:[.!?;]\s+|\ba\s+(?:aj\s+)?)(?:pridaj|uloz|zapis)\s",
    re.IGNORECASE,
)

class DestinationUnavailable(RuntimeError):
    """The authorized target has not been resolved or a write has not started."""


def canonical(text):
    return unicodedata.normalize("NFC", text.replace("\r\n", "\n").replace("\r", "\n")).strip()


def _named_destination(text, match):
    suffix = text[match.start(2):].strip()
    quoted = QUOTED_TARGET.fullmatch(suffix)
    if quoted:
        name, body = quoted[1] or quoted[2], quoted[3]
    else:
        parts = re.split(r"[:,]", suffix, maxsplit=1)
        name, body = parts if len(parts) == 2 else ("", "")
    alias = " ".join(name.translate(SLOVAK_FOLD).casefold().split())
    if not alias or not body.strip() or name.startswith(('"', '„')):
        return "needs-target", [text]
    provider = "list" if match[1].lower() == "zoznamu" else "document"
    return f"{provider}:{alias}", [body.strip()]


def classify_transcript(text):
    text = canonical(text)
    command_text = text.translate(SLOVAK_FOLD)
    if WORK.search(command_text):
        return "work", [text]
    if CALENDAR.match(command_text):
        return "calendar", [text]
    if ANOTHER_COMMAND.search(command_text):
        return "sorting", [text]
    named = NAMED_TARGET.fullmatch(command_text)
    if named:
        return _named_destination(text, named)

    for pattern, kind in ((SHOPPING, "shopping"), (TASK, "tasks"), (DOC, "docs")):
        match = pattern.fullmatch(command_text)
        if not match:
            continue
        # The command spelling may be accentless; the original payload is kept.
        body = text[match.start(1):].strip()
        if kind == "shopping":
            products = [value.strip(" .!?") for value in re.split(r"\s*(?:,|;)\s*|\s+(?:a|aj)\s+", body)]
            products = list(dict.fromkeys(value for value in products if value))
            if products:
                return kind, products
        else:
            payload = body.rstrip(".!?").rstrip() if kind == "tasks" else body
            if payload:
                return kind, [payload]
    return "general", [text]


def _recording_actions(device_id, note, text):
    if not isinstance(device_id, str) or not device_id.strip() or type(note) is not int or note <= 0:
        raise ValueError("Pala device and note identity are required.")
    text = canonical(text)
    if not text or len(text.encode("utf-8")) > 32000 or "\x00" in text:
        raise ValueError("The saved transcript is empty or exceeds the Pala text limit.")

    # Normalize the original text so a Companion export and a USB replay
    # from the same recording generate the same identity.
    digest = hashlib.sha256(json.dumps(
        [device_id.casefold(), note, text], ensure_ascii=False).encode("utf-8")).hexdigest()
    kind, entries = classify_transcript(text)
    return kind, [(f"{digest}-{index}", body) for index, body in enumerate(entries)]


def _delivery_target(bindings, kind):
    target = bindings.get(kind)
    if target is None and kind == "sorting":
        target = bindings.get("general")
    return target


def general_fallback_kinds(config):
    routes = config.get("google_routes", {})
    if not config.get("personal_note_collection") or "general" not in routes:
        return frozenset()
    return frozenset(("work", "tasks", "shopping", "sorting")) - routes.keys()


class DeliveryQueue:
    def __init__(self, directory):
        self.path = Path(directory) / "google-delivery.sqlite"
        with self.database() as db:
            db.execute("BEGIN IMMEDIATE")
            db.execute("""CREATE TABLE IF NOT EXISTS google_actions (
                id TEXT PRIMARY KEY, kind TEXT NOT NULL, captured TEXT, body TEXT NOT NULL,
                status TEXT NOT NULL, target_id TEXT, provider_id TEXT, device TEXT,
                confirmed_at REAL, recording_id TEXT
            )""")
            columns = {row[1] for row in db.execute("PRAGMA table_info(google_actions)")}
            if "device" not in columns:
                db.execute("ALTER TABLE google_actions ADD COLUMN device TEXT")
            if "confirmed_at" not in columns:
                db.execute("ALTER TABLE google_actions ADD COLUMN confirmed_at REAL")
            if "recording_id" not in columns:
                db.execute("ALTER TABLE google_actions ADD COLUMN recording_id TEXT")
            db.execute("CREATE INDEX IF NOT EXISTS google_actions_device ON google_actions(device)")

    @contextlib.contextmanager
    def database(self):
        with contextlib.closing(sqlite3.connect(self.path, timeout=5)) as db, db:
            yield db

    def stage(self, device_id, note, captured, text, *, ready=True, archive=False, general_fallback=()):
        kind, actions = _recording_actions(device_id, note, text)
        if archive or kind in general_fallback:
            kind, actions = "general", [(actions[0][0], canonical(text))]
        recorded = recording_time(captured, timezone.utc)
        recording_id = f"{note}:{recorded.isoformat()}" if recorded is not None else None
        status = {"work": "local", "calendar": "needs-confirmation",
                  "needs-target": "needs-target"}.get(kind, "pending")
        if not ready:
            status = "awaiting-usb"
        with self.database() as db:
            for key, body in actions:
                db.execute("""INSERT OR IGNORE INTO google_actions
                    (id, kind, captured, body, status, device, recording_id)
                    VALUES (?, ?, ?, ?, ?, ?, ?)""",
                    (key, kind, captured, body, status, device_id.casefold(), recording_id))
                db.execute("UPDATE google_actions SET device=COALESCE(device, ?), "
                           "recording_id=COALESCE(recording_id, ?) "
                           "WHERE id=? AND (device IS NULL OR (recording_id IS NULL AND ? IS NOT NULL))",
                           (device_id.casefold(), recording_id, key, recording_id))
                if captured:
                    db.execute("UPDATE google_actions SET captured=? WHERE id=? AND captured IS NULL",
                               (captured, key))
                if ready:
                    db.execute("UPDATE google_actions SET status=? WHERE id=? AND status='awaiting-usb'",
                               (status, key))
        return [key for key, _ in actions]

    def home_counts(self, device, bindings, *, now=None, with_last=False):
        # The firmware clock uses the Central European time and DST rules.
        zone = ZoneInfo("Europe/Bratislava")
        today = (now or datetime.now(timezone.utc)).astimezone(zone).date()
        start = datetime.combine(today, datetime.min.time(), zone).timestamp()
        end = datetime.combine(today + timedelta(days=1), datetime.min.time(), zone).timestamp()
        waiting = set()
        confirmed = {}
        with self.database() as db:
            rows = db.execute("""SELECT COALESCE(recording_id, substr(id, 1, 64)) AS recording,
                    kind, MIN(status = 'done'), MAX(confirmed_at)
                FROM google_actions
                WHERE device=? AND status IN ('done', 'pending', 'sending', 'uncertain')
                GROUP BY recording, kind""", (device.casefold(),))
            for recording, kind, complete, confirmation in rows:
                if not complete and _delivery_target(bindings, kind) is not None:
                    waiting.add(recording)
                if complete and confirmation is not None:
                    confirmed[recording] = max(confirmed.get(recording, 0), confirmation)
        completed = [stamp for recording, stamp in confirmed.items() if recording not in waiting]
        delivered = sum(start <= stamp < end for stamp in completed)
        result = (today.isoformat(), delivered, len(waiting))
        return (*result, max(completed, default=None)) if with_last else result

    def pending(self):
        with self.database() as db:
            return [key for key, in db.execute(
                "SELECT id FROM google_actions WHERE status='pending' ORDER BY rowid")]

    def deliver(self, key, bindings, send):
        with self.database() as db:
            db.execute("BEGIN IMMEDIATE")
            row = db.execute(
                "SELECT kind, captured, body, status, target_id FROM google_actions WHERE id=?",
                (key,)).fetchone()
            if row is None:
                raise ValueError("Unknown Pala delivery.")
            kind, captured, body, status, previous_target = row
            if status != "pending":
                return "uncertain" if status == "sending" else status
            target = _delivery_target(bindings, kind)
            if (not isinstance(target, dict) or not all(
                    isinstance(target.get(field), str) and target[field].strip()
                    for field in ("id", "title"))
                    or previous_target not in (None, target["id"])):
                return "pending"
            db.execute("UPDATE google_actions SET status='sending', target_id=? WHERE id=?",
                       (target["id"], key))

        try:
            provider_id = send(kind, target, captured, body, key)
            if not provider_id:
                raise RuntimeError("Google did not return a verified result.")
        except DestinationUnavailable:
            self._finish(key, "pending")
            raise
        except Exception:
            self._finish(key, "uncertain")
            raise
        self._finish(key, "done", provider_id)
        return "done"

    def reconcile_task(self, key, bindings, read):
        with self.database() as db:
            row = db.execute("SELECT kind, status, target_id FROM google_actions WHERE id=?",
                             (key,)).fetchone()
        if row is None:
            raise ValueError("Unknown Pala delivery.")
        kind, status, target_id = row
        target = _delivery_target(bindings, kind)
        if status not in ("sending", "uncertain"):
            return status
        task_kind = kind in ("tasks", "shopping", "sorting", "general") or kind.startswith("list:")
        if (not task_kind
                or not isinstance(target, dict) or target.get("provider") == "docs"
                or not target_id or target.get("id") != target_id):
            return "uncertain"
        provider_id = read(target, key)
        if not provider_id:
            return "uncertain"
        with self.database() as db:
            db.execute("UPDATE google_actions SET status='done', provider_id=?, confirmed_at=? "
                       "WHERE id=? AND target_id=? AND status IN ('sending', 'uncertain')",
                       (provider_id, time.time(), key, target_id))
        return "done"

    def _finish(self, key, status, provider_id=None):
        with self.database() as db:
            confirmed_at = time.time() if status == "done" else None
            db.execute("UPDATE google_actions SET status=?, provider_id=?, confirmed_at=? "
                       "WHERE id=? AND status='sending'", (status, provider_id, confirmed_at, key))


def home_delivery_payload(state, config, device, *, queue=None):
    if not device:
        today = datetime.now(ZoneInfo("Europe/Bratislava")).date().isoformat()
        return f"{today},0,0"
    if queue is None:
        path = Path(state) / "google-delivery.sqlite"
        if not path.is_file():
            today = datetime.now(ZoneInfo("Europe/Bratislava")).date().isoformat()
            return f"{today},0,0"
        queue = DeliveryQueue(state)
    from note_web import processing_config

    config = processing_config(state, config)
    bindings = config["google_routes"] if config["processing_mode"] == "kubo" else {}
    if not isinstance(bindings, dict):
        raise ValueError("Google destinations must be explicitly bound.")
    day, delivered, waiting = queue.home_counts(device, bindings)
    return f"{day},{delivered},{waiting}"


class GeneralTaskCount:
    """Keep device requests responsive while checking only the configured Google Tasks list."""

    def __init__(self):
        self.lock = threading.Lock()
        self.key = None
        self.count = None
        self.changed_at = 0
        self.next_check = 0
        self.refreshing = False

    def get(self, state, target):
        key = (state, target["id"], target["title"])
        now = time.monotonic()
        with self.lock:
            if key != self.key:
                self.key = key
                self.count = None
                self.next_check = 0
            if not self.refreshing and now >= self.next_check:
                self.refreshing = True
                self.next_check = now + 60
                threading.Thread(target=self._refresh, args=(key, target), daemon=True).start()
            if self.count is not None and now - self.changed_at <= 300:
                return self.count
        return -1

    def _refresh(self, key, target):
        try:
            count = count_open_general_tasks(key[0], target)
            with self.lock:
                if self.key == key:
                    self.count = count
                    self.changed_at = time.monotonic()
        except Exception:
            # Retain the last good count briefly when the account is offline.
            pass
        finally:
            with self.lock:
                self.refreshing = False


def count_open_general_tasks(state, target):
    with _api(state, "tasks", "v1", TASKS_SCOPE, "google-tasks-token.json") as api:
        _verify_task_list(api, target)
        tasks = items(api.tasks(), tasklist=target["id"], maxResults=100, showCompleted=False,
                      showHidden=False, showDeleted=False, showAssigned=True,
                      fields="nextPageToken,items(status)")
        return sum(task.get("status") == "needsAction" for task in tasks)


general_task_count = GeneralTaskCount()


def general_open_count(state, config):
    from note_web import processing_config

    active = processing_config(state, config)
    target = active["google_routes"].get("general")
    if active["processing_mode"] != "kubo" or not isinstance(target, dict):
        return -1
    if target.get("provider", "tasks") != "tasks" or not target.get("id") or not target.get("title"):
        return -1
    return general_task_count.get(state, target)


def home_codex_usage(state, config):
    from codex_usage import codex_usage
    from note_web import processing_config

    if processing_config(state, config)["processing_mode"] == "kubo":
        return codex_usage.get()
    return None


def home_dashboard_payload(state, config, device, *, queue=None, version=2):
    from note_web import processing_config

    active = processing_config(state, config)
    bindings = active["google_routes"] if active["processing_mode"] == "kubo" else {}
    waiting, confirmed = 0, None
    if device and (queue is not None or (state / "google-delivery.sqlite").is_file()):
        queue = queue if queue is not None else DeliveryQueue(state)
        _, _, waiting, confirmed = queue.home_counts(device, bindings, with_last=True)
    local_time = (datetime.fromtimestamp(confirmed, ZoneInfo("Europe/Bratislava")).strftime("%H:%M")
                  if confirmed is not None else "-")
    counts = f"{general_open_count(state, config) if device else -1},{waiting},{local_time}"
    usage = home_codex_usage(state, config) if version >= 3 and device else None
    if usage is not None:
        if version >= 4:
            short, short_reset = usage.five_hours or (-1, 0)
            week, week_reset = usage.weekly or (-1, 0)
            return f"h4,{counts},{usage.sample_expires},{short},{short_reset},{week},{week_reset}"
        remaining, expires = usage.legacy_window()
        return f"h3,{counts},{remaining},{expires}"
    return f"h2,{counts}"


def _api(state, service, version, scope, token):
    from googleapiclient.discovery import build
    from google_store import google_credentials

    try:
        credentials = google_credentials(state, scopes=[scope], token_name=token)
        return build(service, version, credentials=credentials, cache_discovery=False)
    except Exception as error:
        raise DestinationUnavailable(f"Google {service} authorization or service is unavailable.") from error


def bound_task_list(state, requested_title):
    with _api(state, "tasks", "v1", TASKS_SCOPE, "google-tasks-token.json") as api:
        matches = [row for row in items(api.tasklists(), maxResults=100)
                   if row.get("title") == requested_title]
    if len(matches) != 1:
        raise DestinationUnavailable("The selected Google Tasks list is missing or ambiguous.")
    return {"id": matches[0]["id"], "title": requested_title}


def _verify_task_list(api, target):
    try:
        current = api.tasklists().get(tasklist=target["id"]).execute()
    except Exception as error:
        raise DestinationUnavailable("The bound Google Tasks list could not be verified.") from error
    if current.get("id") != target["id"] or current.get("title") != target["title"]:
        raise DestinationUnavailable("The bound Google Tasks list changed.")


def reconcile_task(state, target, key):
    from google_tasks import read_task_action

    with _api(state, "tasks", "v1", TASKS_SCOPE, "google-tasks-token.json") as api:
        _verify_task_list(api, target)
        saved = read_task_action(api, target["id"], key)
    return saved["id"] if saved else None


TITLE_TOKENS = re.compile(r"[^\W_]+(?:[-‑][^\W_]+)*|[^\s\w]|\n")
TITLE_STOP_WORDS = frozenset("""
    ale ano asi aby ak aj ako a ani alebo bez bol bola boli bolo bude budu
    by byt cez co ci cosi chcem chcel chcela da dam dalsie dnes do dobra
    dobre este evidujem g ked kde k kto ktora ktore ktory lebo len ma mam
    mal mala malo maju medzi mi mimo mohol mohla mozno mu na nad nam
    napriklad neskor nie nieco niekedy niektore nech nechcem nechcel no
    o od odlozim ok on ona oni ono po pod podla potom poznamka
    poznamku poznamky poznamenam poznamenat prave pre pred preto pri proste rozne s sa
    sem si skusim sme so som su svoj svoju tak taka take taky tato teda
    ten teraz tej tejto tema tie tieto to tam tiez tom tomu toto
    tu uz u v vam vas vec veci ved viac vo vsetko vy z za zo ze
""".split())


def _topic_title(text):
    # Rank short phrases from across the note by repeated topics and phrases.
    # Clause boundaries and conversational filler never become part of a title.
    phrases = {}
    terms = Counter()
    run = []
    for match in TITLE_TOKENS.finditer(text):
        word = match[0]
        folded = word.translate(SLOVAK_FOLD).casefold()
        if len(folded) < 3 or folded in TITLE_STOP_WORDS or not any(char.isalpha() for char in folded):
            run.clear()
            continue
        root = folded[:5]
        terms[root] += 1
        run = [*run[-2:], (root, word)]
        for count in range(1, len(run) + 1):
            part = run[-count:]
            roots = tuple(term for term, _ in part)
            title = " ".join(value for _, value in part)
            if len(title) <= 34 and len(set(roots)) == count:
                previous, occurrences = phrases.get(roots, (title, 0))
                phrases[roots] = previous, occurrences + 1

    def score(item):
        roots, (title, occurrences) = item
        frequency = sum(math.log2(terms[root]) for root in roots) / len(roots)
        specificity = sum(min(len(word), 12) for word in title.split()) / len(roots)
        phrase_bonus = 0.6 if len(roots) > 1 else 0
        return frequency + math.log2(occurrences) + phrase_bonus + 0.025 * specificity

    if not phrases:
        return "Poznámka"
    roots, (title, _) = max(phrases.items(), key=score)
    if len(roots) == 1:
        topic = roots[0]
        nearby = [item for item in phrases.items() if len(item[0]) > 1 and topic in item[0]]
        if nearby:
            _, (title, _) = max(nearby, key=score)
        else:
            keywords = [item for item in phrases.items()
                        if len(item[0]) == 1 and item[0][0] != topic
                        and len(title) + 2 + len(item[1][0]) <= 34]
            if keywords:
                _, (related, _) = max(keywords, key=score)
                title = f"{title}, {related}"
    return title[0].upper() + title[1:]


def task_presentation(text):
    """Keep short dictations in the title and give longer notes a topical heading."""
    preview = " ".join(text.split())
    if text == preview and len(preview) <= 64:
        return preview, ""
    return _topic_title(text), text


def deliver_google(state, zone, kind, target, captured, text, key):
    note_route = kind in ("general", "sorting")
    if (kind == "docs" or kind.startswith("document:")
            or (note_route and target.get("provider") == "docs")):
        from google_docs import DOCUMENTS_SCOPE, append_note

        recorded = recording_time(captured, zone)
        if recorded is None:
            raise DestinationUnavailable("The Google Docs note needs its original recording date.")
        with _api(state, "docs", "v1", DOCUMENTS_SCOPE, "google-docs-token.json") as api:
            append_note(api, target["id"], target["title"], recorded.date().isoformat(), text, key)
        return target["id"]

    from google_tasks import apply_task, insert_plain_task

    if kind != "shopping" and len(text) > 8192:
        raise DestinationUnavailable("The Pala note exceeds the Google Tasks details limit.")
    with _api(state, "tasks", "v1", TASKS_SCOPE, "google-tasks-token.json") as api:
        _verify_task_list(api, target)
        if kind == "shopping":
            saved = apply_task(api, target["id"], text, key, coalesce=True)
        else:
            title, notes = task_presentation(text)
            saved = insert_plain_task(api, target["id"], title, notes)
        return saved["id"]


@dataclass(frozen=True)
class StagedGoogleDelivery:
    acknowledge: Callable[[], None]
    organize_calendar: bool

    def __call__(self):
        self.acknowledge()


class UsbGoogleDelivery:
    def __init__(self, state, config):
        self.state = state
        self.queue = DeliveryQueue(state)
        self.config = config
        self.zone = ZoneInfo(config.get("timezone", "Europe/Bratislava"))
        self.next_flush = 0
        self.activity = BridgeActivity(state, "delivery")

    def active_config(self):
        from note_web import processing_config

        config = processing_config(self.state, self.config)
        return config if config["processing_mode"] == "kubo" else {}

    def stager(self, device, staged=None):
        def stage(note, captured, text):
            config = self.active_config()
            organize_calendar = bool(config.get("calendar")) and classify_transcript(text)[0] == "calendar"
            if not config.get("google_routes") and not organize_calendar:
                return None
            fallback = general_fallback_kinds(config)
            self.queue.stage(device, note, captured, text, ready=False, general_fallback=fallback)

            def acknowledge():
                active = self.active_config()
                if not active.get("google_routes") and not (organize_calendar and active.get("calendar")):
                    return
                keys = self.queue.stage(device, note, captured, text, general_fallback=fallback)
                if staged is not None:
                    staged.extend(keys)

            return StagedGoogleDelivery(acknowledge, organize_calendar)

        return stage

    def flush(self, keys=None):
        if keys is None:
            now = time.monotonic()
            if now < self.next_flush:
                return
            self.next_flush = now + 30
            keys = self.queue.pending()
        for key in keys:
            try:
                bindings = self.active_config().get("google_routes")
                if not bindings:
                    return
                outcome = self.queue.deliver(key, bindings, self._send)
                if outcome == "done":
                    print("Pala Google delivery confirmed.", flush=True)
            except Exception as error:
                print(f"Pala Google delivery deferred ({type(error).__name__}).",
                      file=sys.stderr, flush=True)

    def _send(self, kind, target, captured, text, key):
        with self.activity.step("google"):
            return deliver_google(self.state, self.zone, kind, target, captured, text, key)


def route_saved(state, config, note, captured, text, device, *, bindings=None, reconcile=False):
    queue = DeliveryQueue(state)
    targets = config.get("google_routes", {}) if bindings is None else bindings
    if reconcile:
        _, actions = _recording_actions(device, note, text)
        return [queue.reconcile_task(key, targets, lambda target, k: reconcile_task(state, target, k))
                for key, _ in actions]
    keys = queue.stage(device, note, captured, text)
    zone = ZoneInfo(config.get("timezone", "Europe/Bratislava"))

    def send(kind, target, when, body, key):
        return deliver_google(state, zone, kind, target, when, body, key)

    return [queue.deliver(key, targets, send) for key in keys]


def main():
    from pair import STATE

    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--transcript", type=Path, help="One existing UTF-8 Pala TXT export.")
    source.add_argument("--usb-port", help="Read the existing TXT directly from a paired Pala.")
    parser.add_argument("--note", type=int, required=True, help="Original Pala note number.")
    parser.add_argument("--captured", help="Original recording timestamp with its UTC offset.")
    destination = parser.add_mutually_exclusive_group()
    destination.add_argument("--task-list", help="Exact approved Tasks list for a one-note task trial.")
    destination.add_argument("--general-list", help="Exact approved list for a plain General note.")
    destination.add_argument("--docs-id", help="Exact approved Google Docs document ID for one note.")
    operation = parser.add_mutually_exclusive_group()
    operation.add_argument("--apply", action="store_true", help="Write and verify this single note.")
    operation.add_argument("--reconcile", action="store_true", help="Only read back an uncertain Google Tasks action.")
    args = parser.parse_args()

    try:
        captured = args.captured
        config = {}
        device = None
        if args.usb_port:
            from serial.tools.list_ports import comports
            from usb_sync import _open_link, paired_port, read_saved

            config = json.loads((STATE / "config.json").read_text(encoding="utf-8-sig"))
            device, token = paired_port(comports(), args.usb_port, config["device_tokens"])
            with _open_link(args.usb_port) as link:
                text, recorded = read_saved(link, (token,), args.note)
            captured = recorded or captured
        else:
            text = args.transcript.read_text(encoding="utf-8-sig")
        kind, entries = classify_transcript(text)
        if not (args.apply or args.reconcile):
            print(f"Route: {kind}. Actions: {len(entries)}.")
            return
        if device is None:
            config = json.loads((STATE / "config.json").read_text(encoding="utf-8-sig"))
            devices = config.get("device_tokens", {})
            if len(devices) != 1:
                raise ValueError("The transcript must be bound to one identified, paired Pala.")
            device = next(iter(devices))
        bindings = None
        if args.task_list is not None:
            if kind != "tasks":
                raise ValueError("The one-note Tasks trial requires an explicitly dictated task.")
            bindings = {"tasks": bound_task_list(STATE, args.task_list)}
        if args.general_list is not None:
            if kind not in ("general", "sorting"):
                raise ValueError("The General trial requires a plain personal dictation.")
            bindings = {kind: bound_task_list(STATE, args.general_list)}
        if args.docs_id is not None:
            if kind not in ("docs", "general", "sorting"):
                raise ValueError("The one-note Docs trial requires a personal note.")
            bindings = {kind: {"id": args.docs_id, "title": "Kubo notes general", "provider": "docs"}}
        result = route_saved(STATE, config, args.note, captured, text, device,
                             bindings=bindings, reconcile=args.reconcile)
        print(f"Note {args.note}: {kind}: {', '.join(result)}.", flush=True)
        if any(status != "done" for status in result):
            raise SystemExit(2)
    except DestinationUnavailable as error:
        raise SystemExit(str(error))
    except Exception as error:
        raise SystemExit(f"Google delivery stopped ({type(error).__name__}).")


if __name__ == "__main__":
    main()
