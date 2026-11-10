import json
from pathlib import Path
import sys
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import Mock,patch
from starlette.testclient import TestClient
from local_dictation.catalog import Catalog,BUILTINS
from local_dictation.config import RuntimeConfig
from local_dictation.imports import Imports
from test_model_files import make_model

class CatalogInstallationTests(unittest.TestCase):
    def setup(self,folder):
        config=RuntimeConfig(Path(folder)/"originals",Path(folder)/"runtime"/"server-data")
        return config,Catalog(config)

    def spec(self,config,model=None):
        model=model or BUILTINS[0]
        folder=config.data_root.parent/"models"/"installed"
        return {**model,"path":str(folder),"files":[],"download_bytes":1}

    def fill(self,spec,folder=None):
        folder=make_model(folder or spec["path"])
        (folder/"local-manifest.json").write_text(json.dumps({"repo":spec["id"],"revision":spec["revision"]}))
        return folder

    def test_missing_builtin_installs_and_restores_without_false_test_status(self):
        with tempfile.TemporaryDirectory() as d:
            config,c=self.setup(d);spec=self.spec(config);self.fill(spec)
            c.install(spec)
            restored=Catalog(config)
            item=restored.require(spec["id"])
            self.assertEqual(Path(item["path"]).resolve(),Path(spec["path"]).resolve())
            self.assertTrue(item["installed"]);self.assertFalse(item["runtime_tested"])
            self.assertFalse(item["quality_verified"])
            self.assertTrue(item["live_supported"])
            restored.mark_tested(spec["id"])
            self.assertTrue(Catalog(config).require(spec["id"])["runtime_tested"])

    def test_healthy_model_and_different_revision_are_preserved(self):
        with tempfile.TemporaryDirectory() as d:
            config,c=self.setup(d);spec=self.spec(config)
            with self.assertRaises(ValueError):c.install({**spec,"revision":"f"*40})
            self.fill(spec);c.install(spec)
            with self.assertRaises(ValueError):c.install(spec)

    def test_inspection_pins_known_catalog_revision(self):
        with tempfile.TemporaryDirectory() as d:
            config,c=self.setup(d)
            self.assertEqual(c.inspection_target(BUILTINS[0]["id"]),"https://huggingface.co/"+BUILTINS[0]["id"]+"/tree/"+BUILTINS[0]["revision"])
            with self.assertRaises(ValueError):
                c.inspection_target("https://huggingface.co/"+BUILTINS[0]["id"]+"/tree/wrong")

    def test_same_revision_repair_preserves_previous_directory_on_registry_failure(self):
        with tempfile.TemporaryDirectory() as d:
            config,c=self.setup(d);manager=Imports(config,c)
            import hashlib
            model=BUILTINS[0]
            final=config.data_root.parent/"models"/(hashlib.sha256(model["id"].encode()).hexdigest()[:16]+"-"+model["revision"])
            spec={**self.spec(config),"path":str(final)}
            self.fill(spec);c.install(spec)
            (final/"model.safetensors").unlink()
            (final/"keep.marker").write_text("previous")
            job={"id":"repair-stage","cancel":threading.Event(),"state":"queued"}
            def download(spec,stage,*args):self.fill(spec,stage)
            with patch("local_dictation.imports.download_snapshot",side_effect=download),patch.object(c,"save",side_effect=OSError("full")):
                manager.run(job,spec)
            self.assertEqual(job["state"],"failed")
            self.assertEqual((final/"keep.marker").read_text(),"previous")
            self.assertFalse((final/"model.safetensors").exists())

    def test_same_revision_repair_succeeds_without_touching_original_root(self):
        with tempfile.TemporaryDirectory() as d:
            config,c=self.setup(d);manager=Imports(config,c);spec=self.spec(config)
            original=config.model_root/"keep"
            original.parent.mkdir(parents=True);original.write_text("untouched")
            job={"id":"install-stage","cancel":threading.Event(),"state":"queued"}
            with patch("local_dictation.imports.download_snapshot",side_effect=lambda s,p,*args:self.fill(s,p)):
                manager.run(job,spec)
            self.assertEqual(job["state"],"completed")
            entry=c.require(spec["id"]);(Path(entry["path"])/"model.safetensors").unlink()
            job={"id":"repair-stage","cancel":threading.Event(),"state":"queued"}
            with patch("local_dictation.imports.download_snapshot",side_effect=lambda s,p,*args:self.fill(s,p)):
                manager.run(job,spec)
            self.assertEqual(job["state"],"completed")
            self.assertTrue(c.require(spec["id"])["installed"])
            self.assertEqual(original.read_text(),"untouched")

    def test_runtime_can_prepare_without_default_weights(self):
        import model_engine
        engine=model_engine.WhisperEngine.__new__(model_engine.WhisperEngine)
        engine.runtime_ready=False
        engine.torch=SimpleNamespace(cuda=Mock())
        engine._prime_cuda=Mock();engine.unload=Mock();engine._prepare_fast_cpu_cache=Mock()
        transformers=SimpleNamespace(**{name:object for name in ("AutoModelForSpeechSeq2Seq","AutoProcessor","WhisperForConditionalGeneration","WhisperProcessor")})
        with patch.dict(sys.modules,{"transformers":transformers,"av":SimpleNamespace()}):
            engine.prepare_runtime(cache_fast=False)
        self.assertTrue(engine.runtime_ready);engine._prepare_fast_cpu_cache.assert_not_called()
