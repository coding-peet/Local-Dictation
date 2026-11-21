"""Run one scoped Pala Note dictation test against one existing list."""
import argparse
import json
import re
import threading
import unicodedata

from bridge import Bridge, Handler, STATE, digest
from bridge_discovery import allowed_addresses, ipv4_interfaces, serve
from google_tasks import TASKS_SCOPE, insert_verified_task, items, read_verified_task

def words(value):
    plain = unicodedata.normalize("NFKD", value.casefold())
    unaccented = "".join(char for char in plain if not unicodedata.combining(char))
    return re.findall(r"\w+", unaccented)


def find_task_list(api, title):
    matches = [row["id"] for row in items(api.tasklists()) if row["title"] == title]
    if len(matches) != 1:
        raise ValueError(f"Expected one existing Google Tasks list named {title!r}.")
    return matches[0]


class PalaTaskPilot(Bridge):
    def __init__(self, state, config, tasks, list_id, title):
        # The pilot borrows the paired identity and ASR path. It does not
        # initialize or write the normal Bridge's note and action database.
        self.state = state
        self.config = config
        self.tasks = tasks
        self.list_id = list_id
        self.title = title
        self.expected = words("úloha " + title)
        self.lock = threading.Lock()
        self.matching_audio = False
        self.recording = None
        self.verified = threading.Event()

    def marker(self, doc):
        if type(doc.get("pala_note")) is not int or not isinstance(doc.get("pala_created"), str):
            raise ValueError("Missing Pala recording identity.")
        return "Pala Note Tasks pilot " + digest([self.list_id, doc["pala_note"], doc["pala_created"], self.expected])

    def dispatch(self, route, body, content_type, *, device=None):
        if route == "/v1/audio/transcriptions":
            result = self.transcribe(body, content_type)
            if words(result["text"]) == self.expected:
                with self.lock:
                    self.matching_audio = True
                print("Matching Pala audio transcribed locally.", flush=True)
            return result

        if route != "/v1/responses":
            raise ValueError("This pilot accepts transcription and the agreed task only.")
        doc = json.loads(body)
        fmt = doc.get("text", {}).get("format", {})
        if fmt.get("name") != "pala_memory" or words(doc.get("pala_transcript", "")) != self.expected:
            raise ValueError("This recording is outside the one-task pilot.")
        language, = fmt["schema"]["properties"]["language"]["enum"]
        marker = self.marker(doc)

        with self.lock:
            if not self.matching_audio or self.recording not in (None, marker):
                raise ValueError("The pilot requires a fresh recording of the agreed task.")
            self.recording = marker
            if not self.verified.is_set():
                previous = next((task for task in items(
                    self.tasks, tasklist=self.list_id, showCompleted=True, showHidden=True
                ) if task.get("notes") == marker), None)
                if previous is not None:
                    read_verified_task(self.tasks, self.list_id, previous["id"], self.title, marker)
                else:
                    insert_verified_task(self.tasks, self.list_id, self.title, marker)
                self.verified.set()

        memory = {field: [] for field in ("topics", "people", "projects", "decisions", "facts", "questions")}
        memory.update(title=self.title, summary=self.title, kind="task", language=language,
                      importance=1, tasks=[dict(title=self.title, type="todo", dueUtc="",
                                                datePrecision="none", originalTimeText="", confidence=100)])
        return {"status": "completed", "pala_pending": [],
                "output": [{"content": [{"type": "output_text", "text": json.dumps(memory, ensure_ascii=False)}]}]}


def main():
    from googleapiclient.discovery import build
    from google_store import google_credentials

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--list", dest="list_title", required=True, help="Name of the existing Google Tasks list.")
    parser.add_argument("--title", required=True, help="Exact expected title after the spoken word Úloha.")
    args = parser.parse_args()
    if not words(args.title) or len(args.title) > 200:
        parser.error("Use a short test task title.")
    config = json.loads((STATE / "config.json").read_text(encoding="utf-8-sig"))
    if not config.get("discovery_id") or not allowed_addresses(config, [ip for ip, _ in ipv4_interfaces()]):
        raise RuntimeError("The paired Bridge has no approved IPv4 interface for this test.")
    creds = google_credentials(STATE, scopes=[TASKS_SCOPE], token_name="google-tasks-token.json")
    with build("tasks", "v1", credentials=creds, cache_discovery=False) as api:
        pilot = PalaTaskPilot(STATE, config, api.tasks(), find_task_list(api, args.list_title), args.title)
        if not pilot.local_transcription_ready():
            raise RuntimeError("Local Dictation is not ready.")
        stop = threading.Event()

        def close_when_finished():
            if pilot.verified.wait(240):
                stop.wait(3)  # Allow the HTTP worker to return the memory to Pala.
            stop.set()

        threading.Thread(target=close_when_finished, daemon=True).start()
        serve(STATE, config, Handler, pilot, stop=stop)
        if not pilot.verified.is_set():
            raise SystemExit("Timed out waiting for the agreed dictation.")
        print(f"Pala Note task read back from {args.list_title}.", flush=True)


if __name__ == "__main__":
    main()
