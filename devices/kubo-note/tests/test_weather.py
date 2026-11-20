import datetime as dt
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock
from urllib.parse import parse_qs, urlsplit

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bridge import Handler
from weather import ForecastCache, FORECAST_URL


def timestamp(year, month, day, hour=0):
    return int(dt.datetime(year, month, day, hour, tzinfo=dt.timezone.utc).timestamp())


def forecast_fixture():
    first = dt.date(2026, 11, 20)
    return {
        "daily_units": {"temperature_2m_min": "°C", "temperature_2m_max": "°C"},
        "daily": {
            "time": [(first + dt.timedelta(days=i)).isoformat() for i in range(7)],
            "temperature_2m_min": [-3.2] * 7,
            "temperature_2m_max": [8.3] * 7,
            "weather_code": [2] * 7,
        },
    }


class WeatherTests(unittest.TestCase):
    def test_fetch_requests_daily_celsius_forecast_in_local_timezone(self):
        query = parse_qs(urlsplit(FORECAST_URL).query)
        self.assertEqual(query["timezone"], ["Europe/Bratislava"])
        self.assertEqual(query["forecast_days"], ["7"])
        self.assertEqual(query["temperature_unit"], ["celsius"])
        self.assertEqual(set(query["daily"][0].split(",")),
                         {"temperature_2m_min", "temperature_2m_max", "weather_code"})

    def test_cache_uses_the_local_date_and_serves_saved_days_when_provider_is_down(self):
        fetch = Mock(side_effect=[forecast_fixture(), OSError("offline"), OSError("offline")])
        cache = ForecastCache(fetch)
        now = timestamp(2026, 11, 20, 21)
        result = cache.get(now)
        self.assertEqual(len(result["days"]), 7)
        self.assertEqual(result["days"][0],
                         {"date": "2026-11-20", "minimum": -3, "maximum": 8, "code": 2})
        self.assertEqual(cache.get(now + 3600), result)
        fetch.assert_called_once()
        # 23:00 UTC is already the following date in Trnava.
        self.assertEqual(cache.get(now + 7200), result)
        self.assertEqual(cache.get(now + 7201), result)
        self.assertEqual(fetch.call_count, 2)
        self.assertIsNone(cache.get(timestamp(2026, 11, 27)))

    def test_invalid_data_cannot_replace_the_cache(self):
        result = forecast_fixture()
        result["daily"]["temperature_2m_min"][0] = float("nan")
        fetch = Mock(side_effect=[forecast_fixture(), result])
        cache = ForecastCache(fetch)
        now = timestamp(2026, 11, 20)
        before = cache.get(now)
        # Make today's minimum bigger than the maximum or invalid. Keep a usable forecast.
        self.assertEqual(cache.get(now + 14400), before)

    def test_only_paired_devices_can_read_weather(self):
        paired_device_token = "paired-weather-test-token"
        bridge = SimpleNamespace(config={
            "token": "legacy-weather-test-token",
            "device_tokens": {"fixture": paired_device_token}}, weather=Mock())
        handler = Handler.__new__(Handler)
        handler.path = "/pala/weather"
        handler.server = SimpleNamespace(bridge=bridge)
        handler.send_error = Mock()
        handler.send_json = Mock()
        handler.headers = {"Authorization": "Bearer another-device"}
        handler.do_GET()
        handler.send_error.assert_called_once_with(401)
        bridge.weather.get.assert_not_called()
        bridge.weather.get.return_value = {"fetched_at": 1, "days": []}
        handler.headers = {"Authorization": f"Bearer {paired_device_token}"}
        handler.do_GET()
        handler.send_json.assert_called_once_with(200, {"fetched_at": 1, "days": []})
