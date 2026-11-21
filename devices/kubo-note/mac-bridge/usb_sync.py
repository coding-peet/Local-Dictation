"""Synchronize recordings and the configured forecast with a paired Pala."""
import argparse
import contextlib
from datetime import datetime, timezone
from dataclasses import dataclass, replace
import hashlib
import hmac
import importlib.util
import io
import json
import os
import re
import secrets
import sys
import threading
import time
import wave

from bridge_activity import BridgeActivity, activity_step


PREFIX = b"KUBO_USB_V1 "
MAX_WAV_BYTES = 25_000_000
MAX_TEXT_BYTES = 32_000
MAX_FORECAST_BYTES = 1024
# Keep requests and replies within the ESP32-S3 USB CDC buffer.
READ_CHUNK = 96
WRITE_CHUNK = 96
READ_ATTEMPTS = 3
ATTACHMENT_ATTEMPTS = 3
ATTACHMENT_RETRY_SECONDS = 15
MAX_REPLY = 2200
REPLY_TYPES = {
    "HELLO": ("CHALLENGE",),
    "AUTH": ("OK",),
    "NEXT": ("NOTE", "EMPTY"),
    "SELECT": ("NOTE",),
    "META": ("META",),
    "EXPORT": ("TRANSCRIPT",),
    "TEXT": ("OK",),
    "PUT": ("OK",),
    "COMMIT": ("SAVED",),
    "NAME": ("NAME-SAVED",),
    "DELIVERY": ("DELIVERY-SAVED",),
    "WEATHER": ("OK",),
    "WEATHER-COMMIT": ("OK",),
    "WEATHER-READ": ("FORECAST",),
    "CLOSE": ("OK",),
}


FIRMWARE_ERRORS = {"AUTH", "BUSY", "COMMAND", "NOTE", "READ", "SAVE", "TEXT", "WEATHER", "NAME", "DELIVERY"}
BLOCK_COMMANDS = {"READ", "READTXT", "READWEATHER"}


@dataclass(frozen=True)
class UsbFailure:
    Category: str
    Phase: str | None = None
    Command: str | None = None
    FirmwareCode: str | None = None
    Offset: int | None = None
    Length: int | None = None
    Attempt: int | None = None
    TotalBytes: int | None = None
    Observation: str | None = None
    ObservedOffset: int | None = None
    ObservedHexLength: int | None = None
    ObservedBytes: int | None = None
    Errno: int | None = None
    WinError: int | None = None


class UsbSyncError(RuntimeError):
    def __init__(self, message, *, diagnostic=None):
        super().__init__(message)
        self.diagnostic = diagnostic


def _failure(error):
    saved = getattr(error, "diagnostic", None)
    if isinstance(saved, UsbFailure):
        return saved
    if isinstance(error, OSError):
        errno = getattr(error, "errno", None)
        winerror = getattr(error, "winerror", None)
        return UsbFailure("io", Errno=errno if type(errno) is int else None,
                          WinError=winerror if type(winerror) is int else None)
    return UsbFailure("protocol" if isinstance(error, UsbSyncError) else "invalid")


def _failure_context(error, **fields):
    error.diagnostic = replace(_failure(error), **fields)


@contextlib.contextmanager
def _usb_phase(phase):
    try:
        yield
    except (OSError, ValueError, UsbSyncError) as error:
        _failure_context(error, Phase=phase)
        raise


def report_usb_failure(state, error):
    from note_web import atomic_write

    fields = {key: value for key, value in vars(_failure(error)).items() if value is not None}
    fields.update(Pid=os.getpid(), UpdatedAt=datetime.now(timezone.utc).isoformat())
    # Keep a single latest record. Never persist messages, raw frames, or payloads.
    with contextlib.suppress(OSError):
        atomic_write(state / "usb-sync-last-error.json", fields)
    with contextlib.suppress(OSError):
        print("Pala USB failure " + json.dumps(fields, separators=(",", ":")), file=sys.stderr, flush=True)


class UsbReplyTimeout(UsbSyncError):
    pass


@dataclass(frozen=True)
class SyncedNote:
    note: int
    audio_bytes: int


class BufferedSerial:
    """Read queued serial bytes in batches and retain subsequent frames."""

    def __init__(self, port):
        self.port = port
        self.buffer = bytearray()

    def write(self, data):
        return self.port.write(data)

    def read_until(self, terminator, limit):
        deadline = time.monotonic() + 0.20
        while True:
            newline = self.buffer.find(terminator, 0, limit)
            if newline >= 0:
                end = newline + len(terminator)
                break
            if len(self.buffer) >= limit or time.monotonic() >= deadline:
                end = min(len(self.buffer), limit)
                break
            available = max(1, min(self.port.in_waiting, 4096))
            self.buffer.extend(self.port.read(available))

        reply = bytes(self.buffer[:end])
        del self.buffer[:end]
        return reply


def _proof(token, role, host, device):
    message = PREFIX + f"{role} {host} {device}".encode("ascii")
    return hmac.new(token.encode("ascii"), message, hashlib.sha256).hexdigest()


def _exchange(port, command, *, read_block=None):
    verb = command.partition(" ")[0]
    safe_verb = verb if verb in REPLY_TYPES or verb in BLOCK_COMMANDS else None
    request = PREFIX + command.encode("ascii") + b"\n"
    expected = REPLY_TYPES.get(verb, ())
    line = bytearray()
    observation = None
    observed_offset = observed_hex_length = None
    try:
        if port.write(request) != len(request):
            raise UsbSyncError("The USB command was interrupted.", diagnostic=UsbFailure("short-write"))
        deadline = time.monotonic() + (1 if read_block else 6)
        while time.monotonic() < deadline:
            chunk = port.read_until(b"\n", MAX_REPLY - len(line))
            line.extend(chunk)
            if line.endswith(b"\n"):
                if line.startswith(PREFIX):
                    try:
                        response = line[len(PREFIX):].decode("ascii").split()
                    except UnicodeDecodeError as exc:
                        raise UsbSyncError("Invalid USB reply.", diagnostic=UsbFailure("invalid-ascii")) from exc
                    if response[:1] == ["ERROR"]:
                        reason = response[1] if len(response) == 2 and response[1] in FIRMWARE_ERRORS else "UNKNOWN"
                        raise UsbSyncError(f"Pala reported {reason}.",
                                           diagnostic=UsbFailure("firmware", FirmwareCode=reason))
                    if read_block is not None:
                        if _matches_audio_block(response, *read_block):
                            return response
                        if response[:1] == ["DATA"]:
                            observation = "unmatched-data"
                            if len(response) == 3:
                                observed_offset = (int(response[1]) if re.fullmatch(r"[0-9]{1,8}", response[1])
                                                   else None)
                                observed_hex_length = len(response[2])
                            else:
                                observed_offset = observed_hex_length = None
                    elif response and response[0] in expected:
                        return response
                line.clear()
            elif len(line) == MAX_REPLY:
                raise UsbSyncError("The USB reply exceeded its frame limit.", diagnostic=UsbFailure("frame-limit"))
        if line and (line.startswith(PREFIX) or PREFIX.startswith(line)):
            observation = "partial-frame"
            observed_offset = observed_hex_length = None
        raise UsbReplyTimeout("No complete USB sync reply. Check the cable, port and firmware.",
                              diagnostic=UsbFailure("timeout", Observation=observation,
                                                    ObservedOffset=observed_offset,
                                                    ObservedHexLength=observed_hex_length,
                                                    ObservedBytes=len(line) if observation == "partial-frame" else None))
    except (OSError, UsbSyncError) as error:
        _failure_context(error, Command=safe_verb)
        raise


def _matches_audio_block(reply, offset, length):
    return (len(reply) == 3 and reply[:2] == ["DATA", str(offset)]
            and len(reply[2]) == length * 2 and re.fullmatch(r"[0-9a-f]+", reply[2]))


def _expect_ok(port, command):
    if _exchange(port, command) != ["OK"]:
        raise UsbSyncError("Pala did not acknowledge the USB command.")


def _authenticate(port, tokens):
    host = secrets.token_hex(16)
    identity = getattr(port, "peer_id", None)
    hello = f"HELLO {host}" + (f" {identity}" if identity else "")
    reply = _exchange(port, hello)
    if (len(reply) != 3 or reply[0] != "CHALLENGE"
            or not re.fullmatch(r"[0-9a-f]{32}", reply[1])
            or not re.fullmatch(r"[0-9a-f]{64}", reply[2])):
        raise UsbSyncError("Invalid challenge from Pala.")
    device, proof = reply[1:]
    token = next((key for key in tokens
                  if hmac.compare_digest(proof, _proof(key, "device", host, device))), None)
    if token is None:
        raise UsbSyncError("This Pala is not paired with this Bridge.")
    try:
        _expect_ok(port, f"AUTH {_proof(token, 'host', host, device)}")
    except UsbReplyTimeout:
        # The paired device may have accepted AUTH before its reply was lost.
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")
        raise
    return token, host, device


def _check_stop(stop):
    if stop is not None and stop.is_set():
        raise InterruptedError("Pala USB sync stopped.")


def _download_blocks(port, command, size, stop=None):
    data = bytearray()
    while len(data) < size:
        _check_stop(stop)
        offset = len(data)
        length = min(getattr(port, "read_chunk", READ_CHUNK), size - offset)
        for attempt in range(1, READ_ATTEMPTS + 1):
            try:
                reply = _exchange(port, f"{command} {offset} {length}", read_block=(offset, length))
                break
            except UsbReplyTimeout as error:
                if attempt == READ_ATTEMPTS:
                    _failure_context(error, Offset=offset, Length=length, Attempt=attempt, TotalBytes=size)
                    raise UsbSyncError(f"Pala did not return {command} data at byte {offset}.",
                                       diagnostic=_failure(error)) from error
            except (OSError, UsbSyncError) as error:
                _failure_context(error, Offset=offset, Length=length, Attempt=attempt, TotalBytes=size)
                raise
        data.extend(bytes.fromhex(reply[2]))
    return bytes(data)


def _download(port, size, stop):
    audio = _download_blocks(port, "READ", size, stop)
    try:
        with wave.open(io.BytesIO(audio), "rb") as recording:
            frame_bytes = recording.getnframes() * recording.getnchannels() * recording.getsampwidth()
            if (recording.getnchannels() != 1 or recording.getsampwidth() != 2
                    or frame_bytes == 0 or frame_bytes > len(audio) - 44
                    or len(recording.readframes(recording.getnframes())) != frame_bytes):
                raise UsbSyncError("The recording is not a complete mono PCM WAV.",
                                   diagnostic=UsbFailure("invalid-wav", TotalBytes=size))
    except (wave.Error, EOFError) as exc:
        raise UsbSyncError("The recording is not a valid WAV.",
                           diagnostic=UsbFailure("invalid-wav", TotalBytes=size)) from exc
    return audio


def _require_note(note):
    if type(note) is not int or not 0 < note <= 2_147_483_647:
        raise UsbSyncError("Choose a positive Pala note number.")


def _paired_session(port, tokens):
    usable = tuple(token for token in tokens
                   if isinstance(token, str) and re.fullmatch(r"[A-Za-z0-9_-]{16,128}", token))
    if not usable:
        raise UsbSyncError("No device pairing token is available.")
    return _authenticate(port, usable)


def config_exchange(port, data):
    from note_settings import MAX_LINE, PREFIX as CONFIG, SettingsError, decode_response, encode_request

    wire = encode_request(data)
    if len(wire) > getattr(port, "settings_frame_limit", MAX_LINE) or port.write(wire) != len(wire):
        raise SettingsError("The Note settings request could not be sent.")
    deadline = time.monotonic() + 4
    line = bytearray()
    while time.monotonic() < deadline:
        line.extend(port.read_until(b"\n", MAX_LINE - len(line)))
        if line.endswith(b"\n"):
            if line.startswith(CONFIG):
                return decode_response(line)
            line.clear()
        elif len(line) >= MAX_LINE:
            raise SettingsError("The Note settings reply exceeded its size limit.")
        if not line:
            time.sleep(.01)
    raise SettingsError("The Note did not answer. Check that its firmware supports browser settings.")


def paired_config(port, token, request):
    from note_settings import SettingsError

    _paired_session(port, (token,))
    try:
        reply = config_exchange(port, request)
        if reply["ok"] and request["op"] == "get" and "pageCounts" in reply:
            counts = reply["pageCounts"]
            if not isinstance(counts, dict):
                raise SettingsError("The Note settings page list is invalid.")
            for section in ("wifi", "peers"):
                count = counts.get(section)
                if type(count) is not int or not 0 <= count <= 16:
                    raise SettingsError("The Note settings list is too large.")
                reply[section] = []
                for index in range(count):
                    page = config_exchange(port, {"op": "get", "section": section, "index": index})
                    items = page.get(section)
                    if not page["ok"] or not isinstance(items, list) or len(items) != 1:
                        raise SettingsError("The Note settings list could not be read.")
                    reply[section].extend(items)
        return reply
    finally:
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")


def _export_transcript(port, note, stop=None):
    reply = _exchange(port, f"EXPORT {note}")
    if (len(reply) != 4 or reply[:2] != ["TRANSCRIPT", str(note)]
            or not re.fullmatch(r"[0-9]{1,5}", reply[2])):
        raise UsbSyncError("Pala returned an invalid saved-transcript header.")
    size = int(reply[2])
    if not 0 < size <= MAX_TEXT_BYTES:
        raise UsbSyncError("The stored transcript has an unsupported size.")
    captured = _captured(reply[3])
    return _download_blocks(port, "READTXT", size, stop), captured


def _confirmed_saved(port, note, data, captured, stop):
    try:
        _check_stop(stop)
        saved, saved_at = _export_transcript(port, note, stop)
    except (OSError, ValueError, UsbSyncError):
        return False
    return saved_at == captured and hmac.compare_digest(saved, data)


def read_saved(port, tokens, note):
    """Read existing text over an authenticated USB session without running ASR."""
    _require_note(note)
    _paired_session(port, tokens)
    try:
        data, captured = _export_transcript(port, note)
        return data.decode("utf-8"), captured
    finally:
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")


def transfer_note_name(port, tokens, name):
    from bridge import valid_note_name

    if not valid_note_name(name):
        raise ValueError("The Note name must use 1–10 letters, digits, spaces or hyphens.")
    payload = name.encode("ascii").hex()
    token, host, device = _paired_session(port, tokens)
    try:
        signature = _proof(token, f"name {payload}", host, device)
        response = _exchange(port, f"NAME {payload} {signature}")
        proof = _proof(token, f"name-saved {payload}", host, device)
        if len(response) != 3 or response[1] != payload or not hmac.compare_digest(response[2], proof):
            raise UsbSyncError("Pala did not verify the saved display name.")
        return name
    finally:
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")


def transfer_delivery_counts(port, tokens, payload):
    if re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2},[0-9]{1,9},[0-9]{1,9}", payload) is None:
        raise UsbSyncError("Invalid Google delivery counters.")
    token, host, device = _paired_session(port, tokens)
    try:
        signature = _proof(token, f"delivery {payload}", host, device)
        response = _exchange(port, f"DELIVERY {payload} {signature}")
        proof = _proof(token, f"delivery-saved {payload}", host, device)
        if len(response) != 3 or response[1] != payload or not hmac.compare_digest(response[2], proof):
            raise UsbSyncError("Pala did not verify the delivery counters.")
    finally:
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")


def transfer_forecast(port, tokens, forecast):
    """Send one forecast and independently verify SD bytes and today's selected values."""
    data = json.dumps(forecast, ensure_ascii=False, allow_nan=False).encode("utf-8")
    if not 0 < len(data) <= MAX_FORECAST_BYTES:
        raise UsbSyncError("The forecast exceeds the Pala cache limit.")

    _paired_session(port, tokens)
    try:
        _expect_ok(port, f"WEATHER {len(data)} {hashlib.sha256(data).hexdigest()}")
        chunk = getattr(port, "write_chunk", WRITE_CHUNK)
        for offset in range(0, len(data), chunk):
            _expect_ok(port, f"PUT {offset} {data[offset:offset+chunk].hex()}")
        with contextlib.suppress(UsbReplyTimeout):
            _expect_ok(port, "WEATHER-COMMIT")
        # Keep the session open so a display refresh cannot interrupt readback.
        reply = _exchange(port, "WEATHER-READ")
        if (len(reply) not in (3, 6) or re.fullmatch(r"[1-9][0-9]{0,3}", reply[1]) is None
                or int(reply[1]) > MAX_FORECAST_BYTES):
            raise UsbSyncError("Pala reported an unexpected weather cache size.")
        size = int(reply[1])
        if forecast["days"] and size != len(data):
            raise UsbSyncError("Pala reported an unexpected weather cache size.")
        stored = _download_blocks(port, "READWEATHER", size)
        if not forecast["days"]:
            # The device can retain an earlier control or a same-location offline forecast.
            try:
                saved = json.loads(stored)
            except ValueError:
                saved = None
            location = forecast.get("location")
            if isinstance(saved, dict) and location is not None and saved.get("location") == location:
                days = saved.get("days")
                if days == [] and reply[2:] == ["-"]:
                    return None
                if location and isinstance(days, list) and days:
                    return None
            raise UsbSyncError("Pala did not verify the updated weather location.")
        if stored != data:
            raise UsbSyncError("The stored forecast differs from the sent forecast.")
        if reply[2:] == ["-"]:
            raise UsbSyncError("Pala saved the forecast, but its display date has no matching entry.")
        today = next((day for day in forecast["days"] if day["date"] == reply[2]), None)
        if today is None or reply[3:] != [str(today[key]) for key in ("minimum", "maximum", "code")]:
            raise UsbSyncError("Pala's current weather differs from the expected values.")
        return today
    finally:
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")


def _captured(value):
    if value == "-":
        return None
    if re.fullmatch(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ", value) is None:
        raise UsbSyncError("Pala returned an invalid recording timestamp.")
    return value


def _recording_time(port):
    try:
        reply = _exchange(port, "META")
    except UsbSyncError as error:
        if str(error) == "Pala reported COMMAND.":
            return None  # The original USB firmware has no metadata command.
        raise
    if len(reply) != 2 or reply[0] != "META":
        raise UsbSyncError("Pala returned invalid recording metadata.")
    return _captured(reply[1])


def sync_one(port, tokens, transcribe_local, *, note=None, stop=None, stage_transcript=None, activity=None):
    """Use one already-open serial port. Return None for an empty queue."""
    if note is not None:
        _require_note(note)
    _check_stop(stop)
    with _usb_phase("authenticating"):
        _paired_session(port, tokens)
    try:
        with _usb_phase("selecting"):
            reply = _exchange(port, "NEXT" if note is None else f"SELECT {note}")
        if note is None and reply in (["EMPTY"], ["EMPTY", "RECHECK"]):
            port.recheck_when_empty = reply == ["EMPTY", "RECHECK"]
            return None
        if (len(reply) != 3 or reply[0] != "NOTE"
                or not all(re.fullmatch(r"[0-9]{1,10}", part) for part in reply[1:])):
            raise UsbSyncError("Invalid recording selection.")
        selected, size = map(int, reply[1:])
        if not 0 < selected <= 2_147_483_647 or (note is not None and selected != note):
            raise UsbSyncError("Pala selected a different note.")
        if not 44 < size <= MAX_WAV_BYTES:
            raise UsbSyncError("The recording has an unsupported size.")
        captured = _recording_time(port) if stage_transcript is not None else None
        with _usb_phase("receiving"), activity_step(activity, "receiving"):
            audio = _download(port, size, stop)
        _check_stop(stop)
        with _usb_phase("transcribing"), activity_step(activity, "transcribing"):
            result = transcribe_local(audio)
        _check_stop(stop)
        text = result.get("text") if isinstance(result, dict) else None
        data = text.strip().encode("utf-8") if isinstance(text, str) else b""
        if not data or len(data) > MAX_TEXT_BYTES or b"\x00" in data:
            raise UsbSyncError("The local transcript is empty or exceeds the device limit.")
        acknowledge = (stage_transcript(selected, captured, data.decode("utf-8"))
                       if stage_transcript is not None else None)
        with _usb_phase("returning"), activity_step(activity, "returning"):
            _expect_ok(port, f"TEXT {len(data)} {hashlib.sha256(data).hexdigest()}")
            write_chunk = getattr(port, "write_chunk", WRITE_CHUNK)
            for offset in range(0, len(data), write_chunk):
                _check_stop(stop)
                _expect_ok(port, f"PUT {offset} {data[offset:offset + write_chunk].hex()}")
            _check_stop(stop)
            try:
                receipt = _exchange(port, "COMMIT")
            except (OSError, UsbReplyTimeout):
                # A write can reach the Note even when its reply does not. Verify
                # the exact recording on this session before releasing delivery.
                if acknowledge is None or not _confirmed_saved(port, selected, data, captured, stop):
                    raise
            else:
                if receipt != ["SAVED", str(selected)]:
                    raise UsbSyncError("Pala did not confirm saving the transcript.")
        if acknowledge is not None:
            acknowledge()
        return SyncedNote(selected, len(audio))
    finally:
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")


def paired_port(ports, port_name, tokens):
    paired = {key.casefold(): (key, token) for key, token in tokens.items()}
    for port in ports:
        identity = (port.serial_number or "").casefold()
        if (port.device.casefold() == port_name.casefold()
                and (port.vid, port.pid) == (0x303A, 0x1001) and identity in paired):
            return paired[identity]
    raise UsbSyncError("The selected port does not identify a paired Pala.")


def watch_paired(list_ports, tokens, is_ready, sync_attachment, stop, on_error, flush=None,
                 prepare_attachment=None, preparation_revision=None):
    """Refresh a paired device on attachment, then drain when the local ASR is ready."""
    attempts = {}
    retries = {}
    preparations = {}
    previous_revision = None
    previous_tokens = None
    while not stop.is_set():
        current_tokens = tokens() if callable(tokens) else tokens
        if current_tokens != previous_tokens:
            attempts.clear()
            retries.clear()
            preparations.clear()
            previous_tokens = dict(current_tokens)
        paired = {key.casefold(): token for key, token in current_tokens.items()}
        if preparation_revision is not None:
            revision = preparation_revision()
            if revision != previous_revision:
                preparations.clear()
                previous_revision = revision
        attached = {}
        for port in list_ports():
            identity = (port.serial_number or "").casefold()
            if identity and (port.vid, port.pid) == (0x303A, 0x1001) and identity in paired:
                attached[(port.device, identity)] = paired[identity]
        attempts = {key: count for key, count in attempts.items() if key in attached}
        retries = {key: due for key, due in retries.items() if key in attached}
        preparations = {key: count for key, count in preparations.items() if key in attached}
        for key, token in attached.items():
            if stop.is_set():
                break
            if prepare_attachment is not None and preparations.get(key, 0) < ATTACHMENT_ATTEMPTS:
                try:
                    prepare_attachment(key[0], token)
                except (UsbSyncError, OSError, ValueError) as error:
                    preparations[key] = preparations.get(key, 0) + 1
                    if not stop.is_set():
                        on_error(error)
                else:
                    preparations[key] = ATTACHMENT_ATTEMPTS
            if stop.is_set():
                continue
            if attempts.get(key, 0) >= ATTACHMENT_ATTEMPTS:
                # Failures back off without disarming an attached Note after it wakes.
                # A completed one-shot attachment has no retry scheduled.
                due = retries.get(key)
                if due is None or time.monotonic() < due:
                    continue
            if not is_ready():
                continue
            try:
                recheck = sync_attachment(key[0], token)
            except (UsbSyncError, OSError, ValueError) as error:
                if not stop.is_set():
                    attempts[key] = attempts.get(key, 0) + 1
                    if attempts[key] >= ATTACHMENT_ATTEMPTS:
                        retries[key] = time.monotonic() + ATTACHMENT_RETRY_SECONDS
                    on_error(error)
            else:
                retries.pop(key, None)
                attempts[key] = 0 if recheck else ATTACHMENT_ATTEMPTS
        if flush is not None and not stop.is_set():
            flush()
        stop.wait(2)


@contextlib.contextmanager
def _open_link(port_name, peer_id=None):
    import serial

    with serial.Serial(baudrate=115200, timeout=0.035, write_timeout=3) as port:
        port.dtr = False
        port.rts = False
        port.port = port_name
        port.open()
        link = BufferedSerial(port)
        link.peer_id = peer_id
        yield link


def _drain_link(link, token, bridge, stop, stage_transcript=None, activity=None):
    def transcribe(audio):
        deadline = time.monotonic() + 60
        if not bridge.local_transcription_ready():
            with activity_step(activity, "waiting"):
                while not bridge.local_transcription_ready():
                    if time.monotonic() >= deadline:
                        raise UsbSyncError("Local Dictation stayed unavailable or busy.")
                    stop.wait(1)
                    _check_stop(stop)
        _check_stop(stop)
        return bridge.transcribe_local(audio)

    while not stop.is_set():
        result = sync_one(link, (token,), transcribe, stop=stop,
                          stage_transcript=stage_transcript, activity=activity)
        if result is None:
            return link.recheck_when_empty
        print(f"Pala saved the transcript of note {result.note}.", flush=True)
    return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--port", help="Connected Pala USB port, for example COM24.")
    mode.add_argument("--watch", action="store_true", help="Synchronize when a paired Pala appears.")
    parser.add_argument("--note", type=int, help="Select a specific pending note number.")
    metadata = parser.add_mutually_exclusive_group()
    metadata.add_argument("--weather-only", action="store_true", help="Sync the forecast without transcribing.")
    metadata.add_argument("--name-only", action="store_true", help="Sync the display name without transcribing.")
    metadata.add_argument("--delivery-only", action="store_true", help="Sync Google delivery counters without transcribing.")
    parser.add_argument("--managed", action="store_true", help="Stop on closure of the owning control pipe.")
    args = parser.parse_args()
    if (args.watch and args.note is not None) or (args.managed and not args.watch):
        parser.error("--note requires --port, and --managed requires --watch.")
    if (args.weather_only or args.name_only or args.delivery_only) and args.note is not None:
        parser.error("--note cannot be used for metadata-only synchronization.")
    if (args.name_only or args.delivery_only) and args.watch:
        parser.error("--name-only and --delivery-only require --port.")
    try:
        from bridge import (Bridge, local_dictation_note_name, local_dictation_settings,
                            local_dictation_weather_location, managed_stop_event)
        from pair import STATE
        from note_settings import EnrollmentStore, PairingBroker, SettingsError, SettingsRoute, complete_pairing
        from note_transport import SettingsTransport
        from note_web import bridge_preferences, read_object, serve_settings

        config = read_object(STATE / "config.json")
        web_owner = os.environ.get("PALA_NOTE_SETTINGS_OWNER") if args.managed and not args.weather_only else None
        if web_owner:
            STATE.mkdir(parents=True, exist_ok=True)
        enrollments = EnrollmentStore(STATE, config)
        tokens = enrollments.tokens()
        if not tokens and not web_owner:
            raise UsbSyncError("The local Bridge has no paired devices.")

        def paired_link(port, token):
            peer = next((entry["id"] for entry in enrollments.devices().values() if entry["token"] == token), None)
            return _open_link(port, peer)

        def token_device(token):
            return next((device for device, value in enrollments.tokens().items() if value == token), None)
        usb_activity = BridgeActivity(STATE, "usb")

        def configured_name():
            return local_dictation_note_name(local_dictation_settings())

        def send_name(link, token):
            transfer_note_name(link, (token,), configured_name())
            print("Pala verified the saved display name.", flush=True)

        if args.name_only:
            from serial.tools.list_ports import comports

            _, token = paired_port(comports(), args.port, tokens)
            with usb_activity.step("settings"), paired_link(args.port, token) as link:
                send_name(link, token)
            return
        if args.delivery_only:
            from serial.tools.list_ports import comports
            from pala_google import home_delivery_payload

            device, token = paired_port(comports(), args.port, tokens)
            payload = home_delivery_payload(STATE, config, device)
            with paired_link(args.port, token) as link:
                transfer_delivery_counts(link, (token,), payload)
            print("Pala verified the Google delivery counters.", flush=True)
            return
        if args.weather_only or args.watch:
            from serial.tools.list_ports import comports
            from weather import ForecastCache, LOCATION_ZONE
            from pala_google import home_delivery_payload

            weather = ForecastCache(local_dictation_weather_location)
            weather_revision = 0
            delivery_path = STATE / "google-delivery.sqlite"

            def send_delivery(link, token):
                payload = home_delivery_payload(STATE, config, token_device(token),
                                                queue=router.queue if router is not None else None)
                try:
                    transfer_delivery_counts(link, (token,), payload)
                except UsbSyncError as error:
                    if str(error) != "Pala reported COMMAND.":
                        raise

            def metadata_revision():
                nonlocal weather_revision
                try:
                    changed = delivery_path.stat().st_mtime_ns
                except FileNotFoundError:
                    changed = None
                current = datetime.now(LOCATION_ZONE)
                refresh_at = weather.next_refresh_at
                if weather_revision < refresh_at <= current.timestamp():
                    weather_revision = refresh_at
                try:
                    settings = local_dictation_settings()
                except (OSError, ValueError):
                    name = location = None
                else:
                    name = local_dictation_note_name(settings)
                    location = bridge_preferences(STATE, settings)["weatherLocation"]
                return name, current.date(), changed, location, weather_revision

            def send_forecast(link, token, forecast):
                try:
                    today = transfer_forecast(link, (token,), forecast)
                except UsbSyncError as error:
                    response = str(error)
                    if args.watch and (response == "Pala reported COMMAND." or
                                       (not forecast["days"] and response == "Pala reported WEATHER.")):
                        return
                    raise
                if today is None:
                    print("Pala verified the weather location state.", flush=True)
                else:
                    print(f"Pala verified weather for {today['date']}: "
                          f"{today['minimum']} / {today['maximum']} C (code {today['code']}).", flush=True)

            def prepare_usb(port, token):
                with usb_activity.step("weather" if args.weather_only else "settings"):
                    if not args.weather_only:
                        with paired_link(port, token) as link:
                            try:
                                send_name(link, token)
                            except UsbSyncError as error:
                                if str(error) != "Pala reported COMMAND.":
                                    raise
                            send_delivery(link, token)
                    forecast = weather.get()
                    if forecast is None:
                        if args.weather_only and not args.watch:
                            raise UsbSyncError("The weather location settings are currently unavailable.")
                        return
                    with paired_link(port, token) as link:
                        send_forecast(link, token, forecast)

            if args.weather_only:
                if args.watch:
                    stop = managed_stop_event(sys.stdin.buffer) if args.managed else threading.Event()
                    watch_paired(comports, tokens, lambda: False, lambda *_: None, stop,
                                 lambda error: print(f"Pala weather sync interrupted ({type(error).__name__}).",
                                                     file=sys.stderr, flush=True),
                                 prepare_attachment=prepare_usb, preparation_revision=metadata_revision)
                else:
                    _, token = paired_port(comports(), args.port, tokens)
                    prepare_usb(args.port, token)
                return

        bridge = Bridge(STATE, config)
        router = None
        if config.get("google_routes"):
            from pala_google import UsbGoogleDelivery

            router = UsbGoogleDelivery(STATE, config)
        if args.watch:
            stop = managed_stop_event(sys.stdin.buffer) if args.managed else threading.Event()
            transfer_lock = threading.Lock()
            bluetooth_activity = BridgeActivity(STATE, "bluetooth")
            ble_available = sys.platform == "win32" and importlib.util.find_spec("bleak") is not None

            def connected_usb(allow_unpaired=False):
                attached = []
                current = {serial.casefold(): token for serial, token in enrollments.tokens().items()}
                for port in comports():
                    serial = (port.serial_number or "").casefold()
                    if (port.vid, port.pid) == (0x303A, 0x1001) and serial and (allow_unpaired or serial in current):
                        attached.append((port.device, serial, current.get(serial)))
                if len(attached) > 1:
                    raise SettingsError("Connect only one Note by USB for settings.")
                return attached[0] if attached else None

            def read_usb(port, request):
                name, _, token = port
                with usb_activity.step("settings"), paired_link(name, token) as link:
                    return paired_config(link, token, request)

            def enroll(job, exchange, *, attached_serial=None):
                from bridge_discovery import bridge_name

                prefs = bridge_preferences(STATE, local_dictation_settings())
                certificate_path = STATE / "discovery-cert.pem"
                certificate = address = None
                if config.get("discovery_id") and certificate_path.is_file() and (STATE / "discovery-key.pem").is_file():
                    address = bridge_name(config)
                    certificate = certificate_path.read_text(encoding="ascii")
                    if not 0 < len(certificate) <= 2_048:
                        raise SettingsError("The paired Bridge HTTPS certificate is invalid.")
                return complete_pairing(job, exchange, enrollments, prefs["name"],
                                        certificate, address, attached_serial=attached_serial)

            def pair_usb(job):
                port = connected_usb(allow_unpaired=True)
                if port is None:
                    raise SettingsError("Connect the Note by USB and open its pairing menu.")
                if not transfer_lock.acquire(timeout=1):
                    raise SettingsError("The current Note transfer is busy. Try again shortly.")
                try:
                    with usb_activity.step("settings"), _open_link(port[0]) as link:
                        enroll(job, lambda request: config_exchange(link, request), attached_serial=port[1])
                finally:
                    transfer_lock.release()

            transport = SettingsTransport(connected_usb, read_usb, transfer_lock, stop, ble_available, enrollments.tokens)
            pairing = PairingBroker(pair_usb, ble_available)

            def cancel_pairing_on_stop():
                stop.wait()
                pairing.stop()

            if web_owner:
                threading.Thread(target=cancel_pairing_on_stop, name="pala-pair-stop", daemon=True).start()

            def locked_prepare_usb(port, token):
                with transfer_lock:
                    prepare_usb(port, token)

            def drain(link, device, token, activity):
                stage = router.stager(device) if router is not None else None
                return _drain_link(link, token, bridge, stop, stage, activity)

            def drain_usb(port, token):
                with transfer_lock, paired_link(port, token) as link:
                    return drain(link, token_device(token), token, usb_activity)

            def drain_bluetooth(link, device, token, settings_window):
                if transfer_lock.acquire(blocking=False):
                    try:
                        if web_owner:
                            transport.serve_bluetooth(device, lambda request: paired_config(link, token, request),
                                                      keep_open=settings_window)
                        if settings_window:
                            # The Note can close its dedicated settings window mid-transfer.
                            return
                        with bluetooth_activity.step("settings"):
                            try:
                                send_name(link, token)
                            except (UsbSyncError, ValueError) as error:
                                if str(error) != "Pala reported COMMAND.":
                                    bluetooth_error(error)
                            try:
                                send_delivery(link, token)
                            except (UsbSyncError, ValueError) as error:
                                bluetooth_error(error)
                        if bridge.local_transcription_ready():
                            drain(link, device, token, bluetooth_activity)
                        if stop.is_set():
                            return
                        # Weather can take longer than the Note's idle BLE session.
                        with bluetooth_activity.step("weather"):
                            forecast = weather.get()
                            if forecast is not None:
                                try:
                                    send_forecast(link, token, forecast)
                                except (UsbSyncError, ValueError) as error:
                                    bluetooth_error(error)
                    finally:
                        transfer_lock.release()

            bluetooth = None
            if sys.platform == "win32":
                try:
                    from bluetooth_sync import MissingNoteGatt, start_bluetooth_watcher
                except ImportError as error:
                    print(f"Pala Bluetooth sync unavailable ({type(error).__name__}).", file=sys.stderr, flush=True)
                else:
                    def bluetooth_error(error):
                        detail = str(error) if isinstance(error, MissingNoteGatt) else type(error).__name__
                        print(f"Pala Bluetooth sync unavailable or interrupted ({detail}).",
                              file=sys.stderr, flush=True)

                    bluetooth = start_bluetooth_watcher(
                        enrollments.tokens, stop, drain_bluetooth, bluetooth_error,
                        enrollments=enrollments.devices, pairing=pairing if web_owner else None, enroll=enroll,
                        settings_pending=transport.wants_bluetooth if web_owner else None, pairing_lock=transfer_lock)
            try:
                route = SettingsRoute(STATE, local_dictation_settings, transport.request, pairing)
                web = serve_settings(STATE, web_owner, route) if web_owner else contextlib.nullcontext()
                with web:
                    watch_paired(
                        comports, enrollments.tokens, bridge.local_transcription_ready, drain_usb, stop,
                        lambda error: report_usb_failure(STATE, error),
                        router.flush if router is not None else None,
                        prepare_attachment=locked_prepare_usb,
                        preparation_revision=metadata_revision,
                    )
            finally:
                pairing.stop()
                stop.set()
                if bluetooth is not None:
                    bluetooth.join(timeout=2)
            return
        if not bridge.local_transcription_ready():
            raise UsbSyncError("Local Dictation is unavailable or busy.")
        stage = None
        staged = []
        selected_tokens = tokens.values()
        if router is not None:
            from serial.tools.list_ports import comports

            device, token = paired_port(comports(), args.port, tokens)
            selected_tokens = (token,)
            stage = router.stager(device, staged)
        with _open_link(args.port) as link:
            result = sync_one(link, selected_tokens, bridge.transcribe_local, note=args.note,
                              stage_transcript=stage, activity=usb_activity)
        if router is not None:
            router.flush(staged)
    except KeyboardInterrupt:
        return
    except (OSError, ValueError, UsbSyncError) as exc:
        sys.exit(str(exc))
    if result is None:
        print("No pending recording is available on Pala.")
    else:
        print(f"Pala saved the transcript of note {result.note} ({result.audio_bytes} WAV bytes).")


if __name__ == "__main__":
    main()
