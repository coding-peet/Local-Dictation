import asyncio
import io
import http.client
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from urllib.parse import urlsplit
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from bridge import authorize_bridge_device
from note_bluetooth_pair import windows_numeric_pair
from note_settings import (EnrollmentStore, PairJob, PairingBroker, SettingsRoute,
                           complete_pairing, encode_request, note_request, note_response)
from note_transport import BluetoothRequest, SettingsTransport
from note_web import API, SettingsError, SettingsHTTPServer, bridge_preferences, settings_page
from test_usb_sync import FakePala, TOKEN
from usb_sync import main, paired_config


class DisplayPagesTests(unittest.TestCase):
    def test_reordering_and_disabling_are_preserved_in_request_and_readback(self):
        operation = {"op": "pages.save", "pages": "NetC"}
        self.assertEqual(note_request(operation), operation)

        device = {"name": "Willow", "language": "en", "displayPages": "NetC"}
        reply = note_response({"ok": True, "device": device}, "BLE")
        self.assertEqual(reply["device"], device)

    def test_fifth_page_is_preserved_for_new_firmware(self):
        operation = {"op": "pages.save", "pages": "MENtc"}
        self.assertEqual(note_request(operation), operation)
        device = {"name": "Willow", "language": "en", "displayPages": "MENtc"}
        self.assertEqual(note_response({"ok": True, "device": device}, "USB")["device"], device)

    def test_invalid_or_missing_page_preferences(self):
        for invalid in ("TECc", "TECX", "TCN", "TECN\0", ["T", "E", "C", "N"], None):
            with self.subTest(invalid=invalid), self.assertRaises(SettingsError):
                note_request({"op": "pages.save", "pages": invalid})
        for field in ({}, {"displayPages": "TttE"}):
            device = {"name": "Willow", "language": "en", **field}
            self.assertNotIn("displayPages", note_response({"ok": True, "device": device}, "USB")["device"])


class IdleSleepSettingsTests(unittest.TestCase):
    def test_custom_timeout_and_default_round_trip(self):
        for minutes in (0, 1, 99):
            operation = {"op": "sleep.save", "minutes": minutes}
            self.assertEqual(note_request(operation), operation)
            device = {"name": "Willow", "language": "en", "sleepMinutes": minutes}
            self.assertEqual(note_response({"ok": True, "device": device}, "BLE")["device"], device)

    def test_invalid_values_and_unsupported_firmware(self):
        for value in (-1, 100, True, 1.5, "3", None):
            with self.subTest(value=value), self.assertRaises(SettingsError):
                note_request({"op": "sleep.save", "minutes": value})
        old = {"name": "Willow", "language": "en"}
        self.assertNotIn("sleepMinutes", note_response({"ok": True, "device": old}, "USB")["device"])


class LoopbackSettingsTests(unittest.TestCase):
    def setUp(self):
        def route(data):
            self.calls.append(data)
            return {"ok": True}

        self.calls = []
        page = settings_page()
        self.assertIn(b"<!doctype html", page.lower())
        self.server = SettingsHTTPServer(route, page)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.thread.join)
        self.addCleanup(self.server.shutdown)

    def request(self, method, path, headers=None, value=None):
        conn = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=2)
        try:
            conn.request(method, path, json.dumps(value) if value is not None else None, headers or {})
            response = conn.getresponse()
            body = response.read()
            return response.status, body, dict(response.getheaders())
        finally:
            conn.close()

    def test_api_requires_loopback_host_exact_origin_and_nonce(self):
        self.assertEqual(self.request("GET", "/settings")[0], 200)
        self.assertEqual(self.request("GET", API)[0], 403)
        self.assertEqual(self.request("GET", API, {"Host": "example.invalid", "X-Pala-Settings": "1"})[0], 403)
        status, body, headers = self.request("GET", API, {"X-Pala-Settings": "1"})
        self.assertEqual(status, 200)
        self.assertEqual(headers["Cache-Control"], "no-store")
        nonce = json.loads(body)["csrf"]
        trusted = {"Content-Type": "application/json", "Origin": self.server.origin,
                   "X-Pala-Settings": "1", "X-Pala-CSRF": nonce}
        operation = {"op": "wifi.save", "ssid": "Example", "password": "synthetic secret"}
        for override in [{"Origin": "https://example.invalid"}, {"X-Pala-CSRF": "bad"}, {"Host": "example.invalid"}]:
            self.assertEqual(self.request("POST", API, {**trusted, **override}, operation)[0], 403)
        self.assertEqual(self.calls, [{"op": "get"}])
        self.assertEqual(self.request("POST", API, trusted, operation)[0], 200)
        self.assertEqual(self.calls, [{"op": "get"}, {**operation, "_target": None}])

    def test_two_browser_tabs_keep_separate_confirmed_device_bindings(self):
        targets = iter(["known-note-a", "known-note-b"])

        def route(data):
            if data["op"] == "get":
                return {"ok": True, "_selectedDevice": next(targets)}
            self.calls.append(data)
            return {"ok": True}

        self.server.route = route
        headers = {"X-Pala-Settings": "1"}
        tab_a = json.loads(self.request("GET", API, headers)[1])
        tab_b = json.loads(self.request("GET", API, headers)[1])
        self.assertNotEqual(tab_a["csrf"], tab_b["csrf"])
        self.assertNotIn("_selectedDevice", tab_a)
        for tab in (tab_a, tab_b):
            auth = {**headers, "Content-Type": "application/json", "Origin": self.server.origin,
                    "X-Pala-CSRF": tab["csrf"]}
            self.assertEqual(self.request("POST", API, auth, {"op": "wifi.remove", "ssid": "Example",
                                                            "_target": "should-be-ignored"})[0], 200)
        self.assertEqual([call["_target"] for call in self.calls], ["known-note-a", "known-note-b"])

    def test_html_resource_normalizes_windows_line_endings(self):
        with tempfile.TemporaryDirectory() as folder:
            page = Path(folder) / "note_settings_page.inc"
            page.write_bytes(b'R"PALAHTML(\r\n<!doctype html>\r\n)PALAHTML"\r\n')
            self.assertEqual(settings_page(page), b"\n<!doctype html>\n")


class NoteSettingsTests(unittest.TestCase):
    def test_preferences_do_not_rewrite_live_legacy_settings(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            legacy = {"PalaBridgeName": "Legacy", "PalaWeatherLocation": "Trnava, SK", "Endpoint": "unchanged"}
            route = SettingsRoute(state, lambda: legacy, lambda data: ({"ok": False}, "USB"), None)
            self.assertEqual(route({"op": "get"})["bridge"]["weatherLocation"], "Trnava, SK")
            self.assertEqual(route({"op": "bridge.save", "name": "Desk Bridge", "weatherLocation": "Wien, at"})["bridge"],
                             {"name": "Desk Bridge", "weatherLocation": "Wien, AT"})
            self.assertEqual(bridge_preferences(state, legacy)["weatherLocation"], "Wien, AT")
            self.assertEqual(legacy["PalaWeatherLocation"], "Trnava, SK")
            self.assertEqual(legacy["Endpoint"], "unchanged")

    def test_note_response_omits_all_credentials(self):
        wire = {"ok": True, "device": {"name": "Kubo", "language": "sk", "token": "secret"},
                "wifi": [{"ssid": "MyWiFi", "password": "secret"}],
                "peers": [{"id": "bridge-a", "name": "Bridge", "token": "secret", "primary": True}],
                "token": "secret"}
        with tempfile.TemporaryDirectory() as folder:
            route = SettingsRoute(Path(folder), lambda: {}, lambda data: (wire, "USB"), None)
            public = route({"op": "get"})
        self.assertNotIn("secret", json.dumps(public))
        self.assertEqual(public["wifi"], [{"ssid": "MyWiFi"}])
        self.assertEqual(public["peers"], [{"id": "bridge-a", "name": "Bridge", "primary": True}])
        self.assertEqual(public["bridgeOrder"], ["bridge-a"])

    def test_trash_capability_and_only_requested_metadata_pass_through(self):
        replies = {
            "get": {"ok": True, "device": {"name": "Kubo", "language": "en", "memoryTrash": True}},
            "trash.status": {"ok": True, "days": 90, "count": 1, "audio": "private fixture"},
            "trash.entry": {"ok": True, "entry": {"num": 7, "tag": "note", "trashedAt": 0,
                "capturedUtc": "2026-11-21T00:15:00Z", "preview": "private fixture"}},
            "trash.restore": {"ok": True, "extra": "private fixture"},
            "trash.retention": {"ok": True, "extra": "private fixture"},
            "trash.empty": {"ok": True, "more": True, "extra": "private fixture"},
        }
        requests = []

        def transfer(data):
            requests.append(data)
            return replies[data["op"]], "BLE"

        with tempfile.TemporaryDirectory() as folder:
            route = SettingsRoute(Path(folder), lambda: {}, transfer, None)
            self.assertTrue(route({"op": "get"})["device"]["memoryTrash"])
            self.assertEqual(route({"op": "trash.status"}), {"ok": True, "transport": "BLE", "days": 90, "count": 1})
            entry = {"num": 7, "tag": "note", "trashedAt": 0, "capturedUtc": "2026-11-21T00:15:00Z"}
            self.assertEqual(route({"op": "trash.entry", "index": 0, "_target": "bound-note", "extra": "ignored"}),
                             {"ok": True, "transport": "BLE", "entry": entry})
            self.assertEqual(requests[-1], {"op": "trash.entry", "index": 0, "_target": "bound-note"})
            self.assertEqual(route({"op": "trash.restore", "num": 7}), {"ok": True, "transport": "BLE"})
            self.assertEqual(route({"op": "trash.retention", "days": 3650}), {"ok": True, "transport": "BLE"})
            self.assertEqual(route({"op": "trash.empty", "confirmation": "EMPTY TRASH"}),
                             {"ok": True, "transport": "BLE", "more": True})
            self.assertEqual(requests[-1], {"op": "trash.empty", "confirmation": "EMPTY TRASH"})
            del replies["get"]["device"]["memoryTrash"]
            self.assertNotIn("memoryTrash", route({"op": "get"})["device"])

    def test_trash_request_rejects_invalid_metadata_or_missing_exact_confirmation(self):
        with tempfile.TemporaryDirectory() as folder:
            route = SettingsRoute(Path(folder), lambda: {}, self.fail, None)
            for bad in ({"op": "trash.entry", "index": -1}, {"op": "trash.entry", "index": True},
                        {"op": "trash.restore", "num": 0}, {"op": "trash.retention", "days": 3651},
                        {"op": "trash.retention", "days": 0}, {"op": "trash.empty"},
                        {"op": "trash.empty", "confirmation": "EMPTY TRASH "},
                        {"op": "trash.empty", "confirmation": "MOVE ALL"}):
                with self.subTest(bad=bad), self.assertRaises(SettingsError):
                    route(bad)

    def test_trash_malformed_reply_cannot_signal_permanent_deletion_complete(self):
        with tempfile.TemporaryDirectory() as folder:
            route = SettingsRoute(Path(folder), lambda: {}, lambda data: ({"ok": True, "more": "false"}, "USB"), None)
            self.assertFalse(route({"op": "trash.empty", "confirmation": "EMPTY TRASH"})["ok"])

    def test_usb_config_uses_existing_authentication_and_reassembles_ble_pages(self):
        class Device(FakePala):
            def write(self, frame):
                if frame.startswith(b"PALA_CONFIG_V1 "):
                    data = json.loads(frame.removeprefix(b"PALA_CONFIG_V1 "))
                    self.calls.append(("config", data))
                    if "section" in data:
                        reply = {"ok": True, data["section"]: [{"ssid": "MyWiFi"}]}
                    else:
                        reply = {"ok": True, "pageCounts": {"wifi": 1, "peers": 0}}
                    self.outbox = io.BytesIO(encode_request(reply))
                    return len(frame)
                return super().write(frame)

        device = Device()
        result = paired_config(device, TOKEN, {"op": "get"})
        self.assertEqual([call[0] for call in device.calls], ["HELLO", "AUTH", "config", "config", "CLOSE"])
        self.assertEqual(result["wifi"], [{"ssid": "MyWiFi"}])

    def test_settings_transport_does_not_write_to_a_replacement_usb_target(self):
        attached = [("COM24", "note1", TOKEN)]
        requests = []

        def read(port, data):
            requests.append((port[1], data))
            return {"ok": True}

        transport = SettingsTransport(lambda: attached[0], read, threading.Lock(), threading.Event(), False)
        transport.request({"op": "get"})
        attached[0] = ("COM25", "note2", TOKEN)
        with self.assertRaisesRegex(SettingsError, "Refresh"):
            transport.request({"op": "wifi.remove", "ssid": "Example"})
        self.assertEqual(requests, [("note1", {"op": "get"})])

    def test_recording_advertisement_serves_an_active_settings_request_and_yields(self):
        transport = SettingsTransport(lambda: None, None, threading.Lock(), threading.Event(), True)
        first = BluetoothRequest({"op": "get"})
        second = BluetoothRequest({"op": "get"})
        transport.bluetooth.put_nowait(first)
        requests = []

        def exchange(data):
            requests.append(data)
            if len(requests) == 1:
                transport.bluetooth.put_nowait(second)
            else:
                transport.stop.set()
            return {"ok": True}

        transport.serve_bluetooth("note1", exchange)
        self.assertEqual(requests, [{"op": "get"}])
        self.assertTrue(first.done.is_set())
        self.assertFalse(second.done.is_set())
        self.assertTrue(transport.wants_bluetooth("note1"))

        transport.serve_bluetooth("note1", exchange, keep_open=True)
        self.assertEqual(requests, [{"op": "get"}, {"op": "get"}])
        self.assertTrue(second.done.is_set())

    def test_pair_status_preserves_independent_legacy_credentials_and_cert_chunks(self):
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            discovery = "pala-1234567890abcdef12345678"
            serial = "010203040506"
            legacy = {"discovery_id": discovery, "token": "baseline-api-token", "device_tokens": {serial: TOKEN}}
            store = EnrollmentStore(state, legacy)
            chunk_calls = []
            ticket = "ticket-for-note"
            paired = {"ok": True, "state": "paired", "id": discovery, "serial": serial,
                      "advertiser": "111213141516", "token": "independent-pairing-token"}

            def exchange(data):
                match data["op"]:
                    case "pair.start":
                        self.assertEqual(data["host"], f"{discovery}.local")
                        return {"ok": True, "state": "approved", "ticket": ticket}
                    case "pair.certificate":
                        self.assertEqual(data["ticket"], ticket)
                        chunk_calls.append((data["offset"], len(data["chunk"]), data["final"]))
                        return {"ok": True}
                    case "pair.status":
                        return paired
                raise AssertionError("Unexpected pairing operation.")

            complete_pairing(PairJob("usb"), exchange, store, "My Bridge", "A" * 544,
                             f"{discovery}.local", attached_serial=serial)
            self.assertEqual(chunk_calls, [(0, 240, False), (240, 240, False), (480, 64, True)])
            self.assertEqual(legacy["device_tokens"][serial], TOKEN)
            self.assertEqual(store.tokens()[serial], paired["token"])
            bridge = type("BridgeFixture", (), {"config": legacy, "state": state})
            self.assertTrue(authorize_bridge_device(bridge, "Bearer " + TOKEN))
            self.assertTrue(authorize_bridge_device(bridge, "Bearer " + paired["token"]))
            self.assertFalse(authorize_bridge_device(bridge, "Bearer remote-unpaired-token"))

    def test_transport_only_ble_enrollment_waits_for_receipt_after_approval(self):
        with tempfile.TemporaryDirectory() as folder:
            store = EnrollmentStore(Path(folder), {})
            bridge = store.bridge_id()
            ticket = "note-transient-ticket"
            requests = []

            def exchange(data):
                requests.append(data)
                if data["op"] == "pair.start":
                    return {"ok": True, "state": "approved", "ticket": ticket}
                return {"ok": True, "state": "paired", "id": bridge, "serial": "010203040506",
                        "advertiser": "020304050607", "token": TOKEN}

            self.assertEqual(complete_pairing(PairJob("ble"), exchange, store, "My Bridge"), "010203040506")
            self.assertEqual([call["op"] for call in requests], ["pair.start", "pair.status"])
            self.assertNotIn("host", requests[0])
            self.assertEqual(store.tokens()["010203040506"], TOKEN)

    def test_broker_only_exposes_explicit_numeric_code_and_can_cancel(self):
        broker = PairingBroker(lambda _: None, True)
        ticket = broker.route({"op": "pair.start", "transport": "ble"})["ticket"]
        job = broker.claim_ble()
        self.assertIsNone(broker.claim_ble())
        with self.assertRaises(SettingsError):
            broker.route({"op": "pair.confirm", "ticket": ticket, "accepted": True})
        self.assertTrue(job.show_code("061234"))
        self.assertEqual(broker.route({"op": "pair.status", "ticket": ticket})["code"], "061234")
        self.assertEqual(broker.route({"op": "pair.confirm", "ticket": ticket, "accepted": True})["state"], "pending")
        self.assertTrue(job.accepted)
        response = broker.route({"op": "pair.confirm", "ticket": ticket, "accepted": False})
        self.assertEqual(response["state"], "cancelled")
        self.assertNotIn("code", response)



class WindowsPairingTests(unittest.IsolatedAsyncioTestCase):
    async def test_requires_the_exact_numeric_ceremony_and_protection(self):
        for old_bond, callback_kind in ((False, 8), (False, 1), (True, 8)):
            with self.subTest(old_bond=old_bond, callback_kind=callback_kind):
                callback_complete = asyncio.Event()
                receipts = []
                args = SimpleNamespace(pairing_kind=callback_kind, pin="061234",
                    accept=lambda: receipts.append("accept"),
                    get_deferral=lambda: SimpleNamespace(complete=callback_complete.set))

                class Custom:
                    def add_pairing_requested(self, handler):
                        self.handler = handler
                        return 17

                    def remove_pairing_requested(self, token):
                        receipts.append(("removed", token))

                    async def pair_with_protection_level_async(self, ceremony, protection):
                        receipts.append((ceremony, protection))
                        self.handler(None, args)
                        if callback_kind == 8:
                            await asyncio.wait_for(callback_complete.wait(), timeout=2)
                        return SimpleNamespace(status=0, protection_level_used=3)

                info = SimpleNamespace(pairing=SimpleNamespace(is_paired=old_bond, can_pair=True, custom=Custom()))
                native = SimpleNamespace(device_information=SimpleNamespace(id="synthetic"),
                                         close=lambda: receipts.append("closed"))

                async def open_address(address):
                    self.assertEqual(address, 0x010203040506)
                    return native

                async def load_info(name):
                    self.assertEqual(name, "synthetic")
                    return info

                modules = {
                    "winrt.windows.devices.bluetooth": SimpleNamespace(BluetoothLEDevice=SimpleNamespace(
                        from_bluetooth_address_async=open_address)),
                    "winrt.windows.devices.enumeration": SimpleNamespace(
                        DeviceInformation=SimpleNamespace(create_from_id_async=load_info),
                        DevicePairingKinds=SimpleNamespace(CONFIRM_PIN_MATCH=8),
                        DevicePairingProtectionLevel=SimpleNamespace(ENCRYPTION_AND_AUTHENTICATION=3),
                        DevicePairingResultStatus=SimpleNamespace(PAIRED=0)),
                }
                job = PairJob("ble")
                device = SimpleNamespace(address="01:02:03:04:05:06")
                with patch.dict(sys.modules, modules):
                    if old_bond:
                        with self.assertRaisesRegex(SettingsError, "Remove this exact Note"):
                            await windows_numeric_pair(device, job)
                        self.assertEqual(receipts, ["closed"])
                    elif callback_kind != 8:
                        with self.assertRaisesRegex(SettingsError, "not confirmed"):
                            await windows_numeric_pair(device, job)
                        self.assertEqual(receipts, [(8, 3), ("removed", 17), "closed"])
                    else:
                        pairing = asyncio.create_task(windows_numeric_pair(device, job))
                        for _ in range(20):
                            if job.snapshot()["state"] == "confirm":
                                break
                            await asyncio.sleep(.01)
                        self.assertEqual(job.snapshot()["code"], "061234")
                        self.assertNotIn("accept", receipts)
                        job.confirm(True)
                        await asyncio.wait_for(pairing, timeout=2)
                        self.assertEqual(receipts, [(8, 3), "accept", ("removed", 17), "closed"])


class ManagedWatcherSettingsTests(unittest.TestCase):
    def test_managed_bluetooth_drains_ordinary_backlog_before_weather(self):
        stop = threading.Event()
        link = object()
        actions = []
        forecast = {"days": []}

        def start_bluetooth(_tokens, _stop, drain, _error, **_options):
            drain(link, "fixture-note", TOKEN, True)
            actions.append("ordinary advertisement")
            drain(link, "fixture-note", TOKEN, False)
            actions.append("ordinary advertisement while ASR is busy")
            drain(link, "fixture-note", TOKEN, False)

        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder) / "synthetic-profile"
            with (patch("pair.STATE", state),
                  patch("bridge.Bridge") as bridge,
                  patch("bridge.managed_stop_event", return_value=stop),
                  patch("bridge.local_dictation_settings", return_value={}),
                  patch("note_web.serve_settings"),
                  patch("note_transport.SettingsTransport.serve_bluetooth",
                        side_effect=lambda _device, _exchange, *, keep_open: actions.append(("settings", keep_open))),
                  patch("usb_sync.transfer_note_name", side_effect=lambda *_: actions.append("metadata")),
                  patch("usb_sync.transfer_delivery_counts"),
                  patch("weather.ForecastCache.get",
                        side_effect=lambda: actions.append("weather lookup") or forecast),
                  patch("usb_sync.transfer_forecast",
                        side_effect=lambda *_: actions.append("weather transfer")),
                  patch("usb_sync._drain_link", side_effect=lambda *_: actions.append("recording")),
                  patch("usb_sync.watch_paired", side_effect=lambda *_args, **_kwargs: stop.set()),
                  patch("bluetooth_sync.start_bluetooth_watcher", side_effect=start_bluetooth),
                  patch.object(sys, "platform", "win32"),
                  patch.object(sys, "argv", ["usb_sync.py", "--watch", "--managed"]),
                  patch.dict(os.environ, PALA_NOTE_SETTINGS_OWNER="a" * 32)):
                bridge.return_value.local_transcription_ready.side_effect = [True, False]
                main()

        self.assertEqual(actions, [("settings", True), "ordinary advertisement",
                                   ("settings", False), "metadata", "recording", "weather lookup", "weather transfer",
                                   "ordinary advertisement while ASR is busy", ("settings", False),
                                   "metadata", "weather lookup", "weather transfer"])
        self.assertEqual(bridge.return_value.local_transcription_ready.call_count, 2)

    def test_managed_watcher_first_run_serves_page_until_its_owner_stops(self):
        owner = "a" * 32
        stop = threading.Event()
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder) / "synthetic-profile"
            descriptor = state / "note-settings-runtime.json"
            with (patch("pair.STATE", state), patch("bridge.STATE", state),
                  patch("bridge.managed_stop_event", return_value=stop),
                  patch("bridge.local_dictation_settings", return_value={}),
                  patch("serial.tools.list_ports.comports", return_value=[]),
                  patch("bluetooth_sync.start_bluetooth_watcher", return_value=None),
                  patch.object(sys, "argv", ["usb_sync.py", "--watch", "--managed"]),
                  patch.dict(os.environ, PALA_NOTE_SETTINGS_OWNER=owner)):
                process = threading.Thread(target=main, daemon=True)
                process.start()
                try:
                    deadline = time.monotonic() + 3
                    while process.is_alive() and not descriptor.exists() and time.monotonic() < deadline:
                        time.sleep(.01)
                    self.assertTrue(descriptor.is_file())
                    status = json.loads(descriptor.read_text())
                    self.assertEqual(status["Owner"], owner)
                    link = urlsplit(status["Url"])
                    self.assertEqual((link.scheme, link.hostname, link.path), ("http", "127.0.0.1", "/settings"))
                    connection = http.client.HTTPConnection(link.hostname, link.port, timeout=3)
                    try:
                        connection.request("GET", API, headers={"X-Pala-Settings": "1"})
                        reply = connection.getresponse()
                        data = json.loads(reply.read())
                        self.assertFalse(data["ok"])
                        self.assertTrue(data["capabilities"]["pair"])
                        self.assertEqual(data["bridge"]["weatherLocation"], "")
                        self.assertIn("csrf", data)
                    finally:
                        connection.close()
                finally:
                    stop.set()
                    process.join(4)
                self.assertFalse(process.is_alive())
                self.assertFalse(descriptor.exists())
                self.assertFalse((state / "config.json").exists())
