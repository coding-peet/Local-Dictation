import asyncio
import hashlib
import hmac
import struct
from types import SimpleNamespace
from unittest.mock import patch
import sys
import unittest
from pathlib import Path

from cryptography.exceptions import InvalidTag

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bluetooth_sync import BleFrames, BleLink, CAPABILITY, REQUEST, RESPONSE, SERVICE, SIGNAL, identity, watch_bluetooth
from test_usb_sync import FakePala, TOKEN
from usb_sync import sync_one


class GattPeer:
    """Synthetic v2 endpoint with an independent HKDF transcript check."""

    def __init__(self, token, pala_nonce, *, mtu=23, capability=None, proof=None, command=None):
        self.token = token
        self.characteristics = {endpoint: object() for endpoint in (CAPABILITY, REQUEST, RESPONSE, SIGNAL)}
        self.services = SimpleNamespace(get_service={
            SERVICE: SimpleNamespace(get_characteristic=self.characteristics.get)
        }.get)
        self.mtu_size = mtu
        self.capability = b"\x02" + pala_nonce if capability is None else capability
        self.proof = proof
        self.command = command or (lambda _: b"\nKUBO_USB_V1 OK\n")
        self.frames = None
        self.parts = bytearray()
        self.chunks = []
        self.plaintext = None
        self.raw_hello = None
        self.reply = b""
        self.subscribed = False
        self.hello_received = asyncio.Event()

    async def start_notify(self, characteristic, callback):
        assert characteristic == SIGNAL
        assert self.raw_hello is None
        self.subscribed = True
        self.notify = callback

    async def read_gatt_char(self, characteristic, *, use_cached):
        assert not use_cached
        if characteristic == CAPABILITY:
            return self.capability
        assert characteristic == RESPONSE and self.reply
        return self.reply

    async def write_gatt_char(self, characteristic, data, *, response):
        assert characteristic == REQUEST and response and self.subscribed
        assert len(data) <= self.mtu_size - 3
        self.chunks.append(bytes(data))
        self.parts.extend(data)
        if len(self.parts) < 2:
            return
        size = int.from_bytes(self.parts[:2], "big")
        assert len(self.parts) <= size + 2
        if len(self.parts) != size + 2:
            return
        packet = bytes(self.parts[2:])
        self.parts.clear()
        if self.frames is None:
            assert len(packet) == 49 and packet[:1] == b"\x01"
            self.raw_hello = packet
            nonce = packet[1:17]
            transcript = self.capability + nonce
            info = b"KUBO_BLE_V2 keys"
            prk = hmac.digest(self.capability[1:] + nonce, self.token.encode("ascii"), "sha256")
            auth_key = hmac.digest(prk, info + b"\x01", "sha256")
            self.session_key = hmac.digest(prk, auth_key + info + b"\x02", "sha256")
            assert hmac.compare_digest(packet[17:], hmac.digest(auth_key, b"bridge" + transcript, "sha256"))
            correct = b"\x02" + hmac.digest(auth_key, b"pala" + transcript, "sha256")
            self.reply = correct if self.proof is None else self.proof(correct, packet)
            self.hello_received.set()
            if self.reply is None:
                return
            self.frames = BleFrames(self.session_key, host=False)
        else:
            self.plaintext = self.frames.open(packet)
            result = self.command(self.plaintext)
            if result is None:
                return
            self.reply = self.frames.seal(result)
        self.notify(None, b"\x01")


class BluetoothSyncTests(unittest.IsolatedAsyncioTestCase):
    token = "synthetic-pairing-token"
    challenge = bytes(range(16))

    async def test_encryption_detects_tampering_replay_and_wrong_pairing(self):
        key = b"A" * 32
        host = BleFrames(key, host=True)
        pala = BleFrames(key, host=False)
        command = b"KUBO_USB_V1 NEXT\n"
        packet = host.seal(command)
        self.assertNotIn(command, packet)
        for invalid in (packet[:-1] + bytes([packet[-1] ^ 1]),):
            with self.assertRaises(InvalidTag):
                pala.open(invalid)
        with self.assertRaises(InvalidTag):
            BleFrames(b"B" * 32, host=False).open(packet)
        self.assertEqual(pala.open(packet), command)
        with self.assertRaises(InvalidTag):
            pala.open(packet)
        self.assertEqual(host.open(pala.seal(b"OK")), b"OK")
        self.assertNotIn(self.token.encode(), identity(self.token))
        self.assertNotEqual(identity(self.token), identity("different-pairing-token"))

    async def test_discovers_paired_service_data_and_legacy_service_list(self):
        tag = hmac.digest(self.token.encode(), b"KUBO_BLE_V1 identity", hashlib.sha256)[:6]
        enrolled_tag = bytes.fromhex("010203040506")
        for accepted in (
            SimpleNamespace(local_name="Peter-Note", service_data={SERVICE: tag}, service_uuids=[]),
            SimpleNamespace(local_name="Kubo-" + tag.hex(), service_data={}, service_uuids=[SERVICE]),
            SimpleNamespace(local_name="Peter-Note Settings", service_data={SERVICE: b"P" + enrolled_tag},
                            service_uuids=[]),
            SimpleNamespace(local_name="Peter-Note", service_data={SERVICE: b"D" + enrolled_tag},
                            service_uuids=[]),
        ):
            with self.subTest(accepted=accepted.local_name):
                stop = asyncio.Event()
                found = []

                class Scanner:
                    def __init__(self, *, detection_callback):
                        self.detect = detection_callback

                    async def __aenter__(self):
                        self.detect("friendly-name-only", SimpleNamespace(
                            local_name="Peter-Note", service_data={}, service_uuids=[]))
                        self.detect("wrong-tag", SimpleNamespace(
                            local_name="Kubo-" + tag.hex(), service_data={SERVICE: b"x" * 6}, service_uuids=[SERVICE]))
                        self.detect("wrong-service", SimpleNamespace(
                            local_name="Kubo-" + tag.hex(), service_data={}, service_uuids=[]))
                        self.detect("other-settings-device", SimpleNamespace(
                            local_name="Other-Note", service_data={SERVICE: b"PFFFFFF"}, service_uuids=[]))
                        self.detect("other-delivery-device", SimpleNamespace(
                            local_name="Other-Note", service_data={SERVICE: b"DFFFFFF"}, service_uuids=[]))
                        self.detect("paired-device", accepted)

                    async def __aexit__(self, *_exception):
                        pass

                class Client(GattPeer):
                    def __init__(self, device, **options):
                        super().__init__(BluetoothSyncTests.token, BluetoothSyncTests.challenge)
                        self.device = device
                        self.options = options

                    async def __aenter__(self):
                        found.append((self.device, self.options))
                        return self

                    async def __aexit__(self, *_exception):
                        pass

                def drain(_link, device, token, settings_window, *, delivery_only=False):
                    found.append((device, token, settings_window, delivery_only))
                    stop.set()

                def fail(error):
                    stop.set()
                    self.fail(repr(error))

                with patch.dict(sys.modules, bleak=SimpleNamespace(BleakScanner=Scanner, BleakClient=Client)):
                    watcher = watch_bluetooth(
                        {"fixture": self.token}, stop, drain, fail,
                        enrollments=lambda: {"fixture": {"advertiser": enrolled_tag.hex(), "token": self.token}},
                        settings_pending=lambda device: device == "fixture")
                    await asyncio.wait_for(watcher, timeout=2)
                settings_window = accepted.service_data.get(SERVICE, b"") == b"P" + enrolled_tag
                self.assertEqual(found, [
                    ("paired-device", {"timeout": 18 if settings_window else 6, "services": [SERVICE],
                                       "winrt": {"use_cached_services": False}}),
                    ("fixture", self.token, settings_window,
                     accepted.service_data.get(SERVICE, b"") == b"D" + enrolled_tag),
                ])

    async def test_incomplete_gatt_reports_expected_endpoints_without_handshake(self):
        from bluetooth_sync import MissingNoteGatt, require_note_gatt

        peer = GattPeer(self.token, self.challenge)
        require_note_gatt(peer)
        del peer.characteristics[CAPABILITY]
        del peer.characteristics[SIGNAL]
        with self.assertRaisesRegex(MissingNoteGatt, "missing GATT: CAPABILITY, SIGNAL"):
            require_note_gatt(peer)
        peer.services = SimpleNamespace(get_service=lambda _: None)
        with self.assertRaisesRegex(MissingNoteGatt, "missing GATT: SERVICE"):
            require_note_gatt(peer)
        self.assertFalse(peer.subscribed)
        self.assertFalse(peer.chunks)

    async def test_handshake_matches_wire_vector_before_fragmented_application_frame(self):
        token = "fixture-ble-pairing-token-42"
        bridge_nonce = bytes.fromhex("101112131415161718191a1b1c1d1e1f")
        request = bytes.fromhex(
            "01101112131415161718191a1b1c1d1e1f5c37d01f1f4e683c36a54fcc7c5d4f6f"
            "4edbf9da3111be51511becbb686696aa")
        proof = bytes.fromhex("020cfafff9f9de6fc27f703ec95e4749a2406f66bc58e26fcb99efba86333f675b")
        device = GattPeer(token, self.challenge)
        link = BleLink(device, asyncio.get_running_loop())

        with patch("secrets.token_bytes", return_value=bridge_nonce) as random:
            await link.initialize(token)
        random.assert_called_once_with(16)
        self.assertEqual(device.raw_hello, request)
        self.assertEqual(device.reply, proof)
        self.assertEqual(len(device.chunks), 3)
        self.assertEqual(b"".join(device.chunks), b"\x00\x31" + request)

        command = b"KUBO_USB_V1 PUT 0 " + b"ab" * 50 + b"\n"
        self.assertEqual(await asyncio.to_thread(link.write, command), len(command))
        self.assertEqual(device.plaintext, command)
        self.assertEqual(link.read_until(b"\n", 100), b"\n")
        self.assertEqual(link.read_until(b"\n", 100), b"KUBO_USB_V1 OK\n")

    async def test_legacy_and_malformed_capabilities_fail_without_sending(self):
        for capability in (self.challenge, b"\x03" + self.challenge,
                           b"\x02" + self.challenge[1:], b"\x02" + self.challenge + b"\0"):
            with self.subTest(capability=capability.hex()):
                device = GattPeer(self.token, self.challenge, capability=capability)
                link = BleLink(device, asyncio.get_running_loop())
                with self.assertRaises(ValueError):
                    await link.initialize(self.token)
                self.assertFalse(device.subscribed)
                self.assertFalse(device.chunks)
                with self.assertRaises(OSError):
                    link.write(b"KUBO_USB_V1 HELLO")

    async def test_invalid_pala_proof_never_installs_application_cipher(self):
        for proof in (
            lambda _correct, request: b"\x02" + request[17:],
            lambda correct, _request: correct[:-1],
            lambda correct, _request: correct + b"\0",
            lambda correct, _request: b"\x01" + correct[1:],
        ):
            with self.subTest(proof=proof):
                device = GattPeer(self.token, self.challenge, proof=proof)
                link = BleLink(device, asyncio.get_running_loop())
                with patch("bluetooth_sync.BleFrames") as frames:
                    with self.assertRaises(ValueError):
                        await link.initialize(self.token)
                frames.assert_not_called()
                with self.assertRaises(OSError):
                    link.write(b"KUBO_USB_V1 HELLO")
                self.assertEqual(len(device.chunks), 3)
                self.assertIsNone(device.plaintext)

    async def test_fresh_bridge_nonce_changes_key_for_every_new_connection(self):
        loop = asyncio.get_running_loop()
        first = GattPeer(self.token, self.challenge)
        second = GattPeer(self.token, self.challenge)
        with patch("secrets.token_bytes", side_effect=[b"A" * 16, b"B" * 16]) as random:
            await BleLink(first, loop).initialize(self.token)
            await BleLink(second, loop).initialize(self.token)
        self.assertEqual(random.call_count, 2)
        self.assertEqual(first.raw_hello[1:17], b"A" * 16)
        self.assertEqual(second.raw_hello[1:17], b"B" * 16)
        self.assertNotEqual(first.session_key, second.session_key)

    async def test_handshake_can_be_cancelled_while_waiting_for_the_signal(self):
        device = GattPeer(self.token, self.challenge, proof=lambda *_: None)
        link = BleLink(device, asyncio.get_running_loop())
        attempt = asyncio.create_task(link.initialize(self.token))
        try:
            await asyncio.wait_for(device.hello_received.wait(), timeout=1)
            attempt.cancel()
            with self.assertRaises(asyncio.CancelledError):
                await attempt
        finally:
            if not attempt.done():
                attempt.cancel()
        with self.assertRaises(OSError):
            link.write(b"KUBO_USB_V1 HELLO")
        self.assertEqual(len(device.chunks), 3)

    async def test_downloads_binary_audio_notifications_and_checks_integrity_before_saving(self):
        for failure in (None, "tampered", "wrong-offset", "disconnected"):
            with self.subTest(failure=failure):
                note = FakePala()
                windows = []
                transcribed = []

                def command(payload):
                    if payload == b"KUBO_USB_V1 AUDIO\n":
                        return b"\nKUBO_USB_V1 AUDIO 1024\n"
                    if payload.startswith(b"\x03"):
                        offset, size = struct.unpack("!IH", payload[1:])
                        windows.append((offset, size))
                        returned_offset = offset + 1 if failure == "wrong-offset" else offset
                        plaintext = struct.pack("!I", returned_offset) + note.audio[offset:offset + size]
                        frames = device.frames
                        block = frames.cipher.encrypt(frames.nonce(1, frames.sent), plaintext, None)
                        frames.sent += 1
                        if failure == "tampered":
                            block = block[:-1] + bytes([block[-1] ^ 1])
                        for start in range(0, len(block), 19):
                            device.notify(None, b"\x02" + block[start:start + 19])
                            if failure == "disconnected":
                                raise OSError("The Note disconnected mid-block.")
                        return None
                    if payload.startswith(b"KUBO_USB_V1 READ "):
                        self.fail("The legacy audio read was used after starting an audio transfer.")
                    note.write(payload)
                    note.read_until(b"\n", 500)
                    return note.read_until(b"\n", 500)

                def transcribe(audio):
                    transcribed.append(audio)
                    return {"text": "Dictated text."}

                device = GattPeer(TOKEN, self.challenge, command=command)
                link = BleLink(device, asyncio.get_running_loop())
                await link.initialize(TOKEN)
                if failure is not None:
                    with self.assertRaises(OSError):
                        await asyncio.to_thread(sync_one, link, (TOKEN,), transcribe)
                    self.assertEqual(transcribed, [])
                    self.assertEqual(note.text, b"")
                    self.assertTrue(link.failed)
                else:
                    result = await asyncio.to_thread(sync_one, link, (TOKEN,), transcribe)
                    self.assertEqual(transcribed, [note.audio])
                    self.assertEqual(result.audio_bytes, 2844)
                    self.assertEqual(windows, [(0, 588), (588, 588), (1176, 588), (1764, 588), (2352, 492)])
                    self.assertEqual(note.text, b"Dictated text.")

    async def test_recording_survives_a_dropped_ble_session_and_saves_on_next_connection(self):
        note = FakePala()
        staged = []
        transcribed = []

        def record_command(*, drop):
            def command(payload):
                if payload == b"KUBO_USB_V1 AUDIO\n":
                    return b"\nKUBO_USB_V1 ERROR COMMAND\n"
                if drop and payload.startswith(b"KUBO_USB_V1 READ "):
                    raise OSError("The synthetic link went out of range.")
                note.write(payload)
                note.read_until(b"\n", 500)
                return note.read_until(b"\n", 500)
            return command

        def transcribe(audio):
            transcribed.append(audio)
            return {"text": "Kontrola akvária."}

        def stage(number, captured, text):
            staged.append((number, captured, text))
            return lambda: staged.append("saved")

        loop = asyncio.get_running_loop()
        first = BleLink(GattPeer(TOKEN, b"A" * 16, mtu=247, command=record_command(drop=True)), loop)
        await first.initialize(TOKEN)
        with self.assertRaises(OSError):
            await asyncio.to_thread(sync_one, first, (TOKEN,), transcribe, stage_transcript=stage)
        self.assertEqual(transcribed, [])
        self.assertEqual(staged, [])
        self.assertEqual(note.text, b"")

        second = BleLink(GattPeer(TOKEN, b"B" * 16, mtu=247, command=record_command(drop=False)), loop)
        await second.initialize(TOKEN)
        result = await asyncio.to_thread(sync_one, second, (TOKEN,), transcribe, stage_transcript=stage)
        self.assertEqual(result.note, 42)
        self.assertEqual(transcribed, [note.audio])
        self.assertEqual(note.text.decode(), "Kontrola akvária.")
        self.assertEqual(staged, [(42, note.captured, "Kontrola akvária."), "saved"])
        self.assertEqual(sum(command == "COMMIT" for command, _ in note.calls), 1)
