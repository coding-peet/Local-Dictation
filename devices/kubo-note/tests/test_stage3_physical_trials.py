"""PC-only qualification harness checks. Uses temporary synthetic media."""
from contextlib import redirect_stdout
import io
from pathlib import Path
import json
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import stage3_archive_trial as archive
import stage3_card_fixture as fixture
import stage3_capture_trial as capture


def trace_rows(case, pcm, edge=100):
    vals = [
        ["pcm", [edge, (edge + 350) & 0xffffffff, (edge + 355) & 0xffffffff]],
        ["archive", [edge, 1073796179]],
        ["counts", [edge, len(pcm) // 2, len(pcm) // 2, 0, 1]],
        ["microphone", [edge, 1]],
        ["crc", [edge, zlib.crc32(pcm)]],
    ]
    if case == "interrupt":
        vals += [["speech-start", [(edge - 100) & 0xffffffff]], ["speech-end", [(edge + 25) & 0xffffffff, 1, 0]]]
    else:
        vals += [["lock", [edge, edge + 5400, edge + 6200, 1 if case == "long" else 2]]]
    return vals


class CaptureTests(unittest.TestCase):
    def test_device_lines_and_duplicate_speech(self):
        self.assertEqual(capture.parse("[S3] pcm edge=6 at=356 now=358"), ("pcm", [6, 356, 358]))
        self.assertEqual(capture.parse("[RecCRC] e=6 crc=1234abcd"), ("crc", [6, 0x1234abcd]))
        self.assertIsNone(capture.parse("PALA_CONFIG_V1 private reply"))
        rows = trace_rows("interrupt", bytes(32), 0xfffffffe)
        result = capture.timing(rows, "interrupt")
        self.assertEqual((result["edge_to_output_stop_ms"], result["output_stop_to_pcm_ms"]), (25, 325))
        for bad in (rows[:-1], rows + [rows[-2]], rows[:-1] + [["speech-end", [400, 1, 0]]]):
            with self.assertRaises(ValueError):
                capture.timing(bad, "interrupt")

    def test_watch_runs_cues_from_device_pcm_and_writes_a_replayable_trace(self):
        from builtins import print as write_line
        from contextlib import nullcontext
        from unittest.mock import patch
        import usb_sync
        import note_settings
        import serial.tools.list_ports
        timer = SimpleNamespace(ticks=0)
        timer.monotonic = lambda: timer.ticks / 10
        items = iter([(0.9, "[S3] pcm edge=100"),
                      (1.0, " at=450 now=650\n"),
                      (316, "[S3] lock edge=100 locked=5500 released=6500 stop=1\n"),
                      (316.1, "[S3] microphone edge=100 intact=1\n"),
                      (316.2, "[RecCheck] e=100 in=5040000 out=5040000 ov=0 ok=1\n"),
                      (316.3, "[RecCRC] e=100 crc=0000abcd\n"),
                      (316.4, "[S3] archive edge=100 note=1073796179\n")])
        upcoming = next(items)
        delivered_at = {}
        def receive(*_):
            nonlocal upcoming
            timer.ticks += 1
            if upcoming and timer.monotonic() >= upcoming[0]:
                line = upcoming[1].encode()
                upcoming = next(items, None)
                return line
            return b""
        def record_console(message, **kwargs):
            if "file" in kwargs:
                write_line(message, **kwargs)
            else:
                delivered_at[message] = timer.monotonic()
        port = SimpleNamespace(device="fixture", serial_number=capture.bench.DEV, vid=0x303a, pid=0x1001)
        store = SimpleNamespace(devices=lambda: {capture.bench.DEV: {"id": "fixture", "token": "fixture"}})
        with tempfile.TemporaryDirectory() as tmp, redirect_stdout(io.StringIO()), \
             patch.object(capture, "time", timer), patch.object(capture, "print", record_console, create=True), \
             patch.object(serial.tools.list_ports, "comports", return_value=[port]), \
             patch.object(note_settings, "EnrollmentStore", return_value=store), \
             patch.object(usb_sync, "_open_link", return_value=nullcontext(SimpleNamespace(read_until=receive))), \
             patch.object(usb_sync, "_paired_session"), patch.object(usb_sync, "_expect_ok"), \
             patch.object(usb_sync, "_exchange", return_value=["BENCH", capture.bench.DEV, capture.BUILD]):
            trace = Path(tmp)/"capture.jsonl"
            capture.watch(SimpleNamespace(trace=trace, state=Path(tmp), case="long"))
            rows = [json.loads(line) for line in trace.read_text().splitlines()]
            cues = [row for row in rows if row[0] == "cue"]
            self.assertEqual([row[1].split()[0] for row in cues], ["Sentence", "SPEAK:", "CONTINUE:", "STOP"])
            actual = [delivered_at[row[1]] for row in cues]
            for observed, expected in zip(actual, [290.8, 299.8, 301.3, 315.8]):
                self.assertGreaterEqual(observed, expected)
                self.assertLessEqual(observed - expected, .105)
            self.assertTrue(all(0 <= row[2] <= 105 for row in cues))
            self.assertEqual(rows[-1], ["finished", capture.timing(rows, "long")])

    def test_stop_buttons_and_microphone_fault(self):
        rows = trace_rows("short", bytes(1024))
        self.assertEqual(capture.timing(rows, "short")["stop_button"], 2)
        rows[-1][1][-1] = 1
        with self.assertRaises(ValueError):
            capture.timing(rows, "short")
        rows[-1][1][-1] = 2
        rows[3][1][-1] = 0
        with self.assertRaises(ValueError):
            capture.timing(rows, "short")

    def test_long_download_transcript_and_part_integrity(self):
        head = b"\x01\x03" * 4800000
        tail = b"\x12\x34" * 240000
        note = 1073796179
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            folder = root / "recordings/202611"
            folder.mkdir(parents=True)
            wav = folder / f"note_{note}.wav"
            second = folder / (wav.name + ".p1")
            wav.write_bytes(capture.pack_wav(head))
            second.write_bytes(capture.pack_wav(tail))
            manifest = [(0, len(head), zlib.crc32(head)), (1, len(tail), zlib.crc32(tail)), (2, 0, 0)]
            (folder / (wav.name + ".parts")).write_bytes(b"".join(
                struct.pack("<IIII", i, n, crc, 0x50415254 ^ i ^ n ^ crc) for i, n, crc in manifest))
            wav.with_suffix(".txt").write_text(capture.PHRASE, encoding="utf8")
            download, listing, trace = root / "download.wav", root / "listing.html", root / "trace.jsonl"
            download.write_bytes(capture.pack_wav(head + tail))
            link = f"<a href='/wav?num={note}'>Download WAV</a>"
            listing.write_text(link)
            events = trace_rows("long", head + tail)
            rows = [["trial", "long", capture.bench.DEV, capture.BUILD], *events,
                    ["finished", capture.timing(events, "long")]]
            trace.write_text("\n".join(json.dumps(row) for row in rows))
            args = SimpleNamespace(card=root, trace=trace, download=download, listing=listing, boundary=None)
            with redirect_stdout(io.StringIO()):
                capture.verify(args)
            listing.write_text(link * 2)
            with self.assertRaisesRegex(ValueError, "one download link"):
                capture.verify(args)
            listing.write_text(link)
            second.write_bytes(capture.pack_wav(b"\0\0" + tail[2:]))
            with self.assertRaisesRegex(ValueError, "manifest"):
                capture.verify(args)


class ArchiveTests(unittest.TestCase):
    def test_full_month_pair_and_damage(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "fixture"
            manifest = fixture.build(root)
            card = root / "card-root"
            disk = {"serial": "fixture", "total": 2000000000, "free": 1800000000}
            settings = dict(device=capture.bench.DEV, build=capture.BUILD, sdTotalMiB=1800,
                            sdUsedMiB=80, pendingAudio=2, pendingDelivery=1)
            html = ("<h1>Delete 2026-11?</h1><p>Pending audio: 2</p><p>Pending delivery: 1</p>"
                    "<input name='month' value='202611'><input name='legacy' value='0'>")
            def before():
                return archive.snapshot(manifest, card, "before", settings, disk, confirmation=html)
            unrelated = card / "recordings/202611/existing.wav"
            unrelated.write_bytes(b"keep")
            with self.assertRaisesRegex(ValueError, "Unrelated files"):
                before()
            unrelated.unlink()
            base = before()
            for name in manifest["removed"]:
                (card / name).unlink()
            new = dict(settings, sdUsedMiB=76, pendingAudio=0, pendingDelivery=0)
            more_space = dict(disk, free=disk["free"] + 4546560)
            result = archive.snapshot(manifest, card, "after-delete", new, more_space, base)
            self.assertEqual(result["free_gain_bytes"], 4546560)
            self.assertEqual(result["logical_bytes"], 4544738)
            with self.assertRaisesRegex(ValueError, "volume identity"):
                archive.snapshot(manifest, card, "after-delete", new, dict(more_space, serial="other"), base)
            with self.assertRaisesRegex(ValueError, "free-space"):
                archive.snapshot(manifest, card, "after-delete", new, disk, base)
            retained = next((card / "recordings/202612").glob("*.wav"))
            retained.write_bytes(b"broken")
            with self.assertRaisesRegex(ValueError, "manifest mismatch"):
                archive.snapshot(manifest, card, "after-delete", new, more_space, base)


if __name__ == "__main__":
    unittest.main()
