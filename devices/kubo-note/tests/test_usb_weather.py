import hashlib
import json
import sys
from pathlib import Path
import unittest
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from test_usb_sync import FakePala, PREFIX, TOKEN
from usb_sync import UsbSyncError, transfer_forecast, watch_paired


TODAY = "2026-11-20"
FORECAST = {
    "fetched_at": 1795176000,
    "days": [
        {"date": TODAY, "minimum": 6, "maximum": 13, "code": 3},
        {"date": "2026-11-21", "minimum": 4, "maximum": 12, "code": 2},
    ],
}


class ForecastPala(FakePala):
    def __init__(self, *, corrupt=False, wrong_display=False):
        super().__init__()
        self.forecast = bytearray()
        self.stored = b""
        self.corrupt = corrupt
        self.wrong_display = wrong_display

    def write(self, frame):
        self.assert_frame(frame)
        command, *args = frame[len(PREFIX):].decode("ascii").split()
        if command not in {"WEATHER", "PUT", "WEATHER-COMMIT", "WEATHER-READ", "READWEATHER"}:
            return super().write(frame)
        self.calls.append((command, args))
        if command == "WEATHER":
            self.length, self.digest = int(args[0]), args[1]
            self.reply("OK")
        elif command == "PUT":
            assert int(args[0]) == len(self.forecast)
            self.forecast.extend(bytes.fromhex(args[1]))
            self.reply("OK")
        elif command == "WEATHER-COMMIT":
            assert len(self.forecast) == self.length
            assert hashlib.sha256(self.forecast).hexdigest() == self.digest
            self.stored = bytes(self.forecast)
            self.reply("OK")
        elif command == "WEATHER-READ":
            maximum = 14 if self.wrong_display else 13
            self.reply(f"FORECAST {len(self.stored)} {TODAY} 6 {maximum} 3")
        else:
            offset, length = map(int, args)
            payload = self.stored
            if self.corrupt:
                payload = b"X" + payload[1:]
            self.reply(f"DATA {offset} {payload[offset:offset + length].hex()}")
        return len(frame)


class UsbWeatherTests(unittest.TestCase):
    def test_refreshes_on_each_attachment_even_when_transcription_is_unavailable(self):
        port = SimpleNamespace(vid=0x303A, pid=0x1001, serial_number="paired", device="COM24")
        attached = [[port], [port], [], [port], [port]]
        seen = []

        class Stop:
            cycle = 0

            def is_set(self):
                return self.cycle == len(attached)

            def wait(self, _seconds):
                self.cycle += 1

        stop = Stop()
        watch_paired(lambda: attached[stop.cycle], {"paired": TOKEN}, lambda: False,
                     self.fail, stop, self.fail,
                     prepare_attachment=lambda name, token: seen.append((stop.cycle, name, token)))
        self.assertEqual(seen, [(0, "COM24", TOKEN), (3, "COM24", TOKEN)])

    def test_weather_is_verified_after_reauth_without_selecting_recordings(self):
        device = ForecastPala()
        result = transfer_forecast(device, (TOKEN,), FORECAST)
        self.assertEqual(result, FORECAST["days"][0])
        self.assertEqual(json.loads(device.stored), FORECAST)
        self.assertEqual([c for c, _ in device.calls].count("AUTH"), 2)
        self.assertIn(("WEATHER-READ", []), device.calls)
        self.assertEqual(device.calls[-1][0], "CLOSE")
        self.assertTrue(all(c not in ("NEXT", "SELECT", "READ", "TEXT", "COMMIT")
                            for c, _ in device.calls))

    def test_rejects_changed_sd_bytes(self):
        with self.assertRaisesRegex(UsbSyncError, "stored forecast differs"):
            transfer_forecast(ForecastPala(corrupt=True), (TOKEN,), FORECAST)

    def test_rejects_mismatched_current_display_values(self):
        with self.assertRaisesRegex(UsbSyncError, "current weather differs"):
            transfer_forecast(ForecastPala(wrong_display=True), (TOKEN,), FORECAST)
