import unittest
import weakref
from types import SimpleNamespace
from unittest.mock import patch
from model_engine import WhisperEngine
import numpy as np
from model_engine import resolve_request, generation_options, split_audio

FAST='kinit/whisper-large-v3-turbo-sk'
PRECISE='openai/whisper-large-v3'

class PolicyTests(unittest.TestCase):
    def test_automatic_language_is_per_request(self):
        self.assertEqual(resolve_request(PRECISE,'sk'),(PRECISE,'sk'))
        self.assertEqual(resolve_request(PRECISE,'auto'),(PRECISE,None))
        self.assertEqual(resolve_request(PRECISE,'en'),(PRECISE,'en'))
        self.assertEqual(resolve_request(PRECISE,None),(PRECISE,None))
    def test_fast_auto_forces_slovak_and_rejects_english(self):
        self.assertEqual(resolve_request(FAST,'auto'),(FAST,'sk'))
        with self.assertRaises(ValueError): resolve_request(FAST,'en')
    def test_unknown_model_is_rejected(self):
        with self.assertRaises(ValueError): resolve_request('not-installed','auto')
    def test_precise_uses_memory_fitting_quality_profile(self):
        opts=generation_options(PRECISE,'en')
        self.assertEqual(opts['language'],'en')
        self.assertEqual(opts['task'],'transcribe')
        self.assertEqual(opts['num_beams'],5)
        self.assertTrue(opts['use_cache'])
        self.assertTrue(opts['return_timestamps'])
    def test_fast_preserves_cached_greedy_profile(self):
        opts=generation_options(FAST,'sk')
        self.assertEqual(opts['num_beams'],1)
        self.assertTrue(opts['use_cache'])
        self.assertFalse(opts['return_timestamps'])
        self.assertNotIn('condition_on_prev_tokens',opts)
    def test_precise_requests_repetition_fallback_without_retaining_all_beam_scores(self):
        opts=generation_options(PRECISE,'sk')
        # Transformers compares a token-space compression ratio, unlike the
        # text-space ratio in OpenAI Whisper. Use the model's published policy.
        self.assertEqual(opts.get('compression_ratio_threshold'),1.35)
        # Both score-based thresholds retain the complete vocabulary scores
        # for every beam/token, which exhausts the 4 GiB FP16 runtime.
        self.assertNotIn('logprob_threshold',opts)
        self.assertNotIn('no_speech_threshold',opts)
        self.assertEqual(opts.get('temperature'),(0.0,0.2,0.4,0.6,0.8,1.0))
        self.assertNotIn('no_repeat_ngram_size',opts)
        self.assertNotIn('repetition_penalty',opts)
    def test_short_audio_remains_intact(self):
        audio=np.arange(16000,dtype=np.float32)
        chunks=split_audio(audio)
        self.assertEqual(len(chunks),1)
        np.testing.assert_array_equal(chunks[0],audio)
    def test_long_audio_has_no_gaps_duplicates_or_oversized_chunks(self):
        audio=np.arange(81*16000,dtype=np.float32)
        chunks=split_audio(audio)
        self.assertGreater(len(chunks),1)
        self.assertTrue(all(0<len(x)<=29*16000 for x in chunks))
        np.testing.assert_array_equal(np.concatenate(chunks),audio)


class ResidencyTests(unittest.TestCase):
    def test_constructor_waits_for_runtime_preparation_without_loading_a_model(self):
        operations=[]
        cuda=SimpleNamespace(is_available=lambda:True,is_initialized=lambda:False,
            set_per_process_memory_fraction=lambda fraction:operations.append('cuda-allocation'))
        torch=SimpleNamespace(cuda=cuda,set_num_threads=lambda n:None,manual_seed=lambda n:None,
            backends=SimpleNamespace(cuda=SimpleNamespace(matmul=SimpleNamespace(allow_tf32=True)),cudnn=SimpleNamespace(allow_tf32=True)))
        def fake_load(engine,model_id):
            engine.model=SimpleNamespace()
            engine.loaded_model_id=model_id
        with patch.dict('sys.modules',{'torch':torch}),patch.object(WhisperEngine,'_load_model',fake_load):
            engine=WhisperEngine()
        self.assertIsNone(engine.model)
        self.assertFalse(engine.status()['ready'])
        self.assertFalse(engine.status()['model_loaded'])
        self.assertEqual(operations,[])

    def test_unload_drops_resources_before_releasing_allocator_cache(self):
        class Resource: pass
        engine=object.__new__(WhisperEngine)
        engine.model=Resource();engine.processor=Resource();engine.loaded_model_id=FAST
        model_ref=weakref.ref(engine.model);processor_ref=weakref.ref(engine.processor)
        operations=[]
        def empty_cache():
            self.assertIsNone(model_ref())
            self.assertIsNone(processor_ref())
            operations.append('cache-cleared')
        engine.torch=SimpleNamespace(cuda=SimpleNamespace(is_initialized=lambda:True,
            synchronize=lambda:None,empty_cache=empty_cache))
        engine.unload()
        self.assertIsNone(engine.loaded_model_id)
        self.assertEqual(operations,['cache-cleared'])

    def test_unload_of_uninitialized_cuda_does_not_create_a_context(self):
        engine=object.__new__(WhisperEngine)
        engine.model=None;engine.processor=None;engine.loaded_model_id=None
        def unexpected(): raise AssertionError('Idle cleanup must not initialize CUDA')
        engine.torch=SimpleNamespace(cuda=SimpleNamespace(is_initialized=lambda:False,
            synchronize=unexpected,empty_cache=unexpected))
        engine.unload()
        self.assertIsNone(engine.model)


class RuntimePreparationTests(unittest.TestCase):
    def engine_and_state(self):
        state = dict(initialized=False, cache_cleared=False)
        engine = object.__new__(WhisperEngine)
        engine.model = None
        engine.processor = None
        engine.loaded_model_id = None
        engine.runtime_ready = False
        engine.runtime_warmup_seconds = None
        def initialize():
            state['initialized'] = True
        def clear_cache():
            self.assertIsNone(engine.model)
            self.assertIsNone(engine.processor)
            state['cache_cleared'] = True
        engine.torch = SimpleNamespace(cuda=SimpleNamespace(
            init=initialize, is_initialized=lambda:state['initialized'],
            set_per_process_memory_fraction=lambda fraction:None,
            synchronize=lambda:None, empty_cache=clear_cache,
            memory_allocated=lambda:0, memory_reserved=lambda:0))
        return engine, state

    def runtime_modules(self):
        return {'transformers':SimpleNamespace(AutoModelForSpeechSeq2Seq=object,
            AutoProcessor=object, WhisperForConditionalGeneration=object,
            WhisperProcessor=object), 'av':SimpleNamespace()}

    def test_runtime_preparation_makes_engine_ready_with_no_resident_weights(self):
        engine, state = self.engine_and_state()
        with patch.dict('sys.modules', self.runtime_modules()), patch.object(
                WhisperEngine, '_prime_cuda', lambda self:None, create=True), patch.object(
                WhisperEngine, '_prepare_fast_cpu_cache', lambda self:None, create=True):
            engine.prepare_runtime()
        self.assertTrue(state['initialized'])
        self.assertTrue(state['cache_cleared'])
        self.assertTrue(engine.status()['ready'])
        self.assertTrue(engine.status()['runtime_ready'])
        self.assertTrue(engine.status()['cuda_initialized'])
        self.assertIsNone(engine.status()['model'])
        self.assertFalse(engine.status()['model_loaded'])
        self.assertIsNotNone(engine.status()['runtime_warmup_seconds'])

    def test_failed_gpu_preparation_is_not_ready_and_releases_temporary_memory(self):
        engine, state = self.engine_and_state()
        def fail(self):
            raise RuntimeError('warm-up failed')
        with patch.dict('sys.modules', self.runtime_modules()), patch.object(
                WhisperEngine, '_prime_cuda', fail, create=True):
            with self.assertRaisesRegex(RuntimeError, 'warm-up failed'):
                engine.prepare_runtime()
        self.assertFalse(engine.status()['ready'])
        self.assertFalse(engine.status()['model_loaded'])
        self.assertTrue(state['cache_cleared'])

if __name__=='__main__': unittest.main()
