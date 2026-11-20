"""Run a one-PC Google Tasks pilot with one synthetic item."""
import argparse
from pathlib import Path


PILOT_LIST = "Kubo pilot"
PILOT_TITLE = "mlieko"
PILOT_MARKER = "Kubo Bridge Google Tasks pilot"
TASKS_SCOPE = "https://www.googleapis.com/auth/tasks"


def items(resource, **filters):
    token = None
    while True:
        page = resource.list(pageToken=token, **filters).execute()
        yield from page.get("items", [])
        token = page.get("nextPageToken")
        if not token:
            return


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
