"""Run a real offline Slovak ASR comparison across a five-minute WAV boundary."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import unicodedata
import wave
import zlib
from difflib import SequenceMatcher

NOTE = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(NOTE / "mac-bridge"), str(NOTE.parents[1] / "server")]
from recording_parts import PART_BYTES, SegmentedTranscription, pack_wav

REFERENCE = "Na stanici čaká červený vlak. Na námestí stojí modrý bicykel."


def words(text):
    return re.findall(r"[^\W_]+", unicodedata.normalize("NFC", text).casefold())


def compare(text, reference=REFERENCE):
    expected, actual = words(reference), words(text)
    differences = [{"edit": op, "expected": expected[i:j], "heard": actual[k:l]}
                   for op, i, j, k, l in SequenceMatcher(None, expected, actual, autojunk=False).get_opcodes()
                   if op != "equal"]
    return {"pass": not differences, "text": text, "differences": differences}


def fixture(speech, case):
    import numpy as np
    start = PART_BYTES // 2 - len(speech) * 7 // 10
    end = start + len(speech)
    data = np.zeros(end + 16000, dtype=np.float32)
    if case != "noise-only":
        data[start:end] = speech
    if case != "clean":
        if case == "quiet-start":
            data[start:start + 4800] *= 10 ** (-18 / 20)
        if case == "quiet-end":
            data[start + int(4.35 * 16000):end] *= 10 ** (-18 / 20)
        rng = np.random.default_rng(314159)
        data += rng.normal(0, 32768 * 10 ** (-60 / 20), size=data.size).astype(np.float32)
    return np.clip(np.rint(data), -32768, 32767).astype("<i2").tobytes()


def main():
    import numpy as np
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("spoken", type=Path)
    parser.add_argument("output", type=Path, help="fresh result directory")
    parser.add_argument("--model-root", type=Path, required=True)
    args = parser.parse_args()
    reference_hash = hashlib.sha256(args.spoken.read_bytes()).hexdigest()
    if reference_hash != 'a92eba4d29f431529be9c0408469013f1536d3379c528e6180a5b9108a1aeb21':
        raise ValueError('Supply the verified synthetic Slovak reference. The attenuation windows are calibrated to it.')
    with wave.open(str(args.spoken)) as f:
        if (f.getframerate(), f.getnchannels(), f.getsampwidth()) != (16000, 1, 2):
            raise ValueError("Expected the two-sentence 16 kHz PCM16 mono reference.")
        speech = np.frombuffer(f.readframes(f.getnframes()), dtype="<i2")
    if not 5 * 16000 < len(speech) < 7 * 16000:
        raise ValueError("Reference duration is outside the prepared voice windows.")
    args.output.mkdir()
    os.environ.update(LOCAL_DICTATION_MODEL_ROOT=str(args.model_root), HF_HUB_OFFLINE="1",
                      TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1",
                      HF_HOME=str(args.output / "offline-cache"))
    from model_engine import WhisperEngine, FAST_MODEL_ID
    model = WhisperEngine(threads=3)
    results = {}
    for case in ("clean", "noisy", "quiet-start", "quiet-end", "noise-only"):
        pcm = fixture(speech, case)
        with tempfile.TemporaryDirectory(dir=args.output) as tmp:
            bridge = SegmentedTranscription(Path(tmp))
            def recognize(audio):
                return model.transcribe(audio, "", FAST_MODEL_ID, "sk")
            for i, part in enumerate((pcm[:PART_BYTES], pcm[PART_BYTES:])):
                receipt = bridge.receive("stage3-synthetic", 1, i, 2, zlib.crc32(part), pack_wav(part), recognize)
        results[case] = dict(compare(receipt["text"], "" if case == "noise-only" else REFERENCE),
                             frames=len(pcm) // 2, crc32=f"{zlib.crc32(pcm):08x}", source_sha256=reference_hash)
        (args.output / (case + ".json")).write_text(json.dumps(results[case], ensure_ascii=False, indent=2), encoding="utf8")
        print(case, json.dumps(results[case], ensure_ascii=True), flush=True)
    return 0 if all(row["pass"] for row in results.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
