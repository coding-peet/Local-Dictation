import io
import sys
import unittest
import wave
from pathlib import Path
from types import SimpleNamespace
from threading import Lock
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
import bridge


class NoteSpeechModelTests(unittest.TestCase):
    def test_bridge_uses_note_model_and_returns_complete_audio_in_both_formats(self):
        text = "Slovenská veta. " * 40
        settings = {"PalaSpeechModel": "supertonic-3", "SpeechModel": "piper-lili",
                    "PalaSpeechDevice": "70041dd816a8"}
        for fmt in ("pcm", "wav"):
            with self.subTest(fmt=fmt):
                chunks = []
                def render(connection, headers, options):
                    chunks.append(options)
                    return bytes([len(chunks), 0])
                with patch.object(bridge, "local_dictation_settings", return_value=settings), \
                        patch.object(bridge, "local_dictation_application_key", return_value="fixture"), \
                        patch.object(bridge, "render_note_pcm", side_effect=render), \
                        patch.object(bridge.http.client, "HTTPConnection"):
                    result = bridge.Bridge.dispatch(SimpleNamespace(lock=Lock()), "/v1/audio/speech",
                        bridge.encode({"input": text, "response_format": fmt}), "application/json", device="70:04:1D:D8:16:A8")
                self.assertGreater(len(chunks), 1)
                self.assertEqual(" ".join(c["input"] for c in chunks), text.strip())
                self.assertTrue(all(c["model"] == "supertonic-3" and c["voice"] == "F1"
                                    and len(c["input"]) <= 500 for c in chunks))
                if fmt == "wav":
                    with wave.open(io.BytesIO(result)) as opened:
                        self.assertEqual(opened.getparams()[:3], (1, 2, 24000))
                        result = opened.readframes(opened.getnframes())
                self.assertEqual(result, bytes([1, 0, 2, 0]))

    def test_another_note_uses_the_existing_voice_profile(self):
        settings = {"PalaSpeechModel": "supertonic-3", "PalaSpeechDevice": "70041dd816a8"}
        with patch.object(bridge, "local_dictation_settings", return_value=settings), \
                patch.object(bridge, "synthesize_note_speech") as render, \
                patch.object(bridge.subprocess, "run", side_effect=OSError("legacy renderer")) as legacy:
            for device in ("70:04:1D:D7:B1:28", None):
                with self.assertRaisesRegex(OSError, "legacy renderer"):
                    bridge.Bridge.dispatch(SimpleNamespace(lock=Lock()), "/v1/audio/speech",
                        bridge.encode({"input": "fixture", "response_format": "pcm"}),
                        "application/json", device=device)
            self.assertEqual(legacy.call_count, 2)
            render.assert_not_called()
