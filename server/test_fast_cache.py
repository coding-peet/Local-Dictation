"""CPU cache ownership tests; external tensor placement is represented by a small device double."""
import unittest
from types import SimpleNamespace
from unittest.mock import patch
from model_engine import WhisperEngine, FAST_MODEL_ID, PRECISE_MODEL_ID

class TinyModel:
    def __init__(self):
        self.device = SimpleNamespace(type="cpu")
        self.fail_park = False
        self.generation_config = SimpleNamespace(eos_token_id=[50257])
    def parameters(self):
        return [SimpleNamespace(dtype="float16", device=self.device,
                                numel=lambda:4, element_size=lambda:2)]
    def eval(self):
        return self
    def to(self, device):
        if device=="cpu" and self.fail_park:
            raise RuntimeError("parking failed")
        self.device = SimpleNamespace(type="cuda" if str(device).startswith("cuda") else "cpu")
        return self

def engine_with_loader():
    engine = object.__new__(WhisperEngine)
    engine.model=None
    engine.processor=None
    engine.loaded_model_id=None
    engine.runtime_ready=True
    engine.runtime_warmup_seconds=0
    engine._fast_model=None
    engine._fast_processor=None
    engine._fast_cache_on_cpu=False
    engine._fast_cache_weight_mib=0
    engine.torch=SimpleNamespace(float16="float16",cuda=SimpleNamespace(
        is_initialized=lambda:True,synchronize=lambda:None,empty_cache=lambda:None,
        set_per_process_memory_fraction=lambda fraction:None,
        memory_allocated=lambda:0,memory_reserved=lambda:0))
    model=TinyModel()
    processor=object()
    library=SimpleNamespace(
        AutoModelForSpeechSeq2Seq=SimpleNamespace(from_pretrained=lambda *args,**kw:model),
        AutoProcessor=SimpleNamespace(from_pretrained=lambda *args,**kw:processor))
    return engine,model,processor,library

class FastCpuCacheTests(unittest.TestCase):
    def test_preload_has_cpu_weights_without_an_active_gpu_model(self):
        engine,model,processor,library=engine_with_loader()
        with patch.dict("sys.modules",{"transformers":library}):
            engine._prepare_fast_cpu_cache()
        state=engine.status()
        self.assertFalse(state["model_loaded"])
        self.assertIsNone(state["model"])
        self.assertEqual(state["cpu_cached_models"],[FAST_MODEL_ID])
        self.assertEqual(model.device.type,"cpu")

    def test_repeated_requests_reuse_cpu_cache_and_release_gpu_residency(self):
        engine,model,processor,library=engine_with_loader()
        with patch.dict("sys.modules",{"transformers":library}):
            engine._prepare_fast_cpu_cache()
            engine._load_model(FAST_MODEL_ID)
        self.assertEqual(model.device.type,"cuda")
        self.assertTrue(engine.status()["model_loaded"])
        self.assertEqual(engine.status()["cpu_cached_models"],[])
        engine.unload()
        self.assertEqual(model.device.type,"cpu")
        self.assertFalse(engine.status()["model_loaded"])
        self.assertEqual(engine.status()["cpu_cached_models"],[FAST_MODEL_ID])
        with patch.dict("sys.modules",{"transformers":SimpleNamespace()}):
            engine._load_model(FAST_MODEL_ID)
        self.assertIs(engine.model,model)
        self.assertIs(engine.processor,processor)
        self.assertEqual(model.device.type,"cuda")
        engine.unload()

    def test_precise_cleanup_preserves_the_fast_cpu_cache(self):
        engine,model,processor,library=engine_with_loader()
        with patch.dict("sys.modules",{"transformers":library}):
            engine._prepare_fast_cpu_cache()
        engine.model=object()
        engine.processor=object()
        engine.loaded_model_id=PRECISE_MODEL_ID
        engine.unload()
        self.assertEqual(engine.status()["cpu_cached_models"],[FAST_MODEL_ID])
        with patch.dict("sys.modules",{"transformers":SimpleNamespace()}):
            engine._load_model(FAST_MODEL_ID)
        self.assertIs(engine.model,model)
        engine.unload()

    def test_failed_gpu_parking_discards_cache_and_active_references(self):
        engine,model,processor,library=engine_with_loader()
        with patch.dict("sys.modules",{"transformers":library}):
            engine._prepare_fast_cpu_cache()
            engine._load_model(FAST_MODEL_ID)
        model.fail_park=True
        with self.assertRaisesRegex(RuntimeError,"parking failed"):
            engine.unload()
        self.assertIsNone(engine.model)
        self.assertIsNone(engine.processor)
        self.assertIsNone(engine.loaded_model_id)
        self.assertEqual(engine.status()["cpu_cached_models"],[])

    def test_discarded_model_is_collectible_before_error_allocator_flush(self):
        import weakref
        engine,model,processor,library=engine_with_loader()
        with patch.dict("sys.modules",{"transformers":library}):
            engine._prepare_fast_cpu_cache()
            engine._load_model(FAST_MODEL_ID)
        model.fail_park=True
        reference=weakref.ref(model)
        del model,library
        collected_at_flush=[]
        engine.torch.cuda.empty_cache=lambda:collected_at_flush.append(reference() is None)
        with self.assertRaisesRegex(RuntimeError,"parking failed"):
            engine.unload()
        self.assertTrue(collected_at_flush)
        self.assertTrue(collected_at_flush[-1],"failed-transfer traceback retained the GPU model during allocator flush")

if __name__=="__main__":
    unittest.main()
