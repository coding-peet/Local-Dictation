import hashlib
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import stage2_bench as bench
import stage2_owner as owner


class HistoryWire:
    def __init__(self, edges=(2348, 19000), seen=None):
        self.edges = edges
        self.seen = len(edges) if seen is None else seen
        self.calls = []
        self.change = None
        self.stamp = [19, 700]

    def _exchange(self, link, command):
        name = command.split()[-1]
        self.calls.append(name)
        if name == "history":
            values = [self.seen, *(self.edges + (0, 0))[:2], 6500]
        elif name == "memory":
            values = [2, 256, 1024, 524288, 532480, 8388608, 7000000]
        elif name == "image":
            values = range(8)
        elif name == "home":
            values = [0] * len(bench.SCREEN_FIELDS)
        else:
            slot, page = name.split("_", 1)
            edge = self.edges[("a", "b").index(slot)]
            fields = {**bench.TRACE_FIELDS, "audio": bench.AUDIO_FIELDS, "release": bench.RELEASE_FIELDS,
                      **bench.STORAGE_IO_PAGES, **owner.OWNER_PAGES, "overlap": bench.OVERLAP_FIELDS}[page]
            row = dict.fromkeys(fields, 0)
            if "edge" in fields:
                row["edge"] = edge
            if page == "summary":
                row.update(injected_ms=6500, crc=edge * 73)
            values = row.values()
        result = ["BENCH", "TRACE1", *map(str, self.stamp), name, *map(str, values)]
        if self.change:
            self.change(name, result)
        return result


class PinnedHistoryTest(unittest.TestCase):
    def test_first_two_pinned_and_empty_baseline(self):
        for edges, seen in (((), 0), ((2348,), 1), ((2348, 19000), 2), ((2348, 19000), 4)):
            with self.subTest(seen=seen):
                wire = HistoryWire(edges, seen)
                h = bench.read_owner_history(None, wire)
                self.assertEqual(h["seen"], seen)
                self.assertEqual((h["boot"], h["revision"], h["injected_ms"]), (19, 700, 6500))
                self.assertEqual([t["summary"]["edge"] for t in h["takes"]], list(edges))
                self.assertEqual([t["summary"]["crc"] for t in h["takes"]], [e * 73 for e in edges])
                self.assertEqual(h["elf_sha256"], "".join(f"{i:08x}" for i in range(8)))
                self.assertTrue(all(t["elf_sha256"] == h["elf_sha256"] for t in h["takes"]))
                self.assertEqual(wire.calls[0], wire.calls[-1], "index bookends")
                self.assertEqual(wire.calls.count("home"), 1)
                self.assertEqual("b_own_open" in wire.calls, len(edges) == 2)

    def test_pcm_during_previous_save_and_physical_indicator(self):
        h = bench.read_owner_history(None, HistoryWire(), include_overlap=True)
        self.assertEqual(h["memory"]["pcm_bytes_per_slot"], 524288)
        a, b = h["takes"]
        a["release"]["button"] = 18000
        b["summary"].update(accepted=19323, pcm=19344)
        p = b["overlap"]
        p.update(prior_edge=2348, pending_at=19323, finished_at=21000, finish_ok=1,
                 visible_on=19900, visible_off=21590)
        out = bench.overlap_trial_result(h)
        self.assertEqual(out["hold_qualified_to_first_pcm_ms"], 24)
        self.assertEqual(out["second_pcm_lead_ms"], 1656)
        self.assertEqual(out["finished_to_indicator_off_ms"], 590)
        p.update(visible_on=0, visible_off=0)
        self.assertFalse(bench.overlap_trial_result(h)["indicator_off_observed"])
        b["summary"]["pcm"] = 21100
        self.assertFalse(bench.overlap_trial_result(h)["second_first_pcm_before_previous_save_end"])
        p["prior_edge"] = 99
        with self.assertRaises(ValueError):
            bench.overlap_trial_result(h)

    def test_missing_overlap_evidence(self):
        old = bench.read_owner_history(None, HistoryWire())
        self.assertEqual(bench.overlap_trial_result(old)["status"], "unsupported")
        partial = bench.read_owner_history(None, HistoryWire((2348,)), include_overlap=True)
        self.assertEqual(bench.overlap_trial_result(partial)["status"], "waiting_for_two_takes")

    def test_restarts_whole_history_after_boot_or_revision_change(self):
        for column in (2, 3):
            wire = HistoryWire()
            def once(name, row):
                if name == "b_own_header1":
                    wire.stamp[column - 2] += 1
                    row[column] = str(wire.stamp[column - 2])
                    wire.change = None
            wire.change = once
            h = bench.read_owner_history(None, wire)
            self.assertEqual(wire.calls.count("a_summary"), 2)
            self.assertTrue(all((t["boot"], t["revision"]) == tuple(wire.stamp) for t in h["takes"]))

    def test_rejects_invalid_indices_edges_and_unstable_snapshot(self):
        for field in ("index", "stale", "malformed", "forever", "closing"):
            wire = HistoryWire()
            def bad(name, row):
                if field == "index" and name == "history":
                    row[7] = row[6]  # duplicate first/second edge
                if field == "stale" and name == "b_own_open":
                    row[5] = str(wire.edges[0])
                if field == "malformed" and name == "b_own_header0":
                    row[-1] = "4294967296"
                if field == "forever" and name == "a_rec":
                    row[2] = "99"
                if field == "closing" and name == "history" and len(wire.calls) > 1:
                    row[5] = "4"
            wire.change = bad
            with self.subTest(field=field), self.assertRaises(ValueError):
                bench.read_owner_history(None, wire)


class OwnerResultTest(unittest.TestCase):
    def setUp(self):
        self.trace = bench.read_owner_history(None, HistoryWire((2348,)))["takes"][0]
        t = self.trace
        t["io_open"].update(valid=1, vfs_lock_us=1800, total_us=1830, lock_blocks=3)
        t["io_header"].update(valid=1)
        t["own_open"].update(waits=3, omitted=1)
        for i, duration in enumerate((1000, 500)):
            t["own_open" + str(i)].update(
                mutex=0x3fc94200, holder=0x3fc93400, waiter_pc=0x820734ad, fat_pc=0x420920bf,
                work=18, fat_op=15, opcode=17, wait_us=duration, held_us=duration - 7,
                host_us=duration - 12, event_us=duration - 15, delay_us=10,
                flags=1 | 2 | 4 | 8 | 16 | 32 | 2048, name0=int.from_bytes(b"loop", "big"))

    @property
    def opening(self):
        return owner.storage_owner_result(self.trace)["wav_open"]

    def test_reports_observed_holder_and_overlapping_intervals(self):
        r = self.opening
        wait = r["longest_waits"][0]
        self.assertEqual((r["status"], r["wait_count"], r["omitted_shorter_waits"]), ("observed", 3, 1))
        self.assertEqual((wait["task"], wait["work"], wait["file_operation"], wait["sd_opcode"]),
                         ("loop", "free space", "f_getfree", 17))
        self.assertEqual((wait["block_to_give_us"], wait["holder_sd_host_us"], wait["holder_sd_event_us"],
                          wait["holder_task_delay_us"], wait["give_to_waiter_return_or_reblock_us"]),
                         (993, 988, 985, 10, 7))
        self.assertEqual(owner.storage_owner_result(self.trace)["header_write"]["status"], "no_vfs_mutex_block_observed")

    def test_incomplete_and_unconfirmed_hold_retain_explicit_uncertainty(self):
        w = self.trace["own_open0"]
        w.update(flags=1 | 512, held_us=0, host_us=0, event_us=0, delay_us=0,
                 opcode=0xffffffff, work=0, fat_op=0, fat_pc=0)
        wait = self.opening["longest_waits"][0]
        self.assertEqual((wait["release"], wait["task"], wait["work"], wait["file_operation"]),
                         ("ambiguous", "loop", "unknown", "unknown"))
        self.assertIsNone(wait["holder_sd_host_us"])
        self.assertIsNone(wait["sd_opcode"])
        self.trace["io_header"]["vfs_lock_us"] = 10
        self.assertEqual(owner.storage_owner_result(self.trace)["header_write"]["status"],
                         "writer_vfs_wait_without_owner_observation")
        self.trace["io_header"]["valid"] = 0
        self.assertEqual(owner.storage_owner_result(self.trace)["header_write"]["status"], "incomplete")

    def test_inconsistent_durations_slots_and_counts_never_prove_owner(self):
        for page, key, value in (("own_open", "omitted", 3), ("own_open1", "edge", 1),
                                 ("own_open0", "flags", 1 | 512), ("own_open0", "host_us", 3000),
                                 ("own_open0", "name0", 0x1b315a31), ("own_open1", "wait_us", 2000)):
            initial = self.trace[page][key]
            self.trace[page][key] = value
            with self.subTest(page=page, key=key):
                self.assertEqual(self.opening["status"], "inconsistent")
            self.trace[page][key] = initial

    def test_symbolizer_matches_runtime_elf_and_normalizes_both_return_pcs(self):
        r = owner.storage_owner_result(self.trace)
        with tempfile.TemporaryDirectory() as folder:
            elf = Path(folder) / "app.elf"
            elf.write_bytes(b"fixture")
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                owner.symbolize_owner_result(r, elf, "addr2line")
            r["elf_sha256"] = hashlib.sha256(elf.read_bytes()).hexdigest()
            with patch.object(owner.subprocess, "run", return_value=SimpleNamespace(
                    stdout="wait_function\nlocks.c:207\nfile_op\nvfs.c:413\n")) as run:
                result = owner.symbolize_owner_result(r, elf, "addr2line")
            self.assertEqual(run.call_args.args[0][-2:], ["0x420734ac", "0x420920be"])
            wait = result["wav_open"]["longest_waits"][0]
            self.assertEqual(wait["waiter_pc_symbol"]["function"], "wait_function")
            self.assertEqual(wait["file_caller_pc_symbol"]["source"], "vfs.c:413")
            self.assertNotIn("waiter_pc_symbol", r["wav_open"]["longest_waits"][0])
