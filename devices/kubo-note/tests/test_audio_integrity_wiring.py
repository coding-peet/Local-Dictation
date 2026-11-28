from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1] / "pala_note" / "src"

class AudioCaptureIntegrityTests(unittest.TestCase):
    def test_rx_callback_is_registered_before_channel_enable(self):
        src = (ROOT / "codec_board/codec_init.c").read_text(encoding="utf-8")
        body = src[src.index("int ret = i2s_new_channel"):src.index("#else\n    i2s_config_t i2s_config")]
        self.assertIn(".on_recv_q_ovf", body)
        self.assertLess(body.index("i2s_channel_register_event_callback"), body.index("i2s_channel_enable"))
        self.assertNotIn(".on_recv =", body)
        callback = src.split("IRAM_ATTR microphone_rx_overflow(", 1)[1].split("\n}", 1)[0]
        for disallowed in ("printf", "malloc", "Serial", "memcpy", "dma_buf", "->data"):
            self.assertNotIn(disallowed, callback)
        self.assertIn("event->size", callback)
        self.assertIn("return false", callback)

    def test_reconfiguration_cannot_supply_invented_pcm(self):
        src = (ROOT / "esp_codec_dev/platform/audio_codec_data_i2s.c").read_text(encoding="utf-8")
        skipped = src.split("if (__atomic_load_n(&i2s_data->in_reconfig,", 1)[1].split("}", 1)[0]
        self.assertNotIn("memset", skipped)
        self.assertIn("AUDIO_RX_RECONFIGURING", skipped)

    def test_one_snapshot_pair_and_one_completion_for_all_recording_paths(self):
        rec = (ROOT / "app/record.cpp").read_text(encoding="utf-8")
        producer = rec.split("void microphoneProducer(void*)", 1)[1].split("class MicrophoneSession", 1)[0]
        self.assertEqual(producer.count("audio_rx_dma_snapshot()"), 2)
        self.assertLess(producer.index("health.read("), producer.index("pushStereo("))
        self.assertLess(producer.index("health.finish("), producer.index("audio_idle_suspend()"))
        self.assertLess(producer.index("health.finish("), producer.index("captureResult.store("))
        self.assertIn("vTaskDelay(", producer)
        self.assertNotIn("Serial.", producer.split("audio_idle_suspend()", 1)[0])
        completion = producer.split("audio_idle_suspend()", 1)[1]
        trial = completion.split("#if PALA_STAGE3_TRIAL\n", 1)[1].split("#endif\n", 1)[0]
        self.assertNotIn("#else", trial)
        self.assertNotIn("#elif", trial)
        self.assertIn('Serial.printf("[S3] microphone', trial)
        self.assertNotIn("Serial.", completion.replace(trial, "", 1))
