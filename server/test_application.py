import hashlib
import json
import tempfile
from pathlib import Path
import unittest
from starlette.testclient import TestClient
from starlette.websockets import WebSocketDisconnect
from local_dictation.application import create_app
from local_dictation.config import RuntimeConfig
from test_sessions import FakeEngine

class ApiTests(unittest.TestCase):
    def test_candidate_scopes_cover_speech_batch_live_and_model_management(self):
        desktop = "desktop-candidate-" + "a" * 32
        bridge = "bridge-candidate-" + "b" * 32
        wav = b"RIFF" + b"\0" * 4 + b"WAVE"
        spoken = []

        def speech(text):
            spoken.append(text)
            return wav

        def headers(key):
            return {"Authorization": "Bearer " + key}

        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            registry = root / "keys.json"
            desktop_digest = hashlib.sha256(desktop.encode()).hexdigest()
            keys = {desktop_digest: ["transcription", "speech", "models"],
                    hashlib.sha256(bridge.encode()).hexdigest(): ["transcription"]}
            registry.write_text(json.dumps(keys), encoding="utf-8")
            app = create_app(RuntimeConfig(root, root), FakeEngine(), application_keys=registry, speech=speech)
            with TestClient(app, base_url="http://127.0.0.1") as client:
                self.assertEqual(client.get("/health").status_code, 200)
                for route in ("/v1/audio/transcriptions", "/v1/audio/normalizations", "/v1/audio/verifications",
                              "/v1/models/test", "/v1/audio/speech"):
                    self.assertEqual(client.post(route, content=b"invalid").status_code, 401)
                self.assertEqual(client.post("/v1/audio/transcriptions", headers=headers(bridge),
                                             content=b"invalid").status_code, 400)
                self.assertEqual(client.get("/v1/models", headers=headers(bridge)).status_code, 403)
                self.assertEqual(client.get("/v1/models", headers=headers(desktop)).status_code, 200)
                self.assertEqual(client.post("/v1/audio/speech", headers=headers(bridge),
                                             json={"input": "Ahoj."}).status_code, 403)
                self.assertEqual(client.post("/v1/audio/speech", headers=headers(desktop),
                                             json={"input": "a" * 501}).status_code, 400)
                response = client.post("/v1/audio/speech", headers=headers(desktop),
                                       json={"input": "Ahoj.", "language": "sk", "voice": "lili"})
                self.assertEqual(response.status_code, 200)
                self.assertEqual(response.headers["content-type"], "audio/wav")
                self.assertEqual(response.headers["x-speech-backend"], "cpu")
                self.assertEqual(response.content, wav)
                self.assertEqual(spoken, ["Ahoj."])
                with self.assertRaises(WebSocketDisconnect):
                    with client.websocket_connect("ws://127.0.0.1/v1/audio/sessions"):
                        self.fail("Unauthenticated WebSocket was accepted")
                with client.websocket_connect("ws://127.0.0.1/v1/audio/sessions", headers=headers(desktop)) as ws:
                    ws.send_json({"type": "start", "protocol_version": 1, "model": "missing"})
                    self.assertEqual(ws.receive_json()["type"], "session.error")
                del keys[desktop_digest]
                registry.write_text(json.dumps(keys), encoding="utf-8")
                self.assertEqual(client.post("/v1/audio/speech", headers=headers(desktop),
                                             json={"input": "Ahoj."}).status_code, 401)

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
