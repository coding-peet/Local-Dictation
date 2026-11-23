"""Synchronize local settings jobs with the watcher's transport ownership."""
import queue
import threading
import time
from dataclasses import dataclass, field

from note_settings import SettingsError


# Leave time for discovery, the settings GATT connection, authentication and the reply.
BLUETOOTH_SETTINGS_TIMEOUT = 40


@dataclass
class BluetoothRequest:
    body: dict
    done: threading.Event = field(default_factory=threading.Event)
    result: dict | None = None
    error: str = ""
    cancelled: bool = False


class SettingsTransport:
    def __init__(self, connected_usb, read_usb, lock, stop, bluetooth_available, known_devices=lambda: True):
        self.connected_usb, self.read_usb = connected_usb, read_usb
        self.lock, self.stop, self.bluetooth_available = lock, stop, bluetooth_available
        self.known_devices = known_devices
        self.bluetooth = queue.Queue(maxsize=1)
        self.device = None

    def _exchange(self, body, device, exchange):
        device = device.casefold()
        payload = dict(body)
        if payload.get("op") != "get" and payload.pop("_target", self.device) != device:
            raise SettingsError("Refresh Note settings before changing this device.")
        reply = exchange(payload)
        if body["op"] == "get" and (reply["ok"] or reply.get("access") in ("locked", "setup")):
            self.device = device
            return {**reply, "_selectedDevice": device}
        return reply

    def request(self, body):
        if self.stop.is_set():
            raise SettingsError("The local Note watcher is stopping.")
        port = self.connected_usb()
        if port is not None:
            timeout = 10 if body["op"] in ("get", "auth.save") else .3
            if not self.lock.acquire(timeout=timeout):
                raise SettingsError("The current Note transfer is busy. Try again shortly.")
            try:
                if self.stop.is_set():
                    raise SettingsError("The local Note watcher is stopping.")
                port = self.connected_usb()
                if port is None:
                    raise SettingsError("The USB connection changed. Reconnect the Note and refresh its settings.")
                reply = self._exchange(body, port[1], lambda data: self.read_usb(port, data))
                return reply, "USB"
            finally:
                self.lock.release()
        if not self.bluetooth_available:
            raise SettingsError("Connect the paired Note by USB. Bluetooth Bridge is unavailable.")
        if not self.known_devices():
            raise SettingsError("Pair a Note first or connect the paired Note by USB.")
        request = BluetoothRequest(body)
        try:
            self.bluetooth.put_nowait(request)
        except queue.Full as error:
            raise SettingsError("The current Note settings request is busy.") from error
        if not request.done.wait(BLUETOOTH_SETTINGS_TIMEOUT):
            request.cancelled = True
            with self.bluetooth.mutex:
                if request in self.bluetooth.queue:
                    self.bluetooth.queue.remove(request)
                    self.bluetooth.not_full.notify()
                    raise SettingsError("Wake the paired Note and retry, or connect it by USB.")
            raise SettingsError("The Note did not finish in time. Refresh its settings before retrying.")
        if request.error:
            raise SettingsError(request.error)
        return request.result, "BLE"

    def wants_bluetooth(self, device):
        with self.bluetooth.mutex:
            if not self.bluetooth.queue:
                return False
            body = self.bluetooth.queue[0].body
            return body["op"] == "get" or body.get("_target", self.device) == device.casefold()

    def serve_bluetooth(self, device, exchange, *, keep_open=False):
        deadline = time.monotonic() + .2
        while not self.stop.is_set():
            remaining = min(.5, deadline - time.monotonic())
            if remaining <= 0:
                return
            if not self.bluetooth.empty() and not self.wants_bluetooth(device):
                return
            try:
                request = self.bluetooth.get(timeout=remaining)
            except queue.Empty:
                continue
            if request.cancelled:
                continue
            try:
                request.result = self._exchange(request.body, device, exchange)
                if keep_open:
                    # The dedicated settings window permits time for interactive edits.
                    deadline = time.monotonic() + 20
            except SettingsError as error:
                request.error = str(error)
            except (OSError, ValueError, RuntimeError):
                request.error = "Bluetooth settings transfer failed. Wake the Note and try again."
                return
            finally:
                request.done.set()
            if not keep_open:
                return
