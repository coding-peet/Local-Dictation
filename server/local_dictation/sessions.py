"""Bounded audio sessions with a single admitted GPU owner."""
import asyncio
from contextlib import asynccontextmanager
import math
import threading
import time
import uuid
from .comparison import compare
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
        self.config,self.engine,self.options,self.send=config,engine,options,send
        self.admission=admission
        self.id,self.revision=str(uuid.uuid4()),0
        self.cancelled=threading.Event()
        self.changed=asyncio.Event()
        self.emit_lock=asyncio.Lock()
        self.finishing,self.verifying=False,False
        self.total_bytes=0
        self.buffer,self.full_audio=bytearray(),bytearray()
        self.live=options.get("live",True)
        self.retain_full=bool(options.get("verification_model")) or not self.live
        self.last_voice,self.last_decode=0,0
        self.committed,self.previous,self.draft="","",""
        self.decoded_until=-1
        self.last_snapshot=None
        self.needs_cleanup=False
        self.cleanup_failed=False
        self.limit_reached=False
        self.limit_announced=False
        self.max_buffer_bytes=32*32000
        self.inference_started=None

    @property
    def retained_bytes(self):return len(self.buffer)+len(self.full_audio)

    def feed(self,frame):
        if self.finishing or self.cancelled.is_set():return False
        if not frame or len(frame)%2 or len(frame)>self.config.max_frame_bytes:
            raise ValueError("Expected an aligned PCM16 frame of at most 64 KiB.")
        available=self.config.max_audio_bytes-self.total_bytes
        if self.live:available=min(available,self.max_buffer_bytes-len(self.buffer))
        accepted=min(len(frame),max(0,available))
        frame=frame[:accepted]
        if frame:
            self.total_bytes+=accepted
            if self.live:self.buffer.extend(frame)
            if self.retain_full:self.full_audio.extend(frame)
            values=memoryview(frame).cast("h")
            rms=math.sqrt(sum(v*v for v in values)/len(values))/32768
            if rms>0.008:self.last_voice=self.total_bytes
        if accepted<len(frame) or self.total_bytes>=self.config.max_audio_bytes or (self.live and len(self.buffer)>=self.max_buffer_bytes):
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
        if len(self.buffer)<27*32000:return len(self.buffer)
        return min(range(20*32000,26*32000,640),
            key=lambda p:sum(v*v for v in memoryview(self.buffer)[p-1600:p+1600].cast("h")))

    async def run(self):
        heartbeat=None
        try:
            await self.emit("session.started",max_seconds=self.config.max_seconds,sample_rate=16000,model=self.options["model"])
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
            if self.cancelled.is_set():
                await self.cleanup()
                await self.emit("session.cancelled",text=self.snapshot_text(self.draft));return
            primary=self.snapshot_text(self.draft)
            await self.emit("transcript.final",text=primary)
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
                        pieces=await asyncio.to_thread(compare,primary,result["text"]))
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
            size=len(self.buffer)
            if self.decoded_until==self.total_bytes and not self.finishing:
                await self.changed.wait();continue
            if not size:
                if self.finishing:break
                await self.changed.wait();continue
            silence=(self.total_bytes-self.last_voice)/32000
            should_cut=size>=27*32000 or silence>=0.7
            elapsed=time.monotonic()-self.last_decode
            if not self.finishing and not should_cut and (size<16000 or elapsed<0.75):
                try:await asyncio.wait_for(self.changed.wait(),timeout=max(0.05,0.75-elapsed))
                except TimeoutError:pass
                continue
            if not self.last_voice or (silence>=size/32000 and not self.draft):
                del self.buffer[:size]
                if self.finishing:break
                continue
            end=self.cut()
            final_chunk=self.finishing or should_cut
            self.decoded_until=self.total_bytes-size+end
            result=await self.decode(bytes(self.buffer[:end]),self.options["model"])
            self.last_decode=time.monotonic()
            if self.cancelled.is_set():break
            text=result["text"].strip()
            if final_chunk:
                self.committed=self.snapshot_text(text)
                self.draft=self.previous=""
                del self.buffer[:end]
            else:self.draft,self.previous=text,text
            snapshot=(self.committed,self.draft)
            if snapshot!=self.last_snapshot:
                await self.emit("transcript.updated",text=self.snapshot_text(self.draft),
                    committed_text=self.committed,draft_text=self.draft,
                    audio_received_seconds=self.total_bytes/32000,decoded_until_seconds=self.decoded_until/32000,
                    pending_audio_seconds=max(0,(self.total_bytes-self.decoded_until)/32000),
                    inference_seconds=result.get("inference_seconds"),load_seconds=result.get("load_seconds"))
                self.last_snapshot=snapshot
            if self.finishing and not self.buffer:break
