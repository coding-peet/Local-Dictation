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
