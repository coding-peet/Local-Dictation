"""Synthetic tests for password-protected Note settings."""
import http.client
import json
from pathlib import Path
import sys
import threading
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from note_settings import SettingsError, note_request, note_response
from note_transport import SettingsTransport
from note_web import SettingsHTTPServer
from usb_sync import paired_config


class NoteAccessTests(unittest.TestCase):
    def test_login_accepts_only_the_password(self):
        self.assertEqual(
            note_request({"op": "auth.login", "password": "test passphrase", "ignored": "discard"}),
            {"op": "auth.login", "password": "test passphrase"},
        )

    def test_password_change_carries_current_and_repeated_values(self):
        fields = {"op": "auth.save", "old": "", "password": "new test secret", "repeat": "new test secret"}
        self.assertEqual(note_request(fields), fields)
        self.assertEqual(note_request({**fields, "old": "unusableč"}), fields)
        for invalid in ("short", "long\nnewline", "a" * 49):
            with self.subTest(invalid=invalid), self.assertRaises(SettingsError):
                note_request({**fields, "password": invalid, "repeat": invalid})

    def test_locked_settings_drop_all_note_data(self):
        reply = {"ok": False, "access": "locked", "device": {"name": "Private"}, "error": "untrusted"}
        self.assertEqual(note_response(reply, "BLE"), {
            "ok": False,
            "transport": "BLE",
            "access": "locked",
            "error": "Enter the Note password.",
        })

    def test_only_a_valid_login_token_is_relayed(self):
        token = "a" * 48
        self.assertEqual(note_response({"ok": True, "session": token, "password": "do not echo"}, "BLE", "auth.login"),
                         {"ok": True, "transport": "BLE", "session": token})
        with self.assertRaises(SettingsError):
            note_response({"ok": True, "session": "invalid"}, "BLE", "auth.login")


class NoteSessionRelayTests(unittest.TestCase):
    def test_usb_password_save_waits_for_a_short_background_transfer(self):
        class Transfer:
            acquired = False

            def acquire(self, *, timeout):
                self.acquired = timeout >= 1
                return self.acquired

            def release(self):
                self.acquired = False

        transfer = Transfer()
        received = []
        def exchange(port, request):
            self.assertTrue(transfer.acquired)
            self.assertEqual(port[1], "a1")
            received.append(request)
            return {"ok": True}

        transport = SettingsTransport(lambda: ("COM24", "a1", "paired"), exchange, transfer,
                                      threading.Event(), False)
        reply, link = transport.request({"op": "auth.save", "_target": "a1"})
        self.assertEqual((reply, link), ({"ok": True}, "USB"))
        self.assertEqual(received, [{"op": "auth.save"}])
        self.assertFalse(transfer.acquired)

    def test_csrf_protected_loopback_uses_only_the_opaque_session_header(self):
        received = []
        server = SettingsHTTPServer(lambda data: received.append(data) or {"ok": True}, b"")
        service = threading.Thread(target=server.serve_forever, daemon=True)
        service.start()
        conn = http.client.HTTPConnection("127.0.0.1", server.server_port, timeout=2)
        try:
            headers = {"X-Pala-Settings": "1", "X-Pala-Note-Session": "a" * 48}
            conn.request("GET", "/api/note-settings", headers=headers)
            reply = conn.getresponse()
            self.assertEqual(reply.status, 200)
            csrf = json.load(reply)["csrf"]
            self.assertEqual(received[-1]["session"], "a" * 48)
            headers.update({"Content-Type": "application/json", "X-Pala-CSRF": csrf,
                            "Origin": f"http://127.0.0.1:{server.server_port}"})
            conn.request("POST", "/api/note-settings",
                         json.dumps({"op": "auth.logout", "session": "b" * 48}), headers=headers)
            reply = conn.getresponse()
            self.assertEqual(reply.status, 200)
            reply.read()
            self.assertEqual(received[-1]["session"], "a" * 48)
            del headers["X-Pala-Note-Session"]
            conn.request("POST", "/api/note-settings",
                         json.dumps({"op": "auth.logout", "session": "b" * 48}), headers=headers)
            reply = conn.getresponse()
            self.assertEqual(reply.status, 200)
            reply.read()
            self.assertNotIn("session", received[-1])
        finally:
            conn.close()
            server.shutdown()
            server.server_close()
            service.join()

    def test_locked_paired_note_remains_the_target_of_login(self):
        transport = SettingsTransport(lambda: None, None, threading.Lock(), threading.Event(), True)
        reply = transport._exchange({"op": "get"}, "A1", lambda _: {"ok": False, "access": "locked"})
        self.assertEqual(reply["_selectedDevice"], "a1")
        self.assertEqual(transport._exchange({"op": "auth.login", "_target": "a1"}, "A1",
                                            lambda _: {"ok": True}), {"ok": True})

    def test_paged_settings_requests_keep_the_session(self):
        token = "a" * 48
        with patch("usb_sync._paired_session"), patch("usb_sync._expect_ok"), patch("usb_sync.config_exchange") as call:
            call.side_effect = [{"ok": True, "pageCounts": {"wifi": 1, "peers": 0}},
                                {"ok": True, "wifi": [{"ssid": "test network"}]}]
            reply = paired_config(object(), "paired", {"op": "get", "session": token})
            self.assertEqual(call.call_args_list[1].args[1],
                             {"op": "get", "section": "wifi", "index": 0, "session": token})
            self.assertEqual(reply["wifi"], [{"ssid": "test network"}])


if __name__ == "__main__":
    unittest.main()
