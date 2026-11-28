import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import wave
from zipfile import ZipFile
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import stage3_card_fixture as fixture


class Stage3CardFixtureTest(unittest.TestCase):
    def setUp(self):
        sandbox = Path(self.enterContext(tempfile.TemporaryDirectory(prefix="stage3-card-")))
        self.bundle = sandbox / "bundle"
        self.manifest = fixture.build(self.bundle)
        self.card = self.bundle / "card-root"

    def test_card_layout_pcm_reservations_and_checksum_bundle(self):
        report = self.manifest
        self.assertEqual(report["pending"], {"audio": 2, "delivery": 1})
        self.assertEqual(len(report["notes"]), 5)
        self.assertEqual((self.card / ".pala-recordings-v2").read_bytes(), b"\x01\x00\x00\x00")
        helper = os.environ.get("PALA_TEST_MCP_NOTE_HELPER")
        for row in report["notes"]:
            with self.subTest(note=row["note"]):
                audio = self.card / row["wav"]
                with wave.open(str(audio), "rb") as wav:
                    self.assertEqual((wav.getnchannels(), wav.getsampwidth(), wav.getframerate()), (1, 2, 16000))
                    self.assertEqual(wav.getnframes(), row["samples"])
                    self.assertEqual(f"{zlib.crc32(wav.readframes(wav.getnframes())):08x}", row["pcm_crc32"])
                if helper:
                    subprocess.run([helper, "check-wave", audio, str(row["samples"])], check=True)

                self.assertIn(f"created_utc={row['captured_utc']}\n", audio.with_suffix(".wav.meta").read_text())
                job = self.card / ".pala-work" / f"{row['note']}.job"
                if row["state"] == "delivered":
                    self.assertFalse(job.exists())
                    self.assertRegex(audio.with_suffix(".delivery").read_text(), r"\A[0-9a-f]{64}\Z")
                else:
                    self.assertFalse(audio.with_suffix(".delivery").exists())
                    self.assertEqual(struct.unpack("<i4s21s", job.read_bytes()),
                                     (row["note"], b"UTC1", row["captured_utc"].encode("ascii") + b"\0"))
                    if helper:
                        subprocess.run([helper, "check-job", job, str(row["note"]), row["captured_utc"]], check=True)
                self.assertEqual(audio.with_suffix(".txt").is_file(), row["state"] != "audio")

        with ZipFile(self.bundle / "stage3-card-fixture.zip") as archive:
            self.assertIsNone(archive.testzip())
            self.assertEqual(json.loads(archive.read("manifest.json")), self.manifest)
            for name, row in report["before"].items():
                self.assertEqual(hashlib.sha256(archive.read(f"card-root/{name}")).hexdigest(), row["sha256"])
            for phase in fixture.PHASES:
                lines = archive.read(f"SHA256-{phase}.txt").decode("ascii").splitlines()
                self.assertEqual(dict(line.split("  ", 1)[::-1] for line in lines),
                                 {name: row["sha256"] for name, row in report[phase].items()})
        self.assertFalse(any(fixture.check(report, self.card, "before").values()))
        with self.assertRaises(FileExistsError):
            fixture.build(self.bundle)

    def test_after_delete_checks_preservation_and_rejects_leftovers(self):
        report = self.manifest
        after = self.bundle / "after"
        for source in [self.card / ".pala-recordings-v2", *(self.card / "recordings" / "202612").glob("*")]:
            expected = after / source.relative_to(self.card)
            expected.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, expected)

        self.assertGreater(report["logical_bytes_to_remove"], 4 * 1024 * 1024)
        self.assertFalse(any(fixture.check(report, after, "after-delete").values()))
        (after / "unrelated-device-file").write_bytes(b"device state")
        self.assertFalse(any(fixture.check(report, after, "after-delete", allow_other_files=True).values()))

        receipt = next(after.rglob("*.delivery"))
        receipt.write_bytes(b"0" * 64)
        old_wav = next(row["wav"] for row in report["notes"] if row["state"] == "audio")
        (after / old_wav).parent.mkdir()
        shutil.copyfile(self.card / old_wav, after / old_wav)
        issues = fixture.check(report, after, "after-delete", allow_other_files=True)
        self.assertEqual(issues, {
            "missing": [], "changed": [receipt.relative_to(after).as_posix()], "unexpected": [old_wav],
        })
