"""Silent protocol regressions: synthetic PCM in memory, scripted recognizer only."""
import asyncio
from pathlib import Path
import unittest

from local_dictation.config import RuntimeConfig
from local_dictation.sessions import AudioSession

SECOND = 32000
FIRST_AUDIO = b"\0\x10" * 16000
SECOND_AUDIO = b"\0\x20" * 16000
SILENCE = bytes(SECOND)
FIRST_TEXT = "Prvá skúšobná veta."
SECOND_TEXT = "Pokračujem po pauze."
FINAL_TEXT = "Opravená prvá skúšobná veta. Pokračujem po pauze."


class ScriptedEngine:
    def __init__(self):
        self.calls = []

    def transcribe_pcm(self, pcm, model, language, cancelled):
        self.calls.append(pcm)
        first = b"\0\x10" in pcm
        second = b"\0\x20" in pcm
        text = FINAL_TEXT if first and second else SECOND_TEXT if second else FIRST_TEXT
        return {"text": text}

    def unload(self):
        pass


class PauseContinuationTests(unittest.IsolatedAsyncioTestCase):
    def prepare(self):
        self.events = []
        self.updates = asyncio.Queue()
        self.engine = ScriptedEngine()

        async def collect(event):
            self.events.append(event)
            if event["type"] == "transcript.updated":
                self.updates.put_nowait(event)

        self.session = AudioSession(
            RuntimeConfig(Path("."), Path("."), progress_interval_seconds=1),
            self.engine, {"model": "primary", "live": True}, collect)

    def feed(self, data):
        for start in range(0, len(data), SECOND):
            self.session.feed(data[start:start + SECOND])

    async def update(self):
        return await asyncio.wait_for(self.updates.get(), 3)

    async def until(self, condition):
        async def wait():
            while not condition():
                await asyncio.sleep(.001)
        await asyncio.wait_for(wait(), 3)

    def verify_final(self, audio):
        final = [e for e in self.events if e["type"] == "transcript.final"]
        self.assertEqual(len(final), 1)
        self.assertEqual(final[0]["text"], FINAL_TEXT)
        self.assertEqual(self.engine.calls[-1], audio)
        self.assertEqual(self.session.retained_bytes, 0)
        self.assertTrue(all(e["text"] for e in self.events if e["type"] == "transcript.updated"))
        self.assertEqual([e["revision"] for e in self.events], list(range(1, len(self.events) + 1)))
        self.assertEqual(self.events[-1]["type"], "session.completed")

    async def test_short_pause_preserves_draft_and_next_update_revises_the_whole_text(self):
        self.prepare()
        self.feed(FIRST_AUDIO * 3)
        task = asyncio.create_task(self.session.run())
        try:
            self.assertEqual((await self.update())["text"], FIRST_TEXT)
            self.feed(SILENCE * 3)
            await self.until(lambda: self.session.decoded_until == 6 * SECOND)
            self.assertEqual(self.session.snapshot_text(self.session.draft), FIRST_TEXT)
            self.assertFalse(self.session.finishing)
            self.feed(SECOND_AUDIO * 3)
            resumed = await self.update()
            self.assertEqual(resumed["text"], FINAL_TEXT)
            self.assertEqual(resumed["audio_received_seconds"], 9)
            self.session.finish()
            await asyncio.wait_for(task, 3)
            self.verify_final(FIRST_AUDIO * 3 + SILENCE * 3 + SECOND_AUDIO * 3)
        finally:
            self.session.finish()
            await asyncio.wait_for(task, 3)

    async def test_long_pause_crosses_quiet_commit_and_resumes_before_final_refinement(self):
        self.prepare()
        self.feed(FIRST_AUDIO * 3)
        task = asyncio.create_task(self.session.run())
        try:
            self.assertEqual((await self.update())["text"], FIRST_TEXT)
            self.feed(SILENCE * 23)
            committed = await self.update()
            self.assertEqual(committed["text"], FIRST_TEXT)
            self.assertEqual(committed["committed_text"], FIRST_TEXT)
            await self.until(lambda: self.session.buffer_start == 26 * SECOND and not self.session.buffer)
            self.assertFalse(self.session.finishing)
            self.feed(SECOND_AUDIO * 3)
            resumed = await self.update()
            self.assertEqual(resumed["text"], FIRST_TEXT + " " + SECOND_TEXT)
            self.assertEqual(resumed["audio_received_seconds"], 29)
            self.assertEqual(resumed["committed_text"], FIRST_TEXT)
            self.assertEqual(resumed["draft_text"], SECOND_TEXT)
            self.session.finish()
            await asyncio.wait_for(task, 3)
            self.verify_final(FIRST_AUDIO * 3 + SILENCE * 23 + SECOND_AUDIO * 3)
        finally:
            self.session.finish()
            await asyncio.wait_for(task, 3)
