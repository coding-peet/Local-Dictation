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
        self.bridge = Bridge(Path(state), {"destination": "google", "timezone": "Europe/Bratislava", "calendar": "primary"})

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

    def test_early_morning_tomorrow_requires_an_explicit_choice(self):
        import json
        from unittest.mock import patch

        class FixedTime(dt.datetime):
            @classmethod
            def now(cls, zone=dt.timezone.utc):
                return cls(2030, 11, 23, 1, 0, tzinfo=dt.timezone.utc).astimezone(zone)

        action = dict(kind="event", title="Veterinár", due="2030-11-24T15:00",
                      end="2030-11-24T15:30", minutes=-1, yearless=False,
                      originalTimeText="zajtra o tretej")
        request = dict(pala_note=3, pala_created="2030-11-23T00:30:00Z",
                       pala_transcript="Pridaj do kalendára zajtra o tretej veterinára",
                       input="", instructions="")
        with patch("bridge.codex", return_value=json.dumps(dict(title="Veterinár", summary="Plán", apple=[action]))):
            _, events = self.bridge.memories(request, {"properties": {}, "required": []})
        proposal = events[0]
        self.assertEqual([choice["due"] for choice in proposal["choices"]],
                         ["2030-11-23T15:00", "2030-11-24T15:00"])
        with patch("bridge.dt.datetime", FixedTime), patch.object(self.bridge, "deliver") as deliver:
            with self.assertRaises(ValueError):
                self.bridge.confirm({"id": proposal["id"], "accept": True})
            deliver.assert_not_called()
            self.bridge.confirm({"id": proposal["id"], "accept": True, "choice": 0})
            self.bridge.confirm({"id": proposal["id"], "accept": True, "choice": 0})
            deliver.assert_called_once()
            self.assertEqual(deliver.call_args.args[0]["due"], "2030-11-23T15:00")
            self.assertEqual(deliver.call_args.args[0]["end"], "2030-11-23T15:30")


    def test_calendar_retry_retains_the_confirmed_date_and_original_target(self):
        import json
        from unittest.mock import patch

        class FixedTime(dt.datetime):
            @classmethod
            def now(cls, zone=dt.timezone.utc):
                return cls(2030, 11, 23, 1, 0, tzinfo=dt.timezone.utc).astimezone(zone)

        self.bridge.config["calendar"] = "original"
        action = dict(kind="event", title="Veterinár", due="2030-11-24T15:00",
                      end="2030-11-24T15:30", minutes=-1, yearless=False,
                      originalTimeText="zajtra o tretej")
        request = dict(pala_note=3, pala_created="2030-11-23T00:30:00Z",
                       pala_transcript="Pridaj do kalendára zajtra o tretej veterinára",
                       input="", instructions="")
        schema = {"properties": {}, "required": []}
        with patch("bridge.codex", return_value=json.dumps(dict(title="Veterinár", summary="Plán", apple=[action]))):
            _, events = self.bridge.memories(request, schema)
        decision = {"id": events[0]["id"], "accept": True, "choice": 0}
        with patch("bridge.dt.datetime", FixedTime), patch.object(self.bridge, "deliver", side_effect=OSError):
            with self.assertRaises(OSError):
                self.bridge.confirm(decision)
        self.bridge.config["calendar"] = "different"
        with patch.object(self.bridge, "deliver") as deliver:
            with self.assertRaises(ValueError):
                self.bridge.memories(request, schema)
            deliver.assert_not_called()
            self.bridge.config["calendar"] = "original"
            self.bridge.config["timezone"] = "UTC"
            with self.assertRaises(ValueError):
                self.bridge.memories(request, schema)
            deliver.assert_not_called()
            self.bridge.config["timezone"] = "Europe/Bratislava"
            _, proposals = self.bridge.memories(request, schema)
            self.assertEqual(proposals, [])
            deliver.assert_called_once()
            self.assertEqual(deliver.call_args.args[0]["due"], "2030-11-23T15:00")

    def test_google_calendar_write_checks_the_saved_provider_event(self):
        import tempfile
        from unittest.mock import Mock, patch
        from google_store import apply_google

        events = Mock()
        config = {"timezone": "Europe/Bratislava", "calendar": "primary"}
        action = dict(kind="event", title="Veterinár", due="2030-11-23T15:00",
                      end="2030-11-23T15:30", minutes=-1)
        def insert(*, calendarId, body):
            self.assertEqual(calendarId, "primary")
            receipt = {"id": body["id"], "summary": action["title"],
                       "start": {"dateTime": "2030-11-23T14:00:00Z"},
                       "end": {"dateTime": "2030-11-23T14:30:00Z"},
                       "status": "confirmed"}
            events.get.return_value.execute.return_value = receipt
            return Mock()

        events.insert.side_effect = insert
        with (tempfile.TemporaryDirectory() as directory,
              patch("google_store.google_credentials"),
              patch("googleapiclient.discovery.build") as build):
            build.return_value.events.return_value = events
            apply_google(config, Path(directory), action, "appointment-key")
        events.insert.assert_called_once()
        events.get.assert_called_once_with(calendarId="primary", eventId=events.insert.call_args.kwargs["body"]["id"])

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
