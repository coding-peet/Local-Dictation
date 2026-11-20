import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bridge_activity import BridgeActivity
from test_usb_sync import FakePala, TOKEN
from usb_sync import sync_one


class BridgeActivityTests(unittest.TestCase):
    def test_overlapping_steps_restore_the_active_phase_and_remove_the_idle_file(self):
        with tempfile.TemporaryDirectory() as directory:
            activity = BridgeActivity(Path(directory), "network")
            with activity.step("processing"):
                path, = Path(directory).glob("bridge-activity-*.json")
                self.assertEqual(json.loads(path.read_text())["Phase"], "processing")
                with activity.step("google"):
                    self.assertEqual(json.loads(path.read_text())["Phase"], "google")
                self.assertEqual(json.loads(path.read_text())["Phase"], "processing")
            self.assertFalse(path.exists())

    def test_usb_sync_reports_each_recording_stage(self):
        from contextlib import contextmanager
        phases = []

        class Reporter:
            @contextmanager
            def step(self, phase):
                phases.append(phase)
                yield

        sync_one(FakePala(), (TOKEN,), lambda _: {"text": "Synthetic note"}, activity=Reporter())
        self.assertEqual(phases, ["receiving", "transcribing", "returning"])

    def test_an_error_clears_activity_without_writing_error_content(self):
        with tempfile.TemporaryDirectory() as directory:
            activity = BridgeActivity(Path(directory), "usb")
            with self.assertRaises(ValueError):
                with activity.step("transcribing"):
                    raise ValueError("synthetic private content")
            self.assertEqual(list(Path(directory).iterdir()), [])
