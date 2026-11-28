import io
from pathlib import Path
import sys
import tempfile
import queue
from contextlib import redirect_stdout
from types import SimpleNamespace
from unittest.mock import patch
import unittest
import wave
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import stage2_bench as bench


class StorageTraceTest(unittest.TestCase):
    # Two synthetic writer phases for one GPIO edge. These are wall microseconds.
    def trace(self):
        trace = {page: dict.fromkeys(fields, 0) for page, fields in bench.TRACE_FIELDS.items()}
        trace.update(boot=72, revision=45)
        trace["summary"]["edge"] = 3210
        trace["io_open"] = dict(edge=3210, total_us=1000, fat_lock_us=530, vfs_lock_us=0,
                                host_lock_us=20, command_us=70, event_us=330, other_us=50,
                                lock_calls=4, lock_blocks=5, commands=3, events=4, valid=1)
        trace["io_header"] = dict(edge=3210, total_us=100, fat_lock_us=0, vfs_lock_us=3,
                                  host_lock_us=0, command_us=6, event_us=80, other_us=11,
                                  lock_calls=1, lock_blocks=1, commands=2, events=3, valid=1)
        trace["io_calls"] = dict(edge=3210, path_us=4, posix_open_us=903, fdopen_us=60,
                                 setvbuf_us=13, close_us=0, open_rc=1, open_errno=0,
                                 header_rc=1, header_errno=0)
        return trace

    def test_full_reader_discards_cross_boot_and_cross_revision_storage_pages(self):
        stable = self.trace()
        pages = ("summary", "home", "rec", "storage", "audio", "release",
                 "io_open", "io_header", "io_calls")
        stable["audio"] = dict.fromkeys(bench.AUDIO_FIELDS, 0)
        stable["release"] = dict.fromkeys(bench.RELEASE_FIELDS, 0)
        for first in ((71, 45), (72, 44)):
            with self.subTest(first=first):
                addressed = []
                def exchange(link, command):
                    page = command.split()[-1]
                    addressed.append(page)
                    boot, rev = first if len(addressed) < len(pages) else (72, 45)
                    return ["BENCH", "TRACE1", str(boot), str(rev), page,
                            *map(str, stable[page].values())]
                actual = bench.read_display_trace(None, SimpleNamespace(_exchange=exchange),
                                                  include_audio=True, include_storage_io=True)
                self.assertEqual(actual, stable)
                self.assertEqual(addressed, list(pages) * 2)

    def test_reader_rejects_malformed_or_perpetually_changing_storage_pages(self):
        data = self.trace()
        for fault in ("incomplete", "overflow", "wrong_page", "rolling"):
            with self.subTest(fault=fault):
                def exchange(link, command):
                    page = command.split()[-1]
                    row = ["BENCH", "TRACE1", "72", "45", page, *map(str, data[page].values())]
                    if page == "io_calls":
                        if fault == "incomplete":
                            row.pop()
                        elif fault == "overflow":
                            row[-1] = "4294967296"
                        elif fault == "wrong_page":
                            row[4] = "io_header"
                        else:
                            row[3] = "46"
                    return row
                with self.assertRaises(ValueError):
                    bench.read_display_trace(None, SimpleNamespace(_exchange=exchange), include_storage_io=True)

    def test_same_edge_open_and_unbuffered_header_have_separate_wall_partitions(self):
        measured = bench.display_trial_result(self.trace(), 71)
        self.assertFalse(measured["passed"])  # Display qualification has its own evidence.
        io = measured["storage_io"]
        opened, header = io["wav_open"], io["header_write"]
        self.assertEqual((opened["status"], opened["dominant"]), ("measured", "fatfs_lock_block"))
        self.assertEqual((header["status"], header["dominant"]), ("measured", "sdmmc_event_queue_block"))
        self.assertEqual((opened["total_us"], header["total_us"]), (1000, 100))
        self.assertEqual(opened["exclusive_us"], dict(fat_lock_us=530, vfs_lock_us=0, host_lock_us=20,
                                                     command_us=70, event_us=330, other_us=50))
        self.assertEqual((opened["counts"]["lock_calls"], opened["counts"]["lock_blocks"]), (4, 5))
        self.assertEqual(opened["subcalls_us"], dict(path_us=4, posix_open_us=903, fdopen_us=60,
                                                    setvbuf_us=13, close_us=0))
        self.assertNotIn("subcalls_us", header)

    def test_mixed_and_residual_wall_time_have_no_specific_attribution(self):
        trace = self.trace()
        row = trace["io_header"]
        row.update(vfs_lock_us=0, command_us=50, event_us=50, other_us=0)
        self.assertEqual(bench.display_trial_result(trace, 71)["storage_io"]["header_write"]["dominant"], "mixed")
        row.update(command_us=0, event_us=0, other_us=100, commands=0, events=0)
        self.assertEqual(bench.display_trial_result(trace, 71)["storage_io"]["header_write"]["dominant"], "unresolved")
        row.update(other_us=0, total_us=0)
        result = bench.display_trial_result(trace, 71)["storage_io"]["header_write"]
        self.assertEqual(result["status"], "incomplete")
        self.assertIsNone(result["dominant"])

    def test_missing_old_firmware_and_clean_boot_are_distinguishable(self):
        trace = self.trace()
        for page in ("io_calls", "io_open", "io_header"):
            trace[page] = dict.fromkeys(trace[page], 0)
        trace["summary"]["edge"] = 0
        def statuses():
            io = bench.display_trial_result(trace, 71)["storage_io"]
            return tuple(io[phase]["status"] for phase in ("wav_open", "header_write"))
        self.assertEqual(statuses(), ("unobserved", "unobserved"))
        del trace["io_header"]
        self.assertEqual(statuses(), ("unobserved", "incomplete"))
        del trace["io_open"], trace["io_calls"]
        self.assertEqual(statuses(), ("unsupported", "unsupported"))
        trace = self.trace()
        del trace["io_calls"]
        self.assertEqual(statuses(), ("incomplete", "incomplete"))

    def test_inconsistent_edge_and_impossible_aggregates_are_not_interpreted(self):
        for page, field, bad, phase in (
                ("io_open", "edge", 3209, "wav_open"),
                ("io_header", "edge", 0, "header_write"),
                ("io_calls", "edge", 3211, "header_write"),
                ("io_calls", "posix_open_us", 999, "wav_open"),
                ("io_header", "other_us", 12, "header_write"),
                ("io_header", "command_us", -1, "header_write"),
                ("io_header", "total_us", 4294967296, "header_write"),
                ("io_header", "commands", 0, "header_write"),
                ("io_header", "events", 0, "header_write"),
                ("io_open", "lock_calls", 0, "wav_open"),
                ("io_open", "lock_blocks", 0, "wav_open"),
                ("io_header", "valid", 2, "header_write"),
                ("io_calls", "header_rc", 3, "header_write")):
            with self.subTest(page=page, field=field, bad=bad):
                trace = self.trace()
                trace[page][field] = bad
                result = bench.display_trial_result(trace, 71)["storage_io"][phase]
                self.assertEqual(result["status"], "inconsistent")
                self.assertIsNone(result["dominant"])
        trace = self.trace()
        trace["io_header"]["valid"] = 0
        self.assertEqual(bench.display_trial_result(trace, 71)["storage_io"]["header_write"]["status"], "incomplete")

    def test_partial_trace_preserves_a_verified_short_write_outcome(self):
        trace = self.trace()
        del trace["io_header"]
        trace["io_calls"].update(header_rc=2, header_errno=28)
        phases = bench.display_trial_result(trace, 71)["storage_io"]
        self.assertEqual(phases["wav_open"]["status"], "measured")
        partial = phases["header_write"]
        self.assertEqual((partial["status"], partial.get("outcome"), partial.get("errno")),
                         ("incomplete", "short_or_failed_fwrite", 28))
        self.assertIsNone(partial["dominant"])

    def test_open_failures_and_short_header_keep_their_operation_and_errno(self):
        for code, name in ((2, "posix_open_failed"), (3, "fdopen_failed"), (4, "setvbuf_failed")):
            with self.subTest(code=code):
                trace = self.trace()
                trace["io_calls"].update(open_rc=code, open_errno=5)
                if code != 4:
                    trace["io_header"] = dict.fromkeys(trace["io_header"], 0)
                    trace["io_calls"]["header_rc"] = 0
                io = bench.display_trial_result(trace, 71)["storage_io"]
                self.assertEqual((io["wav_open"]["status"], io["wav_open"]["outcome"],
                                  io["wav_open"]["errno"]), ("operation_failed", name, 5))
                header_status = "incomplete" if code == 4 else "unobserved"
                self.assertEqual(io["header_write"]["status"], header_status)
                self.assertIsNone(io["header_write"]["dominant"])
        trace = self.trace()
        trace["io_calls"].update(header_rc=2, header_errno=28)
        result = bench.display_trial_result(trace, 71)["storage_io"]["header_write"]
        self.assertEqual((result["status"], result["outcome"], result["errno"]),
                         ("operation_failed", "short_or_failed_fwrite", 28))


class Stage2BenchTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.note = (1 << 30) | 1024 | 83  # November 2026
        folder = self.root / "recordings" / "202611"
        folder.mkdir(parents=True)
        self.wav = folder / f"note_{self.note}.wav"
        self.pcm = bytes(range(64))
        with wave.open(str(self.wav), "wb") as wav:
            wav.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
            wav.writeframes(self.pcm)
        self.stamp = "2026-11-30T23:59:59Z"
        self.meta = self.wav.with_suffix(".wav.meta")
        self.meta.write_text("created_utc=" + self.stamp + "\n")
        crc = zlib.crc32(self.pcm)
        self.lines = (f"[RecCRC] e=321 crc={crc:08x}\n"
                      "[RecPCM] edge=321 queued_first=2 captured_frames=32 written_frames=32 overflow=0 saved=1\n")

    def verify(self, lines=None):
        return bench.verify_take(self.root, self.note, self.stamp, 321, io.StringIO(self.lines if lines is None else lines))

    def test_take_checks_month_metadata_and_pcm_from_matching_edge(self):
        self.assertEqual(self.verify()["frames"], 32)
        self.assertEqual(self.verify(self.lines.splitlines()[0] + "\n" +
                                    "[RecCheck] e=321 in=32 out=32 ov=0 ok=1\n")["frames"], 32)
        for bad in ("e=321", "captured_frames=32", "overflow=0", "saved=1"):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                self.verify(self.lines.replace(bad, bad + "9"))
        with self.assertRaises(ValueError):
            self.verify(self.lines + self.lines.replace("captured_frames=32", "captured_frames=30"))
        with self.wav.open("r+b") as out:
            out.seek(-1, 2)
            out.write(b"x")
        with self.assertRaises(ValueError):
            self.verify()

    def test_baseline_checks_original_pcm_text_and_time_allows_new_notes(self):
        txt = self.wav.with_suffix(".txt")
        txt.write_bytes(b"first")
        before = bench.snapshot(self.root)
        self.wav.with_suffix(".delivery").write_text("a" * 64)
        bench.compare(before, bench.snapshot(self.root))
        self.meta.write_text("created_utc=" + self.stamp + "\nsynced=1\n")
        bench.compare(before, bench.snapshot(self.root))
        txt.write_bytes(b"other")
        with self.assertRaises(ValueError):
            bench.compare(before, bench.snapshot(self.root))
        txt.write_bytes(b"first")
        self.meta.write_text("created_utc=\n")
        with self.assertRaises(ValueError):
            bench.compare(before, bench.snapshot(self.root))

    def test_original_wav_must_remain_at_its_canonical_name(self):
        before = bench.snapshot(self.root)
        self.wav.rename(self.wav.with_suffix(".wav.bak"))
        with self.assertRaises(ValueError):
            bench.compare(before, bench.snapshot(self.root))

    def test_truncated_wav_and_missing_metadata_fail(self):
        self.wav.write_bytes(self.wav.read_bytes()[:-2])
        with self.assertRaises(ValueError):
            self.verify()
        self.meta.unlink()
        with self.assertRaises(ValueError):
            self.verify()

    def test_display_timing_uses_only_the_initial_screen_for_the_selected_edge(self):
        line = "[RecDisplay] edge=321 request=350 draw=352 refresh=354 wave=380 wait_end=601 done=606 mode=partial status=idle"
        other = line.replace("edge=321", "edge=654")
        self.assertEqual(bench.event_line((line + "\n").encode()), line)
        result = bench.display_timing([other, line], 321)
        self.assertEqual((result["panel_complete_ms"], result["wave_duration_ms"]), (601, 221))
        for status in ("no_busy", "timeout"):
            result = bench.display_timing([line.replace("idle", status)], 321)
            self.assertNotIn("panel_complete_ms", result)
        self.assertEqual(bench.display_timing([other], 321), {"status": "missing"})
        for invalid in ([line, line], [line.replace("wait_end=601", "wait_end=375")]):
            with self.assertRaises(ValueError):
                bench.display_timing(invalid, 321)

    def test_shared_logger_rejects_embedded_and_mixed_frames(self):
        event = b"[BenchCut] phase=receipt note=1073756228 edge=0 window_ms=10000"
        self.assertEqual(bench.event_line(event + b"\n"), event.decode())
        for raw in (event, b"SSID " + event + b"\n", event + b" mixed\n", b"KUBO_USB_V1 AUTH credential\n"):
            self.assertIsNone(bench.event_line(raw))

    def test_single_port_worker_separates_protocol_from_split_diagnostics(self):
        frames = queue.Queue()
        def read_until(*_):
            try:
                return frames.get(timeout=.02)
            except queue.Empty:
                return b""
        raw = SimpleNamespace(read_until=read_until, write=lambda data: len(data))
        usb = SimpleNamespace(MAX_REPLY=2200, PREFIX=b"KUBO_USB_V1 ")
        output = io.StringIO()
        with redirect_stdout(output), bench.WorkLink(raw, usb) as link:
            frames.put(b"[BenchLink] link=usb ")
            frames.put(b"phase=delivered note=1073756228\n")
            frames.put(b"KUBO_USB_V1 OK\n")
            self.assertEqual(link.read_until(b"\n", 2200), b"KUBO_USB_V1 OK\n")
        self.assertEqual(output.getvalue(), "[BenchLink] link=usb phase=delivered note=1073756228\n")

    def test_provider_readback_requires_expected_account_and_unique_marked_task(self):
        receipt = self.root / "sample.delivery"
        receipt.write_text("a" * 64)
        args = SimpleNamespace(receipt=receipt, account="expected", list_id="list", title="Test",
                               state=self.root)
        sys.path.insert(0, str(bench.BRIDGE))
        with patch("pala_google._api") as connect, patch("google_tasks.read_task_action") as read:
            api = connect.return_value.__enter__.return_value
            current = api.tasklists.return_value.get.return_value.execute
            current.return_value = {"id": "different"}
            with self.assertRaises(ValueError):
                bench.provider(args)
            read.assert_not_called()
            current.return_value = {"id": "expected"}
            read.return_value = {"id": "task", "title": "Test"}
            self.assertEqual(bench.provider(args)["matches"], 1)
            read.assert_called_once_with(api, "list", "work1-" + "a" * 64 + "-0")
            read.side_effect = ValueError("Multiple Google Tasks have the same delivery action.")
            with self.assertRaises(ValueError):
                bench.provider(args)

    def test_retained_pages_retry_cross_revision_reads(self):
        def reply(link, command):
            page = command.rsplit(" ", 1)[1]
            revision = "3" if calls else "2"
            calls.append(page)
            return ["BENCH", "TRACE1", "72", revision, page] + ["0"] * len(bench.TRACE_FIELDS[page])
        calls = []
        result = bench.read_display_trace(None, SimpleNamespace(_exchange=reply))
        self.assertEqual(result["revision"], 3)
        self.assertEqual(result["boot"], 72)
        self.assertEqual(calls.count("summary"), 2)
        with self.assertRaises(ValueError):
            bench.read_display_trace(None, SimpleNamespace(_exchange=lambda *_: ["BENCH", "TRACE1"]))

    def test_display_stall_requires_real_overlap_and_independent_pcm(self):
        trace = {page: dict.fromkeys(fields, 0) for page, fields in bench.TRACE_FIELDS.items()}
        trace.update(boot=72, revision=20)
        trace["summary"].update(software_restart=1, injected_ms=6500, edge=1100, accepted=1420,
                                pcm=1452, inbox=1, note=self.note, frames_in=32, frames_out=32,
                                crc=0x12345678, saved=1)
        trace["home"].update(upload=795, plane=799, wave_full=800, busy=820, idle=1720, done=1860)
        trace["rec"].update(request=1452, ready=1457, upload=1861, wave_partial=1863, busy=1864,
                            idle=2425, plane=2426, done=2427)
        trace["storage"].update(stall_begin=1570, stall_end=8070, first_write=8100)
        wav = {"note": self.note, "frames": 32, "crc": "12345678"}
        run = lambda: bench.display_trial_result(trace, 71, wav)
        self.assertTrue(run()["passed"])
        self.assertIsNone(run()["observed_upstream_intact"])
        trace["audio"] = dict.fromkeys(bench.AUDIO_FIELDS, 0)
        trace["audio"]["observed"] = 1
        self.assertTrue(run()["passed"])
        trace["audio"]["dma_overruns"] = 1
        self.assertTrue(run()["wav_matches_microphone"])
        self.assertFalse(run()["passed"])
        del trace["audio"]
        self.assertIsNone(bench.display_trial_result(trace, 71)["wav_matches_microphone"])
        self.assertFalse(bench.display_trial_result(trace, 72, wav)["passed"])
        self.assertFalse(bench.display_trial_result(trace, 71, dict(wav, note=self.note+1024))["passed"])
        for page, key, bad in (("home", "idle", 1440), ("home", "done", 1900), ("home", "timeout", 1720),
                               ("rec", "ready", 1720), ("rec", "no_busy", 1864), ("rec", "plane", 2000),
                               ("storage", "stall_end", 8069), ("storage", "first_write", 8000),
                               ("summary", "crc", 1), ("summary", "overflow", 1),
                               ("summary", "inbox", 0), ("summary", "frames_out", 31)):
            with self.subTest(page=page, key=key):
                previous = trace[page][key]
                trace[page][key] = bad
                self.assertFalse(run()["passed"])
                trace[page][key] = previous

    def test_upstream_integrity_distinguishes_old_measurement_and_real_rx_loss(self):
        a = dict(observed=1, dma_overruns=0, dma_lost_bytes=0, reconfig_reads=0,
                 read_errors=0, max_read_gap_us=16113, ring_high_blocks=410)
        trace = {k: dict.fromkeys(v, 0) for k,v in bench.TRACE_FIELDS.items()}
        trace["boot"] = 9
        result = lambda: bench.display_trial_result(trace, 8)
        self.assertIsNone(result()["observed_upstream_intact"])
        trace["audio"] = a
        self.assertTrue(result()["observed_upstream_intact"])
        for key, bad in (("observed", 0), ("dma_overruns", 1), ("dma_lost_bytes", 960),
                         ("reconfig_reads", 1), ("read_errors", 1)):
            with self.subTest(key=key):
                good, a[key] = a[key], bad
                self.assertFalse(result()["observed_upstream_intact"])
                a[key] = good

    def test_release_measurement_identifies_actual_saving_wave_and_missing_or_coalesced_frame(self):
        r = dict.fromkeys(bench.RELEASE_FIELDS, 0)
        self.assertEqual(bench.release_feedback(r)["status"], "no_release_observed")
        r.update(button=3200, mic_stop=3214, queued=3215, saving_ready=3218, committed=4100)
        self.assertEqual(bench.release_feedback(r)["status"], "no_activation_observed")
        r["superseded"] = 3220
        self.assertEqual(bench.release_feedback(r)["status"], "superseded_before_activation")
        self.assertIsNone(bench.release_feedback(r)["first_visible_ms"])
        r.update(saved_request=4110, saved_idle=4350)
        self.assertEqual(bench.release_feedback(r)["status"], "superseded_before_activation")
        self.assertEqual(bench.release_feedback(r)["first_visible_screen"], "saved")
        self.assertEqual(bench.release_feedback(r)["first_visible_ms"], 1150)
        r["saving_ready"] = 0
        self.assertEqual(bench.release_feedback(r)["status"], "superseded_before_activation")
        r.update(saving_ready=3218, superseded=0, wave_partial=3700, busy=3705, idle=4260)
        feedback = bench.release_feedback(r)
        self.assertEqual(feedback["status"], "acknowledged")
        self.assertEqual(feedback["from_button_ms"]["mic_stop"], 14)
        self.assertEqual(feedback["from_button_ms"]["idle"], 1060)
        self.assertEqual(feedback["first_visible_screen"], "saving")
        self.assertEqual(feedback["first_visible_ms"], 1060)
        self.assertEqual(feedback["from_button_ms"]["committed"], 900)
        r["idle"] = 3600
        self.assertEqual(bench.release_feedback(r)["status"], "inconsistent")
        r.update(idle=0, timeout=8705)
        self.assertEqual(bench.release_feedback(r)["status"], "panel_failed")
        r.update(timeout=0, wave_full=3699)
        self.assertEqual(bench.release_feedback(r)["status"], "inconsistent")
        wrapped = dict.fromkeys(bench.RELEASE_FIELDS, 0)
        wrapped.update(button=0xfffffff0, queued=0xfffffff5, saving_ready=0xfffffff9,
                       wave_partial=4, busy=5, idle=14)
        self.assertEqual(bench.release_feedback(wrapped)["from_button_ms"]["idle"], 30)
        self.assertEqual(bench.release_feedback(wrapped)["status"], "acknowledged")

    def test_inbox_can_show_saved_before_catalog_admission(self):
        trace = {k: dict.fromkeys(fields, 0) for k, fields in bench.TRACE_FIELDS.items()}
        trace["boot"] = 2
        trace["summary"].update(inbox=1, saved=1, note=self.note)
        trace["storage"]["first_write"] = 12278
        release = trace["release"] = dict.fromkeys(bench.RELEASE_FIELDS, 0)
        release.update(button=10774, mic_stop=10783, queued=10783, saving_ready=10784,
                       wave_partial=10785, busy=10785, idle=11358,
                       saved_request=13407, saved_idle=13984, committed=38634)
        def feedback():
            return bench.display_trial_result(trace, 1)["release_feedback"]

        observed = feedback()
        self.assertEqual(observed["status"], "acknowledged")
        self.assertEqual((observed["first_visible_screen"], observed["first_visible_ms"]), ("saving", 584))
        self.assertEqual(observed["from_button_ms"]["committed"], 27860)

        for field in ("inbox", "saved", "note"):
            with self.subTest(field=field):
                previous = trace["summary"][field]
                trace["summary"][field] = 0
                self.assertEqual(feedback()["status"], "inconsistent")
                trace["summary"][field] = previous
        release["saved_request"] = 12000  # The first PCM write was still delayed.
        self.assertEqual(feedback()["status"], "inconsistent")
        trace["storage"]["first_write"] = 10600
        release["saved_request"] = 10775  # Audio was still being captured.
        self.assertEqual(feedback()["status"], "inconsistent")

    def test_only_numeric_restart_prompt_fields_are_logged(self):
        row = (b"[RestartPrompt] prev=1 ticket=3 mode=2 attempt=9 seen=1 draw=10 ok=1 "
               b"ack=42 returned=44 armed=43 held=0 choice=1 at=100")
        self.assertEqual(bench.event_line(row + b"\n"), row.decode())
        self.assertIsNone(bench.event_line(row.replace(b"at=100", b"at=100 permission=secret") + b"\n"))
        self.assertIsNone(bench.event_line(row.replace(b"held=0", b"held=untrusted") + b"\n"))

    def test_optional_audio_page_belongs_to_same_boot_and_revision(self):
        calls = []
        def exchange(link, cmd):
            page = cmd.split()[-1]
            calls.append(page)
            fields = dict(bench.TRACE_FIELDS, audio=bench.AUDIO_FIELDS, release=bench.RELEASE_FIELDS)
            size = len(fields[page])
            revision = 2 if "audio" in calls else 1
            return ["BENCH", "TRACE1", "9", str(revision), page] + ["0"] * size
        usb = SimpleNamespace(_exchange=exchange)
        measured = bench.read_display_trace(None, usb, include_audio=True)
        self.assertEqual(measured["revision"], 2)
        self.assertEqual(measured["audio"], dict.fromkeys(bench.AUDIO_FIELDS, 0))
        self.assertEqual(measured["release"], dict.fromkeys(bench.RELEASE_FIELDS, 0))
        self.assertEqual(calls.count("summary"), 2)
        def short_release(link, cmd):
            row = exchange(link, cmd)
            return row[:-1] if cmd.endswith(" release") else row
        with self.assertRaises(ValueError):
            bench.read_display_trace(None, SimpleNamespace(_exchange=short_release), include_audio=True)
        calls.clear()
        self.assertNotIn("audio", bench.read_display_trace(None, usb))
        self.assertNotIn("audio", calls)

    def test_sdtrace_identification_preserves_previous_qualification_builds(self):
        storage = "2.0.0-recorder-sdtrace1-stall6500"
        response = ["BENCH", bench.DEV, storage]
        bench.check_target(response)
        for build in (bench.BUILD, bench.AUDIO_BUILD, bench.PREVIOUS_BUILD):
            bench.check_target(["BENCH", bench.DEV, build])
            with self.assertRaises(ValueError):
                bench.check_target(["BENCH", bench.DEV, build], storage)
        with self.assertRaises(ValueError):
            bench.check_target(["BENCH", "123456789abc", storage])

    def test_controller_checks_both_device_identity_and_build(self):
        bench.check_target(["BENCH", bench.DEV, bench.BUILD])
        for response in (["OK"], ["BENCH", "000000000000", bench.BUILD], ["BENCH", bench.DEV, "old"]):
            with self.assertRaises(ValueError):
                bench.check_target(response)


class UsbStorageCommandTest(unittest.TestCase):
    build = "2.0.0-recorder-msc1"
    actions = ("usb-storage", "usb-storage-next", "usb-storage-cancel")

    def setUp(self):
        from contextlib import nullcontext
        from unittest.mock import Mock

        self.runtime = ["BENCH", bench.DEV, self.build]
        self.commands = []
        self.ack_error = None

        class ProtocolError(RuntimeError):
            pass

        class ReplyTimeout(ProtocolError):
            pass

        def exchange(link, command):
            self.commands.append(command)
            if command == "BENCH status":
                return self.runtime
            if command == "CLOCK":
                return ["CLOCK", "100", "100"]
            raise AssertionError(command)

        def expect_ok(link, command):
            self.commands.append(command)
            if command != "CLOSE" and self.ack_error:
                raise self.ack_error

        self.usb = SimpleNamespace(
            paired_port=Mock(return_value=(bench.DEV, "test-token")),
            _open_link=lambda port, peer: nullcontext("simulated link"),
            _paired_session=Mock(return_value=("test-token", "host", "peer")),
            _exchange=exchange, _expect_ok=expect_ok, UsbSyncError=ProtocolError,
            UsbReplyTimeout=ReplyTimeout,
        )
        record = {"id": "paired-peer", "token": "test-token"}
        store = Mock()
        store.return_value.devices.return_value = {bench.DEV: record}
        self.enterContext(patch.dict(sys.modules, {
            "usb_sync": self.usb,
            "serial.tools.list_ports": SimpleNamespace(comports=lambda: []),
            "note_settings": SimpleNamespace(EnrollmentStore=store),
            "pair": SimpleNamespace(STATE=Path(".")),
        }))

    def invoke(self, action):
        import json
        self.commands.clear()
        output = io.StringIO()
        with patch.object(sys, "argv", ["stage2_bench.py", action, "--port", "simulated"]), redirect_stdout(output):
            bench.main()
        return json.loads(output.getvalue())

    def test_cli_authenticates_and_sends_one_request_for_each_action(self):
        for op in self.actions:
            with self.subTest(op=op):
                self.assertEqual(self.invoke(op), {"applied": op, "serial": bench.DEV})
                self.usb._paired_session.assert_called_with("simulated link", ("test-token",))
                expected = ["BENCH status", "BENCH " + op]
                self.assertEqual(self.commands, expected if op == "usb-storage" else expected + ["CLOSE"])

    def test_storage_commands_reject_other_builds_and_devices_before_sending(self):
        for op in self.actions:
            for serial, build in ((bench.DEV, bench.OVERLAP_BUILD), ("123456789abc", self.build)):
                with self.subTest(op=op, serial=serial, build=build):
                    self.runtime = ["BENCH", serial, build]
                    with self.assertRaisesRegex(ValueError, "Qualification build"):
                        self.invoke(op)
                    self.assertNotIn("BENCH " + op, self.commands)

        self.usb.paired_port.return_value = ("123456789abc", "test-token")
        with self.assertRaisesRegex(ValueError, "USB identity mismatch"):
            self.invoke("usb-storage")
        self.assertEqual(self.commands, [])

    def test_lost_restart_reply_releases_port_after_one_request(self):
        for error in (self.usb.UsbReplyTimeout("timeout"), OSError("disconnected")):
            with self.subTest(error=error):
                self.ack_error = error
                with self.assertRaisesRegex(RuntimeError, "enumeration"):
                    self.invoke("usb-storage")
                self.assertEqual(self.commands, ["BENCH status", "BENCH usb-storage"])

    def test_busy_is_a_firmware_rejection_and_leaves_serial_session_closed(self):
        self.ack_error = self.usb.UsbSyncError("BUSY")
        with self.assertRaisesRegex(self.usb.UsbSyncError, "BUSY"):
            self.invoke("usb-storage")
        self.assertEqual(self.commands, ["BENCH status", "BENCH usb-storage", "CLOSE"])

    def test_msc_build_retains_overlap_storage_and_common_controls(self):
        with patch.object(bench, "read_owner_history", return_value={}) as owner:
            self.invoke("owner-trace")
            owner.assert_called_once_with("simulated link", self.usb, include_overlap=True)
        with patch.object(bench, "read_display_trace", return_value={}) as display:
            self.invoke("display-trace")
            display.assert_called_once_with("simulated link", self.usb, include_audio=True, include_storage_io=True)
        self.assertEqual(self.invoke("clock")["rtc"], 100)
