"""Discover a paired Pala in its BLE fallback window and transport encrypted frames."""
import asyncio
import hashlib
import hmac
import re
import threading
from concurrent.futures import TimeoutError as FutureTimeout

from cryptography.hazmat.primitives.ciphers.aead import AESGCM


SERVICE = "a7727a10-7a9d-4bdd-aaf5-d3e331eddf71"
CAPABILITY = "a7727a11-7a9d-4bdd-aaf5-d3e331eddf71"
REQUEST = "a7727a12-7a9d-4bdd-aaf5-d3e331eddf71"
RESPONSE = "a7727a13-7a9d-4bdd-aaf5-d3e331eddf71"
SIGNAL = "a7727a14-7a9d-4bdd-aaf5-d3e331eddf71"

LABEL = b"KUBO_BLE_V1 "
MAX_PACKET = 512
TAG_BYTES = 16


def identity(token):
    digest = hmac.digest(token.encode("ascii"), LABEL + b"identity", hashlib.sha256)
    return "Kubo-" + digest[:6].hex()


class BleFrames:
    def __init__(self, token, challenge, *, host):
        if len(challenge) != 16:
            raise ValueError("Invalid Pala BLE challenge.")
        key = hmac.digest(token.encode("ascii"), LABEL + challenge, hashlib.sha256)
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

    def __init__(self, client, loop):
        self.client = client
        self.loop = loop
        self.buffer = bytearray()
        self.ready = asyncio.Event()
        self.failed = False

    async def initialize(self, token):
        challenge = await self.client.read_gatt_char(CAPABILITY, use_cached=False)
        self.frames = BleFrames(token, challenge, host=True)
        await self.client.start_notify(SIGNAL, self._on_ready)

    def _on_ready(self, _characteristic, data):
        if data == b"\x01":
            self.ready.set()

    async def _exchange(self, data):
        packet = self.frames.seal(data)
        framed = len(packet).to_bytes(2, "big") + packet
        chunk_size = min(MAX_PACKET, max(20, self.client.mtu_size - 3))
        self.ready.clear()
        for offset in range(0, len(framed), chunk_size):
            await self.client.write_gatt_char(REQUEST, framed[offset:offset + chunk_size], response=True)
        await self.ready.wait()
        packet = await self.client.read_gatt_char(RESPONSE, use_cached=False)
        return self.frames.open(packet)

    def write(self, data):
        if self.failed:
            raise OSError("The Pala BLE session has ended.")
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


async def watch_bluetooth(tokens, stop, drain, on_error):
    from bleak import BleakClient, BleakScanner

    peers = {identity(token): (device, token) for device, token in tokens.items()
             if isinstance(token, str) and re.fullmatch(r"[A-Za-z0-9_-]{16,128}", token)}
    if not peers:
        return
    loop = asyncio.get_running_loop()
    scan_error = None
    while not stop.is_set():
        found = loop.create_future()

        def discover(device, data):
            peer = peers.get(data.local_name)
            if peer and not found.done():
                found.set_result((device, *peer))

        try:
            async with BleakScanner(detection_callback=discover, service_uuids=[SERVICE]):
                scan_error = None
                while not stop.is_set() and not found.done():
                    await asyncio.wait((found,), timeout=1)
                if stop.is_set():
                    return
                device, device_id, token = found.result()
        except Exception as error:
            if scan_error is not type(error):
                on_error(error)
                scan_error = type(error)
            await asyncio.to_thread(stop.wait, 5)
            continue

        try:
            if stop.is_set():
                return
            async with BleakClient(device, timeout=6) as client:
                link = BleLink(client, loop)
                await asyncio.wait_for(link.initialize(token), timeout=6)
                await asyncio.to_thread(drain, link, device_id, token)
        except Exception as error:
            if not stop.is_set():
                on_error(error)
                await asyncio.to_thread(stop.wait, 1)


def start_bluetooth_watcher(tokens, stop, drain, on_error):
    def run():
        try:
            asyncio.run(watch_bluetooth(tokens, stop, drain, on_error))
        except Exception as error:
            if not stop.is_set():
                on_error(error)

    thread = threading.Thread(target=run, name="Pala Bluetooth sync", daemon=True)
    thread.start()
    return thread
