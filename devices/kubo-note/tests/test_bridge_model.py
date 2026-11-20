
import sys
from pathlib import Path
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"mac-bridge"))
from bridge import local_dictation_model

class PalaBridgeModelTests(unittest.TestCase):
    def test_preserves_old_profile_override_before_an_explicit_choice(self):
        self.assertEqual(local_dictation_model({"transcription_model": "legacy/sk"},
                         {"PrimaryModel": "default/v3"}), "legacy/sk")

    def test_inherited_model_follows_the_latest_primary(self):
        profile = {"transcription_model": "legacy/sk"}
        for model in ("default/v3", "default/turbo"):
            self.assertEqual(local_dictation_model(profile,
                             {"PrimaryModel": model, "PalaBridgeModel": ""}), model)

    def test_separate_model_takes_priority(self):
        self.assertEqual(local_dictation_model({}, {"PrimaryModel": "default/v3",
                         "PalaBridgeModel": "bridge/override"}), "bridge/override")
