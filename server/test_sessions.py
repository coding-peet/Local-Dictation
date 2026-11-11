import asyncio
from pathlib import Path
import threading
import unittest
from local_dictation.config import RuntimeConfig
from local_dictation.sessions import AudioSession, Admission, BusyError

class FakeEngine:
    def __init__(self, fail_verify=False):
        self.calls, self.cleaned = [], 0
        self.fail_verify = fail_verify
    def transcribe_pcm(self, pcm, model, language, cancelled):
        self.calls.append((len(pcm), model))
        if model == "verify" and self.fail_verify:
            raise RuntimeError("private engine error")
        return {"text": "Ahoj svet." if model == "fast" else "Ahoj, svet."}
    def unload(self):
        self.cleaned += 1

class SessionTests(unittest.IsolatedAsyncioTestCase):
    async def test_live_revisions_replace_the_draft_without_appending_earlier_decodes(self):
        class RevisingEngine(FakeEngine):
            def transcribe_pcm(self, pcm, model, language, cancelled):
                self.calls.append((len(pcm),model))
                return {"text": ["Áno, áno.","Áno, áno, to môžeme skúsiť."][len(self.calls)-1]}
        engine, events=RevisingEngine(),[]
        async def send(event):
            events.append(event)
            if event["type"]=="transcript.updated" and len(engine.calls)==1:
                session.feed(b"\0\x10"*16000)
                session.finish()
        session=AudioSession(RuntimeConfig(Path("."),Path(".")),engine,
            {"model":"fast"},send)
        session.feed(b"\0\x10"*16000)
        await session.run()
        self.assertEqual(engine.calls,[(32000,"fast"),(64000,"fast")])
        self.assertEqual([e["text"] for e in events if e["type"]=="transcript.updated"],
            ["Áno, áno.","Áno, áno, to môžeme skúsiť."])
        self.assertEqual(next(e["text"] for e in events if e["type"]=="transcript.final"),
            "Áno, áno, to môžeme skúsiť.")

    async def test_live_cut_and_stop_decode_every_sample_once_and_preserve_spoken_repetition(self):
        class ChunkEngine(FakeEngine):
            def __init__(self):super().__init__();self.pcm=[]
            def transcribe_pcm(self, pcm, model, language, cancelled):
                self.pcm.append(pcm)
                return {"text":"Áno, áno." if len(self.pcm)==1 else "Nie, nie, nie."}
        engine, events=ChunkEngine(),[]
        session=AudioSession(RuntimeConfig(Path("."),Path(".")),engine,
            {"model":"fast"},self.sender(events))
        frames=[b"\0\x10"*16000]*20+[b"\0\0"*16000]+[b"\0\x20"*16000]*6
        for frame in frames:session.feed(frame)
        session.finish()
        await session.run()
        self.assertEqual(len(engine.pcm),2)
        self.assertEqual(b"".join(engine.pcm),b"".join(frames))
        self.assertEqual(next(e["text"] for e in events if e["type"]=="transcript.final"),
            "Áno, áno. Nie, nie, nie.")

    async def test_silence_after_a_committed_phrase_is_not_decoded_as_another_phrase(self):
        engine, events=FakeEngine(),[]
        async def send(event):
            events.append(event)
            if event["type"]=="transcript.updated":
                session.feed(b"\0\0"*16000)
                session.finish()
        session=AudioSession(RuntimeConfig(Path("."),Path(".")),engine,
            {"model":"fast"},send)
        session.feed(b"\0\x10"*16000)
        session.feed(b"\0\0"*16000)
        await session.run()
        self.assertEqual(engine.calls,[(64000,"fast")])
        self.assertEqual(next(e["text"] for e in events if e["type"]=="transcript.final"),"Ahoj svet.")

    async def test_final_and_verification_are_distinct_and_audio_released(self):
        engine, events = FakeEngine(), []
        session = AudioSession(RuntimeConfig(Path("."), Path(".")), engine,
                               {"model": "fast", "language": "sk", "verification_model": "verify"},
                               self.sender(events))
        session.feed(b"\x00\x10" * 16000)
        session.finish()
        await session.run()
        types = [e["type"] for e in events]
        self.assertLess(types.index("transcript.final"), types.index("verification.completed"))
        self.assertEqual(types[-1], "session.completed")
        self.assertEqual([e["revision"] for e in events], list(range(1, len(events)+1)))
        self.assertEqual(engine.calls, [(32000, "fast"), (32000, "verify")])
        self.assertEqual(engine.cleaned, 2)
        self.assertEqual(session.retained_bytes, 0)

    async def test_verification_failure_preserves_primary(self):
        events = []
        session = AudioSession(RuntimeConfig(Path("."), Path(".")), FakeEngine(True),
                               {"model": "fast", "verification_model": "verify"}, self.sender(events))
        session.feed(b"\x00\x10" * 16000)
        session.finish()
        await session.run()
        self.assertTrue(any(e["type"] == "transcript.final" for e in events))
        self.assertTrue(any(e["type"] == "verification.failed" for e in events))
        self.assertNotIn("private engine error", str(events))

    async def test_audio_limits_and_frame_alignment(self):
        s = AudioSession(RuntimeConfig(Path("."), Path("."), max_seconds=1), FakeEngine(),
                         {"model":"fast"}, self.sender([]))
        with self.assertRaises(ValueError): s.feed(b"x")
        s.feed(b"\0" * 32000)
        self.assertFalse(s.feed(b"\0\0"))
        s.cancel()
        await s.run()
        self.assertEqual(s.retained_bytes, 0)

    async def test_gate_rejects_primary_and_preempts_optional_verification(self):
        gate = Admission()
        cancel = threading.Event()
        async with gate.own("primary", cancel):
            with self.assertRaises(BusyError):
                async with gate.own("primary", threading.Event()): pass
        async with gate.own("verification", cancel):
            task = asyncio.create_task(gate.acquire("primary", threading.Event()))
            await asyncio.sleep(0.01)
            self.assertTrue(cancel.is_set())
        await task
        gate.release()

    @staticmethod
    def sender(events):
        async def send(event): events.append(event)
        return send
