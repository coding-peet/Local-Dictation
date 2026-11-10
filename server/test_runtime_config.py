import os
import unittest
from pathlib import Path
from unittest.mock import patch
from local_dictation.config import RuntimeConfig

class RuntimeConfigTests(unittest.TestCase):
    def test_external_roots_and_limits(self):
        with patch.dict(os.environ, {"LOCAL_DICTATION_MODEL_ROOT": "C:/models",
                                     "LOCAL_DICTATION_DATA_ROOT": "C:/state"}):
            config = RuntimeConfig.from_env()
        self.assertEqual(config.model_root, Path("C:/models"))
        self.assertEqual(config.data_root, Path("C:/state"))
        self.assertEqual(config.max_audio_bytes, 600 * 16000 * 2)

    def test_bounds_are_validated(self):
        with self.assertRaises(ValueError):
            RuntimeConfig(Path("."), Path("."), max_seconds=0)
