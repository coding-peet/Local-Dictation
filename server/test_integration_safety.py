import asyncio
import json
from pathlib import Path
import tempfile
import threading
import unittest
from starlette.testclient import TestClient
from local_dictation.application import create_app
from local_dictation.config import RuntimeConfig
from local_dictation.engine_adapter import EngineAdapter
from local_dictation.sessions import AudioSession
from test_sessions import FakeEngine

class AdapterTests(unittest.TestCase):
    def test_worker_error_does_not_retain_original_inference_traceback(self):
        class Broken:
            def transcribe(self,*args,**kwargs):
                hidden_cuda_tensor=object()
                raise RuntimeError("private inference detail")
        adapter=EngineAdapter(Broken())
        try: adapter.transcribe(b"x","","some/model","sk",threading.Event())
        except RuntimeError as exc:
            names=[]
            trace=exc.__traceback__
            while trace:
                names.append(trace.tb_frame.f_code.co_name)
                trace=trace.tb_next
            self.assertNotIn("private inference detail",str(exc))
            self.assertNotIn("hidden_cuda_tensor",str([*locals()]))
            self.assertFalse(any("hidden_cuda_tensor" in t.tb_frame.f_locals for t in self.traces(exc)))
        else:self.fail("Expected safe inference failure.")

    @staticmethod
    def traces(exc):
        trace=exc.__traceback__
        while trace:
            yield trace
            trace=trace.tb_next

class AudioLifecycleTests(unittest.IsolatedAsyncioTestCase):
    async def test_early_primary_failure_still_unloads_and_releases_audio(self):
        class Broken(FakeEngine):
            def transcribe_pcm(self,*args):raise RuntimeError("failure")
        engine=Broken();events=[]
        session=AudioSession(RuntimeConfig(Path("."),Path(".")),engine,
            {"model":"fast"},self.sender(events))
        session.feed(b"\0\x10"*16000);session.finish()
        with self.assertRaises(RuntimeError):await session.run()
        self.assertEqual(engine.cleaned,1)
        self.assertEqual(session.retained_bytes,0)

    async def test_silent_recording_does_not_invoke_verifier(self):
        engine=FakeEngine();events=[]
        session=AudioSession(RuntimeConfig(Path("."),Path(".")),engine,
            {"model":"fast","verification_model":"verify"},self.sender(events))
        session.feed(b"\0"*32000);session.finish();await session.run()
        self.assertEqual(engine.calls,[])

    @staticmethod
    def sender(events):
        async def send(e):events.append(e)
        return send

class ModelTestSafety(unittest.TestCase):
    def test_cleanup_failure_does_not_mark_model_runtime_tested(self):
        class Catalog:
            marked=False
            def require(self,model):return {"id":"test/model","languages":["sk","en","auto"]}
            def mark_tested(self,model):self.marked=True
        class Engine:
            def transcribe(self,*args):return {"text":"sample"}
            def unload(self):raise RuntimeError("cleanup failed")
        with tempfile.TemporaryDirectory() as d:
            catalog=Catalog()
            app=create_app(RuntimeConfig(Path(d),Path(d)),Engine(),catalog)
            with TestClient(app,base_url="http://127.0.0.1") as client:
                response=client.post("/v1/models/test",data={"model":"test/model"},files={"file":("sample.wav",b"audio")})
                self.assertEqual(response.status_code,500)
                self.assertFalse(catalog.marked)
                self.assertFalse(client.get("/health").json()["ready"])
