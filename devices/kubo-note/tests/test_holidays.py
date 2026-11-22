import sys
import unittest
from datetime import date, datetime
from pathlib import Path
import tempfile
from types import SimpleNamespace
from unittest.mock import Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bridge import Handler
from holidays import holiday_snapshot, statutory_holidays
from note_web import save_bridge_preferences
from weather import LOCATION_ZONE


class HolidayTests(unittest.TestCase):
    def test_slovak_work_rest_days_and_temporary_exception(self):
        self.assertEqual(statutory_holidays("SK", 2026), tuple(date.fromisoformat(text) for text in (
            "2026-01-01", "2026-01-06", "2026-04-03", "2026-04-06",
            "2026-05-01", "2026-07-05", "2026-08-29", "2026-11-01",
            "2026-12-24", "2026-12-25", "2026-12-26",
        )))
        days = statutory_holidays("SK", 2027)
        self.assertIn(date(2027, 5, 8), days)
        self.assertIn(date(2027, 9, 15), days)
        self.assertIn(date(2027, 3, 26), days)
        self.assertIn(date(2027, 3, 29), days)
        self.assertNotIn(date(2027, 11, 17), days)

    def test_permanent_slovak_changes_apply_from_the_correct_year(self):
        self.assertIn(date(2023, 9, 1), statutory_holidays("SK", 2023))
        self.assertNotIn(date(2024, 9, 1), statutory_holidays("SK", 2024))
        self.assertIn(date(2024, 11, 17), statutory_holidays("SK", 2024))
        self.assertNotIn(date(2025, 11, 17), statutory_holidays("SK", 2025))

    def test_czech_holidays_apply_czech_law(self):
        days = statutory_holidays("CZ", 2026)
        self.assertEqual(len(days), 13)
        self.assertIn(date(2026, 5, 8), days)
        self.assertIn(date(2026, 10, 28), days)
        self.assertIn(date(2026, 11, 17), days)
        self.assertNotIn(date(2026, 1, 6), days)

    def test_snapshot_covers_two_years_and_previous_december_for_home_week(self):
        self.assertEqual(holiday_snapshot("SK", 2026),
                         "SK,2026,1226,01010106040304060501070508291101122412251226,"
                         "0101010603260329050105080705082909151101122412251226")

    def test_wifi_endpoint_uses_paired_bridge_preferences_without_weather(self):
        token = "paired-holidays-test-token"
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            save_bridge_preferences(state, "Test Bridge", "", "CZ")
            bridge = SimpleNamespace(state=state, config={
                "token": "legacy-unused-test-token", "device_tokens": {"fixture": token}})
            handler = Handler.__new__(Handler)
            handler.path = "/pala/holidays"
            handler.server = SimpleNamespace(bridge=bridge)
            handler.send_error = Mock()
            handler.send_json = Mock()
            handler.headers = {}
            handler.do_GET()
            handler.send_error.assert_called_once_with(401)
            handler.send_json.assert_not_called()

            handler.headers = {"Authorization": f"Bearer {token}"}
            handler.do_GET()
            year = datetime.now(LOCATION_ZONE).year
            handler.send_json.assert_called_once_with(200, {"holidays": holiday_snapshot("CZ", year)})
