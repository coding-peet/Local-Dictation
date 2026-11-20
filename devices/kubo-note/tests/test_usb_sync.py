import hashlib
import hmac
import io
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import wave

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

import usb_sync
from usb_sync import UsbSyncError, sync_one


TOKEN = "test-pairing-token-for-local-tests"
PREFIX = b"KUBO_USB_V1 "


def sample_wav():
    stream = io.BytesIO()
    with wave.open(stream, "wb") as audio:
        audio.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
        audio.writeframes(b"\x00\x02" * 1400)
    return stream.getvalue()


class FakePala:
    def __init__(self, *, fail_read=False, fail_commit=False, empty=False, drop_last_read=False, drop_auth=False):
        self.audio = sample_wav()
        self.peer = "0123456789abcdef" * 2
        self.outbox = io.BytesIO()
        self.calls = []
        self.text = bytearray()
        self.saved_text = ("Úloha: kúpiť zápisník. " * 9).encode("utf-8")
        self.captured = "2026-11-19T23:15:00Z"
        self.fail_read = fail_read
        self.fail_commit = fail_commit
        self.empty = empty
        self.drop_last_read = drop_last_read
        self.drop_auth = drop_auth

    def reply(self, text):
        self.outbox = io.BytesIO(b"unrelated USB log\r\n" + PREFIX + text.encode() + b"\r\n")

    def proof(self, role):
        wire = f"KUBO_USB_V1 {role} {self.host} {self.peer}".encode()
        return hmac.new(TOKEN.encode(), wire, hashlib.sha256).hexdigest()

    def write(self, raw):
        self.assert_frame(raw)
        command, *args = raw[len(PREFIX):].decode().strip().split()
        self.calls.append((command, args))
        if command == "HELLO":
            self.host, = args
            self.reply(f"CHALLENGE {self.peer} {self.proof('device')}")
        elif command == "AUTH":
            if self.drop_auth:
                self.outbox = io.BytesIO(b"")
            else:
                self.reply("OK" if args == [self.proof("host")] else "ERROR AUTH")
        elif command in ("NEXT", "SELECT"):
            self.reply("EMPTY" if self.empty else f"NOTE 42 {len(self.audio)}")
        elif command == "META":
            self.reply(f"META {self.captured}")
        elif command == "EXPORT":
            self.reply(f"TRANSCRIPT 42 {len(self.saved_text)} {self.captured}")
        elif command == "READTXT":
            offset, length = map(int, args)
            self.reply(f"DATA {offset} {self.saved_text[offset:offset + length].hex()}")
        elif command == "READ":
            if self.fail_read:
                self.reply("ERROR READ")
            else:
                offset, length = map(int, args)
                reply = f"DATA {offset} {self.audio[offset:offset + length].hex()}"
                if self.drop_last_read and offset + length == len(self.audio):
                    self.drop_last_read = False
                    self.outbox = io.BytesIO(PREFIX + reply.encode()[:25])
                else:
                    self.reply(reply)
        elif command == "TEXT":
            self.expected_size, self.expected_hash = int(args[0]), args[1]
            self.reply("OK")
        elif command == "PUT":
            offset, data = args
            assert int(offset) == len(self.text)
            self.text.extend(bytes.fromhex(data))
            self.reply("OK")
        elif command == "COMMIT":
            assert len(self.text) == self.expected_size
            assert hashlib.sha256(self.text).hexdigest() == self.expected_hash
            self.reply("ERROR SAVE" if self.fail_commit else "SAVED 42")
        elif command == "CLOSE":
            self.reply("OK")
        return len(raw)

    def read_until(self, expected=b"\n", size=None):
        return self.outbox.readline(size)

    @staticmethod
    def assert_frame(raw):
        assert raw.startswith(PREFIX) and raw.endswith(b"\n")
        assert len(raw) <= 720
        assert TOKEN.encode() not in raw


class UsbSyncTests(unittest.TestCase):
    def test_watcher_waits_for_local_api_and_drains_once_per_paired_attachment(self):
        paired = SimpleNamespace(vid=0x303A, pid=0x1001, serial_number="ABC", device="COM24")
        moved = SimpleNamespace(vid=0x303A, pid=0x1001, serial_number="ABC", device="COM25")
        foreign = SimpleNamespace(vid=0x303A, pid=0x1001, serial_number="OTHER", device="COM30")
        wrong_hardware = SimpleNamespace(vid=1, pid=2, serial_number="ABC", device="COM31")
        frames = [[foreign, wrong_hardware], [paired], [paired], [paired], [], [moved], [moved]]
        synced = []

        class Stop:
            cycle = 0

            def is_set(self):
                return self.cycle == len(frames)

            def wait(self, _seconds):
                self.cycle += 1

        stop = Stop()
        usb_sync.watch_paired(
            lambda: frames[stop.cycle], {"abc": TOKEN}, lambda: stop.cycle >= 2,
            lambda port, token: synced.append((port, token, stop.cycle)),
            stop, self.fail,
        )
        self.assertEqual(synced, [("COM24", TOKEN, 2), ("COM25", TOKEN, 5)])

    def test_watcher_rechecks_new_firmware_while_the_data_cable_stays_attached(self):
        paired = SimpleNamespace(vid=0x303A, pid=0x1001, serial_number="ABC", device="COM24")
        synced = []

        class Stop:
            cycle = 0

            def is_set(self):
                return self.cycle == 3

            def wait(self, _seconds):
                self.cycle += 1

        def check(port, token):
            synced.append((port, token, stop.cycle))
            return True

        stop = Stop()
        usb_sync.watch_paired(lambda: [paired], {"abc": TOKEN}, lambda: True, check, stop, self.fail)
        self.assertEqual(synced, [("COM24", TOKEN, cycle) for cycle in range(3)])

    def test_serial_buffer_retains_second_frame_from_same_transfer(self):
        class Link:
            def __init__(self):
                self.incoming = io.BytesIO(b"first\nsecond\n")
                self.read_sizes = []

            @property
            def in_waiting(self):
                return len(self.incoming.getbuffer()) - self.incoming.tell()

            def read(self, count):
                self.read_sizes.append(count)
                return self.incoming.read(count)

        link = Link()
        port = usb_sync.BufferedSerial(link)
        self.assertEqual(port.read_until(b"\n", 16), b"first\n")
        self.assertEqual(port.read_until(b"\n", 16), b"second\n")
        self.assertEqual(link.read_sizes, [13])

    def test_local_transcript_returns_to_the_requested_note(self):
        device = FakePala()
        captured = []
        reply = "Trnava, žlté rožky. " * 35

        def transcribe(audio):
            captured.append(audio)
            return {"text": reply.strip()}

        result = sync_one(device, [TOKEN], transcribe, note=42)
        self.assertEqual(result.note, 42)
        self.assertEqual(result.audio_bytes, len(device.audio))
        self.assertEqual(captured, [device.audio])
        self.assertEqual(device.text.decode(), reply.strip())
        self.assertIn(("SELECT", ["42"]), device.calls)
        self.assertGreater(sum(name == "PUT" for name, _ in device.calls), 1)
        self.assertEqual(device.calls[-1][0], "CLOSE")

    def test_reads_existing_transcript_without_selecting_wav_or_writing_sd(self):
        device = FakePala()
        text, captured = usb_sync.read_saved(device, [TOKEN], 42)
        self.assertEqual(text, device.saved_text.decode("utf-8"))
        self.assertEqual(captured, device.captured)
        self.assertEqual([name for name, _ in device.calls],
                         ["HELLO", "AUTH", "EXPORT", "READTXT", "READTXT", "READTXT", "CLOSE"])

    def test_existing_usb_note_flows_to_one_bound_task_without_asr(self):
        from pala_google import route_saved

        device = FakePala()
        device.saved_text = "Úloha: skontrolovať zápisník".encode("utf-8")
        text, captured = usb_sync.read_saved(device, [TOKEN], 42)
        verified_destination = {"id": "tasks-list", "title": "Approved"}
        config = {"google_routes": {"tasks": verified_destination}}
        with (tempfile.TemporaryDirectory() as folder,
              patch("pala_google.deliver_google", return_value="provider-task") as lookup):
            args = (Path(folder), config, 42, captured, text, "fixture-paired-device")
            self.assertEqual(route_saved(*args), ["done"])
            self.assertEqual(route_saved(*args), ["done"])
        lookup.assert_called_once()
        self.assertEqual(lookup.call_args.args[2:6],
                         ("tasks", verified_destination, captured, "skontrolovať zápisník"))
        self.assertNotIn("READ", [name for name, _ in device.calls])
        self.assertNotIn("COMMIT", [name for name, _ in device.calls])

    def test_queues_transcript_before_commit_but_acknowledges_only_after_saved(self):
        device = FakePala()
        trace = []

        def stage(note, captured, text):
            trace.append((note, captured, text, device.calls[-1][0]))
            return lambda: trace.append(device.calls[-1][0])

        result = sync_one(device, [TOKEN], lambda _audio: {"text": "Úloha, upozorniť"},
                          stage_transcript=stage)
        self.assertEqual(result.note, 42)
        self.assertEqual(trace, [(42, device.captured, "Úloha, upozorniť", "READ"), "COMMIT"])

        trace.clear()
        device = FakePala(fail_commit=True)
        with self.assertRaisesRegex(UsbSyncError, "SAVE"):
            sync_one(device, [TOKEN], lambda _audio: {"text": "Úloha, upozorniť"},
                     stage_transcript=stage)
        self.assertEqual(trace, [(42, device.captured, "Úloha, upozorniť", "READ")])

    def test_retries_the_same_final_block_after_a_truncated_reply(self):
        device = FakePala(drop_last_read=True)

        def transcribe(audio):
            self.assertEqual(audio, device.audio)
            return {"text": "Trnava"}

        ticks = iter(step / 10 for step in range(10000))
        with patch("usb_sync.time.monotonic", side_effect=ticks.__next__):
            result = sync_one(device, [TOKEN], transcribe)
        self.assertEqual(result.note, 42)
        reads = [args for name, args in device.calls if name == "READ"]
        self.assertEqual(reads[-2], reads[-1])

    def test_unpaired_device_is_rejected_before_reading_wav(self):
        device = FakePala()
        with self.assertRaisesRegex(UsbSyncError, "paired"):
            sync_one(device, ["other-device-token"], self.fail)
        self.assertEqual([name for name, _ in device.calls], ["HELLO"])

    def test_closes_the_proven_session_when_auth_acknowledgment_is_lost(self):
        device = FakePala(drop_auth=True)
        ticks = iter(step / 10 for step in range(10000))
        with patch("usb_sync.time.monotonic", side_effect=ticks.__next__):
            with self.assertRaisesRegex(UsbSyncError, "No complete USB sync reply"):
                sync_one(device, [TOKEN], self.fail)
        self.assertEqual([name for name, _ in device.calls], ["HELLO", "AUTH", "CLOSE"])

    def test_interrupted_download_never_submits_a_transcript(self):
        device = FakePala(fail_read=True)
        with self.assertRaisesRegex(UsbSyncError, "READ"):
            sync_one(device, [TOKEN], self.fail)
        self.assertNotIn("COMMIT", [name for name, _ in device.calls])
        self.assertEqual(device.calls[-1][0], "CLOSE")

    def test_save_error_is_not_success(self):
        device = FakePala(fail_commit=True)
        with self.assertRaisesRegex(UsbSyncError, "SAVE"):
            sync_one(device, [TOKEN], lambda audio: {"text": "Trnava"})

    def test_empty_queue_does_not_call_asr(self):
        device = FakePala(empty=True)
        self.assertIsNone(sync_one(device, [TOKEN], self.fail))
        self.assertEqual(device.calls[-1][0], "CLOSE")

    def test_empty_asr_result_is_not_persisted(self):
        device = FakePala()
        with self.assertRaisesRegex(UsbSyncError, "empty"):
            sync_one(device, [TOKEN], lambda audio: {"text": "  "})
        self.assertNotIn("TEXT", [name for name, _ in device.calls])


if __name__ == "__main__":
    unittest.main()
