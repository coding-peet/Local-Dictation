"""Durable five-minute WAV intake shared by USB, BLE and paired Wi-Fi."""
import contextlib
import hashlib
import io
import os
from pathlib import Path
import sqlite3
import struct
import tempfile
import wave
import zlib

PART_BYTES = 300 * 16000 * 2
MAX_PARTS = 448

def recognition_window(pcm, final):
    # Keep the end of an unfinished word for the next inference. Choose the
    # quietest 20 ms in the last two seconds; every sample goes to ASR once.
    if final:
        return pcm, b""
    start = len(pcm) - 2 * 32000
    step = 640

    def energy(offset):
        return sum(sample * sample for (sample,) in struct.iter_unpack("<h", pcm[offset:offset + step]))

    cut = min(range(start, len(pcm) - step + 1, step), key=energy) + step // 2
    return pcm[:cut], pcm[cut:]

def speech_input(pcm):
    """Skip prolonged digital zeros at the edges, keeping 250 ms around speech."""
    leading = (len(pcm) - len(pcm.lstrip(b"\0"))) & ~1
    if leading == len(pcm):
        return b""
    trailing = (len(pcm) - len(pcm.rstrip(b"\0"))) & ~1
    start = leading - 8000 if leading >= 320000 else 0
    end = len(pcm) - trailing + 8000 if trailing >= 320000 else len(pcm)
    return pcm[start:end]


def pack_wav(pcm):
    data = io.BytesIO()
    with wave.open(data, "wb") as output:
        output.setparams((1, 2, 16000, 0, "NONE", ""))
        output.writeframes(pcm)
    return data.getvalue()


def recording_key(device, note, index, count, crc):
    if not isinstance(device, str) or not device or type(note) is not int or not 0 < note <= 2_147_483_647:
        raise ValueError("Invalid paired recording identity.")
    if type(index) is not int or type(count) is not int or not 0 <= index < count <= MAX_PARTS:
        raise ValueError("Invalid recording part position.")
    if type(crc) is not int or not 0 <= crc <= 0xffffffff:
        raise ValueError("Invalid PCM checksum.")
    return device.casefold(), note


class SegmentedTranscription:
    def __init__(self, state):
        self.state = Path(state)
        self.database = self.state / "recording-parts.sqlite"

    def receive(self, device, note, index, count, crc, audio, recognize):
        key = recording_key(device, note, index, count, crc)
        with wave.open(io.BytesIO(audio), "rb") as wav:
            if (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) != (1, 2, 16000):
                raise ValueError("Invalid segment format.")
            pcm = wav.readframes(wav.getnframes())
            if not pcm or wav.getnframes() * 2 != len(pcm) or len(audio) != 44 + len(pcm):
                raise ValueError("Incomplete WAV segment.")

        final = index == count - 1
        if len(pcm) > PART_BYTES or (not final and len(pcm) != PART_BYTES) or zlib.crc32(pcm) != crc:
            raise ValueError("WAV segment length or checksum mismatch.")
        digest = hashlib.sha256(pcm).hexdigest()

        with contextlib.closing(sqlite3.connect(self.database, timeout=300)) as db, db:
            db.execute("BEGIN IMMEDIATE")
            db.execute("""CREATE TABLE IF NOT EXISTS parts (
                device TEXT, note INTEGER, idx INTEGER, total INTEGER, digest TEXT,
                crc INTEGER, tail BLOB, text TEXT, PRIMARY KEY(device, note, idx))""")
            existing = db.execute(
                "SELECT total,digest,text FROM parts WHERE device=? AND note=? AND idx=?", (*key, index)
            ).fetchone()
            if existing:
                if existing[:2] != (count, digest):
                    raise ValueError("Recording segment changed.")
                return self._receipt(db, key, index, count, crc, existing[2])

            tail = b""
            if index:
                previous = db.execute(
                    "SELECT total,tail FROM parts WHERE device=? AND note=? AND idx=?", (*key, index - 1)
                ).fetchone()
                if not previous or previous[0] != count:
                    raise ValueError("Previous recording segment missing.")
                tail = previous[1]

            window, tail = recognition_window(tail + pcm, final)
            window = speech_input(window)
            result = recognize(pack_wav(window)) if window else {"text": ""}
            if not isinstance(result, dict) or not isinstance(result.get("text"), str) or "\x00" in result["text"]:
                raise ValueError("Invalid segment recognition.")
            piece = result["text"].strip()
            db.execute("INSERT INTO parts VALUES(?,?,?,?,?,?,?,?)", (*key, index, count, digest, crc, tail, piece))
            if index:
                # Once the successor is durable, replay needs only the previous receipt.
                db.execute("UPDATE parts SET tail=X'' WHERE device=? AND note=? AND idx=?", (*key, index - 1))
            return self._receipt(db, key, index, count, crc, piece)

    def cached(self, device, note, index, count, crc):
        key = recording_key(device, note, index, count, crc)
        if not self.database.exists():
            return None
        with contextlib.closing(sqlite3.connect(self.database, timeout=300)) as db:
            if not db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='parts'").fetchone():
                return None
            row = db.execute(
                "SELECT total,crc,text FROM parts WHERE device=? AND note=? AND idx=?", (*key, index)
            ).fetchone()
            if not row:
                return None
            if row[:2] != (count, crc):
                raise ValueError("Stored recording part identity changed.")
            return self._receipt(db, key, index, count, crc, row[2])

    def _receipt(self, db, key, index, count, crc, piece):
        receipt = {"part": index, "count": count, "crc": crc, "piece": piece}
        if index + 1 < count:
            return receipt
        rows = db.execute("SELECT text FROM parts WHERE device=? AND note=? ORDER BY idx", key).fetchall()
        if len(rows) != count:
            raise ValueError("Incomplete recording.")
        text = " ".join(line for (line,) in rows if line)
        if len(text) > 7600 or len(text.encode("utf-8")) > 30000:
            device, note = key
            directory = self.state / "recording-transcripts"
            directory.mkdir(exist_ok=True)
            prefix = hashlib.sha256(device.encode()).hexdigest()[:16]
            name = f"{prefix}-{note}.txt"
            with tempfile.NamedTemporaryFile(dir=directory, delete=False) as tmp:
                temporary = Path(tmp.name)
            try:
                with temporary.open("wb") as output:
                    output.write(text.encode("utf-8"))
                    output.flush()
                    os.fsync(output.fileno())
                os.replace(temporary, directory / name)
            finally:
                temporary.unlink(missing_ok=True)
            text = text[:1400].rstrip() + (
                f"\n\n[Full transcript: Web Companion, recording #{note}. Bridge: recording-transcripts/{name}]"
            )
        receipt["text"] = text
        return receipt
