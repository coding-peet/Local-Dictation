from pathlib import Path
import unittest


APP = Path(__file__).resolve().parents[1] / "pala_note" / "src" / "app"


class BleClockTraceWiringTests(unittest.TestCase):
    def test_ble_trace_covers_the_actual_notification_submission(self):
        source = (APP / "bluetooth_sync.cpp").read_text(encoding="utf-8")
        packet = source.split("bool queueAudioPacket(", 1)[1].split("bool sendAudioNotifications(", 1)[0]
        self.assertLess(packet.index("stage2::bleBlockRequested("), packet.index("readBluetoothAudio("))
        send = source.split("bool sendAudioNotifications(", 1)[1].split("bool receiveEnrollment(", 1)[0]
        self.assertLess(send.index("esp_ble_gatts_send_indicate("), send.index("stage2::bleBlockNotifications("))
        self.assertIn("stage2::bleBlockNotifications(bleTraceSequence, firstQueuedAt, lastQueuedAt, completed)", send)

    def test_bench_exposes_ble_ring_on_the_recording_timebase(self):
        source = (APP / "stage2_bench.cpp").read_text(encoding="utf-8")
        self.assertIn('requested == "ble_blocks"', source)
        self.assertIn('requested.startsWith("ble_")', source)
        self.assertIn("millis()", source.split("uint32_t bleBlockRequested(", 1)[1].split("\n}", 1)[0])


    def test_fragment_pacing_is_nonblocking_and_limited_to_the_bench_build(self):
        source = (APP / "bluetooth_sync.cpp").read_text(encoding="utf-8")
        send = source.split("bool sendAudioNotifications(", 1)[1].split("bool receiveEnrollment(", 1)[0]
        self.assertIn("#if PALA_STAGE2_BENCH && defined(PALA_TEST_BLE_FRAGMENT_GAP_MS)", send)
        self.assertLess(send.index("millis() - lastPacedFragmentAt"), send.index("esp_ble_gatts_send_indicate("))
        self.assertLess(send.index("esp_ble_gatts_send_indicate("), send.index("lastPacedFragmentAt = lastQueuedAt"))
        self.assertIn("std::min(1U, credits)", send)
        self.assertNotIn("delay(", send)
        env = (APP.parents[2] / "platformio.ini").read_text(encoding="utf-8")
        self.assertIn("[env:pala_note_recorder_msc_ble_paced]", env)
        self.assertEqual(env.count("-D PALA_TEST_BLE_FRAGMENT_GAP_MS=180"), 1)


if __name__ == "__main__":
    unittest.main()
