"""Build-script safety contracts; no real client, SDK, audio or API is launched."""
import ctypes
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / "scripts" / "Build.ps1"


@unittest.skipUnless(os.name == "nt", "Windows PowerShell required")
class BuildScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="local-dictation-build-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.build_root = self.root / "build"
        self.calls_file = self.root / "sdk-calls.jsonl"
        self.sdk = self.root / "sdk.ps1"
        self.sdk.write_text(r"""
$ErrorActionPreference = 'Stop'
ConvertTo-Json -InputObject @($args) -Compress | Add-Content -LiteralPath (Join-Path $env:LOCAL_DICTATION_BUILD_TEST_ROOT 'sdk-calls.jsonl')
if ($args[0] -eq 'test' -and $env:LOCAL_DICTATION_BUILD_TEST_FAIL -eq '1') { exit 1 }
if ($args[0] -eq 'publish') {
    $destination = $args[[Array]::IndexOf($args, '-o') + 1]
    $safeRoot = [IO.Path]::GetFullPath($env:LOCAL_DICTATION_BUILD_TEST_ROOT).TrimEnd('\') + '\'
    if (-not [IO.Path]::GetFullPath($destination).StartsWith($safeRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture refuses output outside its temporary directory' }
    $null = New-Item -ItemType Directory -Path $destination -Force
    [IO.File]::WriteAllText((Join-Path $destination 'LocalDictation.exe'), 'synthetic publish')
}
exit 0
""", encoding="utf-8")
        self.runner = self.root / "run.ps1"
        self.runner.write_text(r"""
$ErrorActionPreference = 'Stop'
# Replace only external process inspection; do not start/stop a real client.
function Get-Process {
    param($Name, $ErrorAction)
    if ($env:LOCAL_DICTATION_BUILD_TEST_PROCESS -eq 'inspection-error') { throw 'fixture inspection failure' }
    if ($env:LOCAL_DICTATION_BUILD_TEST_PROCESS -eq 'start-during-tests') {
        if (Test-Path -LiteralPath (Join-Path $env:LOCAL_DICTATION_BUILD_TEST_ROOT 'sdk-calls.jsonl')) {
            [pscustomobject]@{ Path = (Join-Path $env:LOCAL_DICTATION_BUILD_TEST_ROOT 'build\client-candidate\LocalDictation.exe'); Id = 123; ProcessName = 'LocalDictation' }
        }
        return
    }
    if ($env:LOCAL_DICTATION_BUILD_TEST_PROCESS -eq 'unknown-path') {
        [pscustomobject]@{ Path = $null; Id = 123; ProcessName = 'LocalDictation' }
        return
    }
    if ($env:LOCAL_DICTATION_BUILD_TEST_PROCESS) {
        [pscustomobject]@{ Path = $env:LOCAL_DICTATION_BUILD_TEST_PROCESS; Id = 123; ProcessName = 'LocalDictation' }
    }
}
try {
    Set-Location -LiteralPath $env:LOCAL_DICTATION_BUILD_TEST_ROOT
    & $env:LOCAL_DICTATION_BUILD_TEST_SCRIPT @args
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
""", encoding="utf-8")

    def run_build(self, *extra, process="", fail_tests=False):
        environment = dict(os.environ)
        environment.update(
            LOCAL_DICTATION_BUILD_TEST_ROOT=str(self.root),
            LOCAL_DICTATION_BUILD_TEST_SCRIPT=str(BUILD),
            LOCAL_DICTATION_BUILD_TEST_PROCESS=process,
            LOCAL_DICTATION_BUILD_TEST_FAIL="1" if fail_tests else "0",
        )
        return subprocess.run(
            ["powershell.exe", "-NoProfile", "-NonInteractive", "-File", str(self.runner),
             "-DotnetPath", str(self.sdk), "-BuildRoot", str(self.build_root), *map(str, extra)],
            env=environment, capture_output=True, text=True, timeout=30,
        )

    def sdk_calls(self):
        if not self.calls_file.exists():
            return []
        return [json.loads(line) for line in self.calls_file.read_text(encoding="utf-8-sig").splitlines()]

    def test_default_output_is_candidate_under_build_root(self):
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual([c[0] for c in self.sdk_calls()], ["test", "publish"])
        self.assertEqual((self.build_root / "client-candidate" / "LocalDictation.exe").read_text(), "synthetic publish")

    def test_relative_candidate_uses_powershell_working_directory(self):
        result = self.run_build("-AppDirectory", "relative candidate")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.root / "relative candidate" / "LocalDictation.exe").read_text(), "synthetic publish")

    def test_explicit_candidate_keeps_unrelated_running_client(self):
        output = self.root / "separate candidate"
        result = self.run_build("-AppDirectory", output, process=r"C:\Git\Local-Dictation\app\LocalDictation.exe")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((output / "LocalDictation.exe").read_text(), "synthetic publish")

    def test_installed_directory_alias_is_rejected_before_sdk(self):
        result = self.run_build("-AppDirectory", "C:/Git/Local-Dictation/APP/unused/../")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("installed", result.stderr.lower())
        self.assertEqual(self.sdk_calls(), [])

    def test_running_output_is_preserved_before_sdk(self):
        output = self.root / "running"
        output.mkdir()
        sentinel = output / "LocalDictation.exe"
        sentinel.write_text("preserve loaded client")
        result = self.run_build("-AppDirectory", output, process=str(sentinel).upper())
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("running", result.stderr.lower())
        self.assertEqual(self.sdk_calls(), [])
        self.assertEqual(sentinel.read_text(), "preserve loaded client")

    def test_unknown_process_identity_fails_closed_before_sdk(self):
        result = self.run_build(process="inspection-error")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.sdk_calls(), [])

    def test_checkout_output_is_rejected_before_sdk(self):
        result = self.run_build("-AppDirectory", ROOT / "publish")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.sdk_calls(), [])

    def test_client_started_during_tests_blocks_publish(self):
        result = self.run_build(process="start-during-tests")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("running", result.stderr.lower())
        self.assertEqual([c[0] for c in self.sdk_calls()], ["test"])
        self.assertFalse((self.build_root / "client-candidate").exists())

    def test_unreadable_running_path_fails_before_sdk(self):
        result = self.run_build(process="unknown-path")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.sdk_calls(), [])

    def test_build_artifacts_cannot_overlap_running_client(self):
        result = self.run_build(process=str(self.build_root / "LocalDictation.exe"))
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.sdk_calls(), [])

    def test_private_runtime_output_is_rejected_before_sdk(self):
        result = self.run_build("-AppDirectory", r"C:\AI\LocalDictation\data")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.sdk_calls(), [])

    def test_junction_output_is_rejected_before_sdk(self):
        target = self.root / "target"
        target.mkdir()
        link = self.root / "alias"
        command = "New-Item -ItemType Junction -Path '" + str(link).replace("'", "''") + "' -Target '" + str(target).replace("'", "''") + "' | Out-Null"
        created = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command], capture_output=True, text=True, timeout=30)
        self.assertEqual(created.returncode, 0, created.stdout + created.stderr)
        result = self.run_build("-AppDirectory", link / "new-candidate")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("junction", result.stderr.lower())
        self.assertEqual(self.sdk_calls(), [])
        self.assertEqual(list(target.iterdir()), [])

    def test_short_name_alias_of_checkout_is_rejected_before_sdk(self):
        # PowerShell normally hides 8.3 aliases. Obtain one from Windows itself;
        # skip only on a volume where no short alias exists.
        short_path = ctypes.WinDLL("kernel32", use_last_error=True).GetShortPathNameW
        short_path.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint]
        short_path.restype = ctypes.c_uint
        buffer = ctypes.create_unicode_buffer(32768)
        length = short_path(str(ROOT), buffer, len(buffer))
        self.assertGreater(length, 0, ctypes.get_last_error())
        self.assertLess(length, len(buffer))
        alias = buffer.value
        if alias.casefold() == str(ROOT).casefold():
            self.skipTest("Volume has no 8.3 alias for checkout")
        result = self.run_build("-AppDirectory", alias + r"\new-candidate\not-created")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("source checkout", result.stderr.lower())
        self.assertEqual(self.sdk_calls(), [])

    def test_failed_core_tests_do_not_publish(self):
        result = self.run_build(fail_tests=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual([c[0] for c in self.sdk_calls()], ["test"])
        self.assertFalse((self.build_root / "client-candidate").exists())


if __name__ == "__main__":
    unittest.main()
