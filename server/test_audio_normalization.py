import io,threading,tempfile,unittest,wave
from pathlib import Path
from unittest.mock import patch
import numpy as np
from starlette.testclient import TestClient
from local_dictation.application import create_app
from local_dictation.config import RuntimeConfig
from test_sessions import FakeEngine
from local_dictation.audio_normalization import normalize_audio

def wav(rate=16000,channels=1,seconds=1):
    data=io.BytesIO()
    with wave.open(data,"wb") as w:
        w.setnchannels(channels);w.setsampwidth(2);w.setframerate(rate)
        w.writeframes((b"\x00\x10"*channels)*int(rate*seconds))
    return data.getvalue()

class NormalizationTests(unittest.TestCase):
    def test_original_pcm_samples_are_unchanged(self):
        original=wav();result=normalize_audio(original,threading.Event())
        with wave.open(io.BytesIO(original),'rb') as a,wave.open(io.BytesIO(result),'rb') as b:
            self.assertEqual(b.getparams(),a.getparams());self.assertEqual(b.readframes(b.getnframes()),a.readframes(a.getnframes()))
    def test_stereo_different_rate_converts_without_device(self):
        result=normalize_audio(wav(48000,2),threading.Event())
        with wave.open(io.BytesIO(result),'rb') as w:self.assertEqual((w.getnchannels(),w.getsampwidth(),w.getframerate(),w.getnframes()),(1,2,16000,16000))
    def test_limits_reject_instead_of_truncating(self):
        with self.assertRaisesRegex(ValueError,'limit'):normalize_audio(wav(seconds=2),threading.Event(),max_seconds=1)
        with self.assertRaises(ValueError):normalize_audio(b'bad file',threading.Event())
        with self.assertRaises(ValueError):normalize_audio(wav(seconds=0),threading.Event())
    def test_cancelled_before_decode(self):
        cancel=threading.Event();cancel.set()
        with self.assertRaises(InterruptedError):normalize_audio(wav(),cancel)
    def test_all_advertised_compressed_codecs_decode(self):
        import av
        for container,codec in [('mp3','libmp3lame'),('flac','flac'),('mp4','aac'),('ogg','vorbis'),('ogg','libopus')]:
            with self.subTest(container=container,codec=codec):
                encoded=io.BytesIO()
                with av.open(encoded,mode='w',format=container) as c:
                    stream=c.add_stream(codec,rate=48000,options={'strict':'experimental'} if codec=='vorbis' else {})
                    frame=av.AudioFrame.from_ndarray(np.zeros((2,48000),dtype=np.float32),format='fltp',layout='stereo');frame.sample_rate=48000
                    for packet in stream.encode(frame):c.mux(packet)
                    for packet in stream.encode(None):c.mux(packet)
                result=normalize_audio(encoded.getvalue(),threading.Event())
                with wave.open(io.BytesIO(result),'rb') as w:
                    self.assertEqual((w.getnchannels(),w.getframerate(),w.getsampwidth()),(1,16000,2));self.assertGreater(w.getnframes(),15000)
    def test_route_is_native_loopback_only_and_does_not_run_model_or_write_files(self):
        with tempfile.TemporaryDirectory() as d:
            config=RuntimeConfig(Path(d),Path(d)/'data');engine=FakeEngine()
            app=create_app(config,engine)
            with TestClient(app,base_url='http://127.0.0.1') as c:
                result=c.post('/v1/audio/normalizations',files={'file':('local.wav',wav(),'audio/wav')})
                self.assertEqual(result.status_code,200,result.text if result.status_code!=200 else '')
                self.assertEqual(result.headers['content-type'],'audio/wav')
                self.assertFalse(c.get('/health').json()['busy']);self.assertFalse(config.data_root.exists())
                self.assertEqual(c.post('/v1/audio/normalizations',headers={'Origin':'http://example.com'},files={'file':('local.wav',wav())}).status_code,403)
                app.app.state.admission.unavailable=True
                self.assertEqual(c.post('/v1/audio/normalizations',files={'file':('local.wav',wav())}).status_code,409)
