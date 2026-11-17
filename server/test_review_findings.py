"""Regressions from the independent whole-branch review."""
import asyncio
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
from starlette.testclient import TestClient
from local_dictation.application import create_app
from local_dictation.catalog import Catalog
from local_dictation.config import RuntimeConfig
from local_dictation.downloads import inspect_repository
from local_dictation.sessions import AudioSession
from test_sessions import FakeEngine

class CancellationAndLimitTests(unittest.IsolatedAsyncioTestCase):
    async def test_task_cancellation_joins_worker_before_unload(self):
        class Blocking(FakeEngine):
            def __init__(self):
                super().__init__();self.started=threading.Event();self.release=threading.Event()
                self.running=False;self.overlap=[];self.cancel_event=None
            def transcribe_pcm(self,pcm,model,language,cancelled):
                self.running=True;self.cancel_event=cancelled;self.started.set()
                self.release.wait(0.15);self.running=False
                return {"text":"draft"}
            def unload(self):
                self.overlap.append(self.running);super().unload()
        engine=Blocking();events=[]
        async def send(e):events.append(e)
        s=AudioSession(RuntimeConfig(Path("."),Path(".")),engine,{"model":"fast"},send)
        s.feed(b"\0\x10"*16000);s.finish()
        task=asyncio.create_task(s.run())
        self.assertTrue(await asyncio.to_thread(engine.started.wait,1))
        task.cancel()
        with self.assertRaises(asyncio.CancelledError):await task
        self.assertTrue(engine.cancel_event.is_set())
        self.assertEqual(engine.overlap,[False])
        self.assertEqual(s.retained_bytes,0)

    async def test_limit_finishes_and_transcribes_accepted_batch_audio(self):
        engine=FakeEngine();events=[]
        async def send(e):events.append(e)
        s=AudioSession(RuntimeConfig(Path("."),Path("."),max_seconds=1),engine,
                       {"model":"fast","live":False},send)
        s.feed(b"\0\x10"*16000)
        s.feed(b"\0\x10"*100)
        s.finish();await s.run()
        self.assertEqual(engine.calls,[(32000,"fast")])
        self.assertTrue(any(e["type"]=="recording.limit_reached" for e in events))
        self.assertTrue(any(e["type"]=="transcript.final" for e in events))

    async def test_cooperative_cancel_is_a_cancelled_outcome_not_server_failure(self):
        class Cooperative(FakeEngine):
            def __init__(self):super().__init__();self.started=threading.Event()
            def transcribe_pcm(self,pcm,model,language,cancelled):
                self.started.set();cancelled.wait(1);raise RuntimeError("cancelled generation")
        engine=Cooperative();events=[]
        async def send(e):events.append(e)
        s=AudioSession(RuntimeConfig(Path("."),Path(".")),engine,{"model":"fast","live":False},send)
        s.feed(b"\0\x10"*16000);s.finish()
        task=asyncio.create_task(s.run())
        self.assertTrue(await asyncio.to_thread(engine.started.wait,1))
        s.cancel();await task
        self.assertEqual(events[-1]["type"],"session.cancelled")
        self.assertEqual(engine.cleaned,1)
        self.assertEqual(s.retained_bytes,0)

class RegistryAndDeadlineTests(unittest.TestCase):
    class Models:
        entries={}
        def require(self,model):
            if model=="english":
                return {"id":model,"languages":["en","auto"],"live_supported":False,"runtime_tested":True}
            return {"id":model,"languages":["sk"],"live_supported":True,"runtime_tested":True}
        def models(self):return []
    def test_english_primary_cannot_use_slovak_verifier(self):
        config=RuntimeConfig(Path("."),Path("."))
        app=create_app(config,FakeEngine(),self.Models()).app
        with self.assertRaises(ValueError):
            app.state.validate({"type":"start","protocol_version":1,"model":"english",
                "live":False,"language":"en","verification_model":"slovak"})

    def test_silent_connected_client_has_bounded_idle_time(self):
        config=RuntimeConfig(Path("."),Path("."),idle_timeout_seconds=0.05)
        with TestClient(create_app(config,FakeEngine(),self.Models()),base_url="http://127.0.0.1") as client:
            with client.websocket_connect("ws://127.0.0.1/v1/audio/sessions") as ws:
                ws.send_json({"type":"start","protocol_version":1,"model":"slovak"})
                self.assertEqual(ws.receive_json()["type"],"session.started")
                event=ws.receive_json()
                self.assertEqual(event["type"],"session.error")
                self.assertIn("stalled",event["message"].lower())

    def test_missing_tokenizer_assets_are_rejected(self):
        from test_catalog import CatalogTests
        fetch=CatalogTests().metadata()
        def missing(url):
            data=fetch(url)
            if "/api/models/" in url:
                data["siblings"]=[f for f in data["siblings"] if f["rfilename"] not in ("tokenizer.json","vocab.json","merges.txt")]
            return data
        with self.assertRaises(ValueError):inspect_repository("openai/whisper-tiny",missing)

    def test_failed_registry_persistence_does_not_publish_memory_entry(self):
        with tempfile.TemporaryDirectory() as d:
            c=Catalog(RuntimeConfig(Path(d),Path(d)/"data"))
            with patch.object(c,"save",side_effect=OSError("disk full")):
                with self.assertRaises(OSError):
                    c.add({"id":"new/model","revision":"a"*40,"path":str(Path(d)/"model"),"name":"New",
                           "languages":["en"]})
            self.assertNotIn("new/model",c.entries)
