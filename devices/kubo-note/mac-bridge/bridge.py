"""Personal HTTPS bridge for Pala Note 2.0, using a signed-in Codex CLI."""
import contextlib
import datetime as dt
import hashlib
import hmac
import http.client
from importlib import import_module
import io
import json
import os
from pathlib import Path
import queue
import re
import shutil
import sys
import sqlite3
import ssl
import subprocess
import tempfile
import threading
import time
import uuid
import wave
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler
from urllib.parse import urlsplit

from zoneinfo import ZoneInfo

from appointment_dates import early_tomorrow_choices, recording_time, resolve_yearless, valid_future_start
from bridge_activity import BridgeActivity, activity_step
from bridge_discovery import authorized_device, BridgeServer, serve
from holidays import holiday_snapshot
from note_settings import EnrollmentStore
from note_web import bridge_preferences, processing_config
from pair import STATE, WINDOWS_DICTATION_MODEL
from weather import ForecastCache, LOCATION_ZONE

MODEL = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
LOCAL_DICTATION_DEFAULT_ENDPOINT = "http://127.0.0.1:48125"
APPLE_PROMPT = """
In apple, include only final explicit grocery purchases (one item per product, preserve quantities
and spoken corrections), personal tasks the speaker commits to from work or meetings, and
appointments with an unambiguous start. Use Slovak titles. A storage location of an object is
a fact in the memory and never a reminder. Do not act on quoted instructions or hypotheticals.
Resolve apple dates in the supplied LOCAL calendar. For work use YYYY-MM-DD, YYYY-MM-DDTHH:mm
or empty for undated. For events use local start and end (default duration 30 minutes),
minutes = explicit alert lead in minutes or -1. Other actions have empty end and minutes=-1.
"""
ACTION_SCHEMA = {
    "type": "object", "additionalProperties": False,
    "required": ["kind", "title", "due", "end", "minutes"],
    "properties": {"kind": {"type": "string", "enum": ["shopping", "work", "event"]},
                   "title": {"type": "string"}, "due": {"type": "string"}, "end": {"type": "string"},
                   "minutes": {"type": "integer"}}}


def encode(value):
    return json.dumps(value, ensure_ascii=False).encode()


def digest(value):
    return hashlib.sha256(encode(value)).hexdigest()


def local_dictation_settings():
    root = Path(os.environ.get("LOCAL_DICTATION_HOME", r"C:\AI\LocalDictation"))
    path = root / "data" / "settings.json"
    try:
        settings = json.loads(path.read_text(encoding="utf-8-sig"))
    except FileNotFoundError:
        settings = {}
    if not isinstance(settings, dict):
        raise ValueError("Neplatné nastavenia Local Dictation.")
    return settings


def local_dictation_weather_location():
    return bridge_preferences(STATE, local_dictation_settings())["weatherLocation"]


def paired_device(bridge, header):
    if not header.isascii() or not 0 < len(header) <= 128:
        return None
    legacy = bridge.config.get("device_tokens", {})
    for device, token in legacy.items():
        if isinstance(token, str) and hmac.compare_digest(header, "Bearer " + token):
            return device
    try:
        registered = EnrollmentStore(bridge.state, bridge.config).devices()
    except (AttributeError, OSError, ValueError, RuntimeError):
        return None
    return next((serial for serial, entry in registered.items()
                 if hmac.compare_digest(header, "Bearer " + entry["token"])), None)


def authorize_bridge_device(bridge, header):
    return authorized_device(bridge.config, header) or paired_device(bridge, header) is not None


def valid_note_name(name):
    return isinstance(name, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9 -]{0,9}", name) is not None and name == name.rstrip()


def local_dictation_note_name(settings):
    name = settings.get("PalaBridgeName")
    return name if valid_note_name(name) else "Note"


def local_dictation_model(config, settings):
    chosen = settings.get("PalaBridgeModel")
    primary = settings.get("PrimaryModel") or WINDOWS_DICTATION_MODEL
    if chosen is None:
        chosen = config.get("transcription_model") or primary
    elif chosen == "":
        chosen = primary
    if not isinstance(chosen, str) or not chosen.strip():
        raise ValueError("Invalid Bridge transcription model.")
    return chosen


def local_dictation_address(settings=None):
    settings = local_dictation_settings() if settings is None else settings
    override = os.environ.get("PALA_BRIDGE_LOCAL_API_ENDPOINT")
    value = override or settings.get("Endpoint", LOCAL_DICTATION_DEFAULT_ENDPOINT)
    if not isinstance(value, str):
        raise ValueError("Neplatná adresa Local Dictation.")

    endpoint = urlsplit(value)
    port = endpoint.port
    if port is None:
        port = 80
    if (endpoint.scheme != "http" or endpoint.hostname not in ("127.0.0.1", "localhost")
            or port in (0, 8123) or endpoint.username is not None or endpoint.password is not None
            or endpoint.path not in ("", "/") or endpoint.query or endpoint.fragment):
        raise ValueError("V Local Dictation nastav lokálnu adresu jeho API.")
    return endpoint.hostname, port


def local_dictation_application_key(host, port):
    override = os.environ.get("PALA_BRIDGE_LOCAL_API_KEY_FILE")
    if override is not None:
        path = Path(override)
        if not os.environ.get("PALA_BRIDGE_LOCAL_API_ENDPOINT") or not path.is_absolute():
            raise ValueError("Bind the Bridge application key to its loopback endpoint.")
    else:
        if (host, port) != ("127.0.0.1", 48125):
            return None
        root = Path(os.environ.get("LOCAL_DICTATION_HOME", r"C:\AI\LocalDictation"))
        path = root / "api-keys" / "bridge.key"
        if not path.is_file():
            return None
    with path.open("r", encoding="ascii") as file:
        key = file.read(256).strip()
    if not re.fullmatch(r"[A-Za-z0-9_-]{32,128}", key):
        raise ValueError("Invalid Bridge application key file.")
    return key


def local_dictation_requires_key(health):
    capabilities = health.get("capabilities")
    return isinstance(capabilities, dict) and capabilities.get("application_keys") is True


def windows_packaged_codex():
    # The Codex desktop package is registered per user and may be absent from PATH.
    system_root = Path(os.environ.get("SystemRoot", r"C:\Windows"))
    powershell = system_root / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    try:
        result = subprocess.run(
            [str(powershell), "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
             "[Console]::OutputEncoding = [Text.UTF8Encoding]::new(); "
             "Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | "
             "Select-Object -ExpandProperty InstallLocation"],
            stdin=subprocess.DEVNULL, capture_output=True, text=True, encoding="utf-8",
            timeout=8, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        if result.returncode == 0:
            for location in result.stdout.splitlines():
                root = Path(location.strip())
                exe = root / "app" / "resources" / "codex.exe"
                if root.is_absolute() and exe.is_file():
                    return str(exe)
    except (OSError, ValueError, subprocess.SubprocessError):
        pass
    return None


def codex_command():
    if os.name == "nt":
        exe = shutil.which("codex.exe")
        if exe:
            return [exe]
        shim = shutil.which("codex.cmd")
        if shim:
            script = Path(shim).parent / "node_modules/@openai/codex/bin/codex.js"
            if script.exists():
                return ["node", str(script)]
        exe = windows_packaged_codex()
        if exe:
            return [exe]
        raise RuntimeError("Nainštaluj Codex CLI a prihlás sa cez codex login.")
    return [shutil.which("codex") or "/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex"]


def require_chatgpt_login(cli):
    status = subprocess.run(cli + ["login", "status"], capture_output=True,
                            stdin=subprocess.DEVNULL, timeout=10)
    if status.returncode or b"chatgpt" not in (status.stdout + status.stderr).lower():
        raise RuntimeError("Prihlás Codex CLI cez ChatGPT (codex login).")


CODEX_EFFORTS = {"none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"}
CODEX_MODEL_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._/-]{0,127}")


def codex_options(settings):
    effort = settings.get("PalaKuboReasoningEffort", "high")
    model = settings.get("PalaKuboModel")
    if not isinstance(effort, str) or effort not in CODEX_EFFORTS:
        raise ValueError("Choose a valid processing reasoning effort in Local Dictation.")
    options = ["-c", f'model_reasoning_effort="{effort}"']
    if model is not None:
        if not isinstance(model, str) or not CODEX_MODEL_ID.fullmatch(model):
            raise ValueError("Choose a valid processing model in Local Dictation.")
        options.extend(("--model", model))
    return options


def codex_environment():
    return {key: value for key, value in os.environ.items()
            if key not in ("OPENAI_API_KEY", "CODEX_API_KEY", "OPENAI_BASE_URL")}


def codex_model_catalog():
    cli = codex_command()
    require_chatgpt_login(cli)
    with tempfile.TemporaryDirectory() as work:
        command = cli + ["app-server", "--listen", "stdio://",
                         "-c", 'model_provider="openai"', "-c", "analytics.enabled=false"]
        with subprocess.Popen(command, cwd=work, env=codex_environment(), encoding="utf-8",
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                              creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)) as child:
            messages = queue.Queue()
            deadline = time.monotonic() + 24

            def read_messages():
                try:
                    for line in child.stdout:
                        with contextlib.suppress(ValueError):
                            messages.put(json.loads(line))
                finally:
                    messages.put(None)

            def send(message):
                child.stdin.write(json.dumps(message) + "\n")
                child.stdin.flush()

            def receive(ident):
                while time.monotonic() < deadline:
                    try:
                        reply = messages.get(timeout=max(0, deadline - time.monotonic()))
                    except queue.Empty:
                        break
                    if reply is None:
                        raise RuntimeError("Codex closed the model catalog connection.")
                    if not isinstance(reply, dict) or reply.get("id") != ident:
                        continue
                    if "result" not in reply:
                        raise RuntimeError("Codex did not return its model catalog.")
                    return reply["result"]
                raise TimeoutError("Codex did not return the model catalog in time.")

            threading.Thread(target=read_messages, daemon=True).start()
            items = {}
            try:
                send({"id": 1, "method": "initialize", "params": {"clientInfo": {"name": "kubo-bridge", "version": "1"}}})
                receive(1)
                send({"method": "initialized"})
                cursor = None
                for ident in range(2, 12):
                    params = {"limit": 100, "includeHidden": False}
                    if cursor is not None:
                        params["cursor"] = cursor
                    send({"id": ident, "method": "model/list", "params": params})
                    page = receive(ident)
                    for model in page["data"]:
                        name = model.get("model") or model.get("id")
                        efforts = [item["reasoningEffort"] for item in model.get("supportedReasoningEfforts", [])
                                   if item.get("reasoningEffort") in CODEX_EFFORTS]
                        if isinstance(name, str) and CODEX_MODEL_ID.fullmatch(name) and efforts and not model.get("hidden"):
                            items[name] = {"id": name, "label": model.get("displayName") or name,
                                           "efforts": efforts, "isDefault": model.get("isDefault") is True}
                    cursor = page.get("nextCursor")
                    if not cursor:
                        break
                else:
                    raise RuntimeError("The Codex model catalog did not finish.")
            finally:
                with contextlib.suppress(OSError):
                    child.stdin.close()
                try:
                    child.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait()
    if not items:
        raise RuntimeError("Codex returned no compatible processing models.")
    return list(items.values())


def codex(prompt, schema, *, profile=None, timeout=120):
    cli = codex_command()
    require_chatgpt_login(cli)
    with tempfile.TemporaryDirectory() as work:
        out = Path(work) / "result"
        args = cli + ["exec", "--ephemeral", "--ignore-user-config",
                "--skip-git-repo-check", "--sandbox", "read-only", "--disable", "shell_tool", "--disable", "apps",
                "-c", 'web_search="disabled"', "-o", str(out)]
        args += codex_options(local_dictation_settings() if profile is None else profile)
        if schema:
            spec = Path(work) / "schema.json"
            spec.write_bytes(encode(schema))
            args += ["--output-schema", str(spec)]
        subprocess.run(args + ["-"], input=prompt.encode(), cwd=work, env=codex_environment(),
                       stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, check=True, timeout=timeout)
        return out.read_text(encoding="utf-8").strip()


YEARLESS = {
    "type": "boolean",
    "description": "True only for an upcoming date given by day and month without a year. False for explicit years, relative dates and historical references."
}


def resolve_action_yearless(action, reference):
    if not action.get("yearless", False) or not action["due"]:
        action.pop("yearless", None)
        return True
    original = dt.datetime.fromisoformat(action["due"])
    if original.tzinfo is not None:
        return False
    timed = "T" in action["due"]
    due = resolve_yearless(original, reference, timed=timed)
    if due is None:
        return False
    wall = due.replace(tzinfo=None)
    if action["end"]:
        end = dt.datetime.fromisoformat(action["end"])
        if end.tzinfo is not None:
            return False
        action["end"] = (end + (wall - original)).isoformat(timespec="minutes")
    action["due"] = wall.isoformat(timespec="minutes") if timed else wall.date().isoformat()
    action.pop("yearless")
    return True


def validate_action(a):
    if a["kind"] not in ("shopping", "work", "event", "work_event", "work_note", "keep", "apple_note") or not 0 < len(a["title"].strip()) <= (8000 if a["kind"] in ("work_note", "keep") else 250):
        raise ValueError("Neplatná akcia")
    for field in ("due", "end"):
        value = a[field]
        if value:
            pattern = "%Y-%m-%d" if a["kind"] == "work" and field == "due" and len(value) == 10 else "%Y-%m-%dT%H:%M"
            if dt.datetime.strptime(value, pattern).strftime(pattern) != value:
                raise ValueError("Neplatný dátum")
    if a["kind"] in ("event", "work_event") and (not a["due"] or a["end"] <= a["due"]):
        raise ValueError("Neplatný interval")
    if type(a["minutes"]) != int or not -1 <= a["minutes"] <= 525600:
        raise ValueError("Neplatný predstih")


def local_dictation_health(connection):
    connection.request("GET", "/health")
    response = connection.getresponse()
    if response.status != 200:
        raise OSError(f"Kontrola Local Dictation vrátila HTTP {response.status}")
    health = json.loads(response.read(64 * 1024))
    if (not isinstance(health, dict) or health.get("service") != "local-dictation"
            or health.get("ready") is not True):
        raise OSError("Na nastavenej adrese nie je pripravené API Local Dictation.")
    return health


class ProcessingDisabled(RuntimeError):
    """The current mode permits transcription only."""


class Bridge:
    def __init__(self, state, config):
        self.state = state
        self.config = config
        self.lock = threading.Lock()
        self.encoder = None
        self.last_contact = None
        self.activity = BridgeActivity(state, "network")
        self.weather = ForecastCache(local_dictation_weather_location)
        with self.database() as db:
            db.execute("CREATE TABLE IF NOT EXISTS memories (id TEXT PRIMARY KEY, data TEXT)")
            db.execute("CREATE TABLE IF NOT EXISTS actions (id TEXT PRIMARY KEY, data TEXT, status TEXT)")

    @contextlib.contextmanager
    def database(self):
        with contextlib.closing(sqlite3.connect(self.state / "bridge.sqlite")) as db, db:
            yield db

    def deliver(self, action, key, *, config=None):
        if config is None:
            config = processing_config(self.state, self.config)
        if config.get("destination") == "google":
            if config["processing_mode"] != "kubo":
                raise ProcessingDisabled("Bridge is set to transcription only.")
            if action["kind"] in ("shopping", "arrival", "keep"):
                return  # The Samsung downloads these from the private shopping feed.
            if action["kind"] in ("work", "work_note", "work_event"):
                if config["personal_note_collection"]:
                    raise ValueError("This personal Note profile only approves Google destinations.")
                from microsoft_store import deliver
                deliver(self.state, self.config, action, key)
                return
            from google_store import apply_google
            with self.activity.step("google"):
                apply_google(config, self.state, action, key)
            return
        kind = action["kind"]
        if kind == "apple_note":
            subprocess.run(["/usr/bin/osascript", "-l", "JavaScript", str(Path(__file__).with_name("apple_note.js"))],
                           input=encode(dict(id=key, title=action["title"], body=action["body"])),
                           check=True, timeout=90)
            return
        target = self.config[{"arrival": "shopping", "event": "calendar"}.get(kind, kind)]
        cmd = dict(action, id=key, target=target, lat=self.config["lat"], lon=self.config["lon"])
        if kind == "arrival" and self.config.get("stores"):
            cmd["stores"] = self.config["stores"]
        child = self.state / "PalaApple.app/Contents/MacOS/PalaApple"
        subprocess.run([child], input=encode(cmd), check=True, timeout=90)

    def memories(self, doc, schema, *, profile=None):
        config = processing_config(self.state, self.config)
        google = config.get("destination") == "google"
        route = ""
        if google:
            from pala_google import classify_transcript

            route, _ = classify_transcript(doc["pala_transcript"])
        explicit_calendar = route == "calendar"

        def appointment_allowed(action):
            return not google or explicit_calendar or (route == "work" and action["kind"] == "work_event")

        key = digest([doc["pala_note"], doc["pala_created"], doc["pala_transcript"]])
        with self.database() as db:
            row = db.execute("SELECT data FROM memories WHERE id=?", (key,)).fetchone()
            if not row:
                action_schema = json.loads(json.dumps(ACTION_SCHEMA))
                zone = ZoneInfo(self.config.get("timezone", "Europe/Bratislava"))
                reference = recording_time(doc.get("pala_created"), zone)
                action_schema["properties"]["yearless"] = YEARLESS
                action_schema["properties"]["originalTimeText"] = {
                    "type": "string",
                    "description": "Exact spoken start phrase for an appointment, including zajtra when spoken. Empty for non-events.",
                }
                action_schema["required"].extend(["yearless", "originalTimeText"])
                if "tasks" in schema["properties"]:
                    task = schema["properties"]["tasks"]["items"]
                    task["properties"]["yearless"] = YEARLESS
                    task["properties"]["shoppingListEntry"] = {
                        "type": "boolean",
                        "description": "True only if this task merely duplicates adding groceries to the list."
                    }
                    task["required"].extend(["yearless", "shoppingListEntry"])
                date_context = (
                    f" The recording was made at {reference.isoformat(timespec='minutes')} ({zone.key}). "
                    "Use that recording date as the reference for relative dates and dates without a year. "
                    if reference else " The recording time is unavailable. Preserve ambiguous time wording. "
                )
                prompt = APPLE_PROMPT + date_context + (
                    "Respect explicit years and intentional past references. "
                    "A shopping-list instruction is fulfilled by the shopping actions. "
                    "For a shopping-list-only note use kind=reference, tasks=[], and a descriptive title. "
                    "In mixed notes keep independent tasks, events, and explicitly requested timed reminders. "
                    "Never export the list-entry instruction as a work task.")
                if google:
                    action_schema["properties"]["kind"]["enum"] += ["work_event", "work_note", "keep"]
                    prompt += (" Use work_event only for explicitly work-related appointments, otherwise event. "
                               "Use work_note for an explicitly requested OneNote entry only, with its text in title. "
                               "Use keep for an explicitly dictated Google Keep note, preserving its contents in title. "
                               "Work actions go to the organization's Planner, so include only explicit work commitments.")
                    if explicit_calendar:
                        prompt += (" This is an explicit calendar command. Emit an event for every stated "
                                   "appointment with a concrete start. Use the date after the local capture day "
                                   "for zajtra. The Bridge will ask about overnight ambiguity.")
                else:
                    action_schema["properties"]["kind"]["enum"].append("apple_note")
                    action_schema["properties"]["body"] = {"type": "string"}
                    action_schema["required"].append("body")
                    prompt += (" Use apple_note for a standalone dictated note, idea, or remembered fact, "
                               "or when asked to save to Notes. Give it a short Slovak title and the full "
                               "dictated content in body, preserving details and omitting the command. "
                               "Use empty body for other actions. Do not copy shopping, tasks or calendar commands "
                               "to Notes unless separately requested.")
                schema["properties"]["apple"] = {"type": "array", "items": action_schema}
                schema["required"].append("apple")
                result = json.loads(codex(doc["instructions"] + prompt + "\n" + doc["input"], schema, profile=profile))
                actions = result.pop("apple")
                if route.startswith("list:"):
                    actions = []
                arrival = any(a["kind"] == "shopping" for a in actions)
                if "tasks" in result:
                    result["tasks"] = [t for t in result["tasks"]
                                       if not (t.pop("shoppingListEntry", False) and arrival)]
                    if arrival and not result["tasks"] and result.get("kind") == "task":
                        result["kind"] = "reference"
                for task in result.get("tasks", []):
                    if task.pop("yearless", False) and task["dueUtc"]:
                        local = dt.datetime.fromisoformat(task["dueUtc"].replace("Z", "+00:00")).astimezone(zone)
                        due = resolve_yearless(local, reference, timed=task["datePrecision"] == "time")
                        if due is None:
                            task["dueUtc"] = ""
                            task["datePrecision"] = "none"
                        else:
                            task["dueUtc"] = due.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
                if len(actions) > 64 or not result.get("title") or not result.get("summary"):
                    raise ValueError("Neplatná poznámka")
                for i, a in enumerate(actions):
                    time_wording = a.pop("originalTimeText", "")
                    if explicit_calendar and config["personal_note_collection"] and a["kind"] == "work_event":
                        a["kind"] = "event"
                    if config["personal_note_collection"] and a["kind"] in ("work", "work_note", "work_event"):
                        continue
                    if a["kind"] in ("event", "work_event") and not appointment_allowed(a):
                        continue
                    if not resolve_action_yearless(a, reference):
                        # Preserve the proposal locally until the device can collect a date.
                        db.execute("INSERT INTO actions VALUES(?,?,?)",
                                   (key + "-" + str(i), json.dumps(a), "needs-date"))
                        continue
                    if a["kind"] == "apple_note" and not (self.config.get("destination") == "apple" and
                            isinstance(a.get("body"), str) and a["body"].strip()):
                        raise ValueError("Neplatný obsah poznámky")
                    validate_action(a)
                    if a["kind"] in ("event", "work_event"):
                        choices = early_tomorrow_choices(a, reference, time_wording)
                        if choices:
                            a["choices"] = choices
                    db.execute("INSERT INTO actions VALUES(?,?,?)",
                               (key + "-" + str(i), json.dumps(a), "pending" if a["kind"] in ("event", "work_event") else "auto"))
                if arrival:
                    db.execute("INSERT INTO actions VALUES(?,?,?)", (key + "-arrival", json.dumps(
                        dict(kind="arrival", title="Nákup", due="", end="", minutes=-1)), "auto"))
                db.execute("INSERT INTO memories VALUES(?,?)", (key, json.dumps(result)))
            else:
                result = json.loads(row[0])
        pending = []
        with self.database() as db:
            rows = list(db.execute("SELECT id,data,status FROM actions WHERE id LIKE ? ORDER BY rowid", (key + "-%",)))
        now = dt.datetime.now(dt.timezone.utc)
        zone = ZoneInfo(config.get("timezone", "Europe/Bratislava"))
        for ident, data, status in rows:
            action = json.loads(data)
            if status == "auto" and not route.startswith("list:"):
                self.deliver(action, "arrival" if action["kind"] == "arrival" else ident)
                self.mark(ident, "done")
            elif status == "accepted":
                self.deliver_accepted_event(ident, action)
            elif status == "pending" and appointment_allowed(action):
                if config["personal_note_collection"] and action["kind"] == "work_event":
                    continue
                dates = action.get("choices") or (action,)
                if google and not any(all(valid_future_start(date.get(field), zone, now) for field in ("due", "end"))
                                      for date in dates):
                    self.mark(ident, "needs-date", expected="pending")
                    continue
                pending.append(dict(id=ident, **action))
        return json.dumps(result, ensure_ascii=False), pending

    def mark(self, ident, status, *, expected=None):
        with self.database() as db:
            return db.execute("UPDATE actions SET status=? WHERE id=? AND status=COALESCE(?,status)",
                              (status, ident, expected)).rowcount

    def deliver_accepted_event(self, ident, action):
        # A failed provider reply can arrive after creating the event. Reuse
        # the approved interval and target so the event ID stays idempotent.
        config = processing_config(self.state, self.config)
        timezone = config.get("timezone", "Europe/Bratislava")
        if (config.get("destination") != "google" or config.get("calendar") != action["accepted_calendar"] or
                timezone != action["accepted_timezone"]):
            raise ValueError("Potvrdený termín čaká na pôvodný kalendár a časové pásmo.")
        self.deliver(action, ident, config=config)
        self.mark(ident, "done", expected="accepted")

    def confirm(self, doc):
        with self.database() as db:
            row = db.execute("SELECT data,status FROM actions WHERE id=?", (doc["id"],)).fetchone()
        if not row or json.loads(row[0])["kind"] not in ("event", "work_event") or type(doc["accept"]) != bool:
            raise ValueError("Neplatné potvrdenie")
        if row[1] == "needs-date":
            raise ValueError("Termín potrebuje konkrétny dátum.")
        if row[1] == "accepted":
            if not doc["accept"]:
                raise ValueError("Termín už bol potvrdený.")
            self.deliver_accepted_event(doc["id"], json.loads(row[0]))
        if row[1] == "pending":
            if doc["accept"]:
                action = json.loads(row[0])
                choices = action.pop("choices", [])
                if choices:
                    choice = doc.get("choice")
                    if type(choice) is not int or not 0 <= choice < len(choices):
                        raise ValueError("Vyber konkrétny dátum termínu.")
                    action.update(choices[choice])
                config = processing_config(self.state, self.config)
                if config.get("destination") == "google":
                    zone = ZoneInfo(config.get("timezone", "Europe/Bratislava"))
                    now = dt.datetime.now(dt.timezone.utc)
                    if not all(valid_future_start(action[field], zone, now) for field in ("due", "end")):
                        if not choices:
                            self.mark(doc["id"], "needs-date", expected="pending")
                        raise ValueError("Termín uplynul alebo potrebuje nový dátum.")
                    if action["kind"] == "event":
                        if not config.get("calendar"):
                            raise ValueError("Nastav osobný kalendár v nastaveniach Bridge.")
                        action["accepted_calendar"] = config["calendar"]
                        action["accepted_timezone"] = zone.key
                        with self.database() as db:
                            claimed = db.execute(
                                "UPDATE actions SET data=?,status='accepted' WHERE id=? AND status='pending'",
                                (json.dumps(action), doc["id"])).rowcount
                        if not claimed:
                            raise ValueError("Potvrdenie termínu sa už spracúva.")
                        self.deliver_accepted_event(doc["id"], action)
                        return {"ok": True}
                self.deliver(action, doc["id"], config=config)
            if not self.mark(doc["id"], "done" if doc["accept"] else "declined", expected="pending"):
                raise ValueError("Potvrdenie termínu sa už spracúva.")
        return {"ok": True}

    def replay_transcription(self, device, note, audio, recognize):
        """Return one durable result across USB, BLE and HTTP workers/processes."""
        if not device or type(note) is not int or not 0 < note <= 2147483647:
            raise ValueError("Invalid paired recording identity.")
        key = (device.casefold(), note, hashlib.sha256(audio).hexdigest())
        # Independent of delivery/settings databases. Hold the write lock across
        # inference so a reconnect cannot start a second ASR while HTTP is still running.
        with contextlib.closing(sqlite3.connect(self.state / "transcriptions.sqlite", timeout=300)) as db, db:
            db.execute("BEGIN IMMEDIATE")
            db.execute("""CREATE TABLE IF NOT EXISTS results (
                device TEXT, note INTEGER, wav_hash TEXT, result TEXT NOT NULL,
                PRIMARY KEY(device, note, wav_hash))""")
            row = db.execute("SELECT result FROM results WHERE device=? AND note=? AND wav_hash=?", key).fetchone()
            if row:
                return json.loads(row[0])
            result = recognize(audio)
            if not isinstance(result, dict) or not isinstance(result.get("text"), str):
                raise ValueError("Invalid transcription result.")
            db.execute("INSERT INTO results VALUES(?,?,?,?)", (*key, json.dumps(result, ensure_ascii=False)))
            return result

    def transcribe(self, body, content_type, *, device=None):
        # An authenticated recording has an additional note-number field.
        boundary = re.fullmatch(r"multipart/form-data; boundary=([\w-]{1,70})", content_type)
        if not boundary:
            raise ValueError("multipart")
        marker = boundary[1].encode()
        offset = body.index(b'filename="note.wav"')
        start = body.index(b"\r\n\r\n", offset) + 4
        ending = b"\r\n--" + marker + b"--\r\n"
        if not body.endswith(ending):
            raise ValueError("Incomplete WAV")
        audio = body[start:-len(ending)]
        with wave.open(io.BytesIO(audio)) as wav:
            if wav.getnchannels() != 1 or wav.getsampwidth() != 2:
                raise ValueError("PCM mono required")
        identity_field = b'Content-Disposition: form-data; name="pala_note"\r\n\r\n'
        prefix = body[:offset]
        if identity_field in prefix:
            value = prefix.split(identity_field, 1)[1].split(b"\r\n", 1)[0]
            if not re.fullmatch(rb"[1-9][0-9]{0,9}", value):
                raise ValueError("Invalid recording number.")
            # Legacy Wi-Fi pairing has only the shared profile credential.
            # Its cache remains disjoint from identities of individually paired Notes.
            return self.replay_transcription(device or "@legacy-wifi", int(value), audio, self._recognize_upload)
        return self._recognize_upload(audio)

    def _recognize_upload(self, audio):
        if os.name == "nt":
            return self.transcribe_local(audio)
        with tempfile.TemporaryDirectory() as work:
            path = Path(work) / "note.wav"
            path.write_bytes(audio)
            command = ["whisper-cli", "-m", self.config["model"], "-f", str(path), "-l", "auto",
                       "-nt", "-otxt", "-of", str(path)]
            run = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, timeout=240)
            if run.returncode:  # Fallback for systems without a working GPU backend.
                subprocess.run(command + ["-ng"], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                               timeout=240, check=True)
            return {"text": path.with_suffix(".wav.txt").read_text().strip()}

    def local_transcription_status(self):
        if os.name != "nt":
            return HTTPStatus.BAD_GATEWAY
        try:
            host, port = local_dictation_address()
            with contextlib.closing(http.client.HTTPConnection(host, port, timeout=2)) as connection:
                health = local_dictation_health(connection)
                if local_dictation_requires_key(health) and local_dictation_application_key(host, port) is None:
                    return HTTPStatus.BAD_GATEWAY
                busy = health.get("busy", False)
        except (OSError, ValueError, http.client.HTTPException):
            return HTTPStatus.BAD_GATEWAY
        if busy is True:
            return HTTPStatus.SERVICE_UNAVAILABLE
        if busy is False:
            return HTTPStatus.OK
        return HTTPStatus.BAD_GATEWAY

    def local_transcription_ready(self):
        return self.local_transcription_status() == HTTPStatus.OK

    def transcribe_local(self, audio):
        boundary = "pala-" + uuid.uuid4().hex
        settings = local_dictation_settings()
        model = local_dictation_model(self.config, settings)
        fields = {"model": model, "language": "sk", "response_format": "json"}
        parts = [
            f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode()
            for name, value in fields.items()
        ]
        parts.extend((
            f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="note.wav"\r\n'
            'Content-Type: audio/wav\r\n\r\n'.encode(),
            audio,
            f'\r\n--{boundary}--\r\n'.encode(),
        ))
        host, port = local_dictation_address(settings)
        key = local_dictation_application_key(host, port)
        headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
        if key is not None:
            headers["Authorization"] = "Bearer " + key
        connection = http.client.HTTPConnection(host, port, timeout=100)
        try:
            if local_dictation_requires_key(local_dictation_health(connection)) and key is None:
                raise OSError("The Local Dictation API requires a Bridge application key.")
            connection.request("POST", "/v1/audio/transcriptions", b"".join(parts), headers)
            response = connection.getresponse()
            if response.status != 200:
                raise OSError(f"Local Dictation returned HTTP {response.status}")
            result = json.loads(response.read())
            return {"text": result["text"].strip()}
        finally:
            connection.close()

    def dispatch(self, route, body, content_type, *, device=None):
        if route == "/v1/audio/transcriptions":
            if device:
                return self.transcribe(body, content_type, device=device)
            return self.transcribe(body, content_type)
        doc = json.loads(body)
        if route in ("/pala/recorder-plan", "/pala/recorder-apply"):
            from pala_google import DestinationUnavailable
            from recorder_delivery import dispatch_work

            if not device:
                raise ValueError("Recorder delivery requires an individually paired Note.")
            try:
                return dispatch_work(self.state, self.config, route, device, doc)
            except DestinationUnavailable:
                return {"confirmed": False}
        if route == "/pala/transcript-receipt":
            from pala_google import UsbGoogleDelivery
            if not device or type(doc.get("note")) is not int:
                raise ValueError("Invalid transcript receipt.")
            router = UsbGoogleDelivery(self.state, self.config)
            for note, captured, text, route_enabled in router.queue.unconfirmed(device):
                if note != doc["note"]:
                    continue
                if (doc.get("captured") != captured or
                        hashlib.sha256(text.encode("utf-8")).hexdigest() != doc.get("sha256")):
                    raise ValueError("The committed text does not match the Bridge receipt.")
                router.stager(device, route=bool(route_enabled))(note, captured, text)()
                break
            return {"ok": True}
        with self.lock:
            config = processing_config(self.state, self.config) if hasattr(self, "state") else {}
            if config.get("processing_mode") == "transcribe":
                raise ProcessingDisabled("Bridge is set to transcription only.")
            if route == "/pala/event":
                if device is None:
                    return self.confirm(doc)
                from pala_google import DeliveryQueue

                journal = DeliveryQueue(self.state)
                try:
                    result = self.confirm(doc)
                except Exception as error:
                    journal.log_calendar_decision(doc.get("id"), f"decision could not complete ({type(error).__name__})")
                    raise
                journal.log_calendar_decision(doc.get("id"), "confirmation processed" if doc.get("accept")
                                              else "declined on Note")
                return result
            if route == "/v1/responses":
                fmt = doc.get("text", {}).get("format", {})
                if fmt.get("name") == "pala_memory":
                    journal = None
                    profile = local_dictation_settings() if device is not None else None
                    if device is not None:
                        from pala_google import (DeliveryQueue, can_stage_google_route, classify_transcript,
                                                 general_fallback_kinds)

                        journal = DeliveryQueue(self.state)
                        # The Note sends organization only once its transcript is durable on SD.
                        recording = journal.remember_transcript(device, doc["pala_note"], doc["pala_created"],
                                                                 doc["pala_transcript"], on_note=True)
                        kind, _ = classify_transcript(doc["pala_transcript"])
                        if can_stage_google_route(config, kind):
                            journal.stage(device, doc["pala_note"], doc["pala_created"], doc["pala_transcript"],
                                          general_fallback=general_fallback_kinds(config))
                        else:
                            journal.log_decision(recording, "Routing", f"{kind} retained locally")
                        codex_options(profile)
                        model = profile.get("PalaKuboModel") or "Codex default"
                        effort = profile.get("PalaKuboReasoningEffort", "high")
                        journal.log_decision(recording, "Kubo", f"configured model {model}, reasoning {effort}")
                    try:
                        text, pending = self.memories(doc, fmt["schema"], profile=profile)
                    except Exception as error:
                        if journal:
                            journal.log_decision(recording, "Kubo", f"processing failed ({type(error).__name__})")
                        raise
                    if journal and (kind == "calendar" or pending):
                        detail = f"{len(pending)} proposal(s) await confirmation" if pending else "No concrete future proposal"
                        journal.log_calendar_proposals(recording, pending, detail)
                else:
                    text = codex(doc["instructions"] + "\n" + doc["input"], fmt.get("schema"))
                    pending = []
                return {"status": "completed", "output": [{"content": [{"type": "output_text", "text": text}]}],
                        "pala_pending": pending}
            if route == "/v1/embeddings":
                if self.encoder is None:
                    from sentence_transformers import SentenceTransformer
                    try:
                        self.encoder = SentenceTransformer(MODEL, local_files_only=True)
                    except OSError:
                        self.encoder = SentenceTransformer(MODEL)
                vector = self.encoder.encode(doc["input"])[:256]
                norm = max(sum(float(x)**2 for x in vector)**0.5, 1e-12)
                return {"data": [{"embedding": [float(x) / norm for x in vector]}]}
            if route == "/v1/audio/speech":
                with tempfile.TemporaryDirectory() as work:
                    wav = Path(work) / "speech.wav"
                    if os.name == "nt":
                        subprocess.run(["powershell", "-NoProfile", "-File", str(Path(__file__).with_name("speech.ps1")),
                                        str(wav)], input=doc["input"].encode(), check=True, timeout=60)
                    else:
                        subprocess.run(["say", "-v", self.config["voice"], "-f", "-", "-o", str(wav),
                                    "--file-format=WAVE", "--data-format=LEI16@24000"],
                                   input=doc["input"].encode(), check=True, timeout=60)
                    with wave.open(str(wav)) as source:
                        if source.getnframes() < 1 or (source.getnchannels(), source.getsampwidth(), source.getframerate()) != (1, 2, 24000):
                            raise RuntimeError("Skontroluj nainštalovaný hlas.")
                        if doc["response_format"] != "wav":
                            return source.readframes(source.getnframes())
                    return wav.read_bytes()
        raise ValueError("Neznáma požiadavka")


class Handler(BaseHTTPRequestHandler):
    def send_json(self, status, body):
        data = encode(body)
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        bridge = self.server.bridge
        if self.path in ("/pala/delivery", "/pala/home", "/pala/home?v=3", "/pala/home?v=4"):
            from pala_google import home_dashboard_payload, home_delivery_payload

            header = self.headers.get("Authorization", "")
            device = paired_device(bridge, header)
            if device is None:
                self.send_error(401)
                return
            try:
                if self.path == "/pala/delivery":
                    payload = home_delivery_payload(bridge.state, bridge.config, device)
                else:
                    version = int(self.path[-1]) if "?v=" in self.path else 2
                    manual = self.headers.get("X-Pala-Home") == "1"
                    payload = home_dashboard_payload(bridge.state, bridge.config, device, version=version,
                                                     **({"manual": True, "wait_for_sources": 4} if manual else {}))
            except (OSError, sqlite3.Error, ValueError):
                self.send_error(503)
                return
            self.send_json(200, {"counts": payload})
            return
        if self.path in ("/pala/ready", "/pala/weather", "/pala/name", "/pala/holidays"):
            if not authorize_bridge_device(bridge, self.headers.get("Authorization", "")):
                self.send_error(401)
                return
            bridge.last_contact = dt.datetime.now(dt.timezone.utc).isoformat()
            if self.path == "/pala/name":
                try:
                    name = local_dictation_note_name(local_dictation_settings())
                except (OSError, ValueError):
                    self.send_error(503)
                    return
                self.send_json(200, {"name": name})
            elif self.path == "/pala/weather":
                with activity_step(getattr(bridge, "activity", None), "weather"):
                    forecast = bridge.weather.get()
                self.send_json(200 if forecast is not None else 503, forecast or {})
            elif self.path == "/pala/holidays":
                try:
                    country = bridge_preferences(bridge.state, {})["holidayCountry"]
                    snapshot = holiday_snapshot(country, dt.datetime.now(LOCATION_ZONE).year)
                except (OSError, ValueError):
                    self.send_error(503)
                    return
                self.send_json(200, {"holidays": snapshot})
            else:
                if self.headers.get("X-Pala-Home") == "1":
                    self.send_json(200, {"ready": False})
                    return
                # The Note waits on a busy API and tries another link if the API is unavailable.
                status = bridge.local_transcription_status()
                mode = processing_config(bridge.state, bridge.config)["processing_mode"]
                reply = {"ready": status == HTTPStatus.OK, "processing": mode}
                if self.headers.get("X-Pala-Receipts") == "1":
                    device = paired_device(bridge, self.headers.get("Authorization", ""))
                    if device and (bridge.state / "google-delivery.sqlite").is_file():
                        from pala_google import DeliveryQueue
                        pending = DeliveryQueue(bridge.state).next_unconfirmed_note(device)
                        if pending:
                            reply["receipt"] = pending
                self.send_json(status, reply)
            return

        path = bridge.state / "phone-token"
        if (self.path not in ("/pala/shopping", "/pala/keep") or bridge.config.get("destination") != "google" or not path.exists()
                or not hmac.compare_digest(self.headers.get("Authorization", ""), "Bearer " + path.read_text())):
            self.send_error(403)
            return
        with bridge.database() as db:
            rows = db.execute("SELECT id,data FROM actions WHERE status='done' ORDER BY rowid")
            items = []
            for ident, raw in rows:
                action = json.loads(raw)
                if action["kind"] == ("keep" if self.path == "/pala/keep" else "shopping"):
                    items.append({"id": ident, "title": action["title"]})
        self.send_json(200, items)

    def do_POST(self):
        bridge = self.server.bridge
        authorization = self.headers.get("Authorization", "")
        device = paired_device(bridge, authorization)
        if device is None and not authorized_device(bridge.config, authorization):
            self.send_error(401)
            return
        bridge.last_contact = dt.datetime.now(dt.timezone.utc).isoformat()
        try:
            limit = 25100000 if self.path == "/v1/audio/transcriptions" else 1024 * 1024
            size = int(self.headers.get("Content-Length", 0))
            if not 0 < size <= limit:
                self.send_error(413)
                return
            self.connection.settimeout(180)
            activity = getattr(bridge, "activity", None)
            transcription = self.path == "/v1/audio/transcriptions"
            with activity_step(activity, "receiving" if transcription else "processing"):
                data = self.rfile.read(size)
            if len(data) != size:
                self.send_error(400)
                return
            with activity_step(activity, "transcribing" if transcription else "processing"):
                result = bridge.dispatch(self.path, data, self.headers.get("Content-Type", ""), device=device)
            binary = isinstance(result, bytes)
            payload = result if binary else encode(result)
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream" if binary else "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
        except (BrokenPipeError, ConnectionResetError):
            pass
        except ProcessingDisabled:
            self.send_json(409, {"processing": "transcribe"})
        except Exception as error:
            # Operational errors must remain retryable in the firmware's durable queue.
            self.log_error("Spracovanie zlyhalo (%s)", type(error).__name__)
            self.send_error(503)


def managed_stop_event(parent_input):
    """Stop when the owning application's control pipe closes."""
    stop = threading.Event()
    parent_fd = parent_input.fileno()

    def watch_parent():
        try:
            # A blocked buffered stdin reader can prevent interpreter shutdown
            # when startup fails and the parent still holds the other end.
            os.read(parent_fd, 1)
        except OSError:
            pass
        finally:
            stop.set()

    threading.Thread(target=watch_parent, name="bridge-parent", daemon=True).start()
    return stop


def serve_configured_bridge(config, stop, report):
    from pala_google import home_codex_usage

    bridge = Bridge(STATE, config)
    home_codex_usage(STATE, config)
    if config.get("discovery_id"):
        serve(STATE, config, Handler, bridge, stop=stop,
              on_status=lambda listeners, desired: report(
                  "running", listeners=sorted(listeners), last_contact=bridge.last_contact,
                  error=("HTTPS could not bind to every approved interface."
                         if desired - listeners.keys() else None)),
              drain=stop is not None)
        return
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(STATE / "cert.pem", STATE / "key.pem")
    with BridgeServer((config["host"], 8443), Handler, bridge, ctx) as server:
        print("Pala Bridge počúva na", config["host"], flush=True)
        if stop is None and os.name != "nt":
            server.serve_forever()
            return
        server.daemon_threads = stop is None
        server.timeout = 1
        while stop is None or not stop.is_set():
            report("running", listeners=[config["host"]], last_contact=bridge.last_contact)
            server.handle_request()


def run_bridge(config, stop):
    if os.name != "nt":
        serve_configured_bridge(config, stop, lambda *args, **kwargs: None)
        return
    import msvcrt

    # The first byte is an OS lock. Windows releases it when the process exits.
    with (STATE / "bridge-runtime.lock").open("a+b") as lock:
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            sys.exit("A Pala Bridge is already running for this profile.")

        def report(state, *, listeners=(), last_contact=None, error=None):
            data = dict(Pid=os.getpid(), Owner=os.environ.get("PALA_BRIDGE_MANAGED_OWNER"),
                        State=state, UpdatedAt=dt.datetime.now(dt.timezone.utc).isoformat(),
                        Listeners=listeners, LastContact=last_contact, Error=error)
            temporary = STATE / "bridge-runtime.tmp"
            temporary.write_text(json.dumps(data), encoding="utf-8")
            try:
                os.replace(temporary, STATE / "bridge-runtime.json")
            except PermissionError:
                # A Windows reader can block replacement briefly. The next tick retries.
                temporary.unlink(missing_ok=True)

        report("starting")
        try:
            serve_configured_bridge(config, stop, report)
        except KeyboardInterrupt:
            report("stopped")
        except Exception as error:
            report("error", error=f"Bridge startup or listener failed ({type(error).__name__}).")
            raise
        else:
            report("stopped")


if __name__ == "__main__":
    os.umask(0o077)
    if sys.argv[1:] == ["--codex-models", "--managed"]:
        sys.stdout.reconfigure(encoding="utf-8")
        print(json.dumps(codex_model_catalog(), ensure_ascii=False))
        sys.exit(0)
    if len(sys.argv) == 4 and sys.argv[1] == "--note-transcripts" and sys.argv[3] == "--managed":
        from pala_google import DeliveryQueue

        sys.stdout.reconfigure(encoding="utf-8")
        print(json.dumps(DeliveryQueue(STATE).note_transcripts(int(sys.argv[2])), ensure_ascii=False))
        sys.exit(0)
    config = json.loads((STATE / "config.json").read_text())
    if sys.argv[1:] == ["--ms-login"]:
        from microsoft_store import credentials
        credentials(STATE, interactive=True)
        sys.exit(0)
    if sys.argv[1:] == ["--google-login"]:
        from google_store import google_credentials
        google_credentials(STATE, interactive=True)
        sys.exit(0)
    if sys.argv[1:] == ["--tasks-login"]:
        from google_store import google_credentials
        from google_tasks import TASKS_SCOPE
        google_credentials(STATE, interactive=True, scopes=[TASKS_SCOPE], token_name="google-tasks-token.json")
        sys.exit(0)
    if sys.argv[1:] == ["--review"]:
        bridge = Bridge(STATE, config)
        with bridge.database() as db:
            proposals = list(db.execute("SELECT id,data FROM actions WHERE status='pending'"))
        for ident, data in proposals:
            a = json.loads(data)
            print(a["due"], "–", a["end"], a["title"], "predstih:", a["minutes"])
            reply = input("Uložiť? [a/n/Enter = neskôr] ").strip().lower()
            if reply in ("a", "n"):
                bridge.confirm({"id": ident, "accept": reply == "a"})
        sys.exit(0)
    stop = None
    if sys.argv[1:] == ["--managed"]:
        if os.name == "nt":
            # Windows native imports can block once the shutdown thread reads the control pipe.
            # Load the optional runtime first so a model cannot lock up HTTPS requests.
            with contextlib.suppress(ImportError, OSError, RuntimeError):
                import_module("sentence_transformers")
        stop = managed_stop_event(sys.stdin.buffer)
    try:
        run_bridge(config, stop)
    except ModuleNotFoundError as error:
        if error.name not in ("ifaddr", "zeroconf"):
            raise
        sys.exit("Install the mDNS dependencies from mac-bridge/requirements.txt, then start Bridge again.")
