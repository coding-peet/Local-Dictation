import hashlib
import hmac
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
    def __init__(self, *, corrupt=False, wrong_display=False, retained=None):
        super().__init__()
        self.forecast = bytearray()
        self.stored = b""
        self.retained = retained
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
            self.stored = bytes(self.forecast) if self.retained is None else json.dumps(self.retained).encode()
            self.reply("OK")
        elif command == "WEATHER-READ":
            if not json.loads(self.stored)["days"]:
                self.reply(f"FORECAST {len(self.stored)} -")
                return len(frame)
            maximum = 14 if self.wrong_display else 13
            self.reply(f"FORECAST {len(self.stored)} {TODAY} 6 {maximum} 3")
        else:
            offset, length = map(int, args)
            payload = self.stored
            if self.corrupt:
                payload = b"X" + payload[1:]
            self.reply(f"DATA {offset} {payload[offset:offset + length].hex()}")
        return len(frame)


class ReceiptPala(FakePala):
    def __init__(self, bad_ack=False):
        super().__init__()
        self.bad_ack = bad_ack

    def write(self, frame):
        self.assert_frame(frame)
        command, *args = frame[len(PREFIX):].decode("ascii").split()
        if command != "DELIVERY":
            return super().write(frame)
        self.calls.append((command, args))
        payload, signature = args
        assert hmac.compare_digest(signature, self.proof(f"delivery {payload}"))
        proof = self.proof(f"delivery-saved {payload}")
        self.reply(f"DELIVERY-SAVED {payload} {proof if not self.bad_ack else 'wrong'}")
        return len(frame)


class UsbWeatherTests(unittest.TestCase):
    def test_counts_only_uses_the_paired_session_and_checks_the_acknowledgment(self):
        from usb_sync import transfer_delivery_counts

        payload = "2026-11-20,2,1"
        device = ReceiptPala()
        transfer_delivery_counts(device, (TOKEN,), payload)
        self.assertEqual([command for command, _ in device.calls], ["HELLO", "AUTH", "DELIVERY", "CLOSE"])
        with self.assertRaisesRegex(UsbSyncError, "verify the delivery counters"):
            transfer_delivery_counts(ReceiptPala(bad_ack=True), (TOKEN,), payload)

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

    def test_weather_is_verified_in_one_session_without_selecting_recordings(self):
        device = ForecastPala()
        result = transfer_forecast(device, (TOKEN,), FORECAST)
        self.assertEqual(result, FORECAST["days"][0])
        self.assertEqual(json.loads(device.stored), FORECAST)
        self.assertEqual([c for c, _ in device.calls].count("AUTH"), 1)
        self.assertIn(("WEATHER-READ", []), device.calls)
        self.assertEqual(device.calls[-1][0], "CLOSE")
        self.assertTrue(all(c not in ("NEXT", "SELECT", "READ", "TEXT", "COMMIT")
                            for c, _ in device.calls))

    def test_clearing_weather_verifies_empty_saved_payload(self):
        disabled = {"fetched_at": 1795176001, "days": [], "location": ""}
        device = ForecastPala()
        self.assertIsNone(transfer_forecast(device, (TOKEN,), disabled))
        self.assertEqual(json.loads(device.stored), disabled)

    def test_clearing_weather_accepts_an_already_saved_disabled_state(self):
        disabled = {"fetched_at": 1795176001, "days": [], "location": ""}
        earlier = {**disabled, "fetched_at": 1795176000}
        device = ForecastPala(retained=earlier)
        self.assertIsNone(transfer_forecast(device, (TOKEN,), disabled))
        self.assertEqual(json.loads(device.stored), earlier)

    def test_unavailable_weather_accepts_only_same_location_offline_fallback(self):
        unavailable = {"fetched_at": 1795176001, "days": [], "location": "6ffc5c9ac843179d"}
        cached = {**FORECAST, "location": unavailable["location"]}
        self.assertIsNone(transfer_forecast(ForecastPala(retained=cached), (TOKEN,), unavailable))
        different = {**cached, "location": "4eb14249fcb425da"}
        with self.assertRaisesRegex(UsbSyncError, "verify the updated weather location"):
            transfer_forecast(ForecastPala(retained=different), (TOKEN,), unavailable)

    def test_rejects_changed_sd_bytes(self):
        with self.assertRaisesRegex(UsbSyncError, "stored forecast differs"):
            transfer_forecast(ForecastPala(corrupt=True), (TOKEN,), FORECAST)

    def test_rejects_mismatched_current_display_values(self):
        with self.assertRaisesRegex(UsbSyncError, "current weather differs"):
            transfer_forecast(ForecastPala(wrong_display=True), (TOKEN,), FORECAST)
