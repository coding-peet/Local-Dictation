import threading
import unittest
from pathlib import Path
from starlette.testclient import TestClient
from local_dictation.application import create_app
from local_dictation.config import RuntimeConfig
from local_dictation.bootstrap import PreparingEngine
import test_review_findings

class PreparingTests(unittest.TestCase):
    def test_health_is_responsive_and_admission_blocked_during_library_loading(self):
        release=threading.Event()
        class Engine:
            catalog=None
            def status(self):return {"ready":True}
        def load():release.wait(2);return Engine()
        engine=PreparingEngine(load)
        engine.start()
        try:
            with TestClient(create_app(RuntimeConfig(Path("."),Path(".")),engine,test_review_findings.RegistryAndDeadlineTests.Models()),
                            base_url="http://127.0.0.1") as client:
                result=client.get("/health").json()
                self.assertFalse(result["ready"])
                self.assertTrue(result["runtime"]["preparing"])
                with client.websocket_connect("ws://127.0.0.1/v1/audio/sessions") as ws:
                    ws.send_json({"type":"start","protocol_version":1,"model":"slovak"})
                    self.assertEqual(ws.receive_json()["type"],"session.error")
        finally:release.set();engine.thread.join(2)
        self.assertTrue(engine.status()["ready"])
