"""Language policy and wire regressions; synthetic audio/engines, no model loads."""
import asyncio
from contextlib import nullcontext
import json
from pathlib import Path
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import numpy as np
from starlette.testclient import TestClient
import model_engine
from local_dictation.application import create_app
from local_dictation.catalog import Catalog, BUILTINS
from local_dictation.config import RuntimeConfig
from local_dictation.engine_adapter import EngineAdapter
from local_dictation.installation import languages_from_metadata
from local_dictation.sessions import AudioSession
from test_model_files import make_model

TURBO = "openai/whisper-large-v3-turbo"
KINit = "kinit/whisper-large-v3-turbo-sk"
TOKENS = {"<|sk|>": 1, "<|en|>": 2, "<|es|>": 3, "<|de|>": 4,
          "<|ru|>": 5, "<|cs|>": 6, "<|sr|>": 7}
VOICE = b"\0\x10" * 16000


class MetadataTests(unittest.TestCase):
    def test_malformed_language_mapping_is_an_invalid_installation(self):
        for mapping in ([], None, "invalid"):
            with self.subTest(mapping=mapping), self.assertRaises(ValueError):
                languages_from_metadata({}, {"lang_to_id": mapping})

    def test_import_exposes_all_real_language_tokens(self):
        self.assertEqual(languages_from_metadata({}, {"lang_to_id": TOKENS}),
                         ["sk", "en", "es", "de", "ru", "cs", "sr", "auto"])

    def test_installed_catalog_refreshes_old_language_subset_but_keeps_kinit_slovak(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for entry in BUILTINS:
                folder = make_model(root / entry["folder"], entry["id"], entry["revision"])
                (folder / "generation_config.json").write_text(json.dumps(
                    {"is_multilingual": True, "lang_to_id": TOKENS}))
            registry = Catalog(RuntimeConfig(root, root / "data"))
            self.assertIn("es", registry.require(TURBO)["languages"])
            self.assertIn("de", registry.require("openai/whisper-large-v3")["languages"])
            self.assertEqual(registry.require(KINit)["languages"], ["sk"])

    def test_spanish_can_be_forced_on_turbo_and_never_on_kinit(self):
        self.assertEqual(model_engine.resolve_request(TURBO, "es"), (TURBO, "es"))
        with self.assertRaises(ValueError):
            model_engine.resolve_request(KINit, "es")

    def test_registry_reload_reuses_validated_languages_for_managed_builtin_same_path(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            # A supported deployment can use managed storage as its model root.
            # The initial builtin and restored managed entry then share a path.
            config = RuntimeConfig(root / "models", root / "server-data")
            builtin = next(entry for entry in BUILTINS if entry["id"] == TURBO)
            folder = make_model(config.model_root / builtin["folder"], TURBO, builtin["revision"])
            (folder / "generation_config.json").write_text(json.dumps(
                {"is_multilingual": True, "lang_to_id": TOKENS}))
            config.data_root.mkdir()
            (config.data_root / "models.json").write_text(json.dumps([
                {**builtin, "path": str(folder), "managed": True, "runtime_tested": True,
                 "quality_verified": False, "installed_revision": builtin["revision"]}]))
            registry = Catalog(config)
            for item in (registry.require(TURBO), next(model for model in registry.models() if model["id"] == TURBO)):
                self.assertEqual(item["languages"], ["sk", "en", "es", "de", "ru", "cs", "sr", "auto"])
                self.assertEqual(item["installed_revision"], builtin["revision"])
                self.assertTrue(item["runtime_tested"])


class DetectorTests(unittest.TestCase):
    def engine(self):
        class Inputs(dict):
            input_features = object()
            def to(self, **kwargs): return self
        state = SimpleNamespace(detection_configs=[], generation_configs=[])
        config = SimpleNamespace(language="ru", forced_decoder_ids=[[1, 5]], lang_to_id=dict(TOKENS))
        def detect_language(**kwargs):
            current = kwargs["generation_config"]
            state.detection_configs.append(current)
            # Synthetic acoustic scores prefer RU globally, SK over EN.
            scores = {1: 6, 2: 5, 3: 3, 4: 2, 5: 100, 6: 4, 7: 1}
            selected = max(current.lang_to_id.values(), key=lambda value: scores[value])
            return [SimpleNamespace(item=lambda: selected)]
        def generate(**kwargs):
            state.generation_configs.append(kwargs["generation_config"])
            return [1]
        engine = object.__new__(model_engine.WhisperEngine)
        engine.model = SimpleNamespace(generation_config=config, detect_language=detect_language, generate=generate)
        engine.processor = SimpleNamespace(__call__=None)
        class Processor:
            def __call__(self, *args, **kwargs): return Inputs()
            def batch_decode(self, *args, **kwargs): return ["sample words"]
        engine.processor = Processor()
        engine.torch = SimpleNamespace(float16="fp16", inference_mode=nullcontext,
            manual_seed=lambda value: None, cuda=SimpleNamespace(synchronize=lambda: None))
        engine._load_model = lambda model: 0
        return engine, state, config

    def test_auto_allowlist_is_request_local_and_generation_keeps_full_token_mapping(self):
        engine, state, original = self.engine()
        with patch.object(model_engine, "read_audio", return_value=np.zeros(16000)), \
             patch.dict(model_engine.MODEL_LANGUAGES, {TURBO: ["sk", "en", "es", "ru", "auto"]}):
            result = engine.transcribe(b"audio", "", TURBO, "auto", preferred_languages=["sk", "en"])
            unrestricted = engine.transcribe(b"audio", "", TURBO, "auto")
        self.assertEqual((result["language"], result["language_mode"]), ("sk", "detected"))
        self.assertEqual(unrestricted["language"], "ru")
        self.assertEqual(state.detection_configs[0].lang_to_id, {"<|sk|>": 1, "<|en|>": 2})
        self.assertEqual(state.generation_configs[0].lang_to_id, TOKENS)
        self.assertEqual(original.lang_to_id, TOKENS)
        self.assertEqual(original.language, "ru")
        self.assertEqual(original.forced_decoder_ids, [[1, 5]])

    def test_explicit_language_is_validated_against_real_tokens_and_does_not_detect(self):
        engine, state, _ = self.engine()
        with patch.object(model_engine, "read_audio", return_value=np.zeros(16000)):
            result = engine.transcribe(b"audio", "", TURBO, "es")
            with self.assertRaises(ValueError):
                engine.transcribe(b"audio", "", TURBO, "zz")
        self.assertEqual((result["language"], result["language_mode"]), ("es", "forced"))
        self.assertEqual(state.detection_configs, [])


class Models:
    def require(self, model):
        languages = {"multi": ["sk", "en", "es", "de", "auto"], "sk": ["sk"], "en": ["en"]}[model]
        return {"id": model, "languages": languages, "installed": True,
                "live_supported": True, "runtime_tested": True, "revision": "pin"}
    def models(self): return [self.require(model) for model in ("multi", "sk", "en")]


class PolicyApiTests(unittest.TestCase):
    def app(self, engine=None):
        return create_app(RuntimeConfig(Path("."), Path(".")), engine or SimpleNamespace(), Models())

    def validate(self, **options):
        return self.app().app.state.validate({"type": "start", "protocol_version": 1,
            "model": "multi", "language": "auto", **options})

    def test_spanish_session_and_supported_preference_intersection(self):
        self.assertEqual(self.validate(language="es")["language"], "es")
        options = self.validate(preferred_languages=["sk", "en", "ru", "sk"])
        self.assertEqual(options["preferred_languages"], ["sk", "en"])

    def test_empty_malformed_or_disjoint_auto_preferences_are_rejected(self):
        for invalid in ([], "sk", ["auto"], [None], ["ru"]):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                self.validate(preferred_languages=invalid)
        with self.assertRaises(ValueError):
            self.validate(model="sk", preferred_languages=["en", "es"])

    def test_auto_verifier_must_support_every_primary_candidate(self):
        with self.assertRaises(ValueError):
            self.validate(verification_model="en", preferred_languages=["sk", "en"])
        options = self.validate(verification_model="en", preferred_languages=["en"])
        self.assertEqual(options["preferred_languages"], ["en"])

    def test_batch_accepts_json_preferences_and_returns_language_metadata(self):
        class Engine:
            def __init__(self): self.calls = []
            def transcribe(self, data, prompt, model, language, cancelled, preferred_languages=None):
                self.calls.append((model, language, preferred_languages))
                return {"text": "sample", "language": "es", "language_mode": "detected"}
            def unload(self): pass
        engine = Engine()
        with TestClient(self.app(engine), base_url="http://127.0.0.1") as client:
            response = client.post("/v1/audio/transcriptions", data={"model": "multi", "language": "auto",
                "preferred_languages": '["es","de"]', "response_format": "verbose_json"},
                files={"file": ("sample.wav", b"synthetic")})
            self.assertEqual(response.status_code, 200)
            self.assertEqual(response.json()["language"], "es")
            self.assertEqual(engine.calls, [("multi", "auto", ["es", "de"])])
            for invalid in ("not-json", "[]", '["ru"]'):
                response = client.post("/v1/audio/transcriptions", data={"model": "multi", "language": "auto",
                    "preferred_languages": invalid}, files={"file": ("sample.wav", b"synthetic")})
                self.assertEqual(response.status_code, 400)

    def test_adapter_forwards_preferences_without_changing_legacy_call_shape(self):
        calls = []
        class Engine:
            def transcribe(self, *args, **kwargs):
                calls.append(kwargs)
                return {"text": "sample", "language": "en", "language_mode": "detected"}
        adapter = EngineAdapter(Engine())
        cancel = threading.Event()
        adapter.transcribe_pcm(VOICE, "multi", "auto", cancel, ["en", "es"])
        self.assertEqual(calls[0]["preferred_languages"], ["en", "es"])


class LanguageSessionTests(unittest.IsolatedAsyncioTestCase):
    async def test_final_preview_reuse_preserves_detected_language_and_verifier_uses_it(self):
        class Engine:
            def __init__(self): self.calls = []
            def transcribe_pcm(self, pcm, model, language, cancelled, preferred_languages=None):
                self.calls.append((model, language, preferred_languages))
                return {"text": "same words", "language": "es", "language_mode": "detected" if language == "auto" else "forced"}
            def unload(self): pass
        engine, events = Engine(), []
        async def send(event):
            events.append(event)
            if event["type"] == "transcript.updated": session.finish()
        session = AudioSession(RuntimeConfig(Path("."), Path(".")), engine,
            {"model": "multi", "language": "auto", "preferred_languages": ["en", "es"],
             "verification_model": "verify"}, send)
        for _ in range(3): session.feed(VOICE)
        await session.run()
        self.assertEqual(engine.calls, [("multi", "auto", ["en", "es"]), ("verify", "es", None)])
        for kind in ("transcript.updated", "transcript.final"):
            event = next(event for event in events if event["type"] == kind)
            self.assertEqual((event["language"], event["language_mode"]), ("es", "detected"))

    async def test_language_only_change_still_emits_update_and_silence_does_not_guess(self):
        class Engine:
            def __init__(self): self.calls = 0
            def transcribe_pcm(self, *args):
                self.calls += 1
                return {"text": "same words", "language": "sk" if self.calls == 1 else "en", "language_mode": "detected"}
            def unload(self): pass
        events, engine = [], Engine()
        async def send(event):
            events.append(event)
            if event["type"] == "transcript.updated":
                if engine.calls == 1:
                    for _ in range(3): session.feed(VOICE)
                else: session.finish()
        session = AudioSession(RuntimeConfig(Path("."), Path(".")), engine,
            {"model": "multi", "language": "auto"}, send)
        for _ in range(3): session.feed(VOICE)
        await asyncio.wait_for(session.run(), 2)
        updates = [event for event in events if event["type"] == "transcript.updated"]
        self.assertEqual([event["language"] for event in updates], ["sk", "en"])
        silent = []
        async def collect(event): silent.append(event)
        session = AudioSession(RuntimeConfig(Path("."), Path(".")), engine,
            {"model": "multi", "language": "auto"}, collect)
        session.feed(b"\0\0" * 16000); session.finish()
        await session.run()
        final = next(event for event in silent if event["type"] == "transcript.final")
        self.assertIsNone(final["language"])
        self.assertIsNone(final["language_mode"])


if __name__ == "__main__": unittest.main()
