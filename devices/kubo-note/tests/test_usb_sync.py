import hashlib
import hmac
import io
from itertools import count
import json
from pathlib import Path
import sys
import tempfile
import threading
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
        elif command in ("WORK-PROTOCOL", "CLOCK"):
            self.reply("ERROR COMMAND")
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
    def test_usb_password_save_accepts_a_late_reply_without_resending(self):
        from note_settings import PREFIX as CONFIG, SettingsError

        class SlowNote:
            now = 0
            writes = 0

            def write(self, raw):
                self.writes += 1
                return len(raw)

            @property
            def in_waiting(self):
                return 0

            def read(self, _size):
                self.now += .1
                if self.now >= 5:
                    return CONFIG + b'{"ok":true}\n'
                return b""

        for operation in ("auth.save", "get"):
            with self.subTest(operation=operation):
                note = SlowNote()
                link = usb_sync.BufferedSerial(note)
                with patch("usb_sync.time.monotonic", lambda: note.now), patch("usb_sync.time.sleep"):
                    if operation == "auth.save":
                        self.assertEqual(usb_sync.config_exchange(link, {"op": operation}), {"ok": True})
                    else:
                        with self.assertRaises(SettingsError):
                            usb_sync.config_exchange(link, {"op": operation})
                self.assertEqual(note.writes, 1)

    def test_windows_usb_serial_matches_firmware_pairing_id(self):
        port = SimpleNamespace(device="COM25", vid=0x303A, pid=0x1001, serial_number="70:04:1D:D8:16:A8")
        identity = "70041dd816a8"
        self.assertEqual(usb_sync.paired_port([port], "COM25", {identity: TOKEN}), (identity, TOKEN))
        stop = threading.Event()
        seen = []
        def drain(name, token):
            seen.append((name, token))
            stop.set()
        usb_sync.watch_paired(lambda: [port], {identity: TOKEN}, lambda: True, drain, stop,
                              self.fail, flush=stop.set)
        self.assertEqual(seen, [("COM25", TOKEN)])
        port.serial_number = "70:04:1D:D8:16:A9"
        with self.assertRaises(UsbSyncError):
            usb_sync.paired_port([port], "COM25", {identity: TOKEN})

    def test_one_shot_recorder_sync_checks_clock_then_reads_work(self):
        from contextlib import nullcontext
        from unittest.mock import Mock

        port = SimpleNamespace(device="COM25", serial_number="70041dd816a8", vid=0x303a, pid=0x1001)
        link = FakePala(empty=True)
        store = Mock()
        store.tokens.return_value = {port.serial_number: TOKEN}
        store.devices.return_value = {port.serial_number: {"id": "peer", "token": TOKEN}}
        events = []

        def drain(link, token, bridge, device, stop, activity, *, only_note, one):
            self.assertFalse(stop.is_set())
            self.assertEqual((device, only_note, one), (port.serial_number, 42, True))
            events.append("work")
            return False, False

        with (
            tempfile.TemporaryDirectory() as tmp,
            patch("pair.STATE", Path(tmp)),
            patch("sys.argv", ["usb_sync", "--port", "COM25", "--note", "42"]),
            patch("serial.tools.list_ports.comports", return_value=[port]),
            patch("note_settings.EnrollmentStore", return_value=store),
            patch("bridge.Bridge"),
            patch("pala_google.UsbGoogleDelivery"),
            patch.object(usb_sync, "_open_link", return_value=nullcontext(link)),
            patch.object(usb_sync, "transfer_usb_clock", side_effect=lambda *_: events.append("clock")),
            patch.object(usb_sync, "_recorder_protocol", return_value=True),
            patch.object(usb_sync, "_drain_recorder_work", side_effect=drain),
        ):
            usb_sync.main()
        self.assertEqual(events, ["clock", "work"])

    def test_usb_clock_sets_signed_utc_and_verifies_readback(self):
        class ClockNote(FakePala):
            def write(self, raw):
                if not raw.startswith(PREFIX + b"CLOCK "):
                    return super().write(raw)
                self.assert_frame(raw)
                verb, utc, signature = raw[len(PREFIX):].decode().split()
                self.calls.append((verb, [utc]))
                assert signature == self.proof("clock " + utc)
                receipt = self.proof("clock-saved " + (utc if not self.fail_commit else "0"))
                self.reply("CLOCK-SAVED " + receipt)
                return len(raw)

        self.assertTrue(hasattr(usb_sync, "transfer_usb_clock"))
        with patch("usb_sync.time.time", return_value=1795649700.9):
            note = ClockNote()
            self.assertTrue(usb_sync.transfer_usb_clock(note, (TOKEN,)))
            self.assertEqual(note.calls[-2], ("CLOCK", ["1795649700"]))
            self.assertEqual(note.calls[-1][0], "CLOSE")
            note.fail_commit = True
            with self.assertRaises(UsbSyncError):
                usb_sync.transfer_usb_clock(note, (TOKEN,))
            self.assertFalse(usb_sync.transfer_usb_clock(FakePala(), (TOKEN,)))

    def test_holiday_sync_verifies_signed_receipt_for_exact_dates(self):
        from holidays import holiday_snapshot
        snapshot = holiday_snapshot("SK", 2026)

        class HolidayNote(FakePala):
            def write(self, raw):
                if not raw.startswith(PREFIX + b"HOLIDAYS "):
                    return super().write(raw)
                self.assert_frame(raw)
                verb, payload, proof = raw[len(PREFIX):].decode().split()
                self.calls.append((verb, [payload]))
                assert payload == snapshot and len(raw) <= 256
                assert hmac.compare_digest(self.proof("holidays " + payload), proof)
                receipt_date = payload.replace("2026", "2027") if self.fail_commit else payload
                self.reply("HOLIDAYS-SAVED " + self.proof("holidays-saved " + receipt_date))
                return len(raw)

        note = HolidayNote()
        usb_sync.transfer_holidays(note, (TOKEN,), snapshot)
        self.assertEqual([command for command, _ in note.calls], ["HELLO", "AUTH", "HOLIDAYS", "CLOSE"])

        wrong_receipt = HolidayNote(fail_commit=True)
        with self.assertRaisesRegex(UsbSyncError, "did not verify the saved holiday calendar"):
            usb_sync.transfer_holidays(wrong_receipt, (TOKEN,), snapshot)
        self.assertEqual(wrong_receipt.calls[-1][0], "CLOSE")

    def test_home_refresh_runs_even_when_transcription_is_down(self):
        paired = SimpleNamespace(vid=0x303A, pid=0x1001, serial_number="ABC", device="COM24")
        stop = threading.Event()
        refreshed = []

        def status(port, token):
            refreshed.append((port, token))
            stop.set()
            return True

        usb_sync.watch_paired(lambda: [paired], {"abc": TOKEN}, lambda: False,
                              lambda *_: self.fail("ASR unavailable"), stop, self.fail,
                              refresh_attachment=status)
        self.assertEqual(refreshed, [("COM24", TOKEN)])

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

    def test_usb_and_ble_keep_an_explicit_calendar_command_for_wifi_processing(self):
        from pala_google import UsbGoogleDelivery
        text = "Zapíš mi do kalendára kontrolu zajtra o tretej."
        for supported in (False, True):
            class CalendarPala(FakePala):
                def write(self, raw):
                    if raw == PREFIX + b"COMMIT CALENDAR\n" and not supported:
                        self.calls.append(("COMMIT", ["CALENDAR"]))
                        self.reply("ERROR COMMAND")
                        return len(raw)
                    return super().write(raw)

            with self.subTest(supported=supported), tempfile.TemporaryDirectory() as folder:
                router = UsbGoogleDelivery(Path(folder), {"destination": "google", "calendar": "primary"})
                device = CalendarPala()
                result = sync_one(device, [TOKEN], lambda _: {"text": text}, stage_transcript=router.stager("fixture"))
                self.assertEqual(result.note, 42)
                self.assertIn(("COMMIT", ["CALENDAR"]), device.calls)
                self.assertEqual(("COMMIT", []) in device.calls, not supported)
                with router.queue.database() as db:
                    self.assertEqual(db.execute("SELECT kind,status FROM google_actions").fetchall(),
                                     [("calendar", "needs-confirmation")])

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

    def test_watcher_retries_transient_handshake_failures_without_replugging(self):
        paired = SimpleNamespace(vid=0x303A, pid=0x1001, serial_number="ABC", device="COM24")
        attempts = []
        errors = []

        class Stop:
            seconds = 0

            def is_set(self):
                return self.seconds == 40

            def wait(self, seconds):
                self.seconds += seconds

        stop = Stop()

        def synchronize(port, token):
            self.assertEqual((port, token), ("COM24", TOKEN))
            attempts.append(stop.seconds)
            if stop.seconds < 20:
                raise usb_sync.UsbReplyTimeout("No complete USB sync reply.")
            # Old firmware's completed attachment must still stay settled.
            return False

        with patch("usb_sync.time.monotonic", side_effect=lambda: stop.seconds):
            usb_sync.watch_paired(lambda: [paired], {"abc": TOKEN}, lambda: True,
                                  synchronize, stop, errors.append)
        self.assertEqual(attempts, [0, 2, 4, 20])
        self.assertEqual([type(error) for error in errors], [usb_sync.UsbReplyTimeout] * 3)

    def test_usb_failure_report_identifies_failed_block_without_serial_payload(self):
        class FailedSecondBlock(FakePala):
            def write(self, frame):
                if frame.startswith(PREFIX + b"READ 96 "):
                    self.outbox = io.BytesIO(
                        b"unrelated private data: " + TOKEN.encode() +
                        b"\r\n" + PREFIX + b"ERROR READ\r\n")
                    return len(frame)
                return super().write(frame)

        port = FailedSecondBlock()
        with self.assertRaisesRegex(UsbSyncError, "Pala reported READ") as failed:
            sync_one(port, (TOKEN,), self.fail)

        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            with patch("sys.stderr", io.StringIO()) as log:
                usb_sync.report_usb_failure(state, failed.exception)
            payload = (state / "usb-sync-last-error.json").read_text(encoding="utf-8")
            diagnostic = json.loads(payload)
            self.assertEqual(diagnostic["Phase"], "receiving")
            self.assertEqual(diagnostic["Category"], "firmware")
            self.assertEqual(diagnostic["Command"], "READ")
            self.assertEqual(diagnostic["FirmwareCode"], "READ")
            self.assertEqual(diagnostic["Offset"], 96)
            self.assertEqual(diagnostic["Length"], 96)
            self.assertEqual(diagnostic["Attempt"], 1)
            for text in (payload, log.getvalue()):
                self.assertNotIn(TOKEN, text)
                self.assertNotIn("unrelated private data", text)
                self.assertNotIn(port.audio[:16].hex(), text)

    def test_usb_failure_report_never_logs_a_generic_exception_message(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            with patch("sys.stderr", io.StringIO()) as log:
                usb_sync.report_usb_failure(state, OSError(5, TOKEN))
            payload = (state / "usb-sync-last-error.json").read_text(encoding="utf-8")
            diagnostic = json.loads(payload)
            self.assertEqual(diagnostic["Category"], "io")
            self.assertEqual(diagnostic["Errno"], 5)
            self.assertNotIn(TOKEN, payload)
            self.assertNotIn(TOKEN, log.getvalue())

    def test_usb_failure_report_distinguishes_unmatched_data_from_no_reply(self):
        class UnmatchedBlock(FakePala):
            def write(self, frame):
                if frame.startswith(PREFIX + b"READ 96 "):
                    self.reply("DATA 192 " + "5a" * 96)
                    return len(frame)
                return super().write(frame)

        ticks = count()
        with patch("usb_sync.time.monotonic", side_effect=lambda: next(ticks) / 10):
            with self.assertRaisesRegex(UsbSyncError, "data at byte 96") as failed:
                sync_one(UnmatchedBlock(), (TOKEN,), self.fail)
        found = failed.exception.diagnostic
        self.assertEqual((found.Phase, found.Category, found.Command), ("receiving", "timeout", "READ"))
        self.assertEqual((found.Offset, found.Length, found.Attempt), (96, 96, 3))
        self.assertEqual((found.Observation, found.ObservedOffset, found.ObservedHexLength),
                         ("unmatched-data", 192, 192))

    def test_usb_failure_report_counts_partial_frame_bytes_without_logging_them(self):
        partial = PREFIX + b"DATA 96 " + b"5a" * 10

        class PartialFrame(FakePala):
            def write(self, frame):
                if frame.startswith(PREFIX + b"READ 96 "):
                    self.outbox = io.BytesIO(b"\r\n" + partial)
                    return len(frame)
                return super().write(frame)

        ticks = count()
        with patch("usb_sync.time.monotonic", side_effect=lambda: next(ticks) / 10):
            with self.assertRaisesRegex(UsbSyncError, "data at byte 96") as failed:
                sync_one(PartialFrame(), (TOKEN,), self.fail)
        details = failed.exception.diagnostic
        self.assertEqual((details.Category, details.Observation, details.ObservedBytes),
                         ("timeout", "partial-frame", len(partial)))
        self.assertNotIn(partial.decode("ascii"), repr(details))

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
                         ["HELLO", "AUTH", "WORK-PROTOCOL", "EXPORT", "READTXT", "READTXT", "READTXT", "CLOSE"])

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

    def test_lost_saved_receipt_promotes_delivery_only_after_exact_readback(self):
        class LostReceipt(FakePala):
            def __init__(self, mismatch):
                super().__init__()
                self.mismatch = mismatch

            def write(self, raw):
                size = super().write(raw)
                command = self.calls[-1][0]
                if command == "COMMIT":
                    self.saved_text = bytes(self.text)
                    self.outbox = io.BytesIO(b"")
                elif command == "EXPORT":
                    if self.mismatch == "wrong-note":
                        self.reply(f"TRANSCRIPT 43 {len(self.saved_text)} {self.captured}")
                    elif self.mismatch == "wrong-time":
                        self.reply(f"TRANSCRIPT 42 {len(self.saved_text)} 2026-11-20T00:00:00Z")
                    elif self.mismatch == "wrong-text":
                        self.saved_text = b"Different text"
                        self.reply(f"TRANSCRIPT 42 {len(self.saved_text)} {self.captured}")
                    elif self.mismatch == "legacy":
                        self.reply("ERROR COMMAND")
                return size

        for mismatch in (None, "wrong-note", "wrong-time", "wrong-text", "legacy", "no-route"):
            with self.subTest(mismatch=mismatch):
                device = LostReceipt(mismatch)
                acknowledged = []
                stager = None if mismatch == "no-route" else lambda *_: lambda: acknowledged.append(True)
                ticks = iter(step / 10 for step in range(10000))
                with patch("usb_sync.time.monotonic", side_effect=ticks.__next__):
                    if mismatch is None:
                        result = sync_one(device, [TOKEN], lambda _: {"text": "Exact text"}, stage_transcript=stager)
                        self.assertEqual(result.note, 42)
                    else:
                        with self.assertRaises(usb_sync.UsbReplyTimeout):
                            sync_one(device, [TOKEN], lambda _: {"text": "Exact text"}, stage_transcript=stager)
                self.assertEqual(acknowledged, [] if mismatch else [True])
                self.assertEqual(sum(command == "COMMIT" for command, _ in device.calls), 1)
                self.assertEqual(sum(command == "EXPORT" for command, _ in device.calls),
                                 0 if mismatch == "no-route" else 1)
                self.assertEqual(device.calls[-1][0], "CLOSE")

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

    def test_authentication_discards_a_delayed_challenge_for_an_old_nonce(self):
        class DelayedChallenge(FakePala):
            def write(self, raw):
                result = super().write(raw)
                if raw.startswith(PREFIX + b"HELLO "):
                    stale = usb_sync._proof(TOKEN, "device", "0" * 32, self.peer)
                    delayed = PREFIX + f"CHALLENGE {self.peer} {stale}\n".encode("ascii")
                    self.outbox = io.BytesIO(delayed + self.outbox.read())
                return result

        device = DelayedChallenge()
        usb_sync._paired_session(device, [TOKEN])
        self.assertEqual([command for command, _ in device.calls], ["HELLO", "AUTH"])

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
