"""Fetch and cache the public daily forecast for Trnava."""
import datetime as dt
import json
import math
import threading
import time
from urllib.parse import urlencode
from urllib.request import urlopen
from zoneinfo import ZoneInfo


LOCATION_ZONE = ZoneInfo("Europe/Bratislava")
FORECAST_URL = "https://api.open-meteo.com/v1/forecast?" + urlencode({
    "latitude": "48.3774",
    "longitude": "17.5872",
    "daily": "weather_code,temperature_2m_min,temperature_2m_max",
    "temperature_unit": "celsius",
    "timezone": LOCATION_ZONE.key,
    "forecast_days": "7",
})
WEATHER_CODES = {
    0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67,
    71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99,
}
MAX_RESPONSE = 16_384
REFRESH_AFTER = 3 * 3600
RETRY_AFTER = 5 * 60


def fetch_daily():
    with urlopen(FORECAST_URL, timeout=4) as response:
        payload = response.read(MAX_RESPONSE + 1)
    if len(payload) > MAX_RESPONSE:
        raise ValueError("The weather provider returned too much data.")
    return json.loads(payload)


def _read_days(payload, today):
    if not isinstance(payload, dict):
        raise ValueError("The forecast must be an object.")
    units = payload.get("daily_units")
    daily = payload.get("daily")
    if (not isinstance(daily, dict) or not isinstance(units, dict)
            or any(units.get(key) != "°C" for key in ("temperature_2m_min", "temperature_2m_max"))):
        raise ValueError("The daily forecast needs Celsius values.")
    columns = [daily.get(field) for field in ("time", "temperature_2m_min", "temperature_2m_max", "weather_code")]
    if (any(not isinstance(column, list) for column in columns)
            or len({len(column) for column in columns}) != 1 or not 1 <= len(columns[0]) <= 7):
        raise ValueError("The daily forecast has invalid columns.")

    days = {}
    for date, low, high, code in zip(*columns, strict=True):
        parsed = dt.date.fromisoformat(date) if isinstance(date, str) else None
        if parsed is None or parsed.isoformat() != date or not 0 <= (parsed - today).days <= 6 or date in days:
            raise ValueError("The provider returned unexpected forecast dates.")
        days[date] = None
        if (type(low) in (int, float) and type(high) in (int, float)
                and -80 <= low <= high <= 65 and math.isfinite(low) and math.isfinite(high)
                and type(code) is int and code in WEATHER_CODES):
            days[date] = {"date": date, "minimum": round(low), "maximum": round(high), "code": code}
    if days.get(today.isoformat()) is None:
        raise ValueError("A valid forecast for today was unavailable.")
    return [days[date] for date in sorted(days) if days[date] is not None]


class ForecastCache:
    def __init__(self, fetch=fetch_daily):
        self.fetch = fetch
        self.lock = threading.Lock()
        self.saved = None
        self.retry_at = 0

    def get(self, now=None):
        now = int(time.time()) if now is None else now
        today = dt.datetime.fromtimestamp(now, LOCATION_ZONE).date()
        with self.lock:
            saved = self.saved
            if saved is not None and saved["days"][0]["date"] == today.isoformat() and now - saved["fetched_at"] < REFRESH_AFTER:
                return saved
            if now >= self.retry_at:
                try:
                    days = _read_days(self.fetch(), today)
                except (OSError, ValueError):
                    self.retry_at = now + RETRY_AFTER
                else:
                    self.saved = {"fetched_at": now, "days": days}
                    return self.saved
            if saved and any(day["date"] == today.isoformat() for day in saved["days"]):
                return saved
            return None
