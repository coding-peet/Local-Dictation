"""Bounded audio sessions with a single admitted GPU owner."""
import asyncio
from contextlib import asynccontextmanager
import threading
import time
import uuid
from .comparison import compare, comparison_identity
from datetime import datetime, timezone
from .workers import run_worker

class BusyError(RuntimeError):pass

class Admission:
    def __init__(self):
        self.lock=asyncio.Lock()
        self.kind,self.cancelled=None,None
        self.unavailable=False
    async def acquire(self,kind,cancelled):
        if self.lock.locked():
            if kind=="primary" and self.kind=="verification":
                self.cancelled.set()
                try:await asyncio.wait_for(self.lock.acquire(),timeout=20)
                except TimeoutError:raise BusyError("Verification is stopping. Try again shortly.") from None
            else:raise BusyError("A transcription is already running.")
        else:await self.lock.acquire()
        self.kind,self.cancelled=kind,cancelled
    def release(self):
        self.kind,self.cancelled=None,None
        self.lock.release()
    @asynccontextmanager
    async def own(self,kind,cancelled):
        await self.acquire(kind,cancelled)
        try:yield
        finally:self.release()

class AudioSession:
    def __init__(self,config,engine,options,send,admission=None):
        self.config,self.engine,self.options,self.send=config,engine,dict(options),send
        self.admission=admission
        self.id,self.revision=str(uuid.uuid4()),0
        self.cancelled=threading.Event()
        self.changed=asyncio.Event()
        self.emit_lock=asyncio.Lock()
        self.finishing,self.verifying=False,False
        self.total_bytes=0
        self.buffer,self.full_audio=bytearray(),bytearray()
        self.live=options.get("live",True)
        # Final primary refinement needs every accepted sample, even without Verify.
        self.retain_full=True
        self.last_voice,self.last_decode=0,0
        self.committed,self.previous,self.draft="","",""
        self.decoded_until=-1
        self.last_snapshot=None
        self.needs_cleanup=False
        self.cleanup_failed=False
        self.limit_reached=False
        self.limit_announced=False
        self.max_buffer_bytes=config.max_audio_bytes
        self.inference_started=None
        self.buffer_start=0
        self.next_preview_bytes=3*32000
        self.last_primary_span=None
        self.last_primary_profile=None
        self.last_primary_text=""

    @property
    def retained_bytes(self):return len(self.buffer)+len(self.full_audio)

    def feed(self,frame):
        if self.finishing or self.cancelled.is_set():return False
        if not frame or len(frame)%2 or len(frame)>self.config.max_frame_bytes:
            raise ValueError("Expected an aligned PCM16 frame of at most 64 KiB.")
        available=self.config.max_audio_bytes-self.total_bytes
        accepted=min(len(frame),max(0,available))
        frame=frame[:accepted]
        if frame:
            self.total_bytes+=accepted
            if self.live:self.buffer.extend(frame)
            if self.retain_full:self.full_audio.extend(frame)
            # Use the same 20 ms gate as live spans. A brief voiced frame must
            # not disappear merely because a larger capture packet is quiet.
            first=((self.total_bytes-accepted)//640)*640
            for start in range(first,self.total_bytes,640):
                end=min(start+640,self.total_bytes)
                if self.has_voice(memoryview(self.full_audio)[start:end]):
                    self.last_voice=end
        if self.total_bytes>=self.config.max_audio_bytes:
            self.limit_reached=True
            self.finish()
        # A partially accepted final frame is also a preserving stop.
        if accepted==available:
            self.limit_reached=True
            self.finish()
        self.changed.set()
        return not self.finishing

    def finish(self):
        self.finishing=True;self.changed.set()
    def cancel(self):
        self.cancelled.set();self.changed.set()

    async def emit(self,kind,**fields):
        async with self.emit_lock:
            self.revision+=1
            await self.send({"type":kind,"session_id":self.id,"revision":self.revision,**fields})

    def snapshot_text(self,draft):
        return self.committed+(" " if self.committed and draft else "")+draft

    async def decode(self,pcm,model):
        self.needs_cleanup=True
        self.inference_started=time.monotonic()
        try:
            return await run_worker(self.engine.transcribe_pcm,pcm,model,self.options.get("language","sk"),
                self.cancelled,cancelled=self.cancelled,timeout=self.config.inference_timeout_seconds)
        finally:self.inference_started=None

    async def cleanup(self):
        if not self.needs_cleanup:return
        self.needs_cleanup=False
        try:await run_worker(self.engine.unload,cancelled=self.cancelled)
        except asyncio.CancelledError:raise
        except Exception:
            self.cleanup_failed=True
            if self.admission:self.admission.unavailable=True
            raise RuntimeError("Model cleanup failed; restart the candidate API.") from None

    async def progress(self):
        while True:
            await asyncio.sleep(self.config.progress_interval_seconds)
            await self.emit("session.progress",phase="verifying" if self.verifying else "finishing" if self.finishing else "listening",
                audio_received_seconds=self.total_bytes/32000,
                decoding_seconds=0 if self.inference_started is None else time.monotonic()-self.inference_started)

    async def announce_limit(self):
        if self.limit_reached and not self.limit_announced:
            self.limit_announced=True
            await self.emit("recording.limit_reached",message="Recording stopped at its RAM/audio limit. Accepted audio is being transcribed.")

    def cut(self):
        if len(self.buffer)<24*32000:return len(self.buffer)
        # Only compare complete centered 100 ms windows. A truncated window
        # at the current capture edge otherwise wins merely by having fewer
        # samples, and a live session cannot inspect future microphone audio.
        return min(range(12*32000,min(24*32000,len(self.buffer)-1600+1),640),
            key=lambda p:sum(v*v for v in memoryview(self.buffer)[p-1600:p+1600].cast("h")))

    @staticmethod
    def has_voice(pcm):
        values=memoryview(pcm).cast("h")
        for start in range(0,len(values),320):
            window=values[start:start+320]
            if sum(v*v for v in window)>len(window)*(0.008*32768)**2:return True
        return False

    async def refine_primary(self):
        # A matching whole-recording preview already used this model/language
        # snapshot. Reuse it; otherwise skip the isolated tail and decode once.
        if not self.full_audio or not self.last_voice:
            self.committed=self.draft=""
            return
        span=(0,self.total_bytes)
        profile=(self.options["model"],self.options.get("language","sk"),self.options.get("primary_revision"))
        if self.last_primary_span==span and self.last_primary_profile==profile:
            text=self.last_primary_text
        else:
            text=(await self.decode(bytes(self.full_audio),self.options["model"]))["text"].strip()
        if not self.cancelled.is_set():
            if not text and self.snapshot_text(self.draft).strip():
                raise RuntimeError("Final refinement returned no text. Your received text is preserved.")
            self.committed=text
            self.draft=self.previous=""
            self.decoded_until=self.total_bytes

    async def run(self):
        heartbeat=None
        try:
            await self.emit("session.started",max_seconds=self.config.max_seconds,sample_rate=16000,model=self.options["model"],
                model_revision=self.options.get("primary_revision"),verification_model=self.options.get("verification_model"),
                verification_revision=self.options.get("verification_revision"),session_started_utc=datetime.now(timezone.utc).isoformat())
            heartbeat=asyncio.create_task(self.progress())
            await self.announce_limit()
            if self.live:await self.run_live()
            else:
                while not self.finishing and not self.cancelled.is_set():
                    self.changed.clear()
                    await self.changed.wait()
                await self.announce_limit()
                if not self.cancelled.is_set() and self.full_audio and self.last_voice:
                    self.committed=(await self.decode(bytes(self.full_audio),self.options["model"]))["text"].strip()
            if self.live and not self.cancelled.is_set():await self.refine_primary()
            if self.cancelled.is_set():
                await self.cleanup()
                await self.emit("session.cancelled",text=self.snapshot_text(self.draft));return
            primary=self.snapshot_text(self.draft)
            await self.emit("transcript.final",text=primary,audio_received_seconds=self.total_bytes/32000,
                refined=bool(self.live and self.last_voice))
            await self.cleanup()
            verifier=self.options.get("verification_model")
            if verifier and self.full_audio and self.last_voice:
                self.verifying=True
                if self.admission:self.admission.kind="verification"
                await self.emit("verification.started",model=verifier)
                try:
                    result=await self.decode(bytes(self.full_audio),verifier)
                    if self.cancelled.is_set():await self.emit("verification.cancelled",primary=primary)
                    else:await self.emit("verification.completed",primary=primary,verification=result["text"],model=verifier,
                        pieces=await asyncio.to_thread(compare,primary,result["text"]),
                        primary_revision=self.options.get("primary_revision"),verification_revision=self.options.get("verification_revision"),
                        comparison_id=comparison_identity(self.options["model"],self.options.get("primary_revision"),verifier,
                            self.options.get("verification_revision"),primary,result["text"]))
                except Exception:
                    await self.emit("verification.cancelled" if self.cancelled.is_set() else "verification.failed",
                        primary=primary,message="Verification did not finish. Your dictation is preserved.")
                finally:await self.cleanup()
            await self.emit("session.completed",text=primary)
        except asyncio.CancelledError:
            self.cancel();raise
        except Exception:
            if not self.cancelled.is_set() or self.cleanup_failed:raise
            await self.cleanup()
            await self.emit("session.cancelled",text=self.snapshot_text(self.draft))
        finally:
            self.buffer.clear();self.full_audio.clear()
            try:
                if self.needs_cleanup:await self.cleanup()
            finally:
                if heartbeat:
                    heartbeat.cancel()
                    await asyncio.gather(heartbeat,return_exceptions=True)

    async def run_live(self):
        while not self.cancelled.is_set():
            self.changed.clear()
            await self.announce_limit()
            # Finish is a whole-recording primary pass. Do not spend another
            # inference on a tail that would immediately be replaced by it.
            if self.finishing:break
            size=len(self.buffer)
            if self.decoded_until==self.total_bytes and not self.finishing:
                await self.changed.wait();continue
            if not size:
                if self.finishing:break
                await self.changed.wait();continue
            should_cut=size>=24*32000
            if not should_cut and size<self.next_preview_bytes:
                await self.changed.wait()
                continue
            end=self.cut()
            # All-quiet spans are retained for final refinement but never sent
            # to the recognizer as isolated live fragments.
            voiced=self.has_voice(memoryview(self.buffer)[:end])
            if not voiced:
                self.buffer_start+=end
                del self.buffer[:end]
                self.next_preview_bytes=3*32000
                continue
            start=self.buffer_start
            profile=(self.options["model"],self.options.get("language","sk"),self.options.get("primary_revision"))
            result=await self.decode(bytes(self.buffer[:end]),self.options["model"])
            self.last_decode=time.monotonic()
            if self.cancelled.is_set():break
            text=result["text"].strip()
            if not text and self.snapshot_text(self.draft).strip():
                raise RuntimeError("The model returned an empty live replacement. Your received text is preserved.")
            self.decoded_until=start+end
            self.last_primary_span=(start,start+end)
            self.last_primary_profile=profile
            self.last_primary_text=text
            if should_cut:
                self.committed=self.snapshot_text(text)
                self.draft=self.previous=""
                del self.buffer[:end]
                self.buffer_start+=end
                self.next_preview_bytes=3*32000
            else:
                self.draft,self.previous=text,text
                self.next_preview_bytes=(end//(3*32000)+1)*3*32000
            snapshot=(self.committed,self.draft)
            if snapshot!=self.last_snapshot:
                await self.emit("transcript.updated",text=self.snapshot_text(self.draft),
                    committed_text=self.committed,draft_text=self.draft,
                    audio_received_seconds=self.total_bytes/32000,decoded_until_seconds=self.decoded_until/32000,
                    pending_audio_seconds=max(0,(self.total_bytes-self.decoded_until)/32000),
                    inference_seconds=result.get("inference_seconds"),load_seconds=result.get("load_seconds"))
                self.last_snapshot=snapshot
            if self.finishing and not self.buffer:break
