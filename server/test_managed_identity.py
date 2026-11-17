import json
from pathlib import Path
import tempfile
import unittest
from local_dictation.catalog import Catalog
import test_catalog_installation

class ManagedIdentityTests(unittest.TestCase):
    def test_missing_or_mismatched_manifest_clears_evidence_and_allows_repair(self):
        for broken in ("missing","wrong-revision","invalid"):
            with self.subTest(broken=broken),tempfile.TemporaryDirectory() as d:
                fixture=test_catalog_installation.CatalogInstallationTests()
                config,c=fixture.setup(d);spec=fixture.spec(config);fixture.fill(spec);c.install(spec);c.mark_tested(spec["id"])
                manifest=Path(spec["path"])/"local-manifest.json"
                if broken=="missing":manifest.unlink()
                elif broken=="invalid":manifest.write_text("{broken")
                else:manifest.write_text(json.dumps({"repo":spec["id"],"revision":"f"*40}))
                restored=Catalog(config)
                model=next(m for m in restored.models() if m["id"]==spec["id"])
                self.assertFalse(model["installed"])
                self.assertFalse(model["runtime_tested"])
                self.assertFalse(model["quality_verified"])
                self.assertFalse(model["verification_eligible"])
                self.assertTrue(model["can_install"])
