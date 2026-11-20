"""Synchronize recordings and the Trnava forecast with a paired Pala."""
import argparse
import contextlib
from dataclasses import dataclass
import hashlib
import hmac
import io
import json
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
    "WEATHER": ("OK",),
    "WEATHER-COMMIT": ("OK",),
    "WEATHER-READ": ("FORECAST",),
    "CLOSE": ("OK",),
}


class UsbSyncError(RuntimeError):
    pass


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
    request = PREFIX + command.encode("ascii") + b"\n"
    if port.write(request) != len(request):
        raise UsbSyncError("The USB command was interrupted.")
    deadline = time.monotonic() + (1 if read_block else 6)
    expected = REPLY_TYPES.get(command.split(" ", 1)[0], ())
    line = bytearray()
    while time.monotonic() < deadline:
        chunk = port.read_until(b"\n", MAX_REPLY - len(line))
        line.extend(chunk)
        if line.endswith(b"\n"):
            if line.startswith(PREFIX):
                try:
                    response = line[len(PREFIX):].decode("ascii").split()
                except UnicodeDecodeError as exc:
                    raise UsbSyncError("Invalid USB reply.") from exc
                if response[:1] == ["ERROR"]:
                    # Known firmware error codes only. Do not display arbitrary
                    # serial bytes, which can include unrelated setup traffic.
                    known = {"AUTH", "BUSY", "COMMAND", "NOTE", "READ", "SAVE", "TEXT", "WEATHER", "NAME"}
                    reason = response[1] if len(response) == 2 and response[1] in known else "UNKNOWN"
                    raise UsbSyncError(f"Pala reported {reason}.")
                if read_block is not None:
                    if _matches_audio_block(response, *read_block):
                        return response
                elif response and response[0] in expected:
                    return response
            line.clear()
        elif len(line) == MAX_REPLY:
            raise UsbSyncError("The USB reply exceeded its frame limit.")
    raise UsbReplyTimeout("No complete USB sync reply. Check the cable, port and firmware.")


def _matches_audio_block(reply, offset, length):
    return (len(reply) == 3 and reply[:2] == ["DATA", str(offset)]
            and len(reply[2]) == length * 2 and re.fullmatch(r"[0-9a-f]+", reply[2]))


def _expect_ok(port, command):
    if _exchange(port, command) != ["OK"]:
        raise UsbSyncError("Pala did not acknowledge the USB command.")


def _authenticate(port, tokens):
    host = secrets.token_hex(16)
    reply = _exchange(port, f"HELLO {host}")
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
            except UsbReplyTimeout as exc:
                if attempt == READ_ATTEMPTS:
                    raise UsbSyncError(f"Pala did not return {command} data at byte {offset}.") from exc
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
                raise UsbSyncError("The recording is not a complete mono PCM WAV.")
    except (wave.Error, EOFError) as exc:
        raise UsbSyncError("The recording is not a valid WAV.") from exc
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


def read_saved(port, tokens, note):
    """Read existing text over an authenticated USB session without running ASR."""
    _require_note(note)
    _paired_session(port, tokens)
    try:
        reply = _exchange(port, f"EXPORT {note}")
        if (len(reply) != 4 or reply[:2] != ["TRANSCRIPT", str(note)]
                or not re.fullmatch(r"[0-9]{1,5}", reply[2])):
            raise UsbSyncError("Pala returned an invalid saved-transcript header.")
        size = int(reply[2])
        if not 0 < size <= MAX_TEXT_BYTES:
            raise UsbSyncError("The stored transcript has an unsupported size.")
        return _download_blocks(port, "READTXT", size).decode("utf-8"), _captured(reply[3])
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
    finally:
        with contextlib.suppress(OSError, UsbSyncError):
            _expect_ok(port, "CLOSE")

    _paired_session(port, tokens)
    try:
        reply = _exchange(port, "WEATHER-READ")
        if len(reply) not in (3, 6) or reply[1] != str(len(data)):
            raise UsbSyncError("Pala reported an unexpected weather cache size.")
        if _download_blocks(port, "READWEATHER", len(data)) != data:
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
    _paired_session(port, tokens)
    try:
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
        with activity_step(activity, "receiving"):
            audio = _download(port, size, stop)
        _check_stop(stop)
        with activity_step(activity, "transcribing"):
            result = transcribe_local(audio)
        _check_stop(stop)
        text = result.get("text") if isinstance(result, dict) else None
        data = text.strip().encode("utf-8") if isinstance(text, str) else b""
        if not data or len(data) > MAX_TEXT_BYTES or b"\x00" in data:
            raise UsbSyncError("The local transcript is empty or exceeds the device limit.")
        acknowledge = (stage_transcript(selected, captured, data.decode("utf-8"))
                       if stage_transcript is not None else None)
        with activity_step(activity, "returning"):
            _expect_ok(port, f"TEXT {len(data)} {hashlib.sha256(data).hexdigest()}")
            write_chunk = getattr(port, "write_chunk", WRITE_CHUNK)
            for offset in range(0, len(data), write_chunk):
                _check_stop(stop)
                _expect_ok(port, f"PUT {offset} {data[offset:offset + write_chunk].hex()}")
            _check_stop(stop)
            if _exchange(port, "COMMIT") != ["SAVED", str(selected)]:
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
    paired = {key.casefold(): token for key, token in tokens.items()}
    attempts = {}
    preparations = {}
    previous_revision = None
    while not stop.is_set():
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
            if stop.is_set() or attempts.get(key, 0) >= ATTACHMENT_ATTEMPTS or not is_ready():
                continue
            try:
                recheck = sync_attachment(key[0], token)
            except (UsbSyncError, OSError, ValueError) as error:
                if not stop.is_set():
                    attempts[key] = attempts.get(key, 0) + 1
                    on_error(error)
            else:
                attempts[key] = 0 if recheck else ATTACHMENT_ATTEMPTS
        if flush is not None and not stop.is_set():
            flush()
        stop.wait(2)


@contextlib.contextmanager
def _open_link(port_name):
    import serial

    with serial.Serial(baudrate=115200, timeout=0.035, write_timeout=3) as port:
        port.dtr = False
        port.rts = False
        port.port = port_name
        port.open()
        yield BufferedSerial(port)


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
    parser.add_argument("--managed", action="store_true", help="Stop on closure of the owning control pipe.")
    args = parser.parse_args()
    if (args.watch and args.note is not None) or (args.managed and not args.watch):
        parser.error("--note requires --port, and --managed requires --watch.")
    if (args.weather_only or args.name_only) and args.note is not None:
        parser.error("--note cannot be used for metadata-only synchronization.")
    if args.name_only and args.watch:
        parser.error("--name-only requires --port.")
    try:
        from bridge import Bridge, local_dictation_note_name, local_dictation_settings, managed_stop_event
        from pair import STATE

        with (STATE / "config.json").open(encoding="utf-8-sig") as stream:
            config = json.load(stream)
        tokens = config.get("device_tokens")
        if not isinstance(tokens, dict) or not tokens:
            raise UsbSyncError("The local Bridge has no paired devices.")
        usb_activity = BridgeActivity(STATE, "usb")

        def configured_name():
            return local_dictation_note_name(local_dictation_settings())

        def send_name(link, token):
            transfer_note_name(link, (token,), configured_name())
            print("Pala verified the saved display name.", flush=True)

        if args.name_only:
            from serial.tools.list_ports import comports

            _, token = paired_port(comports(), args.port, tokens)
            with usb_activity.step("settings"), _open_link(args.port) as link:
                send_name(link, token)
            return
        if args.weather_only or args.watch:
            from serial.tools.list_ports import comports
            from weather import ForecastCache

            weather = ForecastCache()

            def send_forecast(link, token, forecast):
                today = transfer_forecast(link, (token,), forecast)
                print(f"Pala verified Trnava weather for {today['date']}: "
                      f"{today['minimum']} / {today['maximum']} C (code {today['code']}).", flush=True)

            def prepare_usb(port, token):
                with usb_activity.step("weather" if args.weather_only else "settings"):
                    if not args.weather_only:
                        with _open_link(port) as link:
                            try:
                                send_name(link, token)
                            except UsbSyncError as error:
                                if str(error) != "Pala reported COMMAND.":
                                    raise
                    forecast = weather.get()
                    if forecast is None:
                        if args.weather_only and not args.watch:
                            raise UsbSyncError("The Trnava forecast is currently unavailable.")
                        return
                    with _open_link(port) as link:
                        try:
                            send_forecast(link, token, forecast)
                        except UsbSyncError as error:
                            if args.weather_only or str(error) != "Pala reported COMMAND.":
                                raise

            if args.weather_only:
                if args.watch:
                    stop = managed_stop_event(sys.stdin.buffer) if args.managed else threading.Event()
                    watch_paired(comports, tokens, lambda: False, lambda *_: None, stop,
                                 lambda error: print(f"Pala weather sync interrupted ({type(error).__name__}).",
                                                     file=sys.stderr, flush=True),
                                 prepare_attachment=prepare_usb)
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
            devices = {token: device for device, token in tokens.items() if isinstance(token, str)}
            bluetooth_activity = BridgeActivity(STATE, "bluetooth")

            def drain(link, device, token, activity):
                stage = router.stager(device) if router is not None else None
                return _drain_link(link, token, bridge, stop, stage, activity)

            def drain_usb(port, token):
                with transfer_lock, _open_link(port) as link:
                    return drain(link, devices[token], token, usb_activity)

            def drain_bluetooth(link, device, token):
                if transfer_lock.acquire(blocking=False):
                    try:
                        with bluetooth_activity.step("settings"):
                            try:
                                send_name(link, token)
                            except (UsbSyncError, ValueError) as error:
                                if str(error) != "Pala reported COMMAND.":
                                    bluetooth_error(error)
                            forecast = weather.get()
                            if forecast is not None:
                                try:
                                    send_forecast(link, token, forecast)
                                except (UsbSyncError, ValueError) as error:
                                    if str(error) != "Pala reported COMMAND.":
                                        bluetooth_error(error)
                        if bridge.local_transcription_ready():
                            drain(link, device, token, bluetooth_activity)
                    finally:
                        transfer_lock.release()

            bluetooth = None
            if sys.platform == "win32":
                def bluetooth_error(error):
                    print(f"Pala Bluetooth sync unavailable or interrupted ({type(error).__name__}).",
                          file=sys.stderr, flush=True)

                try:
                    from bluetooth_sync import start_bluetooth_watcher
                    bluetooth = start_bluetooth_watcher(
                        tokens, stop, drain_bluetooth, bluetooth_error)
                except ImportError as error:
                    bluetooth_error(error)
            try:
                watch_paired(
                    comports, tokens, bridge.local_transcription_ready, drain_usb, stop,
                    lambda error: print(f"Pala USB sync interrupted ({type(error).__name__}).",
                                        file=sys.stderr, flush=True),
                    router.flush if router is not None else None,
                    prepare_attachment=prepare_usb,
                    preparation_revision=configured_name,
                )
            finally:
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
