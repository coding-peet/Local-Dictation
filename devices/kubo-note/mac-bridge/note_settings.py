"""Note configuration protocol and private enrollment storage."""
from dataclasses import dataclass, field
import json
import re
import secrets
import threading
import time

from note_web import (SettingsError, atomic_write, bridge_preferences, read_object,
                      save_bridge_preferences, valid_note_session)

PREFIX = b"PALA_CONFIG_V1 "
PEER_ID = r"[A-Za-z0-9_-]{1,32}"
SERIAL = r"[0-9a-f]{12}"
TOKEN = r"[A-Za-z0-9_-]{16,128}"
PAIRINGS = "note-bridge-pairing.json"
MAX_LINE = 4096
NOTE_LANGUAGES = frozenset(("en", "de", "sk", "cs", "fr", "es", "it", "pl", "nl", "pt", "ru", "uk"))
NOTE_NUMBER_MAX = 2_147_483_647
TRASH_DAYS_MAX = 3650
IDLE_SLEEP_MINUTES_MAX = 99
CAPTURED_UTC = r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z"


def encode_request(data):
    return PREFIX + json.dumps(data, ensure_ascii=True, separators=(",", ":")).encode("ascii") + b"\n"


def decode_response(line):
    try:
        if not line.startswith(PREFIX) or not line.endswith(b"\n") or len(line) > MAX_LINE:
            raise ValueError
        result = json.loads(line[len(PREFIX):])
        if not isinstance(result, dict) or type(result.get("ok")) is not bool:
            raise ValueError
        return result
    except (ValueError, RecursionError) as error:
        raise SettingsError("The Note returned an invalid settings reply.") from error


def valid(value, pattern):
    return isinstance(value, str) and re.fullmatch(pattern, value) is not None


def text(value, limit, *, empty=False):
    if (not isinstance(value, str) or (not value and not empty) or
            any(not char.isprintable() for char in value) or len(value.encode("utf-8")) > limit):
        raise SettingsError("A Note settings field has an invalid value.")
    return value


def bounded_integer(value, minimum, maximum):
    return type(value) is int and minimum <= value <= maximum


def valid_display_pages(value):
    if not isinstance(value, str) or re.fullmatch(r"[TtEeCcNnMm]{4,5}", value) is None:
        return False
    expected = set("TECN" if len(value) == 4 else "TECNM")
    return set(value.upper()) == expected


def note_password(value):
    if not valid(value, r"[ -~]{8,48}"):
        raise SettingsError("Use 8 to 48 printable ASCII characters for the password.")
    return value


def note_request(data):
    op = data["op"]
    if op == "auth.login":
        return {"op": op, "password": note_password(data.get("password"))}
    if op == "auth.save":
        password = note_password(data.get("password"))
        repeat = note_password(data.get("repeat"))
        if repeat != password:
            raise SettingsError("Enter the new password twice.")
        old = data.get("old")
        return {"op": op, "old": old if valid(old, r"[ -~]{0,48}") else "",
                "password": password, "repeat": repeat}
    if op == "auth.policy" and type(data.get("required")) is bool:
        return {"op": op, "required": data["required"]}
    if op in ("get", "trash.status", "auth.logout", "restart.status"):
        return {"op": op}
    if op == "restart.request" and data.get("mode") in ("restart", "audit"):
        return {"op": op, "mode": data["mode"]}
    if op == "trash.entry" and bounded_integer(data.get("index"), 0, NOTE_NUMBER_MAX):
        return {"op": op, "index": data["index"]}
    if op == "trash.restore" and bounded_integer(data.get("num"), 1, NOTE_NUMBER_MAX):
        return {"op": op, "num": data["num"]}
    if op == "trash.retention" and bounded_integer(data.get("days"), 1, TRASH_DAYS_MAX):
        return {"op": op, "days": data["days"]}
    if op == "trash.empty" and data.get("confirmation") == "EMPTY TRASH":
        return {"op": op, "confirmation": "EMPTY TRASH"}
    if op in ("wifi.save", "wifi.remove"):
        result = {"op": op, "ssid": text(data.get("ssid"), 32)}
        if op == "wifi.save":
            result["password"] = text(data.get("password"), 63, empty=True)
        return result
    if op == "peers.remove" and valid(data.get("id"), PEER_ID):
        return {"op": op, "id": data["id"]}
    if op == "bridge.address":
        return {"op": op, "address": text(data.get("address"), 15, empty=True)}
    if op == "sleep.save" and bounded_integer(data.get("minutes"), 0, IDLE_SLEEP_MINUTES_MAX):
        return {"op": op, "minutes": data["minutes"]}
    if op == "sync.save" and bounded_integer(data.get("mode"), 0, 3):
        return {"op": op, "mode": data["mode"]}
    if op == "pages.save" and valid_display_pages(data.get("pages")):
        request = {"op": op, "pages": data["pages"]}
        if "wrap" in data:
            if type(data["wrap"]) is not bool:
                raise SettingsError("Invalid display page settings.")
            request["wrap"] = data["wrap"]
        return request
    if op == "wireless.save" and type(data.get("preferBluetooth")) is bool:
        return {"op": op, "preferBluetooth": data["preferBluetooth"]}
    if op == "radios.save":
        wifi, bluetooth = data.get("wifiEnabled"), data.get("bluetoothEnabled")
        if type(wifi) is bool and type(bluetooth) is bool:
            return {"op": op, "wifiEnabled": wifi, "bluetoothEnabled": bluetooth}
    if op == "bridge.order":
        ids = data.get("ids")
        if (isinstance(ids, list) and len(ids) <= 8 and all(valid(value, PEER_ID) for value in ids)
                and len(set(ids)) == len(ids)):
            return {"op": op, "ids": ids}
    if (op == "settings.save" and valid(data.get("name"), r"[A-Za-z0-9][A-Za-z0-9 -]{0,11}")
            and isinstance(data.get("language"), str) and data["language"] in NOTE_LANGUAGES):
        request = {"op": op, "name": data["name"].rstrip(), "language": data["language"]}
        if "weeklyCodexLimit" in data:
            if type(data["weeklyCodexLimit"]) is not bool:
                raise SettingsError("Invalid Codex limit selection.")
            request["weeklyCodexLimit"] = data["weeklyCodexLimit"]
        return request
    raise SettingsError("The requested Note setting is invalid or unsupported.")


def trash_response(reply, op):
    if op == "trash.status":
        days, count = reply.get("days"), reply.get("count")
        if bounded_integer(days, 1, TRASH_DAYS_MAX) and bounded_integer(count, 0, NOTE_NUMBER_MAX):
            return {"days": days, "count": count}
    elif op == "trash.entry":
        entry = reply.get("entry")
        if isinstance(entry, dict):
            num, trashed_at = entry.get("num"), entry.get("trashedAt")
            captured = entry.get("capturedUtc")
            if (bounded_integer(num, 1, NOTE_NUMBER_MAX)
                    and bounded_integer(trashed_at, 0, 253_402_300_799)
                    and (captured == "" or valid(captured, CAPTURED_UTC))):
                return {"entry": {"num": num, "tag": text(entry.get("tag"), 15, empty=True),
                                  "trashedAt": trashed_at, "capturedUtc": captured}}
    elif op == "trash.empty" and type(reply.get("more")) is bool:
        return {"more": reply["more"]}
    elif op in ("trash.restore", "trash.retention"):
        return {}
    raise SettingsError("The Note returned an invalid Trash reply.")


def note_response(reply, transport, op="get"):
    if not reply["ok"]:
        access = reply.get("access")
        if transport == "BLE" and access in ("locked", "setup"):
            return {"ok": False, "transport": transport, "access": access,
                    "error": "Enter the Note password." if access == "locked"
                    else "Set the Note password over USB."}
        error = reply.get("error")
        if op.startswith("auth.") and error in (
                "Incorrect password.", "Wait before trying again.", "Could not save the Note password."):
            return {"ok": False, "error": error, "transport": transport}
        return {"ok": False, "error": "The Note rejected the settings request.", "transport": transport}
    result = {"ok": True, "transport": transport}
    if op == "auth.login":
        if transport != "BLE" or not valid_note_session(reply.get("session")):
            raise SettingsError("The Note returned an invalid sign-in session.")
        return {**result, "session": reply["session"]}
    if op.startswith("trash."):
        return {**result, **trash_response(reply, op)}
    if op == "restart.request":
        if reply.get("pending") is not True:
            raise SettingsError("The Note did not open a restart confirmation.")
        return {**result, "pending": True}
    if op == "restart.status":
        report = reply.get("storageAudit")
        if (not isinstance(report, dict)
                or report.get("state") not in ("never", "running", "incomplete", "complete")
                or not bounded_integer(report.get("notes"), 0, NOTE_NUMBER_MAX)):
            raise SettingsError("The Note returned an invalid storage check report.")
        status = {"state": report["state"], "notes": report["notes"]}
        for field in ("estimateSeconds", "checked", "wavIssues", "metadataIssues", "catalogIssues",
                      "queueIssues", "firstNote", "repaired"):
            if bounded_integer(report.get(field), 0, 2**32 - 1):
                status[field] = report[field]
        return {**result, "storageAudit": status}
    if "device" not in reply:
        return result
    device, wifi, peers = reply["device"], reply.get("wifi", []), reply.get("peers", [])
    if not isinstance(device, dict) or not isinstance(wifi, list) or not isinstance(peers, list):
        raise SettingsError("The Note returned an invalid settings reply.")
    result["device"] = {"name": text(device.get("name"), 24), "language": text(device.get("language"), 8)}
    if type(device.get("memoryTrash")) is bool:
        result["device"]["memoryTrash"] = device["memoryTrash"]
    if type(device.get("r")) is int and device["r"] == 1:
        result["device"]["r"] = 1
    # Earlier Recorder builds identify themselves through their disabled local Trash.
    recorder = type(device.get("rf")) is int and device["rf"] == 1
    if recorder or (result["device"].get("r") == 1 and device.get("memoryTrash") is False):
        result["device"]["recorderFirst"] = True
    if type(device.get("pa")) is bool:
        result["device"]["webPasswordRequired"] = device["pa"]
    if type(device.get("webPasswordConfigured")) is bool:
        result["device"]["webPasswordConfigured"] = device["webPasswordConfigured"]
    if device.get("keepsWifiPassword") is True:
        result["device"]["keepsWifiPassword"] = True
    if bounded_integer(device.get("sleepMinutes"), 0, IDLE_SLEEP_MINUTES_MAX):
        result["device"]["sleepMinutes"] = device["sleepMinutes"]
    if bounded_integer(device.get("syncTiming"), 0, 3):
        result["device"]["syncTiming"] = device["syncTiming"]
    for field in ("pageWrap", "preferBluetooth", "weeklyCodexLimit", "wifiEnabled", "bluetoothEnabled"):
        if type(device.get(field)) is bool:
            result["device"][field] = device[field]
    if valid_display_pages(device.get("displayPages")):
        result["device"]["displayPages"] = device["displayPages"]
    if bounded_integer(device.get("batteryPercent"), 0, 100):
        result["device"]["batteryPercent"] = device["batteryPercent"]
    for field in ("ramUsage", "psramUsage"):
        values = device.get(field)
        if (isinstance(values, list) and len(values) == 2
                and all(bounded_integer(value, 0, 100) for value in values)
                and values[0] <= values[1]):
            result["device"][field] = values
    if "bridgeAddress" in device:
        result["device"]["bridgeAddress"] = text(device["bridgeAddress"], 15, empty=True)
    result["wifi"] = [{"ssid": text(item["ssid"], 32)}
                      for item in wifi if isinstance(item, dict) and "ssid" in item]
    result["peers"] = [{"id": item["id"], "name": text(item["name"], 32), "primary": item.get("primary") is True}
                       for item in peers if isinstance(item, dict) and valid(item.get("id"), PEER_ID) and "name" in item]
    result["bridgeOrder"] = [peer["id"] for peer in result["peers"]]
    result["capabilities"] = {"pair": True, "peers": True, "bridgeOrder": True}
    return result


class EnrollmentStore:
    def __init__(self, state, config):
        self.path, self.config = state / PAIRINGS, config
        self.lock = threading.Lock()

    def bridge_id(self):
        with self.lock:
            data = read_object(self.path)
            if "bridgeId" not in data:
                discovery = self.config.get("discovery_id")
                data["bridgeId"] = discovery if valid(discovery, PEER_ID) else "bridge-" + secrets.token_hex(10)
                atomic_write(self.path, data)
            if not valid(data["bridgeId"], PEER_ID):
                raise SettingsError("The local Bridge identity file is invalid.")
            return data["bridgeId"]

    def devices(self):
        source = read_object(self.path).get("devices", {})
        if not isinstance(source, dict):
            raise SettingsError("The local Bridge pairing file is invalid.")
        return {serial: record for serial, record in source.items()
                if valid(serial, SERIAL) and isinstance(record, dict) and valid(record.get("token"), TOKEN)
                and valid(record.get("advertiser"), SERIAL) and valid(record.get("id"), PEER_ID)}

    def remember(self, reply, bridge_id, *, attached_serial=None):
        serial, token, advertiser = (reply.get(key) for key in ("serial", "token", "advertiser"))
        if (reply.get("id") != bridge_id or not valid(serial, SERIAL) or not valid(token, TOKEN)
                or not valid(advertiser, SERIAL) or (attached_serial and serial != attached_serial.casefold())):
            raise SettingsError("The Note returned an invalid pairing receipt.")
        with self.lock:
            data = read_object(self.path)
            data.setdefault("devices", {})[serial] = {"token": token, "advertiser": advertiser, "id": bridge_id}
            atomic_write(self.path, data)
        return serial

    def tokens(self):
        legacy = self.config.get("device_tokens", {})
        tokens = dict(legacy) if isinstance(legacy, dict) else {}
        tokens.update({serial: record["token"] for serial, record in self.devices().items()})
        return tokens


@dataclass
class PairJob:
    transport: str
    ticket: str = field(default_factory=lambda: secrets.token_urlsafe(24))
    deadline: float = field(default_factory=lambda: time.monotonic() + 90)
    stage: str = "pending"
    code: str = ""
    error: str = ""
    decision: threading.Event = field(default_factory=threading.Event)
    accepted: bool = False
    claimed: bool = False
    lock: threading.Lock = field(default_factory=threading.Lock)

    def pending(self):
        with self.lock:
            return self.stage in ("pending", "confirm") and time.monotonic() < self.deadline

    def snapshot(self):
        with self.lock:
            if self.stage in ("pending", "confirm") and time.monotonic() >= self.deadline:
                self.stage, self.error = "failed", "Pairing timed out. Open pairing on the Note again."
                self.decision.set()
            data = {"ok": True, "ticket": self.ticket, "state": self.stage, "transport": self.transport}
            if self.stage == "confirm":
                data["code"] = self.code
            if self.error:
                data["error"] = self.error
            return data

    def confirm(self, accepted):
        with self.lock:
            if self.stage in ("pending", "confirm"):
                if not accepted:
                    self.stage = "cancelled"
                    self.decision.set()
                elif self.stage == "confirm":
                    self.accepted = True
                    self.stage, self.code = "pending", ""
                    self.decision.set()
                else:
                    raise SettingsError("Wait for the Bluetooth code before confirming its match.")

    def show_code(self, code):
        with self.lock:
            if self.stage != "pending" or time.monotonic() >= self.deadline:
                return False
            self.stage, self.code = "confirm", code
            return True

    def finish(self, error=None):
        with self.lock:
            if self.stage in ("pending", "confirm"):
                self.stage = "failed" if error else "paired"
                self.error = error or ""
            self.code = ""
            self.decision.set()


class PairingBroker:
    def __init__(self, usb_pair, ble_available):
        self.usb_pair, self.ble_available = usb_pair, ble_available
        self.job = None
        self.lock = threading.Lock()

    def route(self, data):
        with self.lock:
            job = self.job
            if data["op"] == "pair.start":
                transport = data.get("transport")
                if transport not in ("usb", "ble"):
                    raise SettingsError("Choose USB or Bluetooth for pairing.")
                if transport == "ble" and not self.ble_available:
                    raise SettingsError("Bluetooth pairing requires the Windows Bridge Bluetooth runtime.")
                if job is not None and job.pending():
                    raise SettingsError("Finish the current Note pairing before starting another.")
                job = self.job = PairJob(transport)
                if transport == "usb":
                    threading.Thread(target=self.run, args=(job, self.usb_pair),
                                     name="pala-usb-pair", daemon=True).start()
            elif not isinstance(data.get("ticket"), str) or job is None or data["ticket"] != job.ticket:
                raise SettingsError("This pairing request has expired.")
            elif data["op"] == "pair.confirm":
                if type(data.get("accepted")) is not bool:
                    raise SettingsError("Confirm or cancel the code.")
                job.confirm(data["accepted"])
            return job.snapshot()

    def claim_ble(self):
        with self.lock:
            job = self.job
            if job is not None and job.transport == "ble" and not job.claimed and job.pending():
                job.claimed = True
                return job
        return None

    def stop(self):
        with self.lock:
            if self.job is not None:
                self.job.confirm(False)

    @staticmethod
    def run(job, exchange):
        try:
            exchange(job)
        except SettingsError as error:
            job.finish(str(error))
        except (OSError, ValueError, RuntimeError):
            job.finish("Pairing could not finish. Reopen pairing on the Note and try again.")
        else:
            job.finish()


def complete_pairing(job, exchange, store, name, certificate=None, address=None, attached_serial=None):
    bridge_id = store.bridge_id()
    request = {"op": "pair.start", "id": bridge_id, "name": name}
    if certificate is not None and address is not None:
        request.update(host=address, port=8443)
    status = exchange(request)
    ticket = status.get("ticket")
    while job.pending():
        if not status["ok"]:
            raise SettingsError("The Note rejected pairing. Open its pairing menu and try again.")
        stage = status.get("state")
        if stage == "paired":
            try:
                return store.remember(status, bridge_id, attached_serial=attached_serial)
            except OSError as error:
                raise SettingsError("Windows could not save the pairing. Remove this Bridge on the Note and retry.") from error
        if not valid(ticket, r"[A-Za-z0-9_-]{8,128}"):
            raise SettingsError("The Note returned an invalid pairing ticket.")
        if stage == "approved" and certificate is not None:
            for offset in range(0, len(certificate), 240):
                if not job.pending():
                    raise SettingsError("Pairing cancelled or expired.")
                chunk = certificate[offset:offset + 240]
                saved = exchange({"op": "pair.certificate", "ticket": ticket, "offset": offset,
                                  "chunk": chunk, "final": offset + len(chunk) == len(certificate)})
                if not saved["ok"]:
                    raise SettingsError("The Note rejected the Bridge HTTPS certificate.")
            certificate = None
        elif stage == "cancelled":
            job.confirm(False)
            return None
        elif stage not in ("pending", "approved"):
            raise SettingsError("The Note did not finish pairing.")
        time.sleep(.35)
        status = exchange({"op": "pair.status", "ticket": ticket})
    raise SettingsError("Pairing cancelled or expired.")


class SettingsRoute:
    def __init__(self, state, legacy, transport, pairing):
        self.state, self.legacy, self.transport, self.pairing = state, legacy, transport, pairing

    def __call__(self, data):
        op = data["op"]
        if op in ("pair.start", "pair.status", "pair.confirm"):
            return self.pairing.route(data)
        if op == "bridge.save":
            try:
                saved = save_bridge_preferences(self.state, data.get("name"), data.get("weatherLocation"),
                                                data.get("holidayCountry"))
            except ValueError as error:
                raise SettingsError(str(error)) from error
            return {"ok": True, "bridge": saved}
        if op == "bridge.processing.save":
            from note_web import save_processing_preferences

            try:
                processing = save_processing_preferences(self.state, data.get("mode"),
                                                         data.get("taskList"), data.get("calendarId"))
            except ValueError as error:
                raise SettingsError(str(error)) from error
            return {"ok": True, "processing": processing}
        if op == "bridge.processing.check":
            from bridge import codex_command, require_chatgpt_login
            from subprocess import TimeoutExpired

            try:
                cli = codex_command()
                require_chatgpt_login(cli)
            except (OSError, RuntimeError, TimeoutExpired):
                return {"ok": False, "error": "Codex CLI is unavailable or signed out. Run codex login, then check again."}
            return {"ok": True}
        request = note_request(data)
        if valid_note_session(data.get("session")):
            request["session"] = data["session"]
        if op != "get" and "_target" in data:
            request["_target"] = data["_target"]
        try:
            reply, link = self.transport(request)
            result = note_response(reply, link, op)
            if op == "get" and (result["ok"] or "access" in result):
                result["_selectedDevice"] = reply.get("_selectedDevice")
        except SettingsError as error:
            result = {"ok": False, "error": str(error), "transport": ""}
        except (OSError, ValueError, RuntimeError):
            result = {"ok": False, "error": "The paired Note could not be reached or authenticated.", "transport": ""}
        # Keep Bridge preferences out of the pre-authentication response.
        if op == "get" and result.get("access") not in ("locked", "setup"):
            capabilities = result.setdefault("capabilities", {"pair": True, "peers": False, "bridgeOrder": False})
            if self.pairing is not None:
                capabilities["pairBle"] = self.pairing.ble_available
            from note_web import processing_preferences

            result["bridge"] = {**bridge_preferences(self.state, self.legacy()),
                                "processing": processing_preferences(self.state)}
        return result
