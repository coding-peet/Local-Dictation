import datetime as dt
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
from urllib.parse import parse_qs, urlsplit

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bridge import Handler
from weather import ForecastCache, fetch_daily, resolve_location


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
        with patch("weather.urlopen") as request:
            request.return_value.__enter__.return_value.read.return_value = b"{}"
            fetch_daily(49.22, 18.74)
        query = parse_qs(urlsplit(request.call_args.args[0]).query)
        self.assertEqual(query["latitude"], ["49.22"])
        self.assertEqual(query["longitude"], ["18.74"])
        self.assertEqual(query["timezone"], ["Europe/Bratislava"])
        self.assertEqual(query["forecast_days"], ["7"])
        self.assertEqual(query["temperature_unit"], ["celsius"])
        self.assertEqual(set(query["daily"][0].split(",")),
                         {"temperature_2m_min", "temperature_2m_max", "weather_code"})

    def test_geocoding_sends_only_public_town_and_country(self):
        with patch("weather.urlopen") as request:
            request.return_value.__enter__.return_value.read.return_value = (
                b'{"results":[{"latitude":49.22,"longitude":18.74,"country_code":"SK"}]}')
            self.assertEqual(resolve_location("Žilina", "SK"), (49.22, 18.74))
        endpoint = urlsplit(request.call_args.args[0])
        self.assertEqual(endpoint.scheme, "https")
        self.assertEqual(endpoint.hostname, "geocoding-api.open-meteo.com")
        self.assertEqual(parse_qs(endpoint.query), {"name": ["Žilina"], "count": ["1"], "countryCode": ["SK"]})

    def test_cache_uses_the_local_date_and_serves_saved_days_when_provider_is_down(self):
        fetch = Mock(side_effect=[forecast_fixture(), OSError("offline"), OSError("offline")])
        cache = ForecastCache(lambda: "Žilina, SK", resolve=Mock(return_value=(49.22, 18.74)), fetch=fetch)
        now = timestamp(2026, 11, 20, 21)
        result = cache.get(now)
        self.assertEqual(len(result["days"]), 7)
        self.assertEqual(result["days"][0],
                         {"date": "2026-11-20", "minimum": -3, "maximum": 8, "code": 2})
        self.assertEqual(cache.get(now + 3600), result)
        fetch.assert_called_once()
        # 23:00 UTC is already the following date on the Note.
        self.assertEqual(cache.get(now + 7200), result)
        self.assertEqual(cache.get(now + 7201), result)
        self.assertEqual(fetch.call_count, 2)
        self.assertEqual(cache.get(timestamp(2026, 11, 27))["days"], [])

    def test_invalid_data_cannot_replace_the_cache(self):
        result = forecast_fixture()
        result["daily"]["temperature_2m_min"][0] = float("nan")
        fetch = Mock(side_effect=[forecast_fixture(), result])
        cache = ForecastCache(lambda: "Žilina, SK", resolve=Mock(return_value=(49.22, 18.74)), fetch=fetch)
        now = timestamp(2026, 11, 20)
        before = cache.get(now)
        # Make today's minimum bigger than the maximum or invalid. Keep a usable forecast.
        self.assertEqual(cache.get(now + 14400), before)

    def test_changed_location_never_reuses_the_previous_towns_forecast(self):
        setting = ["Žilina, SK"]
        resolve = Mock(side_effect=[(49.22, 18.74), OSError("offline"), (48.15, 17.11)])
        fetch = Mock(return_value=forecast_fixture())
        cache = ForecastCache(read_location=lambda: setting[0], resolve=resolve, fetch=fetch)
        now = timestamp(2026, 11, 20, 21)
        first = cache.get(now)
        self.assertEqual(len(first["days"]), 7)
        resolve.assert_called_once_with("Žilina", "SK")

        setting[0] = "Bratislava, SK"
        pending = cache.get(now + 1)
        self.assertEqual(pending["days"], [])
        self.assertNotEqual(pending["location"], first["location"])
        self.assertEqual(cache.get(now + 2), pending)
        replacement = cache.get(now + 301)
        self.assertEqual(replacement["location"], pending["location"])
        self.assertEqual(len(replacement["days"]), 7)

        setting[0] = ""
        cleared = cache.get(now + 302)
        self.assertEqual(cleared["location"], "")
        self.assertEqual(cleared["days"], [])
        self.assertEqual(cache.get(now + 303), cleared)
        self.assertEqual(fetch.call_count, 2)

    def test_location_saved_during_fetch_supersedes_the_in_flight_response(self):
        setting = ["Žilina, SK"]

        def download(*_coordinates):
            setting[0] = "Bratislava, SK"
            return forecast_fixture()

        now = timestamp(2026, 11, 20)
        cache = ForecastCache(read_location=lambda: setting[0], resolve=Mock(return_value=(49.22, 18.74)),
                              fetch=download)
        self.assertIsNone(cache.get(now))
        self.assertEqual(len(cache.get(now + 1)["days"]), 7)

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
