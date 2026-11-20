"""Personal HTTPS bridge for Pala Note 2.0, using a signed-in Codex CLI."""
import contextlib
import datetime as dt
import hashlib
import hmac
import http.client
import io
import json
import os
from pathlib import Path
import re
import shutil
import sys
import sqlite3
import ssl
import subprocess
import tempfile
import threading
import uuid
import wave
from http.server import BaseHTTPRequestHandler
from urllib.parse import urlsplit

from zoneinfo import ZoneInfo

from appointment_dates import recording_time, resolve_yearless, valid_future_start
from bridge_activity import BridgeActivity, activity_step
from bridge_discovery import authorized_device, BridgeServer, serve
from pair import STATE, WINDOWS_DICTATION_MODEL
from weather import ForecastCache

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


def valid_note_name(name):
    return isinstance(name, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9 -]{0,9}", name) is not None and name == name.rstrip()


def local_dictation_note_name(settings):
    name = settings.get("PalaBridgeName")
    return name if valid_note_name(name) else "Kubo"


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
    value = settings.get("Endpoint", LOCAL_DICTATION_DEFAULT_ENDPOINT)
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
        raise RuntimeError("Nainštaluj Codex CLI a prihlás sa cez codex login.")
    return [shutil.which("codex") or "/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex"]


def require_chatgpt_login(cli):
    status = subprocess.run(cli + ["login", "status"], capture_output=True,
                            stdin=subprocess.DEVNULL, timeout=10)
    if status.returncode or b"chatgpt" not in (status.stdout + status.stderr).lower():
        raise RuntimeError("Prihlás Codex CLI cez ChatGPT (codex login).")


def codex(prompt, schema):
    cli = codex_command()
    require_chatgpt_login(cli)
    with tempfile.TemporaryDirectory() as work:
        out = Path(work) / "result"
        args = cli + ["exec", "--ephemeral", "--ignore-user-config",
                "--skip-git-repo-check", "--sandbox", "read-only", "--disable", "shell_tool", "--disable", "apps",
                "-c", 'web_search="disabled"', "-c", 'model_reasoning_effort="low"', "-o", str(out)]
        if schema:
            spec = Path(work) / "schema.json"
            spec.write_bytes(encode(schema))
            args += ["--output-schema", str(spec)]
        env = {k: v for k, v in os.environ.items() if k not in ("OPENAI_API_KEY", "CODEX_API_KEY", "OPENAI_BASE_URL")}
        subprocess.run(args + ["-"], input=prompt.encode(), cwd=work, env=env,
                       stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, check=True, timeout=120)
        return out.read_text().strip()


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


class Bridge:
    def __init__(self, state, config):
        self.state = state
        self.config = config
        self.lock = threading.Lock()
        self.encoder = None
        self.last_contact = None
        self.activity = BridgeActivity(state, "network")
        self.weather = ForecastCache()
        with self.database() as db:
            db.execute("CREATE TABLE IF NOT EXISTS memories (id TEXT PRIMARY KEY, data TEXT)")
            db.execute("CREATE TABLE IF NOT EXISTS actions (id TEXT PRIMARY KEY, data TEXT, status TEXT)")

    @contextlib.contextmanager
    def database(self):
        with contextlib.closing(sqlite3.connect(self.state / "bridge.sqlite")) as db, db:
            yield db

    def deliver(self, action, key):
        if self.config.get("destination") == "google":
            if action["kind"] in ("shopping", "arrival", "keep"):
                return  # The Samsung downloads these from the private shopping feed.
            if action["kind"] in ("work", "work_note", "work_event"):
                from microsoft_store import deliver
                deliver(self.state, self.config, action, key)
                return
            from google_store import apply_google
            with self.activity.step("google"):
                apply_google(self.config, self.state, action, key)
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

    def memories(self, doc, schema):
        key = digest([doc["pala_note"], doc["pala_created"], doc["pala_transcript"]])
        with self.database() as db:
            row = db.execute("SELECT data FROM memories WHERE id=?", (key,)).fetchone()
            if not row:
                action_schema = json.loads(json.dumps(ACTION_SCHEMA))
                zone = ZoneInfo(self.config.get("timezone", "Europe/Bratislava"))
                reference = recording_time(doc.get("pala_created"), zone)
                action_schema["properties"]["yearless"] = YEARLESS
                action_schema["required"].append("yearless")
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
                if self.config.get("destination") == "google":
                    action_schema["properties"]["kind"]["enum"] += ["work_event", "work_note", "keep"]
                    prompt += (" Use work_event only for explicitly work-related appointments, otherwise event. "
                               "Use work_note for an explicitly requested OneNote entry only, with its text in title. "
                               "Use keep for an explicitly dictated Google Keep note, preserving its contents in title. "
                               "Work actions go to the organization's Planner, so include only explicit work commitments.")
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
                result = json.loads(codex(doc["instructions"] + prompt + "\n" + doc["input"], schema))
                actions = result.pop("apple")
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
                    if not resolve_action_yearless(a, reference):
                        # Preserve the proposal locally until the device can collect a date.
                        db.execute("INSERT INTO actions VALUES(?,?,?)",
                                   (key + "-" + str(i), json.dumps(a), "needs-date"))
                        continue
                    if a["kind"] == "apple_note" and not (self.config.get("destination") == "apple" and
                            isinstance(a.get("body"), str) and a["body"].strip()):
                        raise ValueError("Neplatný obsah poznámky")
                    validate_action(a)
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
        for ident, data, status in rows:
            action = json.loads(data)
            if status == "auto":
                self.deliver(action, "arrival" if action["kind"] == "arrival" else ident)
                self.mark(ident, "done")
            elif status == "pending":
                pending.append(dict(id=ident, **action))
        return json.dumps(result, ensure_ascii=False), pending

    def mark(self, ident, status):
        with self.database() as db:
            db.execute("UPDATE actions SET status=? WHERE id=?", (status, ident))

    def confirm(self, doc):
        with self.database() as db:
            row = db.execute("SELECT data,status FROM actions WHERE id=?", (doc["id"],)).fetchone()
        if not row or json.loads(row[0])["kind"] not in ("event", "work_event") or type(doc["accept"]) != bool:
            raise ValueError("Neplatné potvrdenie")
        if row[1] == "needs-date":
            raise ValueError("Termín potrebuje konkrétny dátum.")
        if row[1] == "pending":
            if doc["accept"]:
                action = json.loads(row[0])
                if self.config.get("destination") == "google":
                    zone = ZoneInfo(self.config.get("timezone", "Europe/Bratislava"))
                    if not valid_future_start(action["due"], zone, dt.datetime.now(dt.timezone.utc)):
                        self.mark(doc["id"], "needs-date")
                        raise ValueError("Termín uplynul alebo potrebuje nový dátum.")
                self.deliver(action, doc["id"])
            self.mark(doc["id"], "done" if doc["accept"] else "declined")
        return {"ok": True}

    def transcribe(self, body, content_type):
        # The firmware uploads exactly two multipart fields and a PCM WAV.
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

    def local_transcription_ready(self):
        if os.name != "nt":
            return False
        try:
            host, port = local_dictation_address()
            with contextlib.closing(http.client.HTTPConnection(host, port, timeout=2)) as connection:
                return local_dictation_health(connection).get("busy", False) is False
        except (OSError, ValueError, http.client.HTTPException):
            return False

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
        connection = http.client.HTTPConnection(host, port, timeout=100)
        try:
            local_dictation_health(connection)

            connection.request("POST", "/v1/audio/transcriptions", b"".join(parts), {
                "Content-Type": f"multipart/form-data; boundary={boundary}",
            })
            response = connection.getresponse()
            if response.status != 200:
                raise OSError(f"Local Dictation returned HTTP {response.status}")
            result = json.loads(response.read())
            return {"text": result["text"].strip()}
        finally:
            connection.close()

    def dispatch(self, route, body, content_type):
        if route == "/v1/audio/transcriptions":
            return self.transcribe(body, content_type)
        doc = json.loads(body)
        with self.lock:
            if route == "/pala/event":
                return self.confirm(doc)
            if route == "/v1/responses":
                fmt = doc.get("text", {}).get("format", {})
                if fmt.get("name") == "pala_memory":
                    text, pending = self.memories(doc, fmt["schema"])
                else:
                    text = codex(doc["instructions"] + "\n" + doc["input"], fmt.get("schema"))
                    pending = []
                return {"status": "completed", "output": [{"content": [{"type": "output_text", "text": text}]}],
                        "pala_pending": pending}
            if route == "/v1/embeddings":
                if self.encoder is None:
                    from sentence_transformers import SentenceTransformer
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
        if self.path in ("/pala/ready", "/pala/weather", "/pala/name"):
            if not authorized_device(bridge.config, self.headers.get("Authorization", "")):
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
            else:
                ready = bridge.local_transcription_ready()
                self.send_json(200 if ready else 503, {"ready": ready})
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
        if not authorized_device(bridge.config, self.headers.get("Authorization", "")):
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
                result = bridge.dispatch(self.path, data, self.headers.get("Content-Type", ""))
            binary = isinstance(result, bytes)
            payload = result if binary else encode(result)
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream" if binary else "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
        except (BrokenPipeError, ConnectionResetError):
            pass
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
    bridge = Bridge(STATE, config)
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
    config = json.loads((STATE / "config.json").read_text())
    if sys.argv[1:] == ["--ms-login"]:
        from microsoft_store import credentials
        credentials(STATE, interactive=True)
        sys.exit(0)
    if sys.argv[1:] == ["--google-login"]:
        from google_store import google_credentials
        google_credentials(STATE, interactive=True)
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
    stop = managed_stop_event(sys.stdin.buffer) if sys.argv[1:] == ["--managed"] else None
    try:
        run_bridge(config, stop)
    except ModuleNotFoundError as error:
        if error.name not in ("ifaddr", "zeroconf"):
            raise
        sys.exit("Install the mDNS dependencies from mac-bridge/requirements.txt, then start Bridge again.")
