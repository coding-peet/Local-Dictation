import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from note_settings import SettingsError, note_request, note_response


class RestartSettingsTests(unittest.TestCase):
    def test_remote_operations_can_request_but_cannot_confirm_a_restart(self):
        self.assertEqual(
            note_request({"op": "restart.request", "mode": "audit", "confirmed": True}),
            {"op": "restart.request", "mode": "audit"},
        )
        self.assertEqual(
            note_request({"op": "restart.request", "mode": "restart"}),
            {"op": "restart.request", "mode": "restart"},
        )
        with self.assertRaises(SettingsError):
            note_request({"op": "restart.request", "mode": "erase"})
        with self.assertRaises(SettingsError):
            note_request({"op": "restart.confirm", "mode": "restart"})

    def test_restart_report_survives_the_bridge_response_filter(self):
        report = {"state": "incomplete", "notes": 50_000, "estimateSeconds": 620}
        response = note_response({"ok": True, "storageAudit": report}, "BLE", "restart.status")
        self.assertEqual(response["storageAudit"], report)
        with self.assertRaises(SettingsError):
            note_response({"ok": True, "storageAudit": {"state": "complete", "notes": True}}, "USB",
                          "restart.status")

    def test_expired_confirmation_is_reported_for_the_requested_ticket(self):
        request = note_response({"ok": True, "pending": True, "ticket": 9}, "USB", "restart.request")
        report = note_response({"ok": True, "storageAudit": {"state": "never", "notes": 2},
                                "confirmation": {"ticket": 9, "state": "expired"}}, "USB", "restart.status")
        self.assertEqual(report["confirmation"], {"ticket": request["ticket"], "state": "expired"})

    def test_restart_request_returns_only_a_prompt(self):
        response = note_response({"ok": True, "pending": True, "confirmed": "ignored"}, "USB",
                                 "restart.request")
        self.assertEqual(response, {"ok": True, "transport": "USB", "pending": True})
        with self.assertRaises(SettingsError):
            note_response({"ok": True, "pending": False}, "USB", "restart.request")


if __name__ == "__main__":
    unittest.main()
