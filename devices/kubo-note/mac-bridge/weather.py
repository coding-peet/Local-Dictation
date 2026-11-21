"""Fetch and cache the public daily forecast for the town configured in Local Dictation."""
import datetime as dt
import hashlib
import json
import math
import re
import threading
import time
from urllib.parse import urlencode
from urllib.request import urlopen
from zoneinfo import ZoneInfo


LOCATION_ZONE = ZoneInfo("Europe/Bratislava")
GEOCODING_URL = "https://geocoding-api.open-meteo.com/v1/search"
FORECAST_URL = "https://api.open-meteo.com/v1/forecast"
WEATHER_CODES = {
    0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67,
    71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99,
}
MAX_RESPONSE = 16_384
REFRESH_AFTER = 3 * 3600
RETRY_AFTER = 5 * 60


def _read_json(endpoint, query):
    with urlopen(endpoint + "?" + urlencode(query), timeout=2.5) as response:
        payload = response.read(MAX_RESPONSE + 1)
    if len(payload) > MAX_RESPONSE:
        raise ValueError("The weather provider returned too much data.")
    return json.loads(payload)


def _location_parts(value):
    if not isinstance(value, str):
        raise ValueError("The weather location must be text.")
    value = value.strip()
    if not value:
        return "", ""
    parts = [part.strip() for part in value.split(",")]
    if (len(value) > 100 or re.search(r"[\x00-\x1f\x7f-\x9f]", value)
            or len(parts[0]) < 2 or len(parts) > 2
            or (len(parts) == 2 and re.fullmatch(r"[A-Za-z]{2}", parts[1]) is None)):
        raise ValueError("Enter a town and optionally a two-letter country code.")
    return parts[0], parts[1].upper() if len(parts) == 2 else ""


def resolve_location(town, country):
    query = {"name": town, "count": 1}
    if country:
        query["countryCode"] = country
    payload = _read_json(GEOCODING_URL, query)
    results = payload.get("results") if isinstance(payload, dict) else None
    if not isinstance(results, list) or not results or not isinstance(results[0], dict):
        raise ValueError("The weather provider could not find that town.")
    match = results[0]
    latitude, longitude = match.get("latitude"), match.get("longitude")
    if (type(latitude) not in (int, float) or not -90 <= latitude <= 90
            or type(longitude) not in (int, float) or not -180 <= longitude <= 180
            or (country and match.get("country_code") != country)):
        raise ValueError("The weather provider returned an invalid location.")
    return latitude, longitude


def fetch_daily(latitude, longitude):
    return _read_json(FORECAST_URL, {
        "latitude": latitude,
        "longitude": longitude,
        "daily": "weather_code,temperature_2m_min,temperature_2m_max",
        "temperature_unit": "celsius",
        "timezone": LOCATION_ZONE.key,
        "forecast_days": 7,
    })


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
    def __init__(self, read_location, resolve=resolve_location, fetch=fetch_daily):
        self.read_location = read_location
        self.resolve = resolve
        self.fetch = fetch
        self.lock = threading.Lock()
        self.location = None
        self.coordinates = None
        self.saved = None
        self.unavailable = None
        self.retry_at = 0
        self.next_refresh_at = 0
        self.last_timestamp = 0

    def _read_location(self):
        try:
            return _location_parts(self.read_location())
        except (OSError, ValueError):
            return None

    @staticmethod
    def _key(location):
        return (location[0].casefold(), location[1]) if location is not None else None

    def _payload(self, now, days):
        self.last_timestamp = max(now, self.last_timestamp + 1)
        town, country = self.location
        fingerprint = hashlib.sha256(f"{town},{country}".encode("utf-8")).hexdigest()[:16] if town else ""
        return {"fetched_at": self.last_timestamp, "days": days, "location": fingerprint}

    def get(self, now=None):
        now = int(time.time()) if now is None else now
        today = dt.datetime.fromtimestamp(now, LOCATION_ZONE).date()
        with self.lock:
            location = self._read_location()
            if location is None:
                return None
            key = self._key(location)
            if key != self.location:
                self.location = key
                self.coordinates = self.saved = self.unavailable = None
                self.retry_at = self.next_refresh_at = 0

            if not location[0]:
                self.next_refresh_at = float("inf")
            else:
                saved = self.saved
                if (saved is not None and saved["days"][0]["date"] == today.isoformat()
                        and 0 <= now - saved["fetched_at"] < REFRESH_AFTER):
                    return saved
                if now >= self.retry_at:
                    try:
                        if self.coordinates is None:
                            self.coordinates = self.resolve(*location)
                        days = _read_days(self.fetch(*self.coordinates), today)
                    except (OSError, ValueError):
                        self.retry_at = self.next_refresh_at = now + RETRY_AFTER
                    else:
                        if self._key(self._read_location()) != key:
                            return None
                        self.saved = self._payload(now, days)
                        self.unavailable = None
                        self.next_refresh_at = now + REFRESH_AFTER
                        return self.saved
                    if self._key(self._read_location()) != key:
                        return None
                if saved is not None and any(day["date"] == today.isoformat() for day in saved["days"]):
                    return saved

            if self.unavailable is None:
                self.unavailable = self._payload(now, [])
            return self.unavailable
