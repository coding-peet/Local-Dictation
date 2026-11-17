"""Protocol tests use a lightweight engine; real model inference is tested separately."""
import http.client
import json
import threading
import unittest
from server import LocalServer, MODEL_ID, Handler
from unittest.mock import patch


class Engine:
    def __init__(self):
        self.calls=[]
        self.started=threading.Event()
        self.release=None
        self.loaded_model=None
        self.unload_calls=0
        self.transcribe_error=None
        self.cleanup_error=None
        self.return_value=None
    def transcribe(self, data, prompt, model_id=None, language=None):
        self.calls.append((data,prompt,model_id,language))
        self.loaded_model=model_id
        self.started.set()
        if self.release is not None:
            self.release.wait(5)
        if self.transcribe_error is not None: raise self.transcribe_error
        if self.return_value is not None: return self.return_value
        return {'text':'Skúška prepisu.', 'language':language or 'en', 'duration':1.25}
    def status(self):
        return {'model':self.loaded_model,'model_loaded':self.loaded_model is not None,'device':'cuda:0','dtype':'float16','ready':True}
    def unload(self):
        self.unload_calls+=1
        self.loaded_model=None
        if self.cleanup_error is not None: raise self.cleanup_error


def multipart(model=MODEL_ID, prompt='STM32, mikrofarad', audio=b'encoded-audio', fmt='verbose_json', language='sk'):
    parts=[]
    for key,value in [('model',model),('prompt',prompt),('language',language),('response_format',fmt)]:
        if value is None: continue
        parts.append(f'--asr-test\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode('utf-8'))
    parts.append(b'--asr-test\r\nContent-Disposition: form-data; name="file"; filename="audio.wav"\r\nContent-Type: audio/wav\r\n\r\n'+audio+b'\r\n--asr-test--\r\n')
    return b''.join(parts)


class ProtocolTests(unittest.TestCase):
    def setUp(self):
        self.engine=Engine()
        self.server=LocalServer(('127.0.0.1',0),self.engine)
        self.thread=threading.Thread(target=self.server.serve_forever,daemon=True)
        self.thread.start()
    def tearDown(self):
        if self.engine.release: self.engine.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
    def request(self, method='GET', path='/v1/models', body=None, headers=None):
        conn=http.client.HTTPConnection('127.0.0.1',self.server.server_port,timeout=8)
        try:
            conn.request(method,path,body=body,headers=headers or {})
            r=conn.getresponse()
            return r.status,r.read(),r.getheader('Content-Type')
        finally: conn.close()
    def post(self, body=None, headers=None):
        return self.request('POST','/v1/audio/transcriptions',multipart() if body is None else body,
            {'Content-Type':'multipart/form-data; boundary=asr-test',**(headers or {})})
    def test_gate_is_released_before_response_is_written(self):
        states=[]
        reply=Handler.reply
        def observed(handler,*args,**kwargs):
            states.append(handler.server.gate.locked())
            return reply(handler,*args,**kwargs)
        with patch.object(Handler,'reply',observed):
            status,_,_=self.post()
        self.assertEqual(status,200)
        self.assertEqual(states,[False])
    def test_model_discovery(self):
        status,body,_=self.request()
        self.assertEqual(status,200)
        self.assertEqual(json.loads(body)['data'][0]['id'],MODEL_ID)
    def test_multipart_passes_audio_and_utf8_prompt(self):
        prompt='STM32, napätie, mikrofarad'
        status,body,_=self.post(multipart(prompt=prompt))
        self.assertEqual(status,200)
        self.assertEqual(self.engine.calls,[(b'encoded-audio',prompt,MODEL_ID,'sk')])
        self.assertEqual(json.loads(body)['text'],'Skúška prepisu.')
        self.assertEqual(json.loads(body)['duration'],1.25)
    def test_wrong_model_never_runs_inference(self):
        status,body,_=self.post(multipart(model='not-installed'))
        self.assertEqual(status,400)
        self.assertIn('error',json.loads(body))
        self.assertEqual(self.engine.calls,[])
    def test_malformed_audio_field_rejected(self):
        status,_,_=self.post(b'--asr-test--\r\n')
        self.assertEqual(status,400)
        self.assertEqual(self.engine.calls,[])
    def test_browser_origin_rejected(self):
        status,_,_=self.post(headers={'Origin':'https://unrelated.example'})
        self.assertEqual(status,403)
        self.assertEqual(self.engine.calls,[])
    def test_plain_text_response(self):
        status,body,ctype=self.post(multipart(fmt='text'))
        self.assertEqual(status,200)
        self.assertTrue(ctype.startswith('text/plain'))
        self.assertEqual(body.decode('utf-8'),'Skúška prepisu.')
    def test_busy_request_is_rejected_without_second_inference(self):
        self.engine.release=threading.Event()
        result=[]
        worker=threading.Thread(target=lambda:result.append(self.post()))
        worker.start()
        self.assertTrue(self.engine.started.wait(2))
        try:
            status,_,_=self.post()
            self.assertEqual(status,409)
            self.assertEqual(len(self.engine.calls),1)
        finally:
            self.engine.release.set()
            worker.join(5)
        self.assertEqual(result[0][0],200)


    def test_all_profile_models_are_discoverable(self):
        status,body,_=self.request()
        self.assertEqual(status,200)
        self.assertEqual({m['id'] for m in json.loads(body)['data']},
            {'kinit/whisper-large-v3-turbo-sk','openai/whisper-large-v3','openai/whisper-large-v3-turbo'})

    def test_precise_auto_language_is_not_forced_to_slovak(self):
        status,body,_=self.post(multipart(model='openai/whisper-large-v3',language='auto'))
        self.assertEqual(status,200)
        self.assertEqual(self.engine.calls[0][2:],('openai/whisper-large-v3',None))
        self.assertEqual(json.loads(body)['language'],'en')

    def test_precise_omitted_language_uses_auto_detection(self):
        status,_,_=self.post(multipart(model='openai/whisper-large-v3',language=None))
        self.assertEqual(status,200)
        self.assertEqual(self.engine.calls[0][2:],('openai/whisper-large-v3',None))

    def test_precise_explicit_english_is_forwarded(self):
        status,_,_=self.post(multipart(model='openai/whisper-large-v3',language='en'))
        self.assertEqual(status,200)
        self.assertEqual(self.engine.calls[0][2:],('openai/whisper-large-v3','en'))

    def test_fast_auto_language_is_slovak(self):
        status,_,_=self.post(multipart(model='kinit/whisper-large-v3-turbo-sk',language='auto'))
        self.assertEqual(status,200)
        self.assertEqual(self.engine.calls[0][2:],('kinit/whisper-large-v3-turbo-sk','sk'))

    def test_fast_explicit_english_is_rejected_before_inference(self):
        status,_,_=self.post(multipart(model='kinit/whisper-large-v3-turbo-sk',language='en'))
        self.assertEqual(status,400)
        self.assertEqual(self.engine.calls,[])

    def test_unsupported_format_does_not_start_inference(self):
        status,_,_=self.post(multipart(fmt='srt'))
        self.assertEqual(status,400)
        self.assertEqual(self.engine.calls,[])

    def test_health_reports_engine_device_not_old_cpu_constant(self):
        status,body,_=self.request(path='/health')
        self.assertEqual(status,200)
        self.assertEqual(json.loads(body)['device'],'cuda:0')


    def test_success_releases_model_before_response(self):
        status,body,_=self.post()
        self.assertEqual(status,200)
        self.assertIsNone(self.engine.loaded_model)
        self.assertEqual(self.engine.unload_calls,1)

    def test_inference_error_releases_model_before_response(self):
        self.engine.transcribe_error=RuntimeError('Synthetic inference failure')
        with self.assertLogs('local-asr',level='ERROR'):
            status,_,_=self.post()
        self.assertEqual(status,500)
        self.assertIsNone(self.engine.loaded_model)
        self.assertEqual(self.engine.unload_calls,1)

    def test_invalid_audio_error_also_releases_resources(self):
        self.engine.transcribe_error=ValueError('Cannot decode audio')
        status,_,_=self.post()
        self.assertEqual(status,400)
        self.assertIsNone(self.engine.loaded_model)
        self.assertEqual(self.engine.unload_calls,1)

    def test_cleanup_error_reports_failure_and_releases_gate(self):
        self.engine.cleanup_error=RuntimeError('Synthetic cleanup failure')
        with self.assertLogs('local-asr',level='ERROR'):
            status,_,_=self.post()
        self.assertEqual(status,500)
        self.engine.cleanup_error=None
        status,_,_=self.post()
        self.assertEqual(status,200)
        self.assertFalse(self.server.gate.locked())

    def test_busy_rejection_does_not_unload_active_request(self):
        self.engine.release=threading.Event()
        result=[]
        thread=threading.Thread(target=lambda:result.append(self.post()))
        thread.start()
        self.assertTrue(self.engine.started.wait(2))
        status,_,_=self.post()
        self.assertEqual(status,409)
        self.assertEqual(self.engine.unload_calls,0)
        self.assertIsNotNone(self.engine.loaded_model)
        self.engine.release.set()
        thread.join(5)
        self.assertEqual(result[0][0],200)
        self.assertIsNone(self.engine.loaded_model)

    def test_idle_health_stays_ready_without_model(self):
        status,body,_=self.request(path='/health')
        health=json.loads(body)
        self.assertEqual(status,200)
        self.assertTrue(health['ready'])
        self.assertFalse(health['model_loaded'])
        self.assertIsNone(health['model'])


    def test_malformed_engine_result_is_server_error_and_model_is_released(self):
        self.engine.return_value={'duration':1.25}
        with self.assertLogs('local-asr',level='ERROR'):
            status,body,_=self.post(multipart(fmt='json'))
        self.assertEqual(status,500)
        self.assertIn('error',json.loads(body))
        self.assertIsNone(self.engine.loaded_model)
        self.assertEqual(self.server.completed,0)


    def test_missing_duration_returns_500_without_counting_success(self):
        self.engine.return_value={'text':'ok'}
        try:
            status,body,_=self.post(multipart(fmt='json'))
        except http.client.RemoteDisconnected:
            self.fail('Missing duration closed the connection instead of returning HTTP 500')
        self.assertEqual(status,500)
        self.assertIn('error',json.loads(body))
        self.assertEqual(self.server.completed,0)
        self.assertIsNone(self.engine.loaded_model)
        self.assertFalse(self.server.gate.locked())





class StartupReadinessTests(unittest.TestCase):
    def test_server_prepares_runtime_before_serving_requests(self):
        import server as server_module
        from unittest.mock import patch
        class PreparingEngine:
            runtime_ready = False
            def prepare_runtime(self):
                self.runtime_ready = True
        def check_ready(server):
            self.assertTrue(server.engine.runtime_ready)
            raise KeyboardInterrupt
        with patch('sys.argv', ['server.py', '--port', '0']), patch.object(
                server_module, 'WhisperEngine', PreparingEngine), patch.object(
                server_module.LocalServer, 'serve_forever', check_ready):
            server_module.main()

if __name__=='__main__': unittest.main()
