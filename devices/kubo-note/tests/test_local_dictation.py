import http.client
import io
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from bridge import Bridge, Handler


class LocalDictationTranscriptionTests(unittest.TestCase):
    def setUp(self):
        self.bridge = Bridge.__new__(Bridge)
        self.bridge.config = {"transcription_model": "kinit/whisper-large-v3-turbo-sk"}

    def test_uses_saved_api_address_and_checks_service_before_upload(self):
        with patch("bridge.Path.read_text", return_value='{"Endpoint":"http://localhost:49125"}'), \
                patch("bridge.http.client.HTTPConnection") as connect:
            connection = connect.return_value
            connection.getresponse.side_effect = [
                Mock(status=200, read=Mock(return_value=b'{"service":"local-dictation","ready":true}')),
                Mock(status=200, read=Mock(return_value=b'{"text":"  Test.  "}')),
            ]

            self.assertEqual(self.bridge.transcribe_local(b"sample WAV"), {"text": "Test."})
            connect.assert_called_once_with("localhost", 49125, timeout=100)
            self.assertEqual(connection.request.call_args_list[0].args, ("GET", "/health"))
            upload = connection.request.call_args_list[1]
            self.assertEqual(upload.args[:2], ("POST", "/v1/audio/transcriptions"))
            self.assertIn(b"sample WAV", upload.args[2])

    def test_uses_local_dictation_default_before_first_save(self):
        with patch("bridge.Path.read_text", side_effect=FileNotFoundError), \
                patch("bridge.http.client.HTTPConnection") as connect:
            connection = connect.return_value
            connection.getresponse.side_effect = [
                Mock(status=200, read=Mock(return_value=b'{"service":"local-dictation","ready":true}')),
                Mock(status=200, read=Mock(return_value=b'{"text":"Test."}')),
            ]

            self.bridge.transcribe_local(b"sample WAV")
            connect.assert_called_once_with("127.0.0.1", 48125, timeout=100)

    def test_other_service_never_receives_audio(self):
        with patch("bridge.Path.read_text", return_value='{"Endpoint":"http://127.0.0.1:49125"}'), \
                patch("bridge.http.client.HTTPConnection") as connect:
            connection = connect.return_value
            connection.getresponse.return_value = Mock(
                status=200, read=Mock(return_value=b'{"service":"typewhisper","ready":true}')
            )

            with self.assertRaises(OSError):
                self.bridge.transcribe_local(b"sample WAV")
            connection.request.assert_called_once_with("GET", "/health")
            connection.close.assert_called_once()

    def test_rejects_remote_and_legacy_batch_addresses(self):
        for address in ("http://192.168.1.9:49125", "http://127.0.0.1:8123"):
            with self.subTest(address=address), \
                    patch("bridge.Path.read_text", return_value=f'{{"Endpoint":"{address}"}}'), \
                    patch("bridge.http.client.HTTPConnection") as connect:
                with self.assertRaises(ValueError):
                    self.bridge.transcribe_local(b"sample WAV")
                connect.assert_not_called()


class ReadinessTests(unittest.TestCase):
    def setUp(self):
        self.bridge = Bridge.__new__(Bridge)
        self.bridge.state = Path(".")
        self.bridge.config = {"token": "primary-fixture", "device_tokens": {"pala": "pala-fixture"}}
        self.handler = Handler.__new__(Handler)
        self.handler.server = Mock(bridge=self.bridge)
        self.handler.path = "/pala/ready"
        self.handler.headers = {"Authorization": "Bearer pala-fixture"}
        self.handler.wfile = io.BytesIO()
        self.handler.send_error = Mock()
        self.handler.send_response = Mock()
        self.handler.send_header = Mock()
        self.handler.end_headers = Mock()

    def test_unpaired_client_cannot_probe_local_dictation(self):
        self.handler.headers = {"Authorization": "Bearer unknown-fixture"}
        with patch("bridge.http.client.HTTPConnection") as connect:
            self.handler.do_GET()
        self.handler.send_error.assert_called_once_with(401)
        connect.assert_not_called()

    @patch("bridge.os.name", "nt")
    @patch("bridge.local_dictation_address", return_value=("localhost", 49125))
    @patch("bridge.http.client.HTTPConnection")
    def test_paired_device_can_probe_without_sending_audio(self, connect, _address):
        connection = connect.return_value
        connection.getresponse.return_value = Mock(
            status=200, read=Mock(return_value=b'{"service":"local-dictation","ready":true,"busy":false}')
        )

        self.handler.do_GET()

        self.handler.send_response.assert_called_once_with(200)
        self.assertEqual(json.loads(self.handler.wfile.getvalue()), {"ready": True})
        connect.assert_called_once_with("localhost", 49125, timeout=2)
        connection.request.assert_called_once_with("GET", "/health")
        connection.close.assert_called_once()

    @patch("bridge.os.name", "nt")
    @patch("bridge.local_dictation_address", return_value=("localhost", 49125))
    @patch("bridge.http.client.HTTPConnection")
    def test_unusable_local_endpoint_is_reported_unready(self, connect, _address):
        cases = (
            b'{"service":"local-dictation","ready":false}',
            b'{"service":"local-dictation","ready":true,"busy":true}',
            b'{"service":"typewhisper","ready":true}',
            b'[]',
            b'{',
            TimeoutError(),
            http.client.RemoteDisconnected(),
        )
        connection = connect.return_value
        for health in cases:
            with self.subTest(health=health):
                self.handler.wfile = io.BytesIO()
                self.handler.send_response.reset_mock()
                connection.close.reset_mock()
                connection.getresponse.side_effect = (
                    health if isinstance(health, Exception)
                    else [Mock(status=200, read=Mock(return_value=health))]
                )

                self.handler.do_GET()

                self.handler.send_response.assert_called_once_with(503)
                self.assertEqual(json.loads(self.handler.wfile.getvalue()), {"ready": False})
                connection.close.assert_called_once()


if __name__ == "__main__":
    unittest.main()
