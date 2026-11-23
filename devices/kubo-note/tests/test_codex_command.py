import os
from pathlib import Path
from types import SimpleNamespace
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from bridge import codex_command, codex_options


class CodexCommandTests(unittest.TestCase):
    def test_bridge_defaults_to_high_and_applies_explicit_model_and_effort(self):
        self.assertEqual(codex_options({}), ["-c", 'model_reasoning_effort="high"'])
        self.assertEqual(codex_options({"PalaKuboModel": "gpt-6.2-astra", "PalaKuboReasoningEffort": "xhigh"}),
                         ["-c", 'model_reasoning_effort="xhigh"', "--model", "gpt-6.2-astra"])
        with self.assertRaises(ValueError):
            codex_options({"PalaKuboReasoningEffort": "unknown"})
        with self.assertRaises(ValueError):
            codex_options({"PalaKuboModel": "--unsafe"})

    def test_finds_registered_windows_package_without_codex_on_path(self):
        installation = Path(__file__).resolve().parent / "Installed Codex Package"
        cli = installation / "app" / "resources" / "codex.exe"
        result = SimpleNamespace(returncode=0, stdout=f"{installation}\n")
        with patch("bridge.os", SimpleNamespace(name="nt", environ=os.environ)), \
                patch("bridge.shutil.which", return_value=None), \
                patch("bridge.Path.is_file", autospec=True, side_effect=lambda path: path == cli), \
                patch("bridge.subprocess.run", return_value=result) as lookup:
            self.assertEqual(codex_command(), [str(cli)])
        self.assertIn("Get-AppxPackage", lookup.call_args.args[0][-1])

    def test_keeps_existing_codex_from_path(self):
        with patch("bridge.os", SimpleNamespace(name="nt")), \
                patch("bridge.shutil.which", return_value="existing-codex.exe"), \
                patch("bridge.subprocess.run") as lookup:
            self.assertEqual(codex_command(), ["existing-codex.exe"])
        lookup.assert_not_called()
