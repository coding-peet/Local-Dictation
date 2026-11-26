import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bridge import local_dictation_note_name
from test_usb_sync import FakePala, PREFIX, TOKEN
from usb_sync import UsbSyncError, _proof, transfer_note_name


class NamedPala(FakePala):
    stored = "PALA"

    def write(self, frame):
        command, *parts = frame[len(PREFIX):].decode("ascii").split()
        if command != "NAME":
            return super().write(frame)
        payload, signature = parts
        self.calls.append((command, parts))
        assert signature == _proof(TOKEN, f"name {payload}", self.host, self.peer)
        self.stored = bytes.fromhex(payload).decode("ascii")
        proof = _proof(TOKEN, f"name-saved {payload}", self.host, self.peer)
        self.reply(f"NAME-SAVED {payload} {proof}")
        return len(frame)


class NoteNameTests(unittest.TestCase):
    def test_uses_display_name_or_note_by_default(self):
        self.assertEqual(local_dictation_note_name({"PalaBridgeName": "Pala Bridge"}), "Note")
        self.assertEqual(local_dictation_note_name({"PalaBridgeName": "Kubo 2"}), "Kubo 2")
        self.assertEqual(local_dictation_note_name({"PalaBridgeName": "Name\\ninvalid"}), "Note")

    def test_sends_and_verifies_name_without_selecting_recordings(self):
        device = NamedPala()
        self.assertEqual(transfer_note_name(device, (TOKEN,), "Kubo"), "Kubo")
        self.assertEqual(device.stored, "Kubo")
        self.assertEqual([command for command, _ in device.calls], ["HELLO", "AUTH", "NAME", "CLOSE"])

    def test_rejects_acknowledgment_without_a_valid_session_proof(self):
        class WrongProofPala(NamedPala):
            def reply(self, response):
                if response.startswith("NAME-SAVED "):
                    status, name, _ = response.split()
                    response = f"{status} {name} " + ("0" * 64)
                super().reply(response)

        with self.assertRaisesRegex(UsbSyncError, "did not verify"):
            transfer_note_name(WrongProofPala(), (TOKEN,), "Kubo")

    def test_rejects_name_outside_display_limit_before_using_the_link(self):
        device = NamedPala()
        with self.assertRaises(ValueError):
            transfer_note_name(device, (TOKEN,), "Very long name")
        self.assertEqual(device.calls, [])
