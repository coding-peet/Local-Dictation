"""Deliver tasks to Google Tasks and run the separate synthetic pilot."""
import argparse
from contextlib import closing
import hashlib
import re
import sqlite3
import unicodedata
from pathlib import Path
from threading import Lock


PILOT_LIST = "Kubo pilot"
PILOT_TITLE = "mlieko"
PILOT_MARKER = "Kubo Bridge Google Tasks pilot"
TASKS_SCOPE = "https://www.googleapis.com/auth/tasks"
_DELIVERY_LOCK = Lock()
_NOTE_PREFIX = "[Pala Note Tasks "
_NOTE_MARKER = re.compile(r"\[Pala Note Tasks v1 ([01]) ([a-f0-9]{64}(?:,[a-f0-9]{64})*)\]")
_REPEAT_NOTE = "Opakovaná požiadavka (x{})"


class TaskWriteUncertain(RuntimeError):
    """Google Tasks may have accepted a write that could not be verified."""


def items(resource, **filters):
    token = None
    seen_tokens = set()
    while True:
        page = resource.list(pageToken=token, **filters).execute()
        yield from page.get("items", [])
        token = page.get("nextPageToken")
        if not token:
            return
        if token in seen_tokens:
            raise RuntimeError("Google Tasks returned a repeated page token.")
        seen_tokens.add(token)


def _product_key(title):
    return " ".join(unicodedata.normalize("NFC", title).casefold().split())


# Keep aliases explicit. A qualifier such as "údená" distinguishes products.
_PURCHASE_FORMS = {
    "šunka": ("šunka", "šunku", "šunky", "šunkou"),
    "syr": ("syr", "syra", "syry"),
    "mlieko": ("mlieko", "mlieka", "mliekom"),
    "chlieb": ("chlieb", "chleba", "chleby"),
    "rožok": ("rožok", "rožky", "rožkov"),
    "jablko": ("jablko", "jablká", "jabĺk"),
    "banán": ("banán", "banány", "banánov"),
    "ananás": ("ananás", "ananásy", "ananásov"),
}


def _purchase_spelling(value):
    return "".join(c for c in unicodedata.normalize("NFD", _product_key(value))
                   if not unicodedata.combining(c))


_PURCHASE_ALIASES = {
    _purchase_spelling(form): noun for noun, forms in _PURCHASE_FORMS.items() for form in forms
}


def purchase_name(title):
    return _PURCHASE_ALIASES.get(_purchase_spelling(title), title)


def purchase_key(title):
    return _purchase_spelling(purchase_name(title))


def _contains_note_marker(notes):
    return any(line.startswith(_NOTE_PREFIX) for line in notes.splitlines())


def _note_block(previous_purchase, actions):
    marker = f"{_NOTE_PREFIX}v1 {previous_purchase} {','.join(actions)}]"
    count = previous_purchase + len(actions)
    return f"{_REPEAT_NOTE.format(count)}\n{marker}" if count > 1 else marker


def _split_notes(notes):
    """An exact final block owns the count and action hashes. Preserve the rest verbatim."""
    if not _contains_note_marker(notes):
        return notes, 1, ()
    user_notes, _, block = notes.rpartition("\n\n")
    marker = _NOTE_MARKER.fullmatch(block.rsplit("\n", 1)[-1])
    if marker is not None:
        previous_purchase = int(marker[1])
        actions = tuple(marker[2].split(","))
        if (len(set(actions)) == len(actions)
                and not _contains_note_marker(user_notes)
                and block == _note_block(previous_purchase, actions)):
            return user_notes, previous_purchase, actions
    raise ValueError("Google Tasks contains damaged Pala Note delivery metadata.")


def _write_notes(user_notes, previous_purchase, actions):
    block = _note_block(previous_purchase, actions)
    notes = f"{user_notes}\n\n{block}" if user_notes else block
    if len(notes) > 8192:
        raise ValueError("Google Tasks notes exceed 8192 characters with delivery metadata.")
    return notes


def _task_id(task):
    task_id = task.get("id")
    if not isinstance(task_id, str) or not task_id:
        raise ValueError("Google Tasks did not return a task ID.")
    return task_id


def _scan_tasks(tasks, list_id, action, product=None):
    applied_id = None
    open_purchases = []
    for task in items(tasks, tasklist=list_id, maxResults=100,
                      showCompleted=True, showHidden=True, showDeleted=True, showAssigned=True):
        user_notes, previous_purchase, actions = _split_notes(task.get("notes") or "")
        if action in actions:
            if applied_id is not None:
                raise ValueError("Multiple Google Tasks have the same delivery action.")
            applied_id = _task_id(task)
        if (product is not None and not task.get("deleted")
                and _product_key(task.get("title") or "") == product):
            if task.get("status") == "needsAction":
                open_purchases.append((task, user_notes, previous_purchase, actions))
            elif task.get("status") != "completed":
                raise ValueError("Google Tasks returned an unknown purchase status.")
    return applied_id, open_purchases


def _read_task(tasks, list_id, task_id):
    saved = tasks.get(tasklist=list_id, task=task_id).execute()
    if saved.get("id") != task_id or saved.get("deleted"):
        raise RuntimeError("Could not read back the intended Google task.")
    return saved


def _read_action(tasks, list_id, task_id, action):
    saved = _read_task(tasks, list_id, task_id)
    _, _, actions = _split_notes(saved.get("notes") or "")
    if action not in actions:
        raise RuntimeError("Could not verify the previous Google Tasks delivery.")
    return saved


def _write_task(tasks, list_id, request, title, notes, action=None, task_id=None, remember=None):
    write_error = None
    try:
        result = request.execute()
        if task_id is None:
            task_id = _task_id(result)
    except Exception as error:
        # The response may have been lost after Google committed the write.
        # Reconcile by reading Google Tasks below without resending the request.
        write_error = error

    if remember is not None and task_id is not None:
        remember(task_id, title, notes)
    try:
        if task_id is None and action is not None:
            task_id, _ = _scan_tasks(tasks, list_id, action)
        if task_id is not None:
            saved = _read_task(tasks, list_id, task_id)
            if saved.get("title") == title and (saved.get("notes") or "") == notes:
                return saved
    except Exception as error:
        raise TaskWriteUncertain("Could not verify the Google Tasks write.") from error
    raise TaskWriteUncertain("Could not verify the Google Tasks write.") from write_error


def _action_hash(list_id, action_key):
    if not isinstance(list_id, str) or not list_id or list_id != list_id.strip() or list_id == "@default":
        raise ValueError("An explicit Google Tasks list ID is required.")
    if not isinstance(action_key, str) or not action_key.strip():
        raise ValueError("A stable delivery action key is required.")
    return hashlib.sha256(action_key.encode("utf-8")).hexdigest()


def read_task_action(api, list_id, action_key) -> dict | None:
    """Read back an identifiable previous delivery using only provider reads."""
    action = _action_hash(list_id, action_key)
    with _DELIVERY_LOCK:
        tasks = api.tasks()
        task_id, _ = _scan_tasks(tasks, list_id, action)
        if task_id is None:
            return None
        return _read_action(tasks, list_id, task_id, action)


def apply_task(api, list_id, title, action_key, *, coalesce=False, notes="") -> dict:
    """Apply one action to the given Google Tasks list ID and read it back."""
    action = _action_hash(list_id, action_key)
    if not isinstance(title, str) or not title.strip() or len(title) > 1024:
        raise ValueError("Google Tasks needs a title of 1 to 1024 characters.")
    if not isinstance(notes, str) or len(notes) > 8192 or _contains_note_marker(notes):
        raise ValueError("Invalid Google Tasks notes.")

    new_notes = _write_notes(notes, 0, (action,))
    product = _product_key(title) if coalesce else None

    with _DELIVERY_LOCK:
        tasks = api.tasks()
        applied_id, matches = _scan_tasks(tasks, list_id, action, product)
        if applied_id is not None:
            return _read_action(tasks, list_id, applied_id, action)
        if len(matches) > 1:
            raise ValueError("Multiple open Google Tasks match this purchase.")
        if not matches:
            request = tasks.insert(tasklist=list_id, body={"title": title, "notes": new_notes})
            return _write_task(tasks, list_id, request, title, new_notes, action)

        existing, user_notes, previous_purchase, actions = matches[0]
        etag = existing.get("etag")
        if not etag:
            raise ValueError("Google Tasks did not return a version for the purchase.")
        if (existing.get("assignmentInfo") or {}).get("contextType") == "DOCUMENT":
            raise ValueError("A Google Docs assigned task cannot carry purchase notes.")
        task_id = _task_id(existing)
        patch_notes = _write_notes(user_notes, previous_purchase, actions + (action,))
        request = tasks.patch(tasklist=list_id, task=task_id, body={"notes": patch_notes})
        request.headers["If-Match"] = etag
        return _write_task(tasks, list_id, request, existing["title"], patch_notes, action, task_id)


def _clean_purchase_update(notes):
    user_notes, separator, tail = notes.rpartition("\n\n")
    final = tail if separator else notes
    match = re.fullmatch(r"Opakovaná požiadavka \(x([2-9][0-9]*|1[0-9]+)\)", final)
    if match:
        prefix, count = user_notes, int(match[1])
    else:
        prefix, count = notes, 1
    block = _REPEAT_NOTE.format(count + 1)
    return f"{prefix}\n\n{block}" if prefix else block


def _apply_clean_purchase(tasks, list_id, title, remember):
    product = purchase_key(title)
    matches = [task for task in items(tasks, tasklist=list_id, maxResults=100, showCompleted=True,
                                      showHidden=True, showDeleted=True, showAssigned=True)
               if not task.get("deleted") and task.get("status") == "needsAction"
               and purchase_key(task.get("title") or "") == product]
    if len(matches) > 1:
        raise ValueError("Multiple open Google Tasks match this purchase.")
    if not matches:
        request = tasks.insert(tasklist=list_id, body={"title": title})
        return _write_task(tasks, list_id, request, title, "", remember=remember)

    task = matches[0]
    existing = task.get("notes") or ""
    etag = task.get("etag")
    if (_contains_note_marker(existing) or not etag or
            (task.get("assignmentInfo") or {}).get("contextType") == "DOCUMENT"):
        raise ValueError("The existing purchase cannot be safely updated as a clean item.")
    notes = _clean_purchase_update(existing)
    if len(notes) > 8192:
        raise ValueError("The existing purchase notes are full.")
    task_id = _task_id(task)
    request = tasks.patch(tasklist=list_id, task=task_id, body={"notes": notes})
    request.headers["If-Match"] = etag
    return _write_task(tasks, list_id, request, task["title"], notes, task_id=task_id, remember=remember)


def settle_clean_purchase(state, api, list_id, title, action_key, mode):
    """Persist one attempt before dispatch and a receipt after provider readback."""
    action = _action_hash(list_id, action_key)
    if mode not in ("write", "reconcile"):
        raise ValueError("Invalid purchase delivery mode.")
    with _DELIVERY_LOCK, closing(sqlite3.connect(state / "purchase-receipts.sqlite")) as db:
        with db:
            db.execute("""CREATE TABLE IF NOT EXISTS purchase_receipts (
                action TEXT PRIMARY KEY, task TEXT, title TEXT, details TEXT, confirmed INTEGER NOT NULL
            )""")
            row = db.execute("SELECT task,title,details,confirmed FROM purchase_receipts WHERE action=?",
                             (action,)).fetchone()
            if row is None and mode == "write":
                db.execute("INSERT INTO purchase_receipts (action,confirmed) VALUES (?,0)", (action,))

        def remember(task_id, expected_title, details):
            with db:
                db.execute("UPDATE purchase_receipts SET task=?,title=?,details=? WHERE action=?",
                           (task_id, expected_title, details, action))

        if row is None:
            if mode == "reconcile":
                return False
            _apply_clean_purchase(api.tasks(), list_id, title, remember)
        else:
            task_id, expected_title, details, confirmed = row
            if confirmed:
                return True
            if task_id is None:
                return False
            stored = _read_task(api.tasks(), list_id, task_id)
            if stored.get("title") != expected_title or (stored.get("notes") or "") != details:
                return False
        with db:
            db.execute("UPDATE purchase_receipts SET confirmed=1 WHERE action=?", (action,))
        return True


def insert_plain_task(api, list_id, title, notes):
    """Write a clean task for a caller that owns its durable delivery receipt."""
    body = {"title": title}
    if notes:
        body["notes"] = notes
    tasks = api.tasks()
    request = tasks.insert(tasklist=list_id, body=body)
    return _write_task(tasks, list_id, request, title, notes)


def read_verified_task(tasks, list_id, task_id, title, marker):
    saved = tasks.get(tasklist=list_id, task=task_id).execute()
    if saved.get("title") != title or saved.get("notes") != marker:
        raise RuntimeError("Could not read back the expected pilot task.")


def insert_verified_task(tasks, list_id, title, marker):
    created = tasks.insert(tasklist=list_id, body={"title": title, "notes": marker}).execute()
    read_verified_task(tasks, list_id, created["id"], title, marker)


def run_pilot(api):
    lists = api.tasklists()
    matching_lists = [entry["id"] for entry in items(lists) if entry["title"] == PILOT_LIST]
    if len(matching_lists) > 1:
        raise ValueError(f"Multiple task lists are named {PILOT_LIST!r}.")
    if matching_lists:
        list_id = matching_lists[0]
    else:
        list_id = lists.insert(body={"title": PILOT_LIST}).execute()["id"]

    tasks = api.tasks()
    if any(entry.get("notes") == PILOT_MARKER
           for entry in items(tasks, tasklist=list_id, showCompleted=True, showHidden=True)):
        return False
    insert_verified_task(tasks, list_id, PILOT_TITLE, PILOT_MARKER)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["pilot"],
                        help=f"Create or reuse {PILOT_LIST!r} and insert one synthetic task.")
    parser.add_argument("--client-secrets", type=Path, help="Path to the OAuth desktop client file.")
    args = parser.parse_args()

    from googleapiclient.discovery import build
    from google_store import google_credentials
    from pair import STATE

    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    credentials = google_credentials(STATE, interactive=True, scopes=[TASKS_SCOPE],
                                     token_name="google-tasks-token.json", client_path=args.client_secrets)
    with build("tasks", "v1", credentials=credentials, cache_discovery=False) as api:
        created = run_pilot(api)
    print("Pilot task created and verified." if created else "The marked pilot task already exists.")


if __name__ == "__main__":
    main()
