"""Bounded audio sessions with a single admitted GPU owner."""
import asyncio
from contextlib import asynccontextmanager
import math
import struct
import threading
import time
import uuid
from .comparison import compare

class BusyError(RuntimeError):
    pass

class Admission:
    def __init__(self):
        self.lock = asyncio.Lock()
        self.kind, self.cancelled = None, None

    async def acquire(self, kind, cancelled):
        if self.lock.locked():
            if kind == "primary" and self.kind == "verification":
                self.cancelled.set()
                try:
                    await asyncio.wait_for(self.lock.acquire(), timeout=20)
                except TimeoutError:
                    raise BusyError("Verification is stopping. Try again shortly.") from None
            else:
                raise BusyError("A transcription is already running.")
        else:
            await self.lock.acquire()
        self.kind, self.cancelled = kind, cancelled

    def release(self):
        self.kind, self.cancelled = None, None
        self.lock.release()

    @asynccontextmanager
    async def own(self, kind, cancelled):
        await self.acquire(kind, cancelled)
        try:
            yield
        finally:
            self.release()

class AudioSession:
    def __init__(self, config, engine, options, send, admission=None):
        self.config, self.engine, self.options, self.send = config, engine, options, send
        self.admission = admission
        self.id, self.revision = str(uuid.uuid4()), 0
        self.cancelled = threading.Event()
        self.changed = asyncio.Event()
        self.finishing = False
        self.verifying = False
        self.total_bytes = 0
        self.buffer, self.full_audio = bytearray(), bytearray()
        self.retain_full = bool(options.get("verification_model")) or not options.get("live", True)
        self.live = options.get("live", True)
        self.last_voice = 0
        self.last_decode = 0
        self.committed = ""
        self.previous = ""
        self.draft = ""
        self.needs_cleanup = False
        self.max_buffer_bytes = 32 * 32000

    @property
    def retained_bytes(self):
        return len(self.buffer) + len(self.full_audio)

    def feed(self, frame):
        if self.finishing or self.cancelled.is_set():
            raise ValueError("This session no longer accepts audio.")
        if not frame or len(frame) % 2 or len(frame) > self.config.max_frame_bytes:
            raise ValueError("Expected an aligned PCM16 frame of at most 64 KiB.")
        if self.total_bytes + len(frame) > self.config.max_audio_bytes:
            raise ValueError("Recording limit reached. Stop and start a new dictation.")
        if self.live and len(self.buffer) + len(frame) > self.max_buffer_bytes:
            raise ValueError("The decoder cannot keep up. Audio was preserved up to this limit; use after-recording mode.")
        self.total_bytes += len(frame)
        if self.live:
            self.buffer.extend(frame)
        if self.retain_full:
            self.full_audio.extend(frame)
        values = memoryview(frame).cast("h")
        rms = math.sqrt(sum(v*v for v in values) / len(values)) / 32768
        if rms > 0.008:
            self.last_voice = self.total_bytes
        self.changed.set()

    def finish(self):
        self.finishing = True
        self.changed.set()

    def cancel(self):
        self.cancelled.set()
        self.changed.set()

    async def emit(self, kind, **fields):
        self.revision += 1
        await self.send({"type":kind, "session_id":self.id, "revision":self.revision, **fields})

    def snapshot_text(self, draft):
        return self.committed + (" " if self.committed and draft else "") + draft

    async def decode(self, pcm, model):
        self.needs_cleanup = True
        return await asyncio.to_thread(self.engine.transcribe_pcm, pcm, model,
                                       self.options.get("language", "sk"), self.cancelled)

    async def cleanup(self):
        # Run on the same serialized ownership scope; never free the gate early.
        if not self.needs_cleanup:
            return
        self.needs_cleanup = False
        try:
            await asyncio.to_thread(self.engine.unload)
        except Exception:
            if self.admission:
                self.admission.unavailable = True
            raise RuntimeError("Model cleanup failed; restart the candidate API.") from None

    def cut(self):
        if len(self.buffer) < 27 * 32000:
            return len(self.buffer)
        # Absolute sample cuts; no dropped or overlapping audio at boundaries.
        return min(range(20*32000, 26*32000, 640),
                   key=lambda p: sum(v*v for v in memoryview(self.buffer)[p-1600:p+1600].cast("h")))

    async def run(self):
        try:
            await self.emit("session.started", max_seconds=self.config.max_seconds,
                            sample_rate=16000, model=self.options["model"])
            if self.live:
                await self.run_live()
            else:
                while not self.finishing and not self.cancelled.is_set():
                    self.changed.clear()
                    await self.changed.wait()
                if not self.cancelled.is_set() and self.full_audio:
                    result = await self.decode(bytes(self.full_audio), self.options["model"])
                    self.committed = result["text"].strip()
            if self.cancelled.is_set():
                await self.emit("session.cancelled", text=self.snapshot_text(self.draft))
                return
            primary = self.snapshot_text(self.draft)
            await self.emit("transcript.final", text=primary)
            # Primary is already delivered; verification is a separate optional phase.
            await self.cleanup()
            verifier = self.options.get("verification_model")
            if verifier and self.full_audio:
                self.verifying = True
                if self.admission:
                    self.admission.kind = "verification"
                await self.emit("verification.started", model=verifier)
                try:
                    result = await self.decode(bytes(self.full_audio), verifier)
                    if self.cancelled.is_set():
                        await self.emit("verification.cancelled", primary=primary)
                    else:
                        await self.emit("verification.completed", primary=primary,
                                        verification=result["text"], model=verifier,
                                        pieces=compare(primary, result["text"]))
                except Exception:
                    await self.emit("verification.cancelled" if self.cancelled.is_set() else "verification.failed",
                                    primary=primary, message="Verification did not finish. Your dictation is preserved.")
                finally:
                    await self.cleanup()
            await self.emit("session.completed", text=primary)
        finally:
            self.buffer.clear()
            self.full_audio.clear()
            if self.needs_cleanup:
                await self.cleanup()

    async def run_live(self):
        while not self.cancelled.is_set():
            # Re-check after clear to avoid losing finish/feed signals.
            self.changed.clear()
            size = len(self.buffer)
            if not size:
                if self.finishing: break
                await self.changed.wait()
                continue
            silence = (self.total_bytes-self.last_voice) / 32000
            should_cut = size >= 27*32000 or silence >= 0.7
            elapsed = time.monotonic() - self.last_decode
            if not self.finishing and not should_cut and (size < 16000 or elapsed < 0.75):
                try: await asyncio.wait_for(self.changed.wait(), timeout=max(0.05, 0.75-elapsed))
                except TimeoutError: pass
                continue
            if not self.last_voice or (silence >= size/32000 and not self.draft):
                # Never run Whisper on complete silence.
                del self.buffer[:size]
                if self.finishing: break
                continue
            end = self.cut()
            final_chunk = self.finishing or should_cut
            result = await self.decode(bytes(self.buffer[:end]), self.options["model"])
            self.last_decode = time.monotonic()
            if self.cancelled.is_set(): break
            text = result["text"].strip()
            if final_chunk:
                self.committed = self.snapshot_text(text)
                self.draft = self.previous = ""
                del self.buffer[:end]
            else:
                self.draft, self.previous = text, text
            await self.emit("transcript.updated", text=self.snapshot_text(self.draft),
                            committed_text=self.committed, draft_text=self.draft)
            if self.finishing and not self.buffer: break
