"""Discover and mutually authenticate a paired Pala before transporting encrypted frames."""
import asyncio
import hashlib
import hmac
import re
import secrets
import threading
import time
from concurrent.futures import TimeoutError as FutureTimeout

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF


SERVICE = "a7727a10-7a9d-4bdd-aaf5-d3e331eddf71"
CAPABILITY = "a7727a11-7a9d-4bdd-aaf5-d3e331eddf71"
REQUEST = "a7727a12-7a9d-4bdd-aaf5-d3e331eddf71"
RESPONSE = "a7727a13-7a9d-4bdd-aaf5-d3e331eddf71"
SIGNAL = "a7727a14-7a9d-4bdd-aaf5-d3e331eddf71"
GATT_ENDPOINTS = ((CAPABILITY, "CAPABILITY"), (REQUEST, "REQUEST"),
                  (RESPONSE, "RESPONSE"), (SIGNAL, "SIGNAL"))

IDENTITY_LABEL = b"KUBO_BLE_V1 identity"
KEY_INFO = b"KUBO_BLE_V2 keys"
PALA_VERSION = b"\x02"
BRIDGE_HELLO = b"\x01"
NONCE_BYTES = 16
KEY_BYTES = 32
MAX_PACKET = 512
TAG_BYTES = 16


def identity(token):
    return hmac.digest(token.encode("ascii"), IDENTITY_LABEL, hashlib.sha256)[:6]


class MissingNoteGatt(OSError):
    pass


def require_note_gatt(client):
    service = client.services.get_service(SERVICE)
    if service is None:
        raise MissingNoteGatt("missing GATT: SERVICE")
    missing = [name for endpoint, name in GATT_ENDPOINTS if service.get_characteristic(endpoint) is None]
    if missing:
        raise MissingNoteGatt("missing GATT: " + ", ".join(missing))


class BleFrames:
    def __init__(self, key, *, host):
        if len(key) != KEY_BYTES:
            raise ValueError("Invalid Pala BLE session key.")
        self.cipher = AESGCM(key)
        self.send_direction = int(not host)
        self.read_direction = int(host)
        self.sent = 0
        self.received = 0

    @staticmethod
    def nonce(direction, sequence):
        return direction.to_bytes(4, "big") + sequence.to_bytes(8, "big")

    def seal(self, plaintext):
        if not 0 < len(plaintext) <= MAX_PACKET - TAG_BYTES:
            raise ValueError("Pala BLE command exceeds the frame limit.")
        packet = self.cipher.encrypt(self.nonce(self.send_direction, self.sent), plaintext, None)
        self.sent += 1
        return packet

    def open(self, packet):
        if not TAG_BYTES < len(packet) <= MAX_PACKET:
            raise ValueError("Invalid Pala BLE frame length.")
        plaintext = self.cipher.decrypt(self.nonce(self.read_direction, self.received), packet, None)
        self.received += 1
        return plaintext


class BleLink:
    """Provide the USB worker's synchronous byte interface from a BLE worker thread."""

    read_chunk = 224
    write_chunk = 224
    settings_frame_limit = MAX_PACKET - TAG_BYTES

    def __init__(self, client, loop):
        self.client = client
        self.loop = loop
        self.buffer = bytearray()
        self.ready = asyncio.Event()
        self.frames = None
        self.handshake_started = False
        self.failed = False

    async def initialize(self, token):
        if self.handshake_started:
            raise OSError("The Pala BLE connection has already attempted authentication.")
        self.handshake_started = True
        try:
            capability = bytes(await self.client.read_gatt_char(CAPABILITY, use_cached=False))
            if len(capability) != 1 + NONCE_BYTES or capability[:1] != PALA_VERSION:
                raise ValueError("Pala does not support the required BLE authentication protocol.")
            bridge_nonce = secrets.token_bytes(NONCE_BYTES)
            keys = HKDF(algorithm=hashes.SHA256(), length=2 * KEY_BYTES,
                        salt=capability[1:] + bridge_nonce, info=KEY_INFO).derive(token.encode("ascii"))
            auth_key, session_key = keys[:KEY_BYTES], keys[KEY_BYTES:]
            transcript = capability + bridge_nonce
            bridge_proof = hmac.digest(auth_key, b"bridge" + transcript, "sha256")
            await self.client.start_notify(SIGNAL, self._on_ready)
            proof = await self._request(BRIDGE_HELLO + bridge_nonce + bridge_proof)
            expected = PALA_VERSION + hmac.digest(auth_key, b"pala" + transcript, "sha256")
            if len(proof) != len(expected) or not hmac.compare_digest(proof, expected):
                raise ValueError("Pala BLE authentication failed.")
            self.frames = BleFrames(session_key, host=True)
        except (Exception, asyncio.CancelledError):
            self.failed = True
            raise

    def _on_ready(self, _characteristic, data):
        if data == b"\x01":
            self.ready.set()

    async def _request(self, packet):
        framed = len(packet).to_bytes(2, "big") + packet
        chunk_size = min(MAX_PACKET, max(20, self.client.mtu_size - 3))
        self.ready.clear()
        for offset in range(0, len(framed), chunk_size):
            await self.client.write_gatt_char(REQUEST, framed[offset:offset + chunk_size], response=True)
        await self.ready.wait()
        return await self.client.read_gatt_char(RESPONSE, use_cached=False)

    async def _exchange(self, data):
        reply = await self._request(self.frames.seal(data))
        return self.frames.open(reply)

    def write(self, data):
        if self.failed:
            raise OSError("The Pala BLE session has ended.")
        if self.frames is None:
            raise OSError("Pala BLE authentication is incomplete.")
        request = asyncio.run_coroutine_threadsafe(self._exchange(data), self.loop)
        try:
            reply = request.result(timeout=8)
        except Exception as error:
            self.failed = True
            if isinstance(error, FutureTimeout):
                request.cancel()
            raise OSError("The Pala BLE exchange was interrupted.") from error
        self.buffer.extend(reply)
        return len(data)

    def read_until(self, terminator, limit):
        found = self.buffer.find(terminator, 0, limit)
        size = found + len(terminator) if found >= 0 else min(len(self.buffer), limit)
        result = bytes(self.buffer[:size])
        del self.buffer[:size]
        return result


async def _enroll_bluetooth(device, job, client_type, broker, enroll, transfer_lock):
    from note_bluetooth_pair import windows_numeric_pair
    from note_settings import SettingsError, decode_response, encode_request

    if transfer_lock is not None and not transfer_lock.acquire(blocking=False):
        job.finish("The current Note transfer is busy. Try pairing again shortly.")
        return
    try:
        await asyncio.wait_for(windows_numeric_pair(device, job), max(.1, job.deadline - time.monotonic()))
        if not job.pending():
            return
        loop = asyncio.get_running_loop()
        async with client_type(device, timeout=6) as client:
            link = BleLink(client, loop)
            await asyncio.wait_for(client.start_notify(SIGNAL, link._on_ready), 6)

            def exchange(data):
                packet = encode_request(data)
                if len(packet) > MAX_PACKET:
                    raise SettingsError("The Bluetooth pairing request is too large.")
                request = asyncio.run_coroutine_threadsafe(link._request(packet), loop)
                try:
                    return decode_response(request.result(timeout=6))
                except FutureTimeout:
                    request.cancel()
                    raise SettingsError("The Note did not answer the Bluetooth pairing request.")

            await asyncio.to_thread(broker.run, job, lambda pending: enroll(pending, exchange))
    except SettingsError as error:
        job.finish(str(error))
    except Exception:
        job.finish("Bluetooth Numeric Comparison or enrollment could not finish. Try again or use USB.")
    finally:
        if transfer_lock is not None:
            transfer_lock.release()


async def watch_bluetooth(tokens, stop, drain, on_error, *, enrollments=None, pairing=None,
                          enroll=None, settings_pending=None, pairing_lock=None):
    from bleak import BleakClient, BleakScanner

    def available():
        configured = tokens() if callable(tokens) else tokens
        known = [(device, token) for device, token in configured.items()
                 if isinstance(token, str) and re.fullmatch(r"[A-Za-z0-9_-]{16,128}", token)]
        tagged = {identity(token): (device, token) for device, token in known}
        legacy = {"Kubo-" + tag.hex(): peer for tag, peer in tagged.items()}
        if enrollments is not None:
            tagged.update({bytes.fromhex(record["advertiser"]): (device, record["token"])
                           for device, record in enrollments().items()})
        return known, tagged, legacy

    if not callable(tokens) and not available()[0] and pairing is None:
        return
    loop = asyncio.get_running_loop()
    scan_error = None
    while not stop.is_set():
        found = loop.create_future()

        def discover(device, data):
            if found.done() or (SERVICE not in data.service_data and SERVICE not in data.service_uuids):
                return
            _, peers, legacy_names = available()
            settings_window = False
            if SERVICE in data.service_data:
                tag = data.service_data[SERVICE]
                settings_window = len(tag) == 7 and tag[:1] == b"P"
                if settings_window:
                    job = pairing.claim_ble() if pairing is not None else None
                    if job is not None:
                        found.set_result((device, None, None, job, settings_window))
                        return
                    peer = peers.get(tag[1:])
                    if peer and (settings_pending is None or not settings_pending(peer[0])):
                        peer = None
                else:
                    peer = peers.get(tag)
            elif SERVICE in data.service_uuids:
                peer = legacy_names.get(data.local_name)
            else:
                peer = None
            if peer:
                found.set_result((device, *peer, None, settings_window))

        try:
            async with BleakScanner(detection_callback=discover):
                scan_error = None
                while not stop.is_set() and not found.done():
                    await asyncio.wait((found,), timeout=1)
                if stop.is_set():
                    return
                device, device_id, token, job, settings_window = found.result()
        except Exception as error:
            if scan_error is not type(error):
                on_error(error)
                scan_error = type(error)
            await asyncio.to_thread(stop.wait, 5)
            continue

        try:
            if stop.is_set():
                return
            if job is not None:
                await _enroll_bluetooth(device, job, BleakClient, pairing, enroll, pairing_lock)
                continue
            # An uncached value read does not refresh WinRT's characteristic list.
            async with BleakClient(device, timeout=6, services=[SERVICE],
                                   winrt={"use_cached_services": False}) as client:
                require_note_gatt(client)
                link = BleLink(client, loop)
                await asyncio.wait_for(link.initialize(token), timeout=6)
                await asyncio.to_thread(drain, link, device_id, token, settings_window)
        except Exception as error:
            if not stop.is_set():
                on_error(error)
                await asyncio.to_thread(stop.wait, 1)


def start_bluetooth_watcher(tokens, stop, drain, on_error, **settings):
    def run():
        try:
            asyncio.run(watch_bluetooth(tokens, stop, drain, on_error, **settings))
        except Exception as error:
            if not stop.is_set():
                on_error(error)

    thread = threading.Thread(target=run, name="Pala Bluetooth sync", daemon=True)
    thread.start()
    return thread
