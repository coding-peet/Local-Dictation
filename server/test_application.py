import tempfile
from pathlib import Path
import unittest
from starlette.testclient import TestClient
from local_dictation.application import create_app
from local_dictation.config import RuntimeConfig
from test_sessions import FakeEngine

class ApiTests(unittest.TestCase):
    def test_loopback_host_and_origin_guards(self):
        with tempfile.TemporaryDirectory() as d:
            app = create_app(RuntimeConfig(Path(d), Path(d)), FakeEngine())
            with TestClient(app, base_url="http://127.0.0.1") as client:
                self.assertEqual(client.get("/health").status_code, 200)
                self.assertEqual(client.get("/health", headers={"Origin":"http://evil.example"}).status_code,403)
                self.assertEqual(client.get("/health", headers={"Host":"evil.example"}).status_code,403)

    def test_session_protocol_and_unknown_model(self):
        with tempfile.TemporaryDirectory() as d:
            with TestClient(create_app(RuntimeConfig(Path(d), Path(d)), FakeEngine()),
                            base_url="http://127.0.0.1") as client:
                with client.websocket_connect("ws://127.0.0.1/v1/audio/sessions") as ws:
                    ws.send_json({"type":"start","protocol_version":1,"model":"missing"})
                    event=ws.receive_json()
                    self.assertEqual(event["type"],"session.error")
