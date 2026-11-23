import http.client
import io
import json
import threading
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from bridge import Bridge, Handler, codex
from pala_google import DeliveryQueue, UsbGoogleDelivery


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
        self.assertEqual(json.loads(self.handler.wfile.getvalue()), {"ready": True, "processing": "transcribe"})
        connect.assert_called_once_with("localhost", 49125, timeout=2)
        connection.request.assert_called_once_with("GET", "/health")
        connection.close.assert_called_once()

    @patch("bridge.os.name", "nt")
    @patch("bridge.local_dictation_address", return_value=("localhost", 49125))
    @patch("bridge.http.client.HTTPConnection")
    def test_only_a_verified_busy_endpoint_defers_transport_fallback(self, connect, _address):
        cases = (
            (503, b'{"service":"local-dictation","ready":true,"busy":true}'),
            (502, b'{"service":"local-dictation","ready":false}'),
            (502, b'{"service":"local-dictation","ready":true,"busy":"true"}'),
            (502, b'{"service":"typewhisper","ready":true}'),
            (502, b'[]'),
            (502, b'{'),
            (502, TimeoutError()),
            (502, http.client.RemoteDisconnected()),
        )
        connection = connect.return_value
        for expected_status, health in cases:
            with self.subTest(health=health):
                self.handler.wfile = io.BytesIO()
                self.handler.send_response.reset_mock()
                connection.close.reset_mock()
                connection.getresponse.side_effect = (
                    health if isinstance(health, Exception)
                    else [Mock(status=200, read=Mock(return_value=health))]
                )

                self.handler.do_GET()

                self.handler.send_response.assert_called_once_with(expected_status)
                self.assertEqual(json.loads(self.handler.wfile.getvalue()), {"ready": False, "processing": "transcribe"})
                connection.close.assert_called_once()


class BridgeProcessingModeTests(unittest.TestCase):
    def test_transcribe_only_blocks_advanced_api_and_google_without_restarting(self):
        config = {"destination": "google", "google_routes": {"general": {"id": "list", "title": "General"}}}
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            bridge = Bridge(state, config)
            bridge.transcribe = Mock(return_value={"text": "Test"})
            delivery = UsbGoogleDelivery(state, config)
            queued, = delivery.queue.stage("note", 1, None, "First note")
            response = b'{"instructions":"Return a word","input":"Test"}'
            with patch("bridge.codex", return_value="Test") as codex:
                bridge.dispatch("/v1/responses", response, "application/json")
                self.assertEqual(codex.call_count, 1)
                (state / "bridge-processing.json").write_text('{"mode":"transcribe"}', encoding="utf-8")
                self.assertEqual(bridge.dispatch("/v1/audio/transcriptions", b"WAV", "multipart/form-data"), {"text": "Test"})
                with self.assertRaisesRegex(RuntimeError, "transcription only"):
                    bridge.dispatch("/v1/responses", response, "application/json")
                self.assertEqual(codex.call_count, 1)
            with patch.object(delivery, "_send") as send:
                delivery.flush([queued])
                send.assert_not_called()
            retained = delivery.stager("note")(2, None, "Second note")
            self.assertFalse(retained.organize_calendar)
            retained()
            self.assertTrue(any(record["text"] == "Second note" and record["onNote"]
                                for record in delivery.queue.note_transcripts(0)["records"]))
            self.assertEqual(delivery.queue.pending(), [queued])


class BridgeCodexEncodingTests(unittest.TestCase):
    def test_reads_codex_output_as_utf8(self):
        expected = "Skúška hlasovej nahrávky"

        def write_codex_output(args, **_kwargs):
            result = Path(args[args.index("-o") + 1])
            result.write_bytes(expected.encode("utf-8"))

        with patch("bridge.codex_command", return_value=["codex"]), \
                patch("bridge.require_chatgpt_login"), \
                patch("bridge.subprocess.run", side_effect=write_codex_output), \
                patch("pathlib.io.text_encoding", side_effect=lambda encoding: encoding or "cp1252"):
            self.assertEqual(codex("Synthetic test", None), expected)


class BridgeEmbeddingTests(unittest.TestCase):
    def test_prefers_cached_model_before_contacting_the_hub(self):
        bridge = Bridge.__new__(Bridge)
        bridge.lock = threading.Lock()
        bridge.encoder = None
        constructor = Mock()
        constructor.return_value.encode.return_value = [1.0] * 384

        with patch.dict(sys.modules, {"sentence_transformers": Mock(SentenceTransformer=constructor)}):
            result = bridge.dispatch("/v1/embeddings", b'{"input":"Synthetic"}', "application/json")
            bridge.dispatch("/v1/embeddings", b'{"input":"Again"}', "application/json")

        self.assertEqual(constructor.call_args.kwargs, {"local_files_only": True})
        self.assertEqual(constructor.call_count, 1)
        self.assertEqual(len(result["data"][0]["embedding"]), 256)


class WifiGoogleDeliveryTests(unittest.TestCase):
    def test_only_paired_memory_requests_stage_one_durable_recording(self):
        routes = {"general": {"id": "general", "title": "General"}}
        config = {"google_routes": routes, "token": "bridge-fixture",
                  "device_tokens": {"note-a": "paired-fixture"}}
        capture = {"pala_note": 22, "pala_created": "2026-11-21T15:40:00Z",
                   "pala_transcript": "Modrý zápisník."}

        with tempfile.TemporaryDirectory() as work:
            state = Path(work)
            bridge = Bridge(state, config)
            bridge.memories = Mock(return_value=("{}", []))

            def post(token, name):
                data = json.dumps({**capture, "instructions": "Summarize", "input": "Synthetic",
                                   "text": {"format": {"name": name, "schema": {}}}}).encode()
                handler = Handler.__new__(Handler)
                handler.server = Mock(bridge=bridge)
                handler.path = "/v1/responses"
                handler.headers = {"Authorization": f"Bearer {token}", "Content-Length": str(len(data))}
                handler.connection = Mock()
                handler.rfile, handler.wfile = io.BytesIO(data), io.BytesIO()
                handler.send_response, handler.send_error = Mock(), Mock()
                handler.send_header, handler.end_headers = Mock(), Mock()
                handler.do_POST()
                return handler

            post("unknown-fixture", "pala_memory").send_error.assert_called_once_with(401)
            post("bridge-fixture", "pala_memory").send_response.assert_called_once_with(200)
            with patch("bridge.codex", return_value="4"):
                post("paired-fixture", "pala_action_response").send_response.assert_called_once_with(200)
            queue = DeliveryQueue(state)
            self.assertEqual(queue.pending(), [])

            for _ in range(2):
                post("paired-fixture", "pala_memory").send_response.assert_called_once_with(200)
            pending = queue.pending()
            self.assertEqual(len(pending), 1)
            key = pending[0]
            send = Mock(return_value="receipt")
            self.assertEqual(queue.home_counts("note-a", routes)[1:], (0, 1))
            self.assertEqual(queue.home_counts("note-b", routes)[1:], (0, 0))
            self.assertEqual(queue.deliver(key, routes, send), "done")
            self.assertEqual(queue.home_counts("note-a", routes)[1:], (1, 0))
            self.assertEqual(queue.pending(), [])


if __name__ == "__main__":
    unittest.main()
