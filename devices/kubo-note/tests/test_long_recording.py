
import io
import sys
import tempfile
import unittest
import wave
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
try:
    from recording_parts import SegmentedTranscription
except ImportError:
    SegmentedTranscription = None

def wav(pcm):
    out = io.BytesIO()
    with wave.open(out, "wb") as writer:
        writer.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
        writer.writeframes(pcm)
    return out.getvalue()

class LongRecordingTest(unittest.TestCase):
    def test_one_hour_retry_and_second_bridge(self):
        self.assertIsNotNone(SegmentedTranscription, "segmented Bridge receiver is missing")
        import hashlib
        inputs = hashlib.sha256()
        observed = hashlib.sha256()
        calls = []
        def recognize(blob):
            with wave.open(io.BytesIO(blob)) as audio:
                observed.update(audio.readframes(audio.getnframes()))
            calls.append(1)
            return {"text": "slovo" + str(len(calls))}
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            a, b = SegmentedTranscription(Path(first)), SegmentedTranscription(Path(second))
            for i in range(12):
                pcm = (i+1).to_bytes(2,"little") * 4_800_000
                inputs.update(pcm)
                raw = wav(pcm)
                crc = zlib.crc32(pcm)
                if i < 2:
                    a.receive("dev", 12, i, 12, crc, raw, lambda _: {"text":"other"})
                result = b.receive("dev",12,i,12,crc,raw,recognize)
                self.assertEqual(result,b.receive("dev",12,i,12,crc,raw,recognize))
            self.assertEqual(inputs.digest(), observed.digest())
            self.assertEqual(len(calls),12)
            self.assertEqual(result["text"], " ".join("slovo"+str(i) for i in range(1,13)))
    def test_paired_wifi_and_usb_share_receipts_and_one_commit(self):
        from bridge import Bridge
        from test_usb_sync import FakePala, PREFIX
        from usb_sync import sync_one
        parts=[wav(b"\x01\x02"*4_800_000),wav(b"\x03\x04"*16000)]
        hashes=[zlib.crc32(raw[44:]) for raw in parts]
        class Note(FakePala):
            read_chunk=512
            def __init__(self):
                super().__init__()
                self.pieces=[]
                self.downloaded=0
                self.commits=0
            def write(self,raw):
                verb,*args=raw[len(PREFIX):].decode().strip().split()
                if verb=="SELECT":
                    self.reply("PARTS 42 2")
                elif verb=="PART":
                    i=int(args[0])
                    self.audio=parts[i]
                    self.reply(f"PART {i} {len(self.audio)} {hashes[i]}")
                elif verb=="PART-CACHED":
                    self.reply("OK")
                elif verb=="PIECE":
                    self.text.clear()
                    self.expected_size=int(args[0])
                    self.expected_hash=args[1]
                    self.reply("OK")
                elif verb=="PIECE-END":
                    import hashlib
                    assert len(self.text)==self.expected_size and hashlib.sha256(self.text).hexdigest()==self.expected_hash
                    self.pieces.append(bytes(self.text).decode())
                    self.text.clear()
                    self.reply("OK")
                else:
                    if verb=="READ": self.downloaded+=int(args[1])
                    if verb=="COMMIT": self.commits+=1
                    return super().write(raw)
                return len(raw)
        with tempfile.TemporaryDirectory() as folder:
            server=Bridge.__new__(Bridge)
            server.state=Path(folder)
            seen=[]
            def recognize(audio):
                seen.append(len(audio))
                return {"text":"prvá veta" if len(seen)==1 else "druhá veta"}
            server._recognize_upload=recognize
            for i in (0,0):
                prefix=(f'--p\r\nContent-Disposition: form-data; name="pala_part"\r\n\r\n42 {i} 2 {hashes[i]}\r\n'
                        '--p\r\nContent-Disposition: form-data; name="file"; filename="note.wav"\r\n\r\n').encode()
                response=server.dispatch("/pala/recording-part",prefix+parts[i]+b"\r\n--p--\r\n",
                                         "multipart/form-data; boundary=p",device="dev")
                self.assertEqual(response["piece"],"prvá veta")
            note=Note()
            result=sync_one(note,(),recognize,note=42,session_open=True,device="dev",
                            segmented=server.transcribe_segment,cached_segment=server.cached_segment)
            self.assertEqual(result.note,42)
            self.assertEqual(note.commits,1)
            self.assertEqual(note.downloaded,len(parts[1]))
            self.assertEqual(note.pieces,["prvá veta","druhá veta"])
            self.assertEqual(note.text.decode(),"prvá veta druhá veta")
            self.assertEqual(len(seen),2)

    def test_exact_silence_skips_inference_and_preserves_very_quiet_speech(self):
        from recording_parts import speech_input
        quiet_sample = b"\x00\x01"
        self.assertEqual(speech_input(bytes(9_600_000)),b"")
        self.assertEqual(speech_input(bytes(360_000)+quiet_sample+bytes(320_000)),
                         bytes(8000)+quiet_sample+bytes(8000))
        self.assertEqual(speech_input(bytes(320)+quiet_sample),bytes(320)+quiet_sample)

    def test_corruption_gap_and_long_text(self):
        self.assertIsNotNone(SegmentedTranscription)
        with tempfile.TemporaryDirectory() as folder:
            sink = SegmentedTranscription(Path(folder))
            pcm = b"\x01\x01"*4_800_000
            audio = wav(pcm)
            with self.assertRaises(ValueError):
                sink.receive("dev",1,0,2,1,audio,lambda _: {})
            with self.assertRaises(ValueError):
                sink.receive("dev",1,1,2,zlib.crc32(pcm),audio,lambda _: {})
            recognize = lambda _: {"text":"ž"*6000}
            sink.receive("dev",1,0,2,zlib.crc32(pcm),audio,recognize)
            final = sink.receive("dev",1,1,2,zlib.crc32(pcm),audio,recognize)
            self.assertLess(len(final["text"]),8000)
            files = list((Path(folder) / "recording-transcripts").glob("*.txt"))
            self.assertEqual(len(files), 1)
            self.assertEqual(files[0].read_text(encoding="utf-8"), "ž"*6000+" "+"ž"*6000)

if __name__ == "__main__":
    unittest.main()
