import asyncio,threading,unittest
from pathlib import Path
from local_dictation.engine_adapter import EngineAdapter
from local_dictation.sessions import AudioSession
from local_dictation.config import RuntimeConfig
from benchmark_tools import score_terms,no_speech_metrics

class DictionaryMetricTests(unittest.TestCase):
    def test_terms_use_boundaries_and_occurrences_including_absent_terms(self):
        result=score_terms('Nexus Nexus ICC-1C','Nexus xNexus ICC-1C Clockify',['Nexus','ICC-1C','Clockify'])
        self.assertEqual(result['Nexus'],{'reference':2,'hypothesis':1,'missing':1,'extra':0})
        self.assertEqual(result['ICC-1C']['missing'],0);self.assertEqual(result['Clockify']['extra'],1)
    def test_empty_reference_never_hides_hallucinated_output(self):
        self.assertEqual(no_speech_metrics('',60),{'nonempty':False,'words':0,'words_per_minute':0.0})
        self.assertEqual(no_speech_metrics('thanks everyone',30)['words_per_minute'],4)
        self.assertTrue(no_speech_metrics('...',10)['nonempty'])
    def test_pcm_adapter_forwards_exact_prompt(self):
        class Raw:
            def transcribe(self,data,prompt,model,language,**options):self.prompt=prompt;return {'text':'result'}
        raw=Raw();adapter=EngineAdapter(raw)
        adapter.transcribe_pcm(b'\0\x10'*16000,'model','sk',threading.Event(),None,'Nexus, STM32')
        self.assertEqual(raw.prompt,'Nexus, STM32')

class SessionHintTests(unittest.IsolatedAsyncioTestCase):
    async def test_primary_hint_reaches_decoder_and_changes_exact_reuse_profile(self):
        class Engine:
            def transcribe_pcm(self,pcm,model,language,cancelled,preferred=None,prompt=''):
                self.prompt=prompt;return {'text':'result','language':'sk'}
        async def send(event):pass
        engine=Engine();session=AudioSession(RuntimeConfig(Path('.'),Path('.')),engine,{'model':'m','language':'sk','prompt':'Nexus'},send)
        await session.decode(b'\0\x10'*16000,'m');self.assertEqual(engine.prompt,'Nexus')
        profile=session.primary_profile();session.options['prompt']='Other';self.assertNotEqual(profile,session.primary_profile())
