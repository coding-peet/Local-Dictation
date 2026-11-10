"""Build a Windows Installer pilot from an existing flat client publish.

Uses Windows Installer and CopyFileW only. No installation, client execution,
security-policy changes, certificate changes, data migration, or downloads.
Keep the resulting MSI and its app subdirectory together.
"""
from __future__ import annotations

import argparse
import ctypes as ct
from ctypes import wintypes as wt
import json
import os
from pathlib import Path
import sys
import uuid

PAYLOAD = (
    "LocalDictation.exe", "LocalDictation.dll", "LocalDictation.Core.dll",
    "LocalDictation.deps.json", "LocalDictation.runtimeconfig.json",
    "NAudio.Core.dll", "NAudio.dll", "NAudio.Midi.dll", "NAudio.Wasapi.dll",
    "NAudio.WinMM.dll", "System.Numerics.Tensors.dll",
)
VERSION = "0.1.0"
PRODUCT_CODE = "{6CBA1D36-B8B7-4EE0-B283-E58EAC43DF5C}"
UPGRADE_CODE = "{87942CB7-2466-4277-9A72-6DA26A1C64A3}"
COMPONENT_NAMESPACE = uuid.UUID("b83159d6-a926-48cc-852f-02dcb27e31ad")

SCHEMA = {
    "Property": [("Property", "CHAR(72) NOT NULL"), ("Value", "CHAR(0) LOCALIZABLE")],
    "Directory": [("Directory", "CHAR(72) NOT NULL"), ("Directory_Parent", "CHAR(72)"), ("DefaultDir", "CHAR(255) NOT NULL LOCALIZABLE")],
    "Feature": [("Feature", "CHAR(38) NOT NULL"), ("Feature_Parent", "CHAR(38)"), ("Title", "CHAR(64) LOCALIZABLE"), ("Description", "CHAR(255) LOCALIZABLE"), ("Display", "SHORT"), ("Level", "SHORT NOT NULL"), ("Directory_", "CHAR(72)"), ("Attributes", "SHORT NOT NULL")],
    "Component": [("Component", "CHAR(72) NOT NULL"), ("ComponentId", "CHAR(38)"), ("Directory_", "CHAR(72) NOT NULL"), ("Attributes", "SHORT NOT NULL"), ("Condition", "CHAR(255)"), ("KeyPath", "CHAR(72)")],
    "FeatureComponents": [("Feature_", "CHAR(38) NOT NULL"), ("Component_", "CHAR(72) NOT NULL")],
    "File": [("File", "CHAR(72) NOT NULL"), ("Component_", "CHAR(72) NOT NULL"), ("FileName", "CHAR(255) NOT NULL LOCALIZABLE"), ("FileSize", "LONG NOT NULL"), ("Version", "CHAR(72)"), ("Language", "CHAR(20)"), ("Attributes", "SHORT"), ("Sequence", "SHORT NOT NULL")],
    "Media": [("DiskId", "SHORT NOT NULL"), ("LastSequence", "SHORT NOT NULL"), ("DiskPrompt", "CHAR(64) LOCALIZABLE"), ("Cabinet", "CHAR(255)"), ("VolumeLabel", "CHAR(32)"), ("Source", "CHAR(72)")],
    "Shortcut": [("Shortcut", "CHAR(72) NOT NULL"), ("Directory_", "CHAR(72) NOT NULL"), ("Name", "CHAR(128) NOT NULL LOCALIZABLE"), ("Component_", "CHAR(72) NOT NULL"), ("Target", "CHAR(255) NOT NULL"), ("Arguments", "CHAR(255)"), ("Description", "CHAR(255) LOCALIZABLE"), ("Hotkey", "SHORT"), ("Icon_", "CHAR(72)"), ("IconIndex", "SHORT"), ("ShowCmd", "SHORT"), ("WkDir", "CHAR(72)")],
    "LaunchCondition": [("Condition", "CHAR(255) NOT NULL"), ("Description", "CHAR(255) NOT NULL LOCALIZABLE")],
    "CreateFolder": [("Directory_", "CHAR(72) NOT NULL"), ("Component_", "CHAR(72) NOT NULL")],
}
for sequence_table in ("InstallExecuteSequence", "InstallUISequence"):
    SCHEMA[sequence_table] = [("Action", "CHAR(72) NOT NULL"), ("Condition", "CHAR(255)"), ("Sequence", "SHORT")]
COMPOSITE_KEYS = {"FeatureComponents": ["Feature_", "Component_"], "CreateFolder": ["Directory_", "Component_"]}


class Msi:
    def __init__(self, path: Path):
        self.dll = ct.WinDLL("msi", use_last_error=True)
        self.open_db = self.bind("MsiOpenDatabaseW", [wt.LPCWSTR, ct.c_void_p, ct.POINTER(wt.UINT)])
        self.open_view = self.bind("MsiDatabaseOpenViewW", [wt.UINT, wt.LPCWSTR, ct.POINTER(wt.UINT)])
        self.execute_view = self.bind("MsiViewExecute", [wt.UINT, wt.UINT])
        self.close = self.bind("MsiCloseHandle", [wt.UINT])
        self.commit = self.bind("MsiDatabaseCommit", [wt.UINT])
        self.record = self.bind("MsiCreateRecord", [wt.UINT])
        self.set_string = self.bind("MsiRecordSetStringW", [wt.UINT, wt.UINT, wt.LPCWSTR])
        self.set_int = self.bind("MsiRecordSetInteger", [wt.UINT, wt.UINT, ct.c_int])
        self.get_summary = self.bind("MsiGetSummaryInformationW", [wt.UINT, wt.LPCWSTR, wt.UINT, ct.POINTER(wt.UINT)])
        self.set_summary = self.bind("MsiSummaryInfoSetPropertyW", [wt.UINT, wt.UINT, wt.UINT, ct.c_int, ct.c_void_p, wt.LPCWSTR])
        self.persist_summary = self.bind("MsiSummaryInfoPersist", [wt.UINT])
        handle = wt.UINT()
        self.check(self.open_db(str(path), ct.c_void_p(3), ct.byref(handle)), "create database")
        self.handle = handle.value

    def bind(self, name, types):
        function = getattr(self.dll, name)
        function.argtypes = types
        function.restype = wt.UINT
        return function

    @staticmethod
    def check(code, operation):
        if code:
            raise OSError(code, f"Windows Installer {operation} failed: {code}")

    def sql(self, statement: str, values: tuple = ()):
        view = wt.UINT()
        self.check(self.open_view(self.handle, statement, ct.byref(view)), statement)
        record = 0
        try:
            if values:
                record = self.record(len(values))
                if not record:
                    raise OSError("Unable to create MSI record")
                for index, value in enumerate(values, 1):
                    if value is None:
                        continue
                    code = self.set_int(record, index, value) if isinstance(value, int) else self.set_string(record, index, value)
                    self.check(code, "set record")
            self.check(self.execute_view(view.value, record), statement)
        finally:
            if record:
                self.close(record)
            self.close(view.value)

    def insert(self, table: str, values: tuple):
        columns = ",".join(quote(column[0]) for column in SCHEMA[table])
        placeholders = ",".join("?" for _ in values)
        self.sql(f"INSERT INTO {quote(table)} ({columns}) VALUES ({placeholders})", values)

    def summary(self):
        handle = wt.UINT()
        self.check(self.get_summary(self.handle, None, 12, ct.byref(handle)), "open summary")
        try:
            text_values = {
                2: "Installation Database", 3: "Local Dictation pilot",
                4: "Peter Chnurik", 7: "x64;1033",
                9: "{" + str(uuid.uuid4()).upper() + "}",
            }
            for key, value in text_values.items():
                self.check(self.set_summary(handle.value, key, 30, 0, None, value), "summary string")
            for key, value in {1: 1252, 14: 500, 15: 0}.items():
                self.check(self.set_summary(handle.value, key, 2, value, None, None), "summary integer")
            self.check(self.persist_summary(handle.value), "persist summary")
        finally:
            self.close(handle.value)


def quote(name: str) -> str:
    return chr(96) + name + chr(96)


def build(app_directory: Path, output_directory: Path) -> Path:
    if os.name != "nt":
        raise RuntimeError("Windows Installer is required")
    source = app_directory.resolve(strict=True)
    if source.is_symlink() or source.is_junction():
        raise ValueError("Reparse source directory rejected")
    output = output_directory.absolute()
    if output.exists() or output.is_symlink() or output.is_junction():
        raise FileExistsError(f"Output already exists: {output}")
    for name in PAYLOAD:
        item = source / name
        if not item.is_file() or item.is_symlink() or item.is_junction():
            raise ValueError(f"Missing or unsafe payload file: {name}")
    output.mkdir(parents=True)
    payload_directory = output / "app"
    payload_directory.mkdir()
    kernel = ct.WinDLL("kernel32", use_last_error=True)
    copy_file = kernel.CopyFileW
    copy_file.argtypes = [wt.LPCWSTR, wt.LPCWSTR, wt.BOOL]
    copy_file.restype = wt.BOOL
    for name in PAYLOAD:
        if not copy_file(str(source / name), str(payload_directory / name), True):
            raise ct.WinError(ct.get_last_error())

    path = output / f"LocalDictation-{VERSION}-x64.msi"
    database = Msi(path)
    try:
        for table, columns in SCHEMA.items():
            definitions = ", ".join(quote(name) + " " + data_type for name, data_type in columns)
            keys = COMPOSITE_KEYS.get(table, [columns[0][0]])
            primary = ", ".join(quote(key) for key in keys)
            database.sql(f"CREATE TABLE {quote(table)} ({definitions} PRIMARY KEY {primary})")
        for name, value in {
            "ProductCode": PRODUCT_CODE, "UpgradeCode": UPGRADE_CODE,
            "ProductName": "Local Dictation", "ProductVersion": VERSION,
            "ProductLanguage": "1033", "Manufacturer": "Peter Chnurik",
            "ALLUSERS": "1", "INSTALLLEVEL": "1", "ARPNOMODIFY": "1",
            "ARPCOMMENTS": "Local Dictation installation pilot; API and user data remain external.",
        }.items():
            database.insert("Property", (name, value))
        for row in [
            ("TARGETDIR", None, "SourceDir"),
            ("ProgramFiles64Folder", "TARGETDIR", "."),
            ("INSTALLDIR", "ProgramFiles64Folder", "LOCALD~1|Local Dictation:app"),
            ("ProgramMenuFolder", "TARGETDIR", "."),
        ]:
            database.insert("Directory", row)
        database.insert("Feature", ("MainFeature", None, "Local Dictation", "Native Windows client", 1, 1, "INSTALLDIR", 0))
        for index, name in enumerate(PAYLOAD, 1):
            component = f"C{index:04d}"
            file_id = f"F{index:04d}"
            guid = "{" + str(uuid.uuid5(COMPONENT_NAMESPACE, name.lower())).upper() + "}"
            database.insert("Component", (component, guid, "INSTALLDIR", 256, None, file_id))
            database.insert("FeatureComponents", ("MainFeature", component))
            short_name = f"F{index:07d}.{Path(name).suffix[1:4].upper()}"
            database.insert("File", (file_id, component, short_name + "|" + name, (source / name).stat().st_size, None, None, 512, index))
        database.insert("Media", (1, len(PAYLOAD), None, None, None, None))
        database.insert("Shortcut", ("StartMenu", "ProgramMenuFolder", "LOCALD~1|Local Dictation", "C0001", "[#F0001]", None, "Local speech transcription", None, None, None, 1, "INSTALLDIR"))
        database.insert("CreateFolder", ("INSTALLDIR", "C0001"))
        database.insert("LaunchCondition", ("VersionNT64", "Local Dictation requires 64-bit Windows."))
        execute_actions = {
            "LaunchConditions": 100, "CostInitialize": 800, "FileCost": 900,
            "CostFinalize": 1000, "InstallValidate": 1400, "InstallInitialize": 1500,
            "ProcessComponents": 1600, "UnpublishFeatures": 1800,
            "RemoveShortcuts": 3200, "RemoveFiles": 3500, "RemoveFolders": 3600,
            "CreateFolders": 3700, "InstallFiles": 4000, "CreateShortcuts": 4500,
            "RegisterUser": 6000, "RegisterProduct": 6100, "PublishFeatures": 6300,
            "PublishProduct": 6400, "InstallFinalize": 6600,
        }
        for action, sequence in execute_actions.items():
            database.insert("InstallExecuteSequence", (action, None, sequence))
        for action, sequence in {
            "LaunchConditions": 100, "CostInitialize": 800, "FileCost": 900,
            "CostFinalize": 1000, "ExecuteAction": 1300,
        }.items():
            database.insert("InstallUISequence", (action, None, sequence))
        database.summary()
        database.check(database.commit(database.handle), "commit")
    finally:
        database.close(database.handle)
    metadata = {
        "product": "Local Dictation", "version": VERSION,
        "productCode": PRODUCT_CODE, "msi": path.name,
        "target": r"C:\Program Files\Local Dictation",
        "payload": list(PAYLOAD), "compressed": False,
        "clientAutoLaunch": False, "securityPolicyChanges": False,
        "firstInstallPilot": True,
    }
    (output / "package-info.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app-directory", type=Path, required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    args = parser.parse_args()
    try:
        print(build(args.app_directory, args.output_directory))
    except (OSError, ValueError, RuntimeError) as error:
        print(f"MSI build failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
