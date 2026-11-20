import datetime as dt
from pathlib import Path
import sys
import unittest
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from appointment_dates import recording_time, resolve_yearless


class RecordingDateTests(unittest.TestCase):
    def test_capture_converts_utc_to_the_original_local_day(self):
        zone = ZoneInfo("Europe/Bratislava")
        self.assertEqual(
            recording_time("2026-11-20T23:30:00Z", zone),
            dt.datetime(2026, 11, 21, 0, 30, tzinfo=zone),
        )

    def test_missing_or_invalid_time_stays_unresolved(self):
        for value in (None, "", "2026-02-30T00:00:00Z", "2026-11-20T23:30:00"):
            with self.subTest(value=value):
                self.assertIsNone(recording_time(value, ZoneInfo("Europe/Bratislava")))

    def test_yearless_resolution_is_bounded_by_one_year(self):
        zone = ZoneInfo("Europe/Bratislava")
        captured = dt.datetime(2026, 12, 12, 9, 0, tzinfo=zone)
        self.assertEqual(
            resolve_yearless(dt.datetime(2026, 12, 11, 15, 0), captured, timed=True),
            dt.datetime(2027, 12, 11, 15, 0, tzinfo=zone),
        )
        self.assertIsNone(
            resolve_yearless(dt.datetime(2024, 2, 29, 15, 0), captured, timed=True),
        )

    def test_yearless_resolution_preserves_same_day_future_time(self):
        zone = ZoneInfo("Europe/Bratislava")
        captured = dt.datetime(2026, 11, 20, 9, 0, tzinfo=zone)
        self.assertEqual(
            resolve_yearless(dt.datetime(2025, 11, 20, 15, 0), captured, timed=True),
            dt.datetime(2026, 11, 20, 15, 0, tzinfo=zone),
        )




class BridgeRecordingDateTests(unittest.TestCase):
    def setUp(self):
        import tempfile
        from bridge import Bridge

        state = self.enterContext(tempfile.TemporaryDirectory())
        self.bridge = Bridge(Path(state), {"destination": "google", "timezone": "Europe/Bratislava"})

    def analyze(self, timestamp):
        import json
        from unittest.mock import patch

        action = dict(kind="event", title="Termín", due="2026-12-11T15:00",
                      end="2026-12-11T15:30", minutes=-1, yearless=True)
        request = dict(pala_note=7, pala_created=timestamp, pala_transcript="11. decembra o 15:00",
                       input="", instructions="")
        with patch("bridge.codex", return_value=json.dumps(
                dict(title="Termín", summary="Plán", apple=[action]))) as model:
            text, events = self.bridge.memories(request, {"properties": {}, "required": []})
        return json.loads(text), events, model.call_args.args[0]

    def test_bridge_resolves_the_year_from_capture_not_processing(self):
        _, events, prompt = self.analyze("2026-12-12T08:00:00Z")
        self.assertEqual(events[0]["due"], "2027-12-11T15:00")
        self.assertEqual(events[0]["end"], "2027-12-11T15:30")
        self.assertIn("2026-12-12T09:00+01:00", prompt)

    def test_unresolved_draft_cannot_be_confirmed_as_delivered(self):
        from unittest.mock import patch

        memory, events, _ = self.analyze("")
        self.assertEqual(memory["title"], "Termín")
        self.assertEqual(events, [])
        with self.bridge.database() as db:
            ident, status = db.execute("SELECT id,status FROM actions").fetchone()
        self.assertEqual(status, "needs-date")
        with patch.object(self.bridge, "deliver") as deliver:
            with self.assertRaises(ValueError):
                self.bridge.confirm({"id": ident, "accept": True})
            deliver.assert_not_called()

    def test_expired_pending_appointment_is_held_for_a_new_date(self):
        import json
        from unittest.mock import patch

        action = dict(kind="event", title="Termín", due="2024-12-11T15:00",
                      end="2024-12-11T15:30", minutes=-1)
        with self.bridge.database() as db:
            db.execute("INSERT INTO actions VALUES(?,?,?)", ("expired", json.dumps(action), "pending"))
        with patch.object(self.bridge, "deliver") as deliver:
            with self.assertRaises(ValueError):
                self.bridge.confirm({"id": "expired", "accept": True})
            deliver.assert_not_called()
        with self.bridge.database() as db:
            data, status = db.execute("SELECT data,status FROM actions WHERE id='expired'").fetchone()
        self.assertEqual(json.loads(data), action)
        self.assertEqual(status, "needs-date")


if __name__ == "__main__":
    unittest.main()
