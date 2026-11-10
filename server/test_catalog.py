import json
from pathlib import Path
import tempfile
import threading
import unittest
from local_dictation.catalog import Catalog
from local_dictation.config import RuntimeConfig
from local_dictation.downloads import parse_repository, inspect_repository, download_snapshot

class CatalogTests(unittest.TestCase):
    def test_public_repository_parser(self):
        self.assertEqual(parse_repository("https://huggingface.co/openai/whisper-tiny"), ("openai/whisper-tiny","main"))
        self.assertEqual(parse_repository("openai/whisper-tiny"), ("openai/whisper-tiny","main"))
        self.assertEqual(parse_repository("https://huggingface.co/openai/whisper-tiny/tree/abc"), ("openai/whisper-tiny","abc"))
        for invalid in ("https://evil.example/user/model","../bad","x/y/../../z","file://c:/models"):
            with self.assertRaises(ValueError): parse_repository(invalid)

    def metadata(self, remote_code=False):
        def get(url):
            if "/api/models/" in url:
                return {"sha":"a"*40,"siblings":[{"rfilename":"config.json","size":100},
                    {"rfilename":"preprocessor_config.json","size":100},
                    {"rfilename":"model.safetensors","size":200},
                    {"rfilename":"generation_config.json","size":100},
                    {"rfilename":"tokenizer_config.json","size":100},
                    {"rfilename":"vocab.json","size":100},{"rfilename":"merges.txt","size":100}]}
            if url.endswith("/generation_config.json"):
                return {"is_multilingual":True,"lang_to_id":{"<|sk|>":1,"<|en|>":2}}
            if url.endswith("/tokenizer_config.json"):
                return {"tokenizer_class":"WhisperTokenizer"}
            if url.endswith("/config.json"):
                return {"model_type":"whisper","architectures":["WhisperForConditionalGeneration"],
                        **({"auto_map":{"AutoModel":"bad.code"}} if remote_code else {})}
            return {"sampling_rate":16000,"feature_extractor_type":"WhisperFeatureExtractor"}
        return get

    def test_original_whisper_only_and_pinned_size(self):
        spec=inspect_repository("openai/whisper-tiny",self.metadata())
        self.assertEqual(spec["revision"],"a"*40)
        self.assertEqual(spec["download_bytes"],800)
        with self.assertRaises(ValueError):
            inspect_repository("openai/whisper-tiny",self.metadata(True))

    def test_cancelled_download_does_not_publish_or_damage_existing(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            existing=root/"installed"
            existing.mkdir()
            (existing/"important").write_text("keep")
            cancelled=threading.Event()
            cancelled.set()
            spec=inspect_repository("openai/whisper-tiny",self.metadata())
            with self.assertRaises(InterruptedError):
                download_snapshot(spec,root/"stage",cancelled,lambda *args:None)
            self.assertEqual((existing/"important").read_text(),"keep")
            self.assertFalse((root/"stage"/"model.safetensors").exists())

    def test_registry_persists_dynamic_models_without_claiming_quality(self):
        with tempfile.TemporaryDirectory() as d:
            c=Catalog(RuntimeConfig(Path(d),Path(d)/"data"))
            folder=Path(d)/"new"
            from test_model_files import make_model
            make_model(folder)
            c.add({"id":"test/whisper","revision":"a"*40,"path":str(folder),"name":"Test",
                   "languages":["sk","en","auto"]})
            c2=Catalog(c.config)
            m=c2.require("test/whisper")
            self.assertFalse(m["quality_verified"])
            self.assertFalse(m["runtime_tested"])
            self.assertFalse(m["live_ready"])
            c2.mark_tested("test/whisper")
            self.assertTrue(c2.require("test/whisper")["runtime_tested"])
