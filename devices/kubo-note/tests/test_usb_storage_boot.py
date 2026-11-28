import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "pala_note"


class UsbStorageBootTests(unittest.TestCase):
    def test_service_boot_precedes_recorder_and_button_tasks(self):
        source = (ROOT / "pala_note.ino").read_text(encoding="utf8")
        setup = source.split("void setup() {", 1)[1].split("void loop() {", 1)[0]
        self.assertIn("runUsbCardService()", setup)
        self.assertLess(setup.index("takeUsbStorageBoot()"), setup.index("runUsbCardService()"))
        self.assertLess(setup.index("runUsbCardService()"), setup.index("attachInterrupt"))

    def test_commands_require_authenticated_usb_bench(self):
        source = (ROOT / "src/app/usb_sync.cpp").read_text(encoding="utf8")
        block = source.split('command == "BENCH" && owner == Link::Usb', 1)[1].split('#endif\n  else if (command == "CLOCK"', 1)[0]
        self.assertIn('first == "usb-storage"', block)
        self.assertIn('first == "usb-storage-next"', block)
        self.assertLess(source.index('if (!authenticated) { reply("ERROR AUTH")'), source.index('first == "usb-storage"'))

    def test_shared_card_is_unmounted_before_transition(self):
        source = (ROOT / "pala_note.ino").read_text(encoding="utf8")
        self.assertIn("pollUsbStorageRestart()", source)
        transition = source.split("void pollUsbStorageRestart() {", 1)[1].split("\n}", 1)[0]
        self.assertLess(transition.index("claimRecorderForUsbStorage()"), transition.index("SD_MMC.end()"))
        self.assertLess(transition.index("SD_MMC.end()"), transition.index("ESP.restart()"))
