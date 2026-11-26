"""Interrupt production Bridge workers at either side of the Note transcript commit."""
import asyncio
import hashlib
import io
import json
import os
import subprocess
import tempfile
import threading
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

from test_usb_sync import FakePala, PREFIX, TOKEN, sample_wav
from bridge import Bridge, Handler
from pala_google import UsbGoogleDelivery
import usb_sync


class InterruptedNote(FakePala):
    """A disconnected session loses its transfer buffer; committed SD files survive."""
    def __init__(self, cut):
        super().__init__()
        self.cut = cut
        self.offline = False
        self.queue = [42]
        self.files = {}
        self.commits = []

    def write(self, raw):
        if self.offline:
            raise OSError("link gone")
        command, *args = raw[len(PREFIX):].decode().strip().split()
        pending = [n for n in self.queue if n not in self.files]
        if command == "WORK-PROTOCOL":
            self.reply("ERROR COMMAND")
        elif command == "BATCH":
            self.reply(f"BATCH {len(pending)}")
        elif command == "BATCH-NEXT":
            self.reply(f"WORK {pending[0]} AUDIO" if pending else "EMPTY")
        elif command in ("NEXT", "SELECT"):
            if pending:
                self.selected = int(args[0]) if args else pending[0]
                self.text = bytearray()
                self.audio = sample_wav()[:-2] + self.selected.to_bytes(2, "little")
                self.reply(f"NOTE {self.selected} {len(self.audio)}")
            else:
                self.reply("EMPTY RECHECK" if command == "NEXT" else "ERROR NOTE")
        elif command == "EXPORT":
            n = int(args[0])
            if n not in self.files:
                self.reply("ERROR NOTE")
            else:
                self.saved_text = self.files[n]
                self.reply(f"TRANSCRIPT {n} {len(self.saved_text)} {self.captured}")
        elif command == "AUDIO":
            self.reply("ERROR COMMAND")  # Also exercise encrypted fallback blocks.
        elif command in ("TEXT", "COMMIT") and self.cut == command:
            if command == "COMMIT":
                self.save()
            self.cut = None
            self.offline = True
            self.queue.append(43)  # REC interrupts A and records B during link loss.
            raise OSError("complete disconnect")
        elif command == "COMMIT":
            self.save()
            self.reply(f"SAVED {self.selected}")
        else:
            return super().write(raw)
        return len(raw)

    def save(self):
        assert hashlib.sha256(self.text).hexdigest() == self.expected_hash
        assert self.selected not in self.files
        self.files[self.selected] = bytes(self.text)
        self.commits.append(self.selected)


DEVICE = "note-one"
CONFIG = {"google_routes": {"general": {"id": "target", "title": "Notes"}}}


def upload(note, wav):
    prefix = (f'--test\r\nContent-Disposition: form-data; name="pala_note"\r\n\r\n{note}\r\n'
              '--test\r\nContent-Disposition: form-data; name="file"; filename="note.wav"\r\n'
              'Content-Type: audio/wav\r\n\r\n').encode()
    return prefix + wav + b"\r\n--test--\r\n"


class ReplayTest(unittest.IsolatedAsyncioTestCase):
    async def run_interruption(self, transport, cut, state):
        from test_bluetooth_sync import GattPeer, BleLink

        note = InterruptedNote(cut)
        calls = []
        external = []
        config = {**CONFIG, "processing_mode": "transcribe"}
        # The isolated router has a valid destination for the receipt; the BLE
        # backlog only transcribes. This keeps provider output directly countable.
        def recognize(audio):
            n = int.from_bytes(audio[-2:], "little")
            calls.append(n)
            return {"text": f"Text from recording {n}"}

        stop = threading.Event()
        async def session():
            note.offline = False
            bridge = Bridge(state, config)
            bridge.local_transcription_ready = lambda: True
            bridge.transcribe_local = recognize
            router = UsbGoogleDelivery(state, CONFIG)
            stage = router.stager(DEVICE)
            if transport == "USB":
                return await asyncio.to_thread(usb_sync._drain_link, note, TOKEN, bridge, stop,
                                               stage, device=DEVICE)
            def exchange(frame):
                note.write(frame)
                note.read_until()
                return note.read_until()
            loop = asyncio.get_running_loop()
            link = BleLink(GattPeer(TOKEN, b"B" * 16, mtu=247, command=exchange), loop)
            await link.initialize(TOKEN)
            return await asyncio.to_thread(usb_sync.drain_bluetooth_backlog, link, TOKEN, bridge, DEVICE,
                                           stop, stage)
        with self.assertRaises(OSError):
            await session()
        queue = UsbGoogleDelivery(state, CONFIG).queue
        self.assertEqual(queue.pending(), [])
        self.assertEqual(calls, [42])
        self.assertEqual(note.commits, [42] if cut == "COMMIT" else [])
        await session()
        await session()
        self.assertEqual(note.commits, [42, 43])
        self.assertEqual(calls, [42, 43])
        self.assertEqual(note.files, {42: b"Text from recording 42", 43: b"Text from recording 43"})
        def send(kind, target, captured, body, key):
            external.append(body)
            return key
        keys = queue.pending()
        self.assertEqual(len(keys), 2)
        for key in keys + keys:
            self.assertEqual(queue.deliver(key, CONFIG["google_routes"], send), "done")
        self.assertEqual(external, ["Text from recording 42", "Text from recording 43"])
        with queue.database() as db:
            self.assertEqual(db.execute("SELECT count(*) FROM transcript_receipts").fetchone()[0], 0)

    async def test_usb_and_ble_real_workers_resume_before_and_after_commit(self):
        for transport in ("USB", "BLE"):
            for cut in ("TEXT", "COMMIT"):
                with self.subTest(transport=transport, cut=cut), tempfile.TemporaryDirectory() as folder:
                    await self.run_interruption(transport, cut, Path(folder))

    def test_wifi_lost_asr_reply_reuses_result_over_http_and_cable(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            wav = sample_wav()
            route = "/v1/audio/transcriptions"
            content_type = "multipart/form-data; boundary=test"
            first = Bridge(state, {})
            with patch.object(first, "_recognize_upload", return_value={"text": "original"}) as asr:
                original = first.dispatch(route, upload(42, wav), content_type, device=DEVICE)
                asr.assert_called_once_with(wav)
            # Drop the first HTTP response before the Note commits it and reconnect.
            second = Bridge(state, {})
            with patch.object(second, "_recognize_upload", side_effect=AssertionError("duplicate ASR")):
                self.assertEqual(second.dispatch(route, upload(42, wav), content_type, device=DEVICE), original)
                self.assertEqual(second.replay_transcription(DEVICE.upper(), 42, wav, self.fail), original)
            # The same WAV in a different Note slot or on a changed card is new work.
            for n, audio in ((43, wav), (42, wav[:-2] + b"xy")):
                with patch.object(second, "_recognize_upload", return_value={"text": "new"}) as asr:
                    self.assertEqual(second.dispatch(route, upload(n, audio), content_type, device=DEVICE)["text"], "new")
                    asr.assert_called_once_with(audio)

    def test_wifi_broken_socket_at_asr_and_at_organization_ack(self):
        def post(bridge, path, body, content_type, drop):
            handler = Handler.__new__(Handler)
            handler.server = Mock(bridge=bridge)
            handler.headers = {"Authorization": "Bearer " + TOKEN, "Content-Type": content_type,
                               "Content-Length": str(len(body))}
            handler.path = path
            handler.rfile = io.BytesIO(body)
            handler.wfile = Mock() if drop else io.BytesIO()
            if drop:
                handler.wfile.write.side_effect = BrokenPipeError("REC closed the socket")
            handler.connection = Mock()
            handler.send_header, handler.end_headers = Mock(), Mock()
            handler.send_response, handler.send_error = Mock(), Mock()
            handler.do_POST()
            handler.send_response.assert_called_once_with(200)
            handler.send_error.assert_not_called()
            return None if drop else json.loads(handler.wfile.getvalue())

        for cut in ("asr", "organization"):
            with self.subTest(cut=cut), tempfile.TemporaryDirectory() as folder:
                state = Path(folder)
                cfg = {**CONFIG, "token": "profile", "device_tokens": {DEVICE: TOKEN}}
                bridge = Bridge(state, cfg)
                asr = Mock(side_effect=lambda audio: {"text": f"Recording {audio[-2]}"})
                saved = {}
                model = Mock(return_value=json.dumps({"title": "title", "summary": "summary", "apple": []}))
                queue = UsbGoogleDelivery(state, cfg).queue
                def transfer(n, drop):
                    bridge._recognize_upload = asr
                    if n not in saved:
                        body = upload(n, sample_wav()[:-2] + bytes((n, 0)))
                        result = post(bridge, "/v1/audio/transcriptions", body, "multipart/form-data; boundary=test",
                                      drop == "asr")
                        if result is None:
                            return
                        saved[n] = result["text"]
                    memory = {"pala_note": n, "pala_created": None, "pala_transcript": saved[n],
                              "input": "", "instructions": "", "text": {"format": {"name": "pala_memory",
                              "schema": {"properties": {}, "required": []}}}}
                    post(bridge, "/v1/responses", json.dumps(memory).encode(), "application/json", drop == "organization")
                with patch("bridge.codex", model), patch("bridge.local_dictation_settings", return_value={}):
                    transfer(42, cut)
                    bridge = Bridge(state, cfg)
                    transfer(42, None)
                    transfer(43, None)
                self.assertEqual(list(saved), [42, 43])
                self.assertEqual(asr.call_count, 2)
                self.assertEqual(model.call_count, 2)
                keys = queue.pending()
                self.assertEqual(len(keys), 2)
                send = Mock(return_value="provider receipt")
                for key in keys * 2:
                    self.assertEqual(queue.deliver(key, CONFIG["google_routes"], send), "done")
                self.assertEqual(send.call_count, 2)

    def test_reconnection_preserves_original_delivery_route(self):
        for first_route in (False, True):
            with self.subTest(first_route=first_route), tempfile.TemporaryDirectory() as folder:
                router = UsbGoogleDelivery(Path(folder), CONFIG)
                router.stager(DEVICE, route=first_route)(42, None, "saved")
                # Reconnect over a link that normally requests a different route.
                resumed = router.stager(DEVICE, route=not first_route)(42, None, "saved")
                self.assertEqual(resumed.organize_calendar, not first_route)
                resumed()
                self.assertEqual(len(router.queue.pending()), int(first_route))
                self.assertEqual(router.queue.unconfirmed(DEVICE), [])

    def test_wifi_confirms_exact_usb_bytes_and_replays_after_lost_ack(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            queue = UsbGoogleDelivery(state, CONFIG)
            queue.stager(DEVICE)(42, None, "text on Note")
            self.assertEqual(queue.queue.pending(), [])
            bridge = Bridge(state, CONFIG)
            def confirm(text):
                receipt = json.dumps({"note": 42, "sha256": hashlib.sha256(text).hexdigest()})
                return bridge.dispatch("/pala/transcript-receipt", receipt, "application/json", device=DEVICE)
            with self.assertRaises(ValueError):
                confirm(b"changed")
            self.assertEqual(confirm(b"text on Note"), {"ok": True})
            # Disconnect after the Bridge committed the receipt but before its HTTP reply.
            self.assertEqual(confirm(b"text on Note"), {"ok": True})
            self.assertEqual(len(queue.queue.pending()), 1)
            self.assertEqual(queue.queue.unconfirmed(DEVICE), [])

    @unittest.skipUnless(os.getenv("PALA_TEST_OWNER_HELPER"), "requires built test_recording_owner helper")
    def test_another_bridge_after_uncertain_external_write(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            state_a, state_b = base / "first", base / "second"
            state_a.mkdir()
            state_b.mkdir()
            one = UsbGoogleDelivery(state_a, CONFIG)
            two = UsbGoogleDelivery(state_b, CONFIG)
            receipt = base / "note_042.bridge-owner"
            def try_organization(peer, router):
                # Execute the firmware's real SD ownership code after every reconnect.
                checked = subprocess.run([os.environ["PALA_TEST_OWNER_HELPER"], str(receipt), peer],
                                         capture_output=True, text=True, check=False)
                self.assertIn(checked.returncode, (0, 3))
                if checked.stdout != peer:
                    return False
                router.stager(DEVICE)(42, None, "same text")()
                return True
            self.assertTrue(try_organization("peer-A", one))
            key, = one.queue.pending()
            effects = []
            def uncertain(*args):
                effects.append(args[-1])
                raise OSError("provider wrote but reply lost")
            with self.assertRaises(OSError):
                one.queue.deliver(key, CONFIG["google_routes"], uncertain)
            self.assertFalse(try_organization("peer-B", two))
            self.assertEqual(two.queue.pending(), [])
            self.assertTrue(try_organization("peer-A", one))
            self.assertEqual(one.queue.deliver(key, CONFIG["google_routes"], uncertain), "uncertain")
            self.assertEqual(effects, [key])

    def test_overlapping_wifi_and_usb_after_disconnect_share_inflight_result(self):
        with tempfile.TemporaryDirectory() as folder:
            from concurrent.futures import ThreadPoolExecutor
            gate, entered = threading.Event(), threading.Event()
            wav = sample_wav()
            def recognize(_):
                entered.set()
                assert gate.wait(5)
                return {"text": "one"}
            bridge = Bridge(Path(folder), {})
            with ThreadPoolExecutor(2) as pool:
                first = pool.submit(bridge.replay_transcription, DEVICE, 1, wav, recognize)
                self.assertTrue(entered.wait(5))
                second = pool.submit(bridge.replay_transcription, DEVICE, 1, wav, self.fail)
                gate.set()
                self.assertEqual(first.result(), second.result())
