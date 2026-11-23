import json
from pathlib import Path
import sys
import threading
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
import usb_sync


class BluetoothBacklogTest(unittest.TestCase):
    def test_saved_transcript_uses_device_request_and_only_device_confirmation(self):
        stop = threading.Event()
        bridge = Mock()
        bridge.local_transcription_ready.return_value = False
        bridge.dispatch.side_effect = [{"pala_pending": [{"id": "event"}]}, {"ok": True}]
        note = Mock(read_chunk=96, write_chunk=96)
        transcript = json.dumps({"pala_note": 17, "pala_created": "2026-11-22T21:01:00Z"}).encode()
        decision = b'{"id":"event","choice":1,"accept":true}'
        jobs = iter([("MEMORY", transcript), ("EVENT", decision), None])
        data = None
        batch = iter([["WORK", "17", "TEXT"], ["EMPTY"]])

        def exchange(port, command, **options):
            nonlocal data
            verb = command.partition(" ")[0]
            if verb == "BATCH":
                self.assertEqual(command, "BATCH TEXT")
                return ["BATCH", "1"]
            if verb == "BATCH-NEXT":
                return next(batch)
            if verb == "ORGANIZE":
                self.assertEqual(command, "ORGANIZE 17")
                return ["OK"]
            if verb == "JOB":
                work = next(jobs)
                if work is None:
                    return ["DONE"]
                route, data = work
                return ["REQUEST", route, str(len(data))]
            if verb == "JOB-READ":
                offset, size = map(int, command.split()[1:])
                return ["DATA", str(offset), data[offset:offset + size].hex()]
            if verb in ("JOB-RESPONSE", "JOB-PUT", "JOB-COMMIT", "CLOSE"):
                return ["OK"]
            self.fail(command)

        with patch.object(usb_sync, "_paired_session"), patch.object(usb_sync, "_exchange", side_effect=exchange), \
             patch.object(usb_sync, "processing_config", return_value={"processing_mode": "kubo"}):
            self.assertTrue(usb_sync.drain_bluetooth_backlog(note, "key", bridge, "device", stop))

        self.assertEqual([call.args[0] for call in bridge.dispatch.call_args_list], ["/v1/responses", "/pala/event"])
        self.assertEqual(bridge.dispatch.call_args_list[0].args[1], transcript)
        self.assertEqual(bridge.dispatch.call_args_list[1].args[1], decision)
        self.assertEqual(bridge.dispatch.call_args.kwargs["device"], "device")
        bridge.transcribe_local.assert_not_called()

    def test_full_batch_saves_the_audio_before_organizing_and_stops_at_a_deferred_note(self):
        bridge = Mock()
        bridge.local_transcription_ready.return_value = True
        replies = iter([["BATCH", "2"], ["WORK", "17", "AUDIO"], ["WORK", "17", "TEXT"], ["OK"]])
        with patch.object(usb_sync, "_paired_session"), \
             patch.object(usb_sync, "_exchange", side_effect=lambda *_: next(replies)) as exchange, \
             patch.object(usb_sync, "processing_config", return_value={"processing_mode": "kubo"}), \
             patch.object(usb_sync, "sync_one", return_value=usb_sync.SyncedNote(17, 8000)) as sync, \
             patch.object(usb_sync, "_organize_bluetooth_note", return_value=False) as organize:
            self.assertTrue(usb_sync.drain_bluetooth_backlog(Mock(), "key", bridge, "device", threading.Event()))

        self.assertEqual([call.args[1] for call in exchange.call_args_list],
                         ["BATCH FULL", "BATCH-NEXT", "BATCH-NEXT", "CLOSE"])
        self.assertEqual(sync.call_args.kwargs["note"], 17)
        self.assertTrue(sync.call_args.kwargs["organize"])
        self.assertTrue(sync.call_args.kwargs["session_open"])
        self.assertEqual(organize.call_args.args[1], 17)
        bridge.dispatch.assert_not_called()

    def test_old_note_keeps_audio_only_sync(self):
        bridge = Mock()
        bridge.local_transcription_ready.return_value = True
        unsupported = usb_sync.UsbSyncError("Unsupported", diagnostic=usb_sync.UsbFailure("firmware", FirmwareCode="COMMAND"))
        with patch.object(usb_sync, "_paired_session"), \
             patch.object(usb_sync, "_exchange", side_effect=[unsupported, ["OK"]]), \
             patch.object(usb_sync, "processing_config", return_value={"processing_mode": "kubo"}), \
             patch.object(usb_sync, "_drain_link") as legacy:
            self.assertTrue(usb_sync.drain_bluetooth_backlog(Mock(), "key", bridge, "device", threading.Event()))
        self.assertEqual(legacy.call_args.args[1], "key")

    def test_unrecognized_route_never_reaches_bridge(self):
        bridge = Mock()
        with patch.object(usb_sync, "_exchange", side_effect=[["OK"], ["REQUEST", "OTHER", "39"]]):
            with self.assertRaises(usb_sync.UsbSyncError):
                usb_sync._organize_bluetooth_note(Mock(), 17, bridge, "device", threading.Event())
        bridge.dispatch.assert_not_called()
