from contextlib import contextmanager
from pathlib import Path
import sys
import tempfile
import unittest
import wave
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import stage2_trial as trial


class UsbReplyTimeout(RuntimeError):
    pass


class RawLink:
    def __init__(self, *chunks):
        self.chunks = iter(chunks)
        self.peer_id = "lab"

    def read_until(self, *_):
        chunk = next(self.chunks, b"")
        if isinstance(chunk, Exception):
            raise chunk
        return chunk

    def write(self, data):
        return len(data)


class RestartTraceTest(unittest.TestCase):
    def test_same_reader_preserves_split_events_during_both_protocols(self):
        raw = RawLink(b"[RecCheck] e=3 ", b"in=16 out=16 ov=0 ok=1\r\n", b"KUBO_USB_V1 OK\n",
                      b"[RecCRC] e=3 crc=12345678\n", b"PALA_CONFIG_V1 {}\n",
                      b"secret\n", b"=== Pala Note build-a ===\n")
        events = []
        link = trial.TraceLink(raw, events.append)
        self.assertEqual(link.peer_id, "lab")
        self.assertEqual(link.read_until(b"\n", 2200), b"")
        self.assertEqual(link.read_until(b"\n", 2200), b"")
        self.assertEqual(link.read_until(b"\n", 2200), b"KUBO_USB_V1 OK\n")
        self.assertEqual(link.read_until(b"\n", 4096), b"")
        self.assertEqual(link.read_until(b"\n", 4096), b"PALA_CONFIG_V1 {}\n")
        link.read_until(b"\n", 4096)
        link.read_until(b"\n", 4096)
        self.assertEqual(events, ["[RecCheck] e=3 in=16 out=16 ov=0 ok=1",
                                  "[RecCRC] e=3 crc=12345678", "=== Pala Note build-a ==="])

    def test_timeout_then_detach_does_not_replay_request_or_splice_lines(self):
        attempts, events, opened = [], [], []
        first = RawLink(b"[RecCRC] e=2 crc=12345678\n", b"[RecCRC] e=3 ", OSError("unplugged"))
        second = RawLink(b"crc=abcdef12\n", b"=== Pala Note build-a ===\n",
                         b"[RecCRC] e=3 crc=87654321\n")

        @contextmanager
        def connect():
            link = first if not opened else second
            opened.append(link)
            yield link

        def request(link):
            attempts.append(link)
            link.read_until(b"\n", 2200)
            raise UsbReplyTimeout()

        clock = iter(range(25))
        result = trial.collect_restart(connect, request, "build-a", events.append, 13,
                                       (UsbReplyTimeout,), now=clock.__next__, pause=lambda _: None)
        self.assertEqual(len(attempts), 1)
        self.assertEqual(len(opened), 2)
        self.assertEqual(result["boot_banners"], 1)
        self.assertEqual(result["restart_ticket"], None)
        self.assertIn("[RecCRC] e=2 crc=12345678", events)
        self.assertIn("[RecCRC] e=3 crc=87654321", events)
        self.assertNotIn("[RecCRC] e=3 crc=abcdef12", events)
        self.assertIn("[Stage2] restart_reply_unavailable", events)


class EvidenceTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.card = Path(temp.name)
        self.note = (1 << 30) | 1024 | 83
        self.utc = "2026-11-30T23:59:59Z"
        wav = self.card / "recordings" / "202611" / f"note_{self.note}.wav"
        wav.parent.mkdir(parents=True)
        self.wav = wav
        self.pcm = bytes(range(256)) * 250
        with wave.open(str(wav), "wb") as stream:
            stream.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
            stream.writeframes(self.pcm)
        wav.with_suffix(".wav.meta").write_text("created_utc=" + self.utc + "\n", encoding="utf8")
        self.rec = [
            "[RecBoot] edge=321 software_reset=1",
            "[RecBoot] edge=321 stage=home_drawn boot_ms=800",
            "[Rec] edge=321 first_pcm=339 latency_ms=18",
            "[RecTiming] edge=321 stage=first_write ms=9 elapsed=91",
            "[RecCheck] e=321 in=32000 out=32000 ov=0 ok=1",
            f"[RecCRC] e=321 crc={zlib.crc32(self.pcm):08x}",
            f"[Rec] archived /sdcard/recordings/202611/note_{self.note}.wav",
        ]
        self.boot = "=== Pala Note build-a ==="

    def test_wav_verification_is_scoped_to_one_boot_and_requires_archive_id(self):
        lines = ["[RecCheck] e=321 in=3 out=3 ov=0 ok=1", self.boot, *self.rec]
        out = trial.verify_recording(self.card, self.note, self.utc, 321, lines,
                                     build="build-a", boot=1, software_restart=True,
                                     crosses="2026-12-01T00:00:00Z")
        self.assertEqual(out["frames"], 32000)
        self.assertEqual(out["duration_s"], 2)
        self.assertTrue(out["crossed_utc"])
        self.assertEqual(out["timings"]["first_write"]["since_rec_ms"], 91)
        self.assertEqual((out["first_pcm_ms"], out["first_write_ms"]), (18, 91))
        self.assertEqual(out["boot_marks_ms"]["home_drawn"], 800)
        for contaminated in (lines + ["[RecCRC] e=654 crc=12345678"],
                             lines[:-1] + ["[Stage2] usb_detached", lines[-1]]):
            with self.assertRaises(ValueError):
                trial.verify_recording(self.card, self.note, self.utc, 321, contaminated,
                                       build="build-a", boot=1)
        with self.assertRaises(ValueError):
            trial.verify_recording(self.card, self.note, self.utc, 321,
                                   lines + ["[RecTiming] edge=321 stage=first_write ms=9 elapsed=92"],
                                   build="build-a", boot=1)
        with self.assertRaises(ValueError):
            trial.verify_recording(self.card, self.note, self.utc, 321, lines[:-1], build="build-a", boot=1)
        with self.assertRaises(ValueError):
            trial.verify_recording(self.card, self.note, self.utc, 321, lines, build="wrong", boot=1)
        with self.assertRaises(ValueError):
            trial.verify_recording(self.card, self.note, self.utc, 321, lines, build="build-a", boot=1,
                                   crosses="2026-12-02T00:00:00Z")

    def test_wifi_delivery_requires_real_phases_exact_text_and_one_provider_task(self):
        expected = b"Short test transcript.\n"
        self.wav.with_suffix(".txt").write_bytes(expected)
        receipt = self.wav.with_suffix(".delivery")
        receipt.write_text("a" * 64, encoding="ascii")
        lines = [f"[BenchLink] link=wifi phase={p} note={self.note}"
                 for p in ("transcribed", "write", "confirmed")]
        provider = lambda path: {"matches": 1, "receipt": path.read_text()}
        out = trial.verify_delivery(self.card, self.note, expected, lines, "wifi", provider)
        self.assertTrue(out["transcript_match"])
        self.assertEqual(out["provider"]["matches"], 1)
        for altered in (lines[:2], lines[:2] + lines[1:], [line.replace("wifi", "usb") for line in lines]):
            with self.assertRaises(ValueError):
                trial.verify_delivery(self.card, self.note, expected, altered, "wifi", provider)
        with self.assertRaises(ValueError):
            trial.verify_delivery(self.card, self.note, b"Other", lines, "wifi", provider)
        with self.assertRaises(ValueError):
            trial.verify_delivery(self.card, self.note, expected, lines, "wifi",
                                  lambda _: {"matches": 2, "receipt": "a" * 64})

    def test_second_bridge_must_reconcile_held_receipt_without_new_write(self):
        expected = b"Existing text"
        self.wav.with_suffix(".txt").write_bytes(expected)
        self.wav.with_suffix(".delivery").write_text("a" * 64)
        a = [f"[BenchLink] link=usb phase=write note={self.note}",
             f"[BenchCut] phase=lose-receipt note={self.note}"]
        b = [f"[BenchLink] link=ble phase={phase} note={self.note}" for phase in ("reconcile", "delivered")]
        provider = lambda _: {"matches": 1, "receipt": "a" * 64}
        out = trial.verify_delivery(self.card, self.note, expected, b, "ble", provider, previous=a)
        self.assertEqual(out["path"], "reconcile")
        for bad in (b + [a[0]], b[1:], b + [f"[BenchLink] link=wifi phase=transcribed note={self.note}"]):
            with self.assertRaises(ValueError):
                trial.verify_delivery(self.card, self.note, expected, bad, "ble", provider, previous=a)

    def test_power_cut_checks_trigger_and_both_card_snapshots(self):
        trace = ["[BenchCut] phase=pcm note=-1 edge=7 window_ms=10000",
                 "[Stage2] usb_detached", self.boot]
        old = {"recordings/202610/note_1.wav": "old-hash"}
        repaired = dict(old, **{"recordings/202611/note_2.wav": "new-hash"})
        out = trial.verify_recovery(old, repaired, repaired, trace, "pcm", 7)
        self.assertEqual(out["originals_preserved"], 1)
        for bad in (dict(repaired, **{"recordings/202610/note_1.wav": "damaged"}), old):
            with self.assertRaises(ValueError):
                trial.verify_recovery(old, repaired, bad, trace, "pcm", 7)
        for bad in (trace[:2], trace[:1] + ["[BenchCut] phase=pcm expired", self.boot]):
            with self.assertRaises(ValueError):
                trial.verify_recovery(old, repaired, repaired, bad, "pcm", 7)


if __name__ == "__main__":
    unittest.main()
