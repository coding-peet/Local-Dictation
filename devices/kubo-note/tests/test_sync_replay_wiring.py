from pathlib import Path
import unittest

APP = Path(__file__).resolve().parents[1] / "pala_note" / "src" / "app"


class TransportCommitWiring(unittest.TestCase):
    def test_commit_recovers_an_older_note_before_opening_the_queue_transaction(self):
        source = (APP / "notes.cpp").read_text(encoding="utf-8")
        save = source.split("bool saveNoteTranscript(", 1)[1].split("namespace {", 1)[0]
        compatibility = save.split("#endif", 1)[1]
        recorder = save.split("#endif", 1)[0]
        self.assertLess(recorder.index("note_transcript::storeOnce("), recorder.index("registerRecorderWork("))
        save = compatibility
        self.assertIn("writeNoteQueueState(num, route", save)
        self.assertLess(save.index("replayTranscript()"), save.index("SyncQueueChange change(num)"))
        self.assertLess(save.index("SyncQueueChange change(num)"), save.index("writeNoteQueueState(num, route"))
        self.assertLess(save.index("writeNoteQueueState(num, route"), save.index("note_transcript::storeOnce("))

    def test_route_and_transcript_are_persisted_before_transport_receipt(self):
        source = (APP / "usb_sync.cpp").read_text(encoding="utf-8")
        body = source.split("void commitTranscript(", 1)[1].split("void scheduleBluetoothOrganization(", 1)[0]
        save = "if (!saveNoteTranscript(note, transcript, route))"
        self.assertIn(save, body)
        self.assertLess(body.index(save), body.index('reply(String("SAVED ")'))
        self.assertIn('reply("ERROR SAVE")', body)
