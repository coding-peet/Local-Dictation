"""Synthesize a local Piper voice in the PCM format used by Kubo Note."""
import json
from math import gcd
from pathlib import Path
import sys
import wave

import numpy as np
import onnxruntime
from piper import PiperConfig, PiperVoice, SynthesisConfig
from scipy.signal import resample_poly

NOTE_SAMPLE_RATE = 24000
PEAK_LEVEL = 0.70


def synthesize(model: Path, text: str, output: Path) -> None:
    with model.with_suffix(model.suffix + ".json").open(encoding="utf-8") as settings:
        config = PiperConfig.from_dict(json.load(settings))
    # A small CPU pool avoids contention with the running local ASR service.
    options = onnxruntime.SessionOptions()
    options.intra_op_num_threads = 2
    options.add_session_config_entry("session.intra_op.allow_spinning", "0")
    session = onnxruntime.InferenceSession(str(model), sess_options=options, providers=["CPUExecutionProvider"])
    voice = PiperVoice(session=session, config=config)
    sample_rate = voice.config.sample_rate
    pause = np.zeros(round(sample_rate * 0.15), dtype=np.float32)
    parts = []
    for chunk in voice.synthesize(text, syn_config=SynthesisConfig(normalize_audio=False)):
        if chunk.audio_float_array.size:
            if parts:
                parts.append(pause)
            parts.append(chunk.audio_float_array)
    if not parts:
        raise ValueError("Piper produced no speech audio.")

    samples = np.concatenate(parts)
    if sample_rate != NOTE_SAMPLE_RATE:
        factor = gcd(NOTE_SAMPLE_RATE, sample_rate)
        samples = resample_poly(samples, NOTE_SAMPLE_RATE // factor, sample_rate // factor)
    peak = np.max(np.abs(samples))
    if not np.isfinite(peak) or peak <= 1e-6:
        raise ValueError("Piper produced invalid speech audio.")

    # Use one gain for the answer and leave headroom for the Note speaker.
    pcm = np.rint(samples * (32767 * PEAK_LEVEL / peak)).astype("<i2")
    with wave.open(str(output), "wb") as wav:
        wav.setparams((1, 2, NOTE_SAMPLE_RATE, 0, "NONE", "not compressed"))
        wav.writeframes(pcm.tobytes())


if __name__ == "__main__":
    synthesize(Path(sys.argv[1]), sys.stdin.buffer.read().decode("utf-8").strip(), Path(sys.argv[2]))
