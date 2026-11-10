import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
from local_dictation.catalog import Catalog,BUILTINS
from local_dictation.config import RuntimeConfig
from local_dictation.installation import validate_installation
from local_dictation.downloads import inspect_repository
from local_dictation.imports import Imports

def make_model(folder,repo="test/whisper",revision="a"*40):
    folder=Path(folder);folder.mkdir(parents=True,exist_ok=True)
    values={"config.json":{"model_type":"whisper","architectures":["WhisperForConditionalGeneration"]},
        "generation_config.json":{"is_multilingual":True,"lang_to_id":{"<|sk|>":1,"<|en|>":2}},
        "preprocessor_config.json":{"sampling_rate":16000},
        "tokenizer_config.json":{"tokenizer_class":"WhisperTokenizer"},
        "vocab.json":{"x":0}}
    for name,value in values.items():(folder/name).write_text(json.dumps(value))
    (folder/"merges.txt").write_text("#version: 0.2\na b")
    header=json.dumps({"synthetic":{"dtype":"F32","shape":[1],"data_offsets":[0,4]}}).encode()
    header+=b" "*((-len(header))%8)
    (folder/"model.safetensors").write_bytes(struct.pack("<Q",len(header))+header+b"\0"*4)
    (folder/"local-manifest.json").write_text(json.dumps({"repo":repo,"revision":revision}))
    return folder

class ModelFilesTests(unittest.TestCase):
    def test_deleted_weight_disables_restored_custom_model(self):
        with tempfile.TemporaryDirectory() as d:
            c=Catalog(RuntimeConfig(Path(d),Path(d)/"data"))
            folder=make_model(Path(d)/"model")
            c.add({"id":"test/whisper","revision":"a"*40,"path":str(folder),"name":"Test","languages":["sk","en","auto"]})
            c.mark_tested("test/whisper")
            (folder/"model.safetensors").unlink()
            restored=Catalog(c.config)
            self.assertFalse(next(m for m in restored.models() if m["id"]=="test/whisper")["verification_eligible"])
            with self.assertRaises(ValueError):restored.require("test/whisper")

    def test_truncated_safetensors_is_not_an_installation(self):
        with tempfile.TemporaryDirectory() as d:
            folder=make_model(d)
            target=folder/"model.safetensors"
            target.write_bytes(target.read_bytes()[:-1])
            with self.assertRaises(ValueError):validate_installation(folder)

    def test_failed_registry_write_rolls_back_new_directory_and_memory(self):
        with tempfile.TemporaryDirectory() as d:
            config=RuntimeConfig(Path(d)/"originals",Path(d)/"runtime"/"server-data")
            c=Catalog(config);manager=Imports(config,c)
            job={"id":"safe-stage","cancel":__import__("threading").Event(),"state":"queued"}
            spec={"id":"test/model","revision":"a"*40,"files":[],"languages":["sk","en","auto"],"download_bytes":1}
            def staged(spec,stage,*args):make_model(stage,spec["id"],spec["revision"])
            with patch("local_dictation.imports.download_snapshot",side_effect=staged),patch.object(c,"save",side_effect=OSError("full")):
                manager.run(job,spec)
            self.assertEqual(job["state"],"failed")
            self.assertNotIn("test/model",c.entries)
            root=config.data_root.parent/"models"
            self.assertEqual([p.name for p in root.iterdir() if p.name!=".staging"],[])

    def test_english_only_metadata_and_runtime_options(self):
        from test_catalog import CatalogTests
        base=CatalogTests().metadata()
        def english(url):
            value=base(url)
            if url.endswith("/generation_config.json"):return {"is_multilingual":False}
            if url.endswith("/config.json"):return {**value,"vocab_size":51864}
            return value
        self.assertEqual(inspect_repository("openai/whisper-tiny.en",english)["languages"],["en"])
        import model_engine
        with patch.dict(model_engine.MODEL_LANGUAGES,{"test/english":["en"]}),patch.dict(model_engine.MODELS,{"test/english":"unused"}):
            self.assertEqual(model_engine.resolve_request("test/english",None)[1],"en")
            options=model_engine.generation_options("test/english","en")
            self.assertNotIn("language",options);self.assertNotIn("task",options)
