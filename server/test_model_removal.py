import asyncio
import json
import os
import subprocess
from pathlib import Path
import tempfile
import threading
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from starlette.testclient import TestClient
from local_dictation.application import create_app
from local_dictation.catalog import Catalog, BUILTINS
from local_dictation.config import RuntimeConfig
from test_model_files import make_model
from test_sessions import FakeEngine


class ModelRemovalFixture(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.config = RuntimeConfig(self.root / "originals", self.root / "runtime" / "server-data")
        self.catalog = Catalog(self.config)

    def install(self, model=None, path=None):
        model = model or {"id": "test/whisper", "name": "Synthetic Whisper", "revision": "a" * 40}
        path = path or self.catalog.managed_root / "installed"
        make_model(path, model["id"], model["revision"])
        self.catalog.install({**model, "path": str(path), "files": []})
        return model["id"], path

    def directory_link(self, link, target):
        try:
            link.symlink_to(target,target_is_directory=True)
        except OSError:
            # Windows junctions exercise the same reparse-point guard without symlink privileges.
            if os.name!="nt":self.skipTest("Creating directory links is unavailable.")
            result=subprocess.run([os.environ.get("COMSPEC",r"C:\Windows\System32\cmd.exe"),
                "/c","mklink","/J",str(link),str(target)],capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
            if result.returncode:self.skipTest("Creating directory links or junctions is unavailable.")

    def listing(self, model):
        return next(item for item in self.catalog.models() if item["id"] == model)


class ModelRemovalTests(ModelRemovalFixture):
    def test_shared_original_weights_are_installed_but_never_removable(self):
        model = BUILTINS[0]
        folder = make_model(self.config.model_root / model["folder"], model["id"], model["revision"])
        item = self.listing(model["id"])
        self.assertTrue(item["installed"])
        self.assertFalse(item["can_remove"])
        self.assertIn("shared", item["removal_restriction"].lower())
        with self.assertRaises(ValueError):
            self.catalog.remove(model["id"])
        self.assertTrue((folder / "model.safetensors").is_file())

    def test_owned_custom_installation_is_removed_and_registry_survives_reload(self):
        model, folder = self.install()
        keep = self.config.model_root / "keep.marker"
        keep.parent.mkdir(parents=True)
        keep.write_text("original remains")
        self.assertTrue(self.listing(model)["can_remove"])
        result = self.catalog.remove(model)
        self.assertTrue(result["removed"])
        self.assertFalse(result["cleanup_pending"])
        self.assertFalse(folder.exists())
        self.assertNotIn(model, Catalog(self.config).entries)
        self.assertEqual(keep.read_text(), "original remains")
        with self.assertRaises(ValueError):
            self.catalog.require(model)

    def test_removed_builtin_returns_to_uninstalled_catalog(self):
        model, folder = self.install(BUILTINS[0])
        self.catalog.remove(model)
        item = next(item for item in Catalog(self.config).models() if item["id"] == model)
        self.assertFalse(item["installed"])
        self.assertTrue(item["can_install"])
        self.assertFalse(item["can_remove"])
        self.assertFalse(folder.exists())

    def test_original_builtin_copy_arriving_later_is_preserved_and_remains_installed(self):
        model, folder = self.install(BUILTINS[0])
        original = make_model(self.config.model_root / BUILTINS[0]["folder"], model, BUILTINS[0]["revision"])
        result=self.catalog.remove(model)
        self.assertIn("shared model copy remains",result["message"])
        item = self.listing(model)
        self.assertTrue(item["installed"])
        self.assertFalse(item["can_remove"])
        self.assertTrue((original / "model.safetensors").is_file())
        self.assertFalse(folder.exists())

    def test_registry_failure_restores_directory_registration_and_original_registry_bytes(self):
        model, folder = self.install()
        original = self.catalog.file.read_bytes()
        with patch("local_dictation.catalog.os.replace", wraps=os.replace) as replace:
            # Use the real function captured before patching to avoid recursion.
            actual_replace = replace._mock_wraps
            def replace_checked(source, target):
                if Path(target) == self.catalog.file:
                    raise OSError("synthetic registry write failure")
                return actual_replace(source, target)
            replace.side_effect = replace_checked
            with self.assertRaises(OSError):
                self.catalog.remove(model)
        self.assertTrue((folder / "model.safetensors").is_file())
        self.assertEqual(self.catalog.file.read_bytes(), original)
        self.assertTrue(Catalog(self.config).require(model)["installed"])

    def test_cleanup_failure_reports_remaining_recovery_tree_without_restoring_registration(self):
        model, folder = self.install()
        with patch("local_dictation.catalog.shutil.rmtree", side_effect=OSError("synthetic cleanup failure")):
            result = self.catalog.remove(model)
        self.assertTrue(result["removed"])
        self.assertTrue(result["cleanup_pending"])
        self.assertIn("recovery", result["message"].lower())
        self.assertFalse(folder.exists())
        self.assertNotIn(model, Catalog(self.config).entries)
        preserved = list((self.catalog.managed_root / ".removing").iterdir())
        self.assertEqual(len(preserved), 1)
        self.assertTrue((preserved[0] / "model.safetensors").is_file())

    def test_outside_root_managed_flag_and_root_itself_do_not_grant_deletion(self):
        for target in (self.root / "outside", self.catalog.managed_root):
            with self.subTest(target=target):
                model, folder = self.install(path=target)
                self.assertFalse(self.listing(model)["can_remove"])
                with self.assertRaises(ValueError):
                    self.catalog.remove(model)
                self.assertTrue((folder / "model.safetensors").is_file())
                self.catalog.entries.pop(model)

    def test_shared_registry_entry_or_overlapping_ownership_refuses_removal(self):
        model, folder = self.install()
        self.catalog.entries[model]["managed"] = False
        self.assertFalse(self.listing(model)["can_remove"])
        with self.assertRaises(ValueError):
            self.catalog.remove(model)
        self.catalog.entries[model]["managed"] = True
        self.catalog.entries["another/model"] = {**self.catalog.entries[model], "id": "another/model"}
        with self.assertRaises(ValueError):
            self.catalog.remove(model)
        self.assertTrue((folder / "model.safetensors").is_file())

    def test_linked_child_preserves_external_data_and_refuses_removal(self):
        model, folder = self.install()
        external = self.root / "external"
        external.mkdir()
        marker = external / "keep.marker"
        marker.write_text("keep")
        self.directory_link(folder/"linked",external)
        self.assertFalse(self.listing(model)["can_remove"])
        with self.assertRaises(ValueError):
            self.catalog.remove(model)
        self.assertEqual(marker.read_text(), "keep")

    def test_reparse_attribute_without_symbolic_link_is_rejected(self):
        model, folder = self.install()
        actual_lstat = os.lstat
        def reparse(path, *args, **kwargs):
            value = actual_lstat(path, *args, **kwargs)
            if Path(path) == folder / "model.safetensors":
                return SimpleNamespace(st_mode=value.st_mode, st_file_attributes=0x400)
            return value
        with patch("local_dictation.catalog.os.lstat", side_effect=reparse):
            self.assertFalse(self.listing(model)["can_remove"])
            with self.assertRaises(ValueError):
                self.catalog.remove(model)
        self.assertTrue((folder / "model.safetensors").is_file())

    def test_missing_or_changed_manifest_refuses_removal(self):
        model, folder = self.install()
        (folder / "local-manifest.json").write_text(json.dumps({"repo": "someone/else", "revision": "a" * 40}))
        self.assertFalse(self.listing(model)["can_remove"])
        with self.assertRaises(ValueError):
            self.catalog.remove(model)
        self.assertTrue((folder / "model.safetensors").is_file())

    def test_linked_quarantine_is_rejected_before_directory_moves(self):
        model, folder = self.install()
        external = self.root / "external"
        external.mkdir()
        self.directory_link(self.catalog.managed_root/".removing",external)
        with self.assertRaises(ValueError):
            self.catalog.remove(model)
        self.assertTrue((folder / "model.safetensors").is_file())
        self.assertEqual(list(external.iterdir()), [])

    def test_hard_link_to_shared_weights_is_preserved(self):
        model,folder=self.install()
        original=self.root/"shared-weight"
        original.write_bytes(b"synthetic original")
        (folder/"linked-weight").hardlink_to(original)
        self.assertFalse(self.listing(model)["can_remove"])
        with self.assertRaises(ValueError):self.catalog.remove(model)
        self.assertEqual(original.read_bytes(),b"synthetic original")

    def test_linked_installation_root_is_refused(self):
        model,folder=self.install()
        external=self.root/"external-installation"
        folder.rename(external)
        self.directory_link(folder,external)
        self.assertFalse(self.listing(model)["can_remove"])
        with self.assertRaises(ValueError):self.catalog.remove(model)
        self.assertTrue((external/"model.safetensors").is_file())

    def test_runtime_cache_is_released_only_after_safety_validation_and_before_directory_move(self):
        model, folder = self.install()
        observed = []
        self.catalog.remove(model, lambda selected: observed.append((selected, folder.is_dir())))
        self.assertEqual(observed, [(model, True)])
        with self.assertRaises(ValueError):
            self.catalog.remove(model, lambda selected: observed.append((selected, False)))
        self.assertEqual(len(observed), 1)

    def test_cache_release_failure_preserves_installation_and_registry(self):
        model, folder = self.install()
        def fail(selected):
            raise RuntimeError("synthetic cache failure")
        with self.assertRaises(RuntimeError):
            self.catalog.remove(model, fail)
        self.assertTrue((folder / "model.safetensors").is_file())
        self.assertTrue(Catalog(self.config).require(model)["installed"])


class ModelRemovalApiTests(ModelRemovalFixture):
    def client(self):
        self.app = create_app(self.config, FakeEngine(), self.catalog)
        return self.enterContext(TestClient(self.app,base_url="http://127.0.0.1"))

    def test_explicit_endpoint_removes_only_selected_owned_model_and_rejects_unknown(self):
        model, folder = self.install()
        client = self.client()
        response = client.post("/v1/models/remove", json={"model_id": model})
        self.assertEqual(response.status_code, 200)
        self.assertTrue(response.json()["removed"])
        self.assertEqual(response.json()["model_id"], model)
        self.assertFalse(folder.exists())
        self.assertEqual(client.post("/v1/models/remove", json={"model_id": model}).status_code, 400)
        self.assertEqual(client.post("/v1/models/remove", json={}).status_code, 400)

    def test_busy_inference_or_unavailable_cleanup_refuses_removal(self):
        model, folder = self.install()
        client = self.client()
        admission = self.app.app.state.admission
        async def lock():
            await admission.acquire("primary", threading.Event())
        client.portal.call(lock)
        try:
            self.assertEqual(client.post("/v1/models/remove", json={"model_id": model}).status_code, 409)
        finally:
            client.portal.call(admission.release)
        admission.unavailable = True
        self.assertEqual(client.post("/v1/models/remove", json={"model_id": model}).status_code, 409)
        self.assertTrue((folder / "model.safetensors").is_file())

    def test_queued_or_downloading_import_refuses_removal(self):
        model, folder = self.install()
        client = self.client()
        for state in ("queued", "downloading"):
            self.app.app.state.imports.jobs["active"] = {"state": state}
            self.assertEqual(client.post("/v1/models/remove", json={"model_id": model}).status_code, 409)
        self.assertTrue((folder / "model.safetensors").is_file())

    def test_removal_holds_admission_until_worker_exits_and_refuses_new_download(self):
        model, folder = self.install()
        client = self.client()
        started, release = threading.Event(), threading.Event()
        actual_remove = self.catalog.remove
        def held_remove(*args):
            started.set()
            if not release.wait(5):
                raise AssertionError("Test release was not signalled")
            return actual_remove(*args)
        with patch.object(self.catalog, "remove", side_effect=held_remove), ThreadPoolExecutor(max_workers=1) as executor:
            future = executor.submit(client.post, "/v1/models/remove", json={"model_id": model})
            try:
                self.assertTrue(started.wait(2))
                health = client.get("/health").json()
                self.assertTrue(health["busy"])
                self.assertEqual(health["phase"], "model_removal")
                with client.websocket_connect("ws://127.0.0.1/v1/audio/sessions") as ws:
                    ws.send_json({"type": "start", "protocol_version": 1, "model": model, "language": "en", "live": False})
                    self.assertEqual(ws.receive_json()["type"], "session.error")
                self.app.app.state.imports.inspections["inspected"] = {"id": "test/new", "revision": "b" * 40, "download_bytes": 1}
                response = client.post("/v1/model-imports", json={"inspection_id": "inspected"})
                self.assertEqual(response.status_code, 409)
            finally:
                release.set()
            self.assertEqual(future.result(5).status_code, 200)
        self.assertFalse(self.app.app.state.admission.lock.locked())

    def test_cancelled_request_keeps_admission_until_removal_worker_exits(self):
        from local_dictation.imports import Imports
        from local_dictation.sessions import Admission
        from unittest.mock import AsyncMock
        model,folder=self.install()
        async def scenario():
            admission=Admission()
            manager=Imports(self.config,self.catalog,admission,lambda:True,FakeEngine())
            started,release=threading.Event(),threading.Event()
            def held_remove(*args):
                started.set()
                if not release.wait(5):raise AssertionError("Test worker was not released")
                return {"removed":True}
            with patch.object(self.catalog,"remove",side_effect=held_remove):
                request=SimpleNamespace(json=AsyncMock(return_value={"model_id":model}))
                task=asyncio.create_task(manager.remove(request))
                try:
                    self.assertTrue(await asyncio.to_thread(started.wait,2))
                    task.cancel()
                    await asyncio.sleep(0)
                    self.assertTrue(admission.lock.locked())
                    self.assertFalse(task.done())
                finally:release.set()
                with self.assertRaises(asyncio.CancelledError):await task
                self.assertFalse(admission.lock.locked())
        asyncio.run(scenario())
        self.assertTrue((folder/"model.safetensors").is_file())

    def test_removal_error_releases_admission_and_keeps_weights(self):
        model, folder = self.install()
        client = self.client()
        with patch.object(self.catalog, "save", side_effect=OSError("synthetic disk failure")):
            self.assertEqual(client.post("/v1/models/remove", json={"model_id": model}).status_code, 500)
        self.assertFalse(self.app.app.state.admission.lock.locked())
        self.assertTrue((folder / "model.safetensors").is_file())
