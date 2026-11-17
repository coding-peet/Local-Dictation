"""Bounded, cancellable in-memory decoding; no model, disk or audio devices."""
import asyncio
from io import BytesIO
import threading
import wave
from starlette.responses import Response
from .uploads import read_multipart
from .workers import run_worker,join
from .sessions import BusyError

MAX_UPLOAD=26*1024*1024

def normalize_audio(data,cancelled,max_seconds=600):
    import av
    import numpy as np
    if cancelled.is_set():raise InterruptedError('Audio import cancelled.')
    if not data or len(data)>MAX_UPLOAD:raise ValueError('Audio file exceeds the upload limit or is empty.')
    limit=min(600,max_seconds)*16000*2
    pcm=bytearray()
    resampler=av.AudioResampler(format='s16',layout='mono',rate=16000)
    def append(frame):
        if cancelled.is_set():raise InterruptedError('Audio import cancelled.')
        if len(pcm)+frame.samples*2>limit:raise ValueError('Decoded audio exceeds the ten-minute limit.')
        pcm.extend(frame.to_ndarray().tobytes())
    try:
        # Restrict demuxers and nested protocols: arbitrary playlists/remote resources
        # are not audio uploads. Only the first audio track is imported.
        with av.open(BytesIO(data),options={'format_whitelist':'wav,mp3,flac,mov,ogg','protocol_whitelist':'none'}) as container:
            if not container.streams.audio:raise ValueError('The file contains no audio track.')
            for frame in container.decode(audio=0):
                if cancelled.is_set():raise InterruptedError('Audio import cancelled.')
                if not np.isfinite(frame.to_ndarray()).all():raise ValueError('Audio contains non-finite samples.')
                for converted in resampler.resample(frame):append(converted)
            for converted in resampler.resample(None):append(converted)
        if not pcm:raise ValueError('The file contains no audio samples.')
        output=BytesIO()
        with wave.open(output,'wb') as writer:
            writer.setnchannels(1);writer.setsampwidth(2);writer.setframerate(16000);writer.writeframes(pcm)
        return output.getvalue()
    except (ValueError,InterruptedError):raise
    except Exception:raise ValueError('This audio file could not be decoded. Use a supported WAV, MP3, FLAC, M4A or OGG/Opus file.') from None
    finally:pcm.clear()

def create_normalization_endpoint(config,admission,ready,error):
    async def normalize(request):
        cancelled=threading.Event()
        watcher=work=None
        try:
            if not ready():raise BusyError('The API is preparing or unavailable.')
            async with admission.own('audio_normalization',cancelled):
                _,audio=await read_multipart(request,{})
                async def disconnected():
                    while True:
                        message=await request.receive()
                        if message['type']=='http.disconnect':return
                try:
                    watcher=asyncio.create_task(disconnected())
                    work=asyncio.create_task(run_worker(normalize_audio,audio,cancelled,min(600,config.max_seconds),cancelled=cancelled,timeout=config.inference_timeout_seconds))
                    done,_=await asyncio.wait([work,watcher],return_when=asyncio.FIRST_COMPLETED)
                    if watcher in done:cancelled.set()
                    try:wave_bytes=await join(work)
                    except InterruptedError:return Response(status_code=499)
                    if cancelled.is_set():return Response(status_code=499)
                    return Response(wave_bytes,media_type='audio/wav')
                finally:
                    if work and not work.done():
                        cancelled.set()
                        try:await join(work)
                        except Exception:pass
                    if watcher:
                        watcher.cancel();await asyncio.gather(watcher,return_exceptions=True)
        except BusyError as exc:return error(str(exc),409)
        except ValueError as exc:return error(str(exc))
        except asyncio.CancelledError:
            cancelled.set()
            if work:
                try:await join(work)
                except Exception:pass
            raise
        except Exception:return error('Audio preparation failed. The original file was preserved.',500)
        finally:
            if watcher:
                watcher.cancel();await asyncio.gather(watcher,return_exceptions=True)
    return normalize
