"""Synthetic scheduling and preservation regressions; no models or microphone."""
import asyncio
from pathlib import Path
import threading
import unittest

from local_dictation.config import RuntimeConfig
from local_dictation.sessions import AudioSession

SECOND = 32000
VOICE = b"\0\x10" * 16000
SILENCE = b"\0\0" * 16000


class RecordingEngine:
    def __init__(self):
        self.calls = []
        self.cleaned = 0

    def transcribe_pcm(self, pcm, model, language, cancelled):
        self.calls.append((pcm, model))
        return {"text": "Áno, áno, áno." if model == "primary" else "Áno, iste."}

    def unload(self):
        self.cleaned += 1


class LiveQualityTests(unittest.IsolatedAsyncioTestCase):
    def session(self, engine=None, send=None, **options):
        self.events = []
        async def collect(event):
            self.events.append(event)
            if send:
                await send(event)
        return AudioSession(RuntimeConfig(Path("."), Path("."),
            progress_interval_seconds=.01), engine or RecordingEngine(),
            {"model": "primary", **options}, collect)

    @staticmethod
    def feed(session, pcm):
        for start in range(0, len(pcm), SECOND):
            session.feed(pcm[start:start+SECOND])

    async def run_session(self, session):
        await asyncio.wait_for(session.run(), 3)

    async def test_live_keeps_original_audio_without_optional_verification(self):
        session = self.session()
        self.feed(session, VOICE * 4)
        self.assertTrue(bytes(session.full_audio) == VOICE * 4)

    async def test_backlog_over_32_seconds_is_accepted_until_configured_limit(self):
        session = self.session()
        self.feed(session, VOICE * 41)
        self.assertFalse(session.finishing)
        self.assertFalse(session.limit_reached)
        self.assertEqual(session.total_bytes, SECOND * 41)
        session.finish()
        await self.run_session(session)
        self.assertTrue(session.engine.calls[-1][0] == VOICE * 41)
        self.assertEqual(session.retained_bytes, 0)

    async def test_first_preview_waits_for_three_seconds_of_audio(self):
        engine = RecordingEngine()
        session = self.session(engine)
        self.feed(session, VOICE * 2)
        task = asyncio.create_task(session.run())
        try:
            await asyncio.sleep(.03)
            self.assertEqual(engine.calls, [])
            session.feed(VOICE)
            for _ in range(100):
                if engine.calls:
                    break
                await asyncio.sleep(.005)
            self.assertEqual(len(engine.calls[0][0]), SECOND * 3)
        finally:
            session.finish()
            await asyncio.wait_for(task, 3)

    async def test_finish_refines_original_audio_once_without_tail_decode(self):
        engine = RecordingEngine()
        audio = VOICE * 3 + b"\0\x20" * 16000
        async def stop(event):
            if event["type"] == "transcript.updated":
                session.feed(audio[SECOND*3:])
                session.finish()
        session = self.session(engine, stop)
        self.feed(session, audio[:SECOND*3])
        await self.run_session(session)
        self.assertTrue([pcm for pcm, _ in engine.calls] == [audio[:SECOND*3], audio])
        final = next(e for e in self.events if e["type"] == "transcript.final")
        self.assertEqual(final["text"], "Áno, áno, áno.")
        self.assertEqual(final.get("audio_received_seconds"), 4)
        self.assertTrue(final.get("refined"))
        self.assertEqual(engine.cleaned, 1)

    async def test_finish_reuses_exact_unchanged_whole_recording_preview(self):
        engine = RecordingEngine()
        async def stop(event):
            if event["type"] == "transcript.updated":
                session.finish()
        session = self.session(engine, stop)
        self.feed(session, VOICE * 3)
        await self.run_session(session)
        self.assertTrue(engine.calls == [(VOICE * 3, "primary")])

    async def test_stop_before_first_preview_runs_one_full_primary_pass(self):
        session = self.session()
        self.feed(session, VOICE * 41)
        session.finish()
        await self.run_session(session)
        self.assertTrue(session.engine.calls == [(VOICE * 41, "primary")])

    async def test_whole_preview_reuse_requires_same_generation_profile(self):
        engine = RecordingEngine()
        async def stop(event):
            if event["type"] == "transcript.updated":
                session.options["primary_revision"] = "changed"
                session.finish()
        session = self.session(engine, stop, primary_revision="original")
        self.feed(session, VOICE*3)
        await self.run_session(session)
        self.assertEqual([(len(pcm),model) for pcm,model in engine.calls],
            [(SECOND*3,"primary"),(SECOND*3,"primary")])

    async def test_slow_arrival_coalesces_missed_preview_ticks(self):
        class Revising(RecordingEngine):
            def transcribe_pcm(self, pcm, model, language, cancelled):
                result = super().transcribe_pcm(pcm,model,language,cancelled)
                result["text"] += str(len(self.calls))
                return result
        engine = Revising()
        async def advance(event):
            if event["type"] == "transcript.updated":
                if len(engine.calls) == 1:
                    self.feed(session, VOICE*7)
                else:
                    session.finish()
        session = self.session(engine, advance)
        self.feed(session, VOICE*3)
        await self.run_session(session)
        self.assertEqual([len(pcm) for pcm,_ in engine.calls], [SECOND*3,SECOND*10])

    async def test_backlog_live_commits_are_disjoint_and_final_has_every_sample(self):
        # Tie energies choose earliest 12 s cut. No duplicated or missing samples
        # in disjoint commitments, even with several queued chunks.
        engine = RecordingEngine()
        audio = VOICE*48
        async def stop(event):
            if event["type"] == "transcript.updated" and len(engine.calls) == 3:
                session.finish()
        session = self.session(engine, stop)
        self.feed(session, audio)
        await self.run_session(session)
        self.assertEqual([len(pcm) for pcm,_ in engine.calls], [SECOND*12]*3+[len(audio)])
        self.assertTrue(b"".join(pcm for pcm,_ in engine.calls[:3]) == audio[:SECOND*36])
        self.assertTrue(engine.calls[-1][0] == audio)
        self.assertEqual(next(e["text"] for e in self.events if e["type"]=="transcript.final"),
            "Áno, áno, áno.")

    async def test_final_primary_is_full_recording_and_verification_is_separate(self):
        engine = RecordingEngine()
        async def stop(event):
            if event["type"] == "transcript.updated":
                session.feed(VOICE)
                session.finish()
        session = self.session(engine, stop, verification_model="verify")
        self.feed(session, VOICE * 3)
        await self.run_session(session)
        self.assertTrue(engine.calls == [(VOICE*3,"primary"), (VOICE*4,"primary"),
            (VOICE*4,"verify")])
        self.assertEqual(engine.cleaned, 2)
        types = [e["type"] for e in self.events]
        self.assertLess(types.index("transcript.final"), types.index("verification.started"))
        self.assertEqual([e["revision"] for e in self.events], list(range(1,len(self.events)+1)))

    async def test_quiet_valley_commits_disjoint_spans_bounded_by_24_seconds(self):
        # A unique 100 ms valley centered at 17 s must win the 12..24 s search.
        audio = VOICE * 17
        audio = audio[:-1600] + b"\0"*3200 + audio[-1600:]
        audio += VOICE * 8
        engine = RecordingEngine()
        async def stop(event):
            if event["type"] == "transcript.updated":
                session.finish()
        session = self.session(engine, stop)
        self.feed(session, audio)
        await self.run_session(session)
        self.assertEqual(len(engine.calls[0][0]), 17*SECOND)
        self.assertLessEqual(len(engine.calls[0][0]), 24*SECOND)
        self.assertTrue(engine.calls[-1] == (audio,"primary"))

    async def test_silent_tail_is_never_decoded_in_isolation(self):
        engine = RecordingEngine()
        async def stop(event):
            if event["type"] == "transcript.updated":
                self.feed(session, SILENCE * 2)
                session.finish()
        session = self.session(engine, stop)
        self.feed(session, VOICE * 3)
        await self.run_session(session)
        self.assertTrue([pcm for pcm, _ in engine.calls] == [VOICE*3, VOICE*3+SILENCE*2])

    async def test_all_silent_input_keeps_empty_final_without_inference(self):
        session = self.session(verification_model="verify")
        self.feed(session, SILENCE * 41)
        session.finish()
        await self.run_session(session)
        self.assertEqual(session.engine.calls, [])
        self.assertEqual(next(e["text"] for e in self.events if e["type"]=="transcript.final"), "")

    async def test_brief_voice_in_a_quiet_packet_is_retained_for_primary_refinement(self):
        engine = RecordingEngine()
        async def stop(event):
            if event["type"] == "transcript.updated":
                session.feed(SILENCE)
                session.finish()
        session = self.session(engine, stop)
        # 20 ms at RMS .01, followed by 80 ms silence: packet RMS < .008.
        packet = b"\x48\x01"*320 + b"\0"*2560
        for _ in range(30):
            session.feed(packet)
        await self.run_session(session)
        self.assertEqual(next(e["text"] for e in self.events if e["type"]=="transcript.final"),
            "Áno, áno, áno.")
        self.assertEqual(len(engine.calls), 2)

    async def test_empty_refinement_preserves_previous_text_and_fails_without_blank_final(self):
        class EmptyFinal(RecordingEngine):
            def transcribe_pcm(self, pcm, model, language, cancelled):
                result = super().transcribe_pcm(pcm,model,language,cancelled)
                if len(self.calls)>1:
                    result["text"] = ""
                return result
        engine = EmptyFinal()
        async def stop(event):
            if event["type"]=="transcript.updated":
                session.feed(VOICE)
                session.finish()
        session = self.session(engine, stop)
        self.feed(session, VOICE*3)
        with self.assertRaisesRegex(RuntimeError, "refinement"):
            await self.run_session(session)
        self.assertFalse(any(e["type"]=="transcript.final" for e in self.events))
        self.assertEqual(session.snapshot_text(session.draft), "Áno, áno, áno.")
        self.assertEqual(engine.cleaned, 1)
        self.assertEqual(session.retained_bytes, 0)

    async def assert_empty_live_replacement_preserves_received_words(self, extra_seconds):
        class EmptySecond(RecordingEngine):
            def transcribe_pcm(self, pcm, model, language, cancelled):
                result = super().transcribe_pcm(pcm,model,language,cancelled)
                if len(self.calls)>1:
                    result["text"] = ""
                return result
        engine = EmptySecond()
        async def advance(event):
            if event["type"]=="transcript.updated":
                if len(engine.calls)==1:
                    self.feed(session, VOICE*extra_seconds)
                else:
                    session.finish()
            # Empty unchanged snapshots need no transcript.updated. Finish on
            # heartbeat too, so the RED case fails on data loss, not timeout.
            elif event["type"]=="session.progress" and len(engine.calls)>1:
                session.finish()
        session = self.session(engine, advance)
        self.feed(session, VOICE*3)
        with self.assertRaisesRegex(RuntimeError, "empty live"):
            await self.run_session(session)
        self.assertEqual([e["text"] for e in self.events if e["type"]=="transcript.updated"],
            ["Áno, áno, áno."])
        self.assertFalse(any(e["type"]=="transcript.final" for e in self.events))
        self.assertEqual(session.snapshot_text(session.draft), "Áno, áno, áno.")
        self.assertEqual(session.last_primary_span, (0,SECOND*3))
        self.assertEqual(session.decoded_until, SECOND*3)
        self.assertEqual(engine.cleaned, 1)
        self.assertEqual(session.retained_bytes, 0)

    async def test_empty_growing_preview_preserves_nonempty_received_draft(self):
        await self.assert_empty_live_replacement_preserves_received_words(3)

    async def test_empty_quiet_valley_commit_preserves_nonempty_received_draft(self):
        await self.assert_empty_live_replacement_preserves_received_words(21)

    async def test_audio_arriving_during_preview_is_decoded_at_latest_available_tick(self):
        class SlowFirst(RecordingEngine):
            def __init__(self):
                super().__init__()
                self.started, self.release, self.second = (threading.Event() for _ in range(3))
            def transcribe_pcm(self, pcm, model, language, cancelled):
                if not self.calls:
                    self.started.set()
                    self.release.wait(2)
                else:
                    self.second.set()
                result = super().transcribe_pcm(pcm,model,language,cancelled)
                result["text"] += str(len(self.calls))
                return result
        engine = SlowFirst()
        session = self.session(engine)
        self.feed(session, VOICE*3)
        task = asyncio.create_task(session.run())
        try:
            self.assertTrue(await asyncio.to_thread(engine.started.wait, 1))
            self.feed(session, VOICE*7)
            engine.release.set()
            self.assertTrue(await asyncio.to_thread(engine.second.wait, .25))
        finally:
            engine.release.set()
            session.finish()
            await asyncio.wait_for(task, 3)
        self.assertEqual([len(pcm) for pcm,_ in engine.calls], [SECOND*3,SECOND*10])

    async def test_full_refinement_keeps_finishing_heartbeat_until_final_is_ready(self):
        class SlowFinal(RecordingEngine):
            def __init__(self):
                super().__init__()
                self.started, self.release = threading.Event(), threading.Event()
            def transcribe_pcm(self, pcm, model, language, cancelled):
                if self.calls:
                    self.started.set()
                    self.release.wait(2)
                return super().transcribe_pcm(pcm,model,language,cancelled)
        engine = SlowFinal()
        async def stop(event):
            if event["type"]=="transcript.updated":
                session.feed(VOICE)
                session.finish()
        session = self.session(engine, stop)
        self.feed(session, VOICE*3)
        task = asyncio.create_task(session.run())
        try:
            self.assertTrue(await asyncio.to_thread(engine.started.wait, 1))
            await asyncio.sleep(.03)
            self.assertTrue(any(e["type"]=="session.progress" and e["phase"]=="finishing"
                for e in self.events))
            self.assertFalse(any(e["type"]=="transcript.final" for e in self.events))
        finally:
            engine.release.set()
            await asyncio.wait_for(task, 3)
        self.assertEqual(engine.cleaned, 1)

    async def test_slow_preview_does_not_truncate_queued_audio(self):
        class Slow(RecordingEngine):
            def __init__(self):
                super().__init__()
                self.started, self.release = threading.Event(), threading.Event()
            def transcribe_pcm(self, pcm, model, language, cancelled):
                if not self.calls:
                    self.started.set()
                    self.release.wait(2)
                return super().transcribe_pcm(pcm,model,language,cancelled)
        engine = Slow()
        session = self.session(engine)
        self.feed(session, VOICE*3)
        task = asyncio.create_task(session.run())
        try:
            self.assertTrue(await asyncio.to_thread(engine.started.wait, 1))
            self.feed(session, VOICE*41)
            self.assertEqual(session.total_bytes, SECOND*44)
            self.assertFalse(session.finishing)
            session.finish()
        finally:
            engine.release.set()
            session.finish()
            await asyncio.wait_for(task, 3)
        self.assertTrue(engine.calls[-1] == (VOICE*44,"primary"))
        self.assertTrue(any(e["type"]=="session.progress" for e in self.events))

    async def test_ten_minute_limit_is_preserving_and_memory_is_bounded(self):
        session = self.session()
        for index in range(601):
            session.feed(VOICE)
            if index % 20 == 0:
                await asyncio.sleep(0)
        self.assertTrue(session.limit_reached)
        self.assertEqual(session.total_bytes, SECOND*600)
        self.assertLessEqual(session.retained_bytes, 2*session.config.max_audio_bytes)
        session.cancel()
        await self.run_session(session)
        self.assertEqual(session.retained_bytes, 0)
        self.assertEqual(self.events[-1]["type"], "session.cancelled")


if __name__ == "__main__":
    unittest.main()
