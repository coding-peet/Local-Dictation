import hashlib
from io import BytesIO
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import wave

from googleapiclient.errors import HttpError

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from bridge import Bridge, Handler
from google_store import apply_google


class CalendarWriteReplayTest(unittest.TestCase):
    def test_provider_conflict_requires_matching_receipt_and_never_creates_second_event(self):
        key = "fixture-operation"
        identifier = hashlib.sha256(key.encode()).hexdigest()
        action = {"title": "Synthetic event", "due": "2026-12-04T09:30",
                  "end": "2026-12-04T10:00", "minutes": -1}
        config = {"calendar": "fixture-calendar", "timezone": "Europe/Bratislava"}
        receipt = {"id": identifier, "summary": action["title"], "status": "confirmed",
                   "start": {"dateTime": "2026-12-04T08:30:00Z"},
                   "end": {"dateTime": "2026-12-04T09:00:00Z"}}

        for provider_row, accepted in (
            (receipt, True),
            ({**receipt, "id": "unrelated-id"}, False),
            ({**receipt, "status": "cancelled"}, False),
            ({**receipt, "end": {"dateTime": "2026-12-04T09:01:00Z"}}, False),
        ):
            with self.subTest(row=provider_row), patch("google_store.google_credentials"),                     patch("googleapiclient.discovery.build") as build:
                events = build.return_value.events.return_value
                events.insert.return_value.execute.side_effect = HttpError(
                    SimpleNamespace(status=409, reason="Conflict"), b"{}")
                events.get.return_value.execute.return_value = provider_row
                if accepted:
                    apply_google(config, Path("unused"), action, key)
                else:
                    with self.assertRaises(RuntimeError):
                        apply_google(config, Path("unused"), action, key)
                events.insert.assert_called_once()
                self.assertEqual(events.insert.call_args.kwargs["body"]["id"], identifier)
                events.get.assert_called_once_with(calendarId="fixture-calendar", eventId=identifier)


class SpeechBridgeTest(unittest.TestCase):
    def handler(self, bridge, document):
        body = json.dumps(document).encode()
        return SimpleNamespace(
            path="/v1/audio/speech", server=SimpleNamespace(bridge=bridge),
            headers={"Authorization": "Bearer fixture-note-token", "Content-Length": str(len(body)),
                     "Content-Type": "application/json"},
            connection=Mock(), rfile=BytesIO(body), wfile=BytesIO(),
            send_response=Mock(), send_error=Mock(), send_json=Mock(),
            send_header=Mock(), end_headers=Mock(), log_error=Mock(),
        )

    def test_paired_note_gets_exact_pcm_and_no_provider_mutation(self):
        expected = bytes((1, 2, 3, 4, 5, 6))

        def synthesize(command, *, input, **kwargs):
            self.assertEqual(input, b"Synthetic answer")
            self.assertTrue(kwargs["check"])
            output = command[-1] if os.name == "nt" else command[command.index("-o") + 1]
            with wave.open(output, "wb") as wav:
                wav.setparams((1, 2, 24000, 0, "NONE", "not compressed"))
                wav.writeframes(expected)

        with tempfile.TemporaryDirectory() as temporary,                 patch("bridge.subprocess.run", side_effect=synthesize) as launch:
            bridge = Bridge(Path(temporary), {
                "processing_mode": "kubo", "device_tokens": {"synthetic-note": "fixture-note-token"},
            })
            with patch.object(bridge, "deliver") as deliver:
                handler = self.handler(bridge, {"input": "Synthetic answer", "response_format": "pcm"})
                Handler.do_POST(handler)
                handler.send_response.assert_called_once_with(200)
                handler.send_error.assert_not_called()
                self.assertEqual(handler.wfile.getvalue(), expected)
                deliver.assert_not_called()
            launch.assert_called_once()

    def test_speech_backend_timeout_returns_retryable_error_without_partial_content(self):
        with tempfile.TemporaryDirectory() as temporary,                 patch("bridge.subprocess.run", side_effect=subprocess.TimeoutExpired("fixture-synth", 60)) as launch:
            bridge = Bridge(Path(temporary), {
                "processing_mode": "kubo", "device_tokens": {"synthetic-note": "fixture-note-token"},
            })
            handler = self.handler(bridge, {"input": "Synthetic answer", "response_format": "pcm"})
            Handler.do_POST(handler)
            handler.send_error.assert_called_once_with(503)
            handler.send_response.assert_not_called()
            self.assertEqual(handler.wfile.getvalue(), b"")
            launch.assert_called_once()


if __name__ == "__main__":
    unittest.main()
