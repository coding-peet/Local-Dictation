"""Original Turbo runtime routing tests; synthetic objects, no GPU or downloads."""
from types import SimpleNamespace
from pathlib import Path
import tempfile
from unittest.mock import patch
import unittest

import model_engine
from local_dictation import catalog
from local_dictation.config import RuntimeConfig

TURBO='openai/whisper-large-v3-turbo'


class TurboRuntimeTests(unittest.TestCase):
    def test_pinned_new_profile_does_not_invent_runtime_or_quality_evidence(self):
        new_profile={'id':TURBO,'name':'New Turbo','folder':'synthetic-turbo',
            'revision':'synthetic-pin','languages':['sk','en','auto'],
            'live_supported':True,'live_ready':False,'prevalidated':False}
        models=[m for m in catalog.BUILTINS if m['id']!=TURBO]+[new_profile]
        def installed(self,entry):
            entry.update(installed=True,installed_revision=entry['revision'])
        with tempfile.TemporaryDirectory(prefix='local-dictation-turbo-catalog-') as directory:
            config=RuntimeConfig(Path(directory)/'models',Path(directory)/'data')
            with patch.object(catalog,'BUILTINS',models),patch.object(catalog.Catalog,'check',installed):
                registry=catalog.Catalog(config)
        self.assertFalse(registry.entries[TURBO]['runtime_tested'])
        self.assertFalse(registry.entries[TURBO]['quality_verified'])
        self.assertTrue(registry.entries[models[0]['id']]['runtime_tested'])

    def test_turbo_activation_keeps_its_small_original_encoder_on_cuda(self):
        offloads=[]
        model=SimpleNamespace(
            parameters=lambda:[SimpleNamespace(dtype='fp16',device=SimpleNamespace(type='cuda'))],
            generation_config=SimpleNamespace(eos_token_id=[50257]),
            device=SimpleNamespace(type='cuda'),
            model=SimpleNamespace(encoder=SimpleNamespace(layers=object())))
        model.eval=lambda:model
        processor=object()
        transformers=SimpleNamespace(
            AutoModelForSpeechSeq2Seq=SimpleNamespace(from_pretrained=lambda *a,**k:model),
            AutoProcessor=SimpleNamespace(from_pretrained=lambda *a,**k:processor))
        accelerate=SimpleNamespace(cpu_offload=lambda *a,**k:offloads.append((a,k)))
        engine=object.__new__(model_engine.WhisperEngine)
        engine.model=None;engine.processor=None;engine.loaded_model_id=None
        engine.torch=SimpleNamespace(float16='fp16',device=lambda value:value,
            cuda=SimpleNamespace(set_per_process_memory_fraction=lambda value:None,
                                 synchronize=lambda:None))
        with patch.dict('sys.modules',{'transformers':transformers,'accelerate':accelerate}), \
             patch.dict(model_engine.MODELS,{TURBO:'synthetic-original-turbo'}):
            engine._load_model(TURBO)
        self.assertEqual(offloads,[], 'Turbo must not inherit Large-v3 encoder CPU offload')
        self.assertIs(engine.model,model)
        self.assertIs(engine.processor,processor)
        self.assertEqual(engine.loaded_model_id,TURBO)

    def test_turbo_retains_native_longform_without_large_beam_search(self):
        options=model_engine.generation_options(TURBO,'en')
        self.assertEqual(options['language'],'en')
        self.assertEqual(options['task'],'transcribe')
        self.assertEqual(options['num_beams'],1,'Turbo must use its measured greedy path')
        self.assertTrue(options['return_timestamps'])
        self.assertFalse(options['condition_on_prev_tokens'])
        self.assertNotIn('logprob_threshold',options)
        self.assertNotIn('no_speech_threshold',options)

    def test_turbo_is_not_limited_to_the_slovak_finetune_language(self):
        with patch.dict(model_engine.MODELS,{TURBO:'synthetic-original-turbo'}), \
             patch.dict(model_engine.MODEL_LANGUAGES,{TURBO:['sk','en','auto']}):
            self.assertEqual(model_engine.resolve_request(TURBO,'en'),(TURBO,'en'))
            self.assertEqual(model_engine.resolve_request(TURBO,'auto'),(TURBO,None))


if __name__=='__main__':
    unittest.main()
