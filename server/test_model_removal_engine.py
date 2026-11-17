import unittest
import weakref
from types import SimpleNamespace
from unittest.mock import Mock, patch
from model_engine import WhisperEngine, FAST_MODEL_ID
from local_dictation.engine_adapter import EngineAdapter
from local_dictation.bootstrap import PreparingEngine


class ModelRemovalEngineTests(unittest.TestCase):
    def test_forgetting_fast_model_drops_idle_cpu_cache_without_loading_weights(self):
        class Resource:pass
        engine=object.__new__(WhisperEngine)
        engine.loaded_model_id=None
        engine._fast_model=Resource()
        engine._fast_processor=Resource()
        engine._fast_cache_on_cpu=True
        engine._fast_cache_weight_mib=123
        model=weakref.ref(engine._fast_model)
        processor=weakref.ref(engine._fast_processor)
        engine.unload=Mock()
        engine.forget_model(FAST_MODEL_ID)
        self.assertIsNone(model())
        self.assertIsNone(processor())
        self.assertFalse(engine._fast_cache_on_cpu)
        self.assertEqual(engine._fast_cache_weight_mib,0)
        engine.unload.assert_not_called()

    def test_forgetting_another_model_does_not_discard_unrelated_cpu_cache(self):
        engine=object.__new__(WhisperEngine)
        engine.loaded_model_id=None
        sentinel=object()
        engine._fast_model=sentinel
        engine.unload=Mock()
        engine.forget_model("test/other")
        self.assertIs(engine._fast_model,sentinel)
        engine.unload.assert_not_called()

    def test_adapter_drops_runtime_registration_after_successful_forget(self):
        import model_engine
        engine=SimpleNamespace(forget_model=Mock())
        adapter=EngineAdapter(engine)
        with patch.dict(model_engine.MODELS,{"test/model":"stale"}),patch.dict(model_engine.MODEL_LANGUAGES,{"test/model":["en"]}):
            adapter.forget_model("test/model")
            engine.forget_model.assert_called_once_with("test/model")
            self.assertNotIn("test/model",model_engine.MODELS)
            self.assertNotIn("test/model",model_engine.MODEL_LANGUAGES)

    def test_preparing_wrapper_forwards_cache_invalidation_to_ready_adapter(self):
        engine=PreparingEngine(lambda:None)
        adapter=SimpleNamespace(forget_model=Mock())
        engine.adapter=adapter
        engine.forget_model("test/model")
        adapter.forget_model.assert_called_once_with("test/model")
