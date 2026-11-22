"""Loopback-only settings for the Local Dictation-owned Note watcher."""
import contextlib
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from holidays import DEFAULT_HOLIDAY_COUNTRY, HOLIDAY_COUNTRIES
from weather import _location_parts


PAGE = Path(__file__).resolve().parents[1] / "pala_note" / "src" / "app" / "note_settings_page.inc"
API = "/api/note-settings"
NOTE_SESSION_HEADER = "X-Pala-Note-Session"


def valid_note_session(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{48}", value) is not None
PREFERENCES = "note-bridge-preferences.json"
PROCESSING = "bridge-processing.json"
DISCOVERY = "note-settings-runtime.json"
MAX_BODY = 2_048
NO_CACHE = "no-store"


def read_object(path):
    try:
        result = json.loads(path.read_text(encoding="utf-8-sig"))
    except FileNotFoundError:
        return {}
    if not isinstance(result, dict):
        raise ValueError("Expected a local settings object.")
    return result


def atomic_write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    scratch = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, mode="w", encoding="utf-8", delete=False) as stream:
            scratch = Path(stream.name)
            json.dump(value, stream, ensure_ascii=False)
        os.replace(scratch, path)
    finally:
        if scratch is not None:
            scratch.unlink(missing_ok=True)


def processing_config(state, config):
    """Apply the per-user mode and routing preferences to the pairing profile."""
    settings = read_object(state / PROCESSING)
    legacy_mode = "kubo" if config.get("destination") in ("google", "apple") or config.get("google_routes") else "transcribe"
    mode = settings.get("mode", config.get("processing_mode", legacy_mode))
    if mode not in ("transcribe", "kubo"):
        raise ValueError("Invalid Bridge processing mode.")
    bindings = dict(config.get("google_routes", {}))
    if "general" in settings:
        general = settings["general"]
        if general is None:
            bindings.pop("general", None)
        else:
            bindings["general"] = general
    configured_google = bool(settings.get("general") or settings.get("calendar"))
    return {**config, "processing_mode": mode, "google_routes": bindings,
            "destination": "google" if configured_google else config.get("destination"),
            "personal_note_collection": settings.get("personalNoteCollection", config.get("personal_note_collection", False)),
            "calendar": settings.get("calendar", config.get("calendar", ""))}


def processing_preferences(state):
    config = processing_config(state, read_object(state / "config.json"))
    general = config["google_routes"].get("general", {})
    return {"mode": config["processing_mode"], "taskList": general.get("title", ""),
            "calendarId": config.get("calendar", ""),
            "tasksLoginConfigured": (state / "google-tasks-token.json").is_file(),
            "calendarLoginConfigured": (state / "google-token.json").is_file()}


def save_processing_preferences(state, mode, task_list, calendar_id):
    if mode not in ("transcribe", "kubo"):
        raise ValueError("Select a Bridge processing mode.")
    path = state / PROCESSING
    settings = read_object(path)
    if mode == "kubo":
        if not isinstance(task_list, str) or len(task_list.strip()) > 120 or any(not c.isprintable() for c in task_list):
            raise ValueError("Enter the exact name of an existing Google Tasks list.")
        if not isinstance(calendar_id, str) or re.fullmatch(r"[^\s\x00-\x1f]{0,240}", calendar_id.strip()) is None:
            raise ValueError("Enter a calendar ID or use primary for your personal calendar.")
        before = processing_config(state, read_object(state / "config.json"))
        target = before["google_routes"].get("general")
        task_list = task_list.strip()
        if task_list and (not target or target.get("title") != task_list):
            from pala_google import bound_task_list

            try:
                target = bound_task_list(state, task_list)
            except RuntimeError as error:
                raise ValueError("Connect Google Tasks and enter one existing list name.") from error
        settings.update(general=target if task_list else None, calendar=calendar_id.strip())
    settings["mode"] = mode
    atomic_write(path, settings)
    return processing_preferences(state)


def bridge_preferences(state, legacy):
    value = read_object(state / PREFERENCES)
    return {"name": value.get("name", legacy.get("PalaBridgeName") or "Local Dictation"),
            "weatherLocation": value.get("weatherLocation", legacy.get("PalaWeatherLocation") or ""),
            "holidayCountry": value.get("holidayCountry", DEFAULT_HOLIDAY_COUNTRY)}


def save_bridge_preferences(state, name, weather_location, holiday_country=None):
    if not isinstance(name, str):
        raise ValueError("Enter a Bridge name.")
    name = name.strip()
    if not name or len(name.encode("utf-8")) > 24 or any(not char.isprintable() for char in name):
        raise ValueError("Enter a Bridge name of up to 24 bytes.")
    town, country = _location_parts(weather_location)
    location = f"{town}, {country}" if country else town
    if holiday_country is None:
        holiday_country = bridge_preferences(state, {})["holidayCountry"]
    if holiday_country not in HOLIDAY_COUNTRIES:
        raise ValueError("Select a supported holiday country.")
    value = {"name": name, "weatherLocation": location, "holidayCountry": holiday_country}
    atomic_write(state / PREFERENCES, value)
    return value


def settings_page(path=PAGE):
    source = path.read_bytes().replace(b"\r\n", b"\n").strip()
    start, end = b'R"PALAHTML(', b')PALAHTML"'
    if not source.startswith(start) or not source.endswith(end):
        raise ValueError("Shared Note settings page has an unexpected format.")
    return source[len(start):-len(end)]


class SettingsHTTPServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, route, page):
        super().__init__(("127.0.0.1", 0), SettingsHandler)
        self.host = f"127.0.0.1:{self.server_port}"
        self.origin = f"http://{self.host}"
        self.nonces = {}
        self.nonce_lock = threading.Lock()
        self.target_key = secrets.token_bytes(32)
        self.route = route
        self.page = page

    def issue_nonce(self, device):
        with self.nonce_lock:
            now = time.monotonic()
            self.nonces = {key: value for key, value in self.nonces.items() if value[0] > now}
            if len(self.nonces) >= 64:
                self.nonces.pop(next(iter(self.nonces)))
            token = secrets.token_urlsafe(32)
            self.nonces[token] = (now + 900, device)
            return token

    def check_nonce(self, value):
        with self.nonce_lock:
            for token, (expires, device) in self.nonces.items():
                if hmac.compare_digest(token, value) and expires > time.monotonic():
                    return True, device
        return False, None


class SettingsHandler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        # Provisioning input and query strings must not reach an access log.
        pass

    def setup(self):
        super().setup()
        self.connection.settimeout(3)

    def _header(self, name, value):
        return self.headers.get_all(name, []) == [value]

    def _note_session(self, data):
        data.pop("session", None)
        tokens = self.headers.get_all(NOTE_SESSION_HEADER, [])
        if len(tokens) == 1 and valid_note_session(tokens[0]):
            data["session"] = tokens[0]
        return data

    def _same_site(self):
        origins = self.headers.get_all("Origin", [])
        return (self.client_address[0] == "127.0.0.1" and self._header("Host", self.server.host)
                and origins in ([], [self.server.origin]))

    def _send(self, status, payload, content_type):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", NO_CACHE)
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Content-Security-Policy",
                         "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; "
                         "img-src 'self' data:; connect-src 'self'; base-uri 'none'; "
                         "frame-ancestors 'none'; form-action 'none'")
        self.end_headers()
        with contextlib.suppress(BrokenPipeError, ConnectionResetError, ConnectionAbortedError, TimeoutError):
            self.wfile.write(payload)

    def _json(self, status, result):
        self._send(status, json.dumps(result, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8")

    def _error(self, status, message):
        self._json(status, {"ok": False, "error": message})

    def do_GET(self):
        if not self._same_site():
            self._error(403, "Open Note settings from Local Dictation.")
        elif self.path in ("/settings", "/note-settings"):
            self._send(200, self.server.page, "text/html; charset=utf-8")
        elif self.path == API:
            if self._header("X-Pala-Settings", "1"):
                self._dispatch(self._note_session({"op": "get"}), include_csrf=True)
            else:
                self._error(403, "Open Note settings from Local Dictation.")
        else:
            self._error(404, "Unknown settings page.")

    def _reject_post(self):
        # Closing a Windows TCP socket with unread request bytes can reset the reply.
        sizes = self.headers.get_all("Content-Length", [])
        if (len(sizes) == 1 and re.fullmatch(r"[1-9][0-9]{0,3}", sizes[0])
                and int(sizes[0]) <= MAX_BODY and not self.headers.get_all("Transfer-Encoding")):
            with contextlib.suppress(OSError):
                self.rfile.read(int(sizes[0]))
        self._error(403, "Refresh the Note settings page.")

    def do_POST(self):
        csrf = self.headers.get_all("X-Pala-CSRF", [])
        if (not self._same_site() or not self._header("Origin", self.server.origin)
                or not self._header("X-Pala-Settings", "1") or len(csrf) != 1
                or re.fullmatch(r"[A-Za-z0-9_-]{43}", csrf[0]) is None):
            self._reject_post()
            return
        authorized, device = self.server.check_nonce(csrf[0])
        if not authorized:
            self._reject_post()
            return
        if self.path != API:
            self._error(404, "Unknown settings operation.")
            return
        sizes = self.headers.get_all("Content-Length", [])
        types = self.headers.get_all("Content-Type", [])
        if (len(sizes) != 1 or re.fullmatch(r"[1-9][0-9]{0,3}", sizes[0]) is None
                or int(sizes[0]) > MAX_BODY or self.headers.get_all("Transfer-Encoding")
                or len(types) != 1 or types[0].split(";", 1)[0].strip().lower() != "application/json"):
            self._error(400, "Invalid settings request.")
            return
        try:
            raw = self.rfile.read(int(sizes[0]))
            if len(raw) != int(sizes[0]):
                raise ValueError
            data = json.loads(raw)
            if not isinstance(data, dict) or not isinstance(data.get("op"), str):
                raise ValueError
        except (ValueError, UnicodeError, OSError, RecursionError):
            self._error(400, "Invalid settings request.")
            return
        data["_target"] = device
        self._dispatch(self._note_session(data))

    def _dispatch(self, data, *, include_csrf=False):
        try:
            result = self.server.route(data)
        except SettingsError as error:
            result = {"ok": False, "error": str(error)}
        except (OSError, ValueError, RuntimeError):
            result = {"ok": False, "error": "Note settings are temporarily unavailable."}
        public = dict(result)
        device = public.pop("_selectedDevice", None)
        if include_csrf:
            public["csrf"] = self.server.issue_nonce(device)
            if isinstance(device, str) and device:
                public["targetKey"] = hmac.digest(self.server.target_key, device.encode("utf-8"), "sha256").hex()
        self._json(200, public)


class SettingsError(RuntimeError):
    """A settings error safe to return to the local browser."""


@contextlib.contextmanager
def serve_settings(state, owner, route):
    """Run one browser endpoint for the life of its managed watcher."""
    if re.fullmatch(r"[0-9a-f]{32}", owner or "") is None:
        raise ValueError("The managed settings owner is invalid.")
    path = state / DISCOVERY
    with SettingsHTTPServer(route, settings_page()) as server:
        worker = threading.Thread(target=server.serve_forever, name="pala-settings", daemon=True)
        worker.start()
        try:
            atomic_write(path, {"Owner": owner, "Pid": os.getpid(), "Url": server.origin + "/settings"})
            yield
        finally:
            server.shutdown()
            worker.join()
            try:
                if read_object(path).get("Owner") == owner:
                    path.unlink(missing_ok=True)
            except (OSError, ValueError):
                pass
