"""Contract tests for the Windows Installer pilot; never installs or launches."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
BUILDER = ROOT / "scripts" / "build_client_msi.py"
NAMES = [
    "LocalDictation.exe", "LocalDictation.dll", "LocalDictation.Core.dll",
    "LocalDictation.deps.json", "LocalDictation.runtimeconfig.json",
    "NAudio.Core.dll", "NAudio.dll", "NAudio.Midi.dll", "NAudio.Wasapi.dll",
    "NAudio.WinMM.dll", "System.Numerics.Tensors.dll",
]


@unittest.skipUnless(os.name == "nt", "Windows Installer required")
class MsiContractTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(BUILDER.is_file(), "Missing MSI builder")
        self.temp = tempfile.TemporaryDirectory(prefix="local-dictation-msi-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "source"
        self.source.mkdir()
        for name in NAMES:
            (self.source / name).write_bytes(("fixture:" + name).encode())
        self.output = self.root / "package"

    def build(self):
        return subprocess.run(
            [sys.executable, str(BUILDER), "--app-directory", str(self.source),
             "--output-directory", str(self.output)],
            capture_output=True, text=True, timeout=30,
        )

    def test_database_contains_only_client_and_standard_install_actions(self):
        result = self.build()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        msi_path = self.output / "LocalDictation-0.1.0-x64.msi"
        self.assertTrue(msi_path.is_file())
        escaped = str(msi_path).replace("'", "''")
        command = """
$ErrorActionPreference='Stop'
$i=New-Object -ComObject WindowsInstaller.Installer
$d=$i.OpenDatabase('""" + escaped + """',0)
function Rows($sql,$count) {
 $v=$d.OpenView($sql);$null=$v.Execute();$rows=@()
 while($r=$v.Fetch()) {
  $values=@();for($n=1;$n -le $count;$n++) {$values+=,$r.StringData($n)}
  $rows+=,@($values)
 }
 $null=$v.Close();return ,$rows
}
[ordered]@{
 Files=Rows 'SELECT FileName,FileSize,Attributes FROM File' 3
 Properties=Rows 'SELECT Property,Value FROM Property' 2
 Directories=Rows 'SELECT Directory,Directory_Parent,DefaultDir FROM Directory' 3
 Actions=Rows 'SELECT Action FROM InstallExecuteSequence' 1
 Tables=Rows 'SELECT Name FROM _Tables' 1
 Components=Rows 'SELECT Attributes FROM Component' 1
 Shortcuts=Rows 'SELECT Target,Arguments FROM Shortcut' 2
} | ConvertTo-Json -Depth 7 -Compress
"""
        read = subprocess.run(
            ["powershell.exe", "-NoProfile", "-Command", command],
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(read.returncode, 0, read.stdout + read.stderr)
        data = json.loads(read.stdout)
        self.assertNotIn(None, data["Files"], data)
        self.assertTrue(all(int(row[2]) & 512 for row in data["Files"]), "Every required file must be vital")
        for row in data["Files"]:
            self.assertRegex(row[0].split("|")[0], r"^[A-Z0-9_]{1,8}\.[A-Z0-9_]{1,3}$")
        actual = {row[0].split("|")[-1]: int(row[1]) for row in data["Files"]}
        self.assertEqual(actual, {n: (self.source / n).stat().st_size for n in NAMES})
        properties = dict(data["Properties"])
        self.assertEqual(properties["ALLUSERS"], "1")
        self.assertEqual(properties["ProductName"], "Local Dictation")
        directories = {r[0]: r[1:] for r in data["Directories"]}
        self.assertEqual(directories["INSTALLDIR"], ["ProgramFiles64Folder", "LOCALD~1|Local Dictation:app"])
        self.assertNotIn("CustomAction", {r[0] for r in data["Tables"]})
        self.assertNotIn("RemoveFile", {r[0] for r in data["Tables"]})
        self.assertIn("InstallFiles", {r[0] for r in data["Actions"]})
        self.assertIn("RemoveFiles", {r[0] for r in data["Actions"]})
        self.assertTrue(all(int(r[0]) == 256 for r in data["Components"]))
        self.assertEqual(data["Shortcuts"], [["[#F0001]", ""]])
        for name in NAMES:
            self.assertEqual((self.output / "app" / name).read_bytes(), (self.source / name).read_bytes())
        self.assertFalse(any("data" in r[0].lower() for r in data["Directories"]))

    def test_missing_payload_is_rejected_before_output(self):
        (self.source / "LocalDictation.runtimeconfig.json").unlink()
        result = self.build()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.output.exists())

    def test_existing_destination_is_preserved(self):
        self.output.mkdir()
        sentinel = self.output / "keep.txt"
        sentinel.write_text("preserve", encoding="utf-8")
        result = self.build()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(sentinel.read_text(encoding="utf-8"), "preserve")
        self.assertEqual(list(self.output.iterdir()), [sentinel])


if __name__ == "__main__":
    unittest.main()
