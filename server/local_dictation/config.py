"""Explicit external paths; no generated runtime state in the source tree."""
from dataclasses import dataclass
import os
from pathlib import Path

@dataclass(frozen=True)
class RuntimeConfig:
    model_root: Path
    data_root: Path
    max_seconds: int = 600
    max_frame_bytes: int = 65536
    sample_rate: int = 16000

    def __post_init__(self):
        if not 1 <= self.max_seconds <= 3600:
            raise ValueError("Recording limit must be between 1 and 3600 seconds.")
        if self.sample_rate != 16000 or self.max_frame_bytes < 320:
            raise ValueError("Expected 16 kHz PCM and a valid frame limit.")

    @property
    def max_audio_bytes(self):
        return self.max_seconds * self.sample_rate * 2

    @classmethod
    def from_env(cls):
        return cls(Path(os.environ.get("LOCAL_DICTATION_MODEL_ROOT", r"C:\AI\PalaNote-ASR\models")),
                   Path(os.environ.get("LOCAL_DICTATION_DATA_ROOT", r"C:\AI\LocalDictation\server-data")))
