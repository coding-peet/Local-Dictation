import asyncio
import sys
import unittest
from pathlib import Path

from cryptography.exceptions import InvalidTag

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bluetooth_sync import BleLink, CAPABILITY, REQUEST, RESPONSE, BleFrames, identity
from test_usb_sync import FakePala, TOKEN
from usb_sync import sync_one


class BluetoothSyncTests(unittest.IsolatedAsyncioTestCase):
    token = "synthetic-pairing-token"
    challenge = bytes(range(16))

    async def test_encryption_detects_tampering_replay_and_wrong_pairing(self):
        host = BleFrames(self.token, self.challenge, host=True)
        pala = BleFrames(self.token, self.challenge, host=False)
        command = b"KUBO_USB_V1 NEXT\n"
        packet = host.seal(command)
        self.assertNotIn(command, packet)
        for invalid in (packet[:-1] + bytes([packet[-1] ^ 1]),):
            with self.assertRaises(InvalidTag):
                pala.open(invalid)
        with self.assertRaises(InvalidTag):
            BleFrames("different-pairing-token", self.challenge, host=False).open(packet)
        with self.assertRaises(InvalidTag):
            BleFrames(self.token, b"1" * 16, host=False).open(packet)
        self.assertEqual(pala.open(packet), command)
        with self.assertRaises(InvalidTag):
            pala.open(packet)
        self.assertEqual(host.open(pala.seal(b"OK")), b"OK")
        self.assertNotIn(self.token, identity(self.token))
        self.assertNotEqual(identity(self.token), identity("different-pairing-token"))

    async def test_gatt_fragments_requests_and_returns_one_authenticated_frame(self):
        pala = BleFrames(self.token, self.challenge, host=False)

        class Device:
            mtu_size = 23
            parts = bytearray()

            async def start_notify(self, _characteristic, ready):
                self.signal = ready

            async def read_gatt_char(self, characteristic, **kwargs):
                if characteristic == CAPABILITY:
                    return BluetoothSyncTests.challenge
                assert characteristic == RESPONSE
                return pala.seal(b"\nKUBO_USB_V1 OK\n")

            async def write_gatt_char(self, characteristic, data, *, response):
                assert characteristic == REQUEST and response and len(data) <= 20
                self.parts.extend(data)
                size = int.from_bytes(self.parts[:2], "big")
                if len(self.parts) == size + 2:
                    self.plaintext = pala.open(self.parts[2:])
                    self.signal(None, b"\x01")

        device = Device()
        link = BleLink(device, asyncio.get_running_loop())
        await link.initialize(self.token)
        command = b"KUBO_USB_V1 PUT 0 " + b"ab" * 50 + b"\n"
        self.assertEqual(await asyncio.to_thread(link.write, command), len(command))
        self.assertEqual(device.plaintext, command)
        self.assertEqual(link.read_until(b"\n", 100), b"\n")
        self.assertEqual(link.read_until(b"\n", 100), b"KUBO_USB_V1 OK\n")


    async def test_recording_survives_a_dropped_ble_session_and_saves_on_next_connection(self):
        note = FakePala()
        staged = []
        transcribed = []

        class GattPala:
            mtu_size = 247

            def __init__(self, challenge, drop):
                self.challenge = challenge
                self.drop = drop
                self.frames = BleFrames(TOKEN, challenge, host=False)
                self.parts = bytearray()
                self.reply = b""

            async def start_notify(self, _characteristic, callback):
                self.notify = callback

            async def read_gatt_char(self, characteristic, **kwargs):
                return self.challenge if characteristic == CAPABILITY else self.reply

            async def write_gatt_char(self, characteristic, data, *, response):
                assert characteristic == REQUEST and response
                self.parts.extend(data)
                size = int.from_bytes(self.parts[:2], "big")
                if len(self.parts) != size + 2:
                    return
                command = self.frames.open(self.parts[2:])
                self.parts.clear()
                if self.drop and command.startswith(b"KUBO_USB_V1 READ "):
                    raise OSError("The synthetic link went out of range.")
                note.write(command)
                note.read_until(b"\n", 500)
                self.reply = self.frames.seal(note.read_until(b"\n", 500))
                self.notify(None, b"\x01")

        def transcribe(audio):
            transcribed.append(audio)
            return {"text": "Kontrola akvária."}

        def stage(number, captured, text):
            staged.append((number, captured, text))
            return lambda: staged.append("saved")

        loop = asyncio.get_running_loop()
        first = BleLink(GattPala(b"A" * 16, drop=True), loop)
        await first.initialize(TOKEN)
        with self.assertRaises(OSError):
            await asyncio.to_thread(sync_one, first, (TOKEN,), transcribe, stage_transcript=stage)
        self.assertEqual(transcribed, [])
        self.assertEqual(staged, [])
        self.assertEqual(note.text, b"")

        second = BleLink(GattPala(b"B" * 16, drop=False), loop)
        await second.initialize(TOKEN)
        result = await asyncio.to_thread(sync_one, second, (TOKEN,), transcribe, stage_transcript=stage)
        self.assertEqual(result.note, 42)
        self.assertEqual(transcribed, [note.audio])
        self.assertEqual(note.text.decode(), "Kontrola akvária.")
        self.assertEqual(staged, [(42, note.captured, "Kontrola akvária."), "saved"])
        self.assertEqual(sum(command == "COMMIT" for command, _ in note.calls), 1)
