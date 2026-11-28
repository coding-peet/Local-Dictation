"""Stage 2 bench control for the second Note and offline SD evidence."""
import argparse
from contextlib import suppress
from datetime import datetime, timezone
import hashlib
import hmac
import json
from pathlib import Path
import re
import queue
import threading
import struct
import sys
import time
import zlib

from stage2_owner import OWNER_BUILD, OWNER_PAGES, storage_owner_result, symbolize_owner_result

DEV = "70041dd816a8"
BUILD = "2.0.0-recorder-display3-stall6500"
OVERLAP_BUILD = "2.0.0-recorder-overlap1-stall6500"
AUDIO_BUILD = "2.0.0-recorder-periph1-stall6500"
STORAGE_BUILD = "2.0.0-recorder-sdtrace1-stall6500"
PREVIOUS_BUILD = "2.0.0-recorder-stage2-qual2"
MSC_BUILD = "2.0.0-recorder-msc1"
MSC_BUILDS = (MSC_BUILD, "2.0.0-recorder-msc2", "2.0.0-recorder-msc3", "2.0.0-recorder-msc4-ble-paced")
OVERLAP_BUILDS = (OVERLAP_BUILD, *MSC_BUILDS)
OWNER_BUILDS = (OWNER_BUILD, *OVERLAP_BUILDS)
STORAGE_BUILDS = (STORAGE_BUILD, *OWNER_BUILDS)
AUDIO_BUILDS = (AUDIO_BUILD, *STORAGE_BUILDS)
DISPLAY_BUILDS = (BUILD, *AUDIO_BUILDS)
USB_STORAGE_ACTIONS = ("usb-storage", "usb-storage-next", "usb-storage-cancel")
BRIDGE = Path(__file__).resolve().parents[1] / "mac-bridge"
POINTS = ("open", "pcm", "publish", "text", "receipt", "lose-receipt", "off")
CHECK = re.compile(r"\[RecPCM\] edge=(\d+) queued_first=\d+ captured_frames=(\d+) written_frames=(\d+) overflow=(\d+) saved=(\d+)(?:\s|$)")
SHORT_CHECK = re.compile(r"\[RecCheck\] e=(\d+) in=(\d+) out=(\d+) ov=(\d+) ok=(\d+)(?:\s|$)")
CRC = re.compile(r"\[RecCRC\] e=(\d+) crc=([a-fA-F0-9]{8})(?:\s|$)")
DISPLAY = re.compile(
    r"\[RecDisplay\] edge=(\d+) request=(\d+) draw=(\d+) refresh=(\d+) wave=(\d+) "
    r"wait_end=(\d+) done=(\d+) mode=(full|partial|none) status=(idle|no_busy|timeout|missing)"
)
TRACE_FORMATS = (CHECK, SHORT_CHECK, CRC, DISPLAY, *(re.compile(form) for form in (
    r"\[RestartPrompt\] prev=[01] ticket=[0-9]+ mode=[012] attempt=[0-9]+ seen=[0-9]+ draw=[0-9]+ "
    r"ok=[01] ack=[0-9]+ returned=[0-9]+ armed=[0-9]+ held=[0-9]+ choice=[012] at=[0-9]+",
    r"\[RecStall\] e=\d+ ms=\d+ blocks=\d+",
    r"\[RecBoot\] edge=\d+ (?:software_reset=[01]|stage=[a-z_]{1,24} boot_ms=\d+)",
    r"\[RecTiming\] edge=\d+ (?:stage=[a-z_]{1,24} ms=\d+ elapsed=\d+|stages=\d+ end)",
    r"\[Rec\] (?:edge=\d+ first_pcm=\d+ latency_ms=\d+|first_write=\d+ write_latency_ms=\d+|done: \d+ bytes, \d+ moments)",
    r"\[Rec\] (?:archived )?(?:/sdcard)?/(?:recordings/\d{6}/note_\d+\.wav|\.rec-inbox/s\d+_[a-f0-9]{8}\.partial)",
    r"\[BenchCut\] phase=(?:open|pcm|publish|text|receipt) (?:note=-?\d+ edge=\d+ window_ms=10000|expired)",
    r"\[BenchCut\] phase=lose-receipt note=\d+",
    r"\[BenchLink\] link=(?:usb|ble|wifi) phase=(?:write|reconcile|confirmed|delivered|transcribed) note=\d+",
)))


def event_line(raw):
    if not raw.endswith(b"\n") or len(raw) > 240:
        return None
    line = raw.rstrip(b"\r\n").decode("ascii", errors="replace")
    return line if any(pattern.fullmatch(line) for pattern in TRACE_FORMATS) else None


class WorkLink:
    """Read the single USB port throughout ASR, delivering protocol frames only to its worker."""
    def __init__(self, link, usb):
        self.link, self.usb = link, usb
        self.frames = queue.Queue(maxsize=32)
        self.stop = threading.Event()
        self.reader = threading.Thread(target=self.pump, daemon=True)
        self.error = None

    def __enter__(self):
        self.reader.start()
        return self

    def __exit__(self, *_):
        self.stop.set()
        self.reader.join()

    def pump(self):
        try:
            line = bytearray()
            discard = False
            while not self.stop.is_set():
                chunk = self.link.read_until(b"\n", self.usb.MAX_REPLY)
                if not chunk:
                    continue
                if not discard:
                    line.extend(chunk)
                    if len(line) > self.usb.MAX_REPLY:
                        discard = True
                        line.clear()
                if not chunk.endswith(b"\n"):
                    continue
                if not discard:
                    if line.startswith(self.usb.PREFIX):
                        self.frames.put_nowait(bytes(line))
                    else:
                        event = event_line(line)
                        if event:
                            print(event, flush=True)
                discard = False
                line.clear()
        except (OSError, queue.Full) as error:
            self.error = error

    def read_until(self, terminator, limit):
        if self.error is not None:
            raise OSError("USB bench reader interrupted.") from self.error
        try:
            frame = self.frames.get(timeout=.2)
        except queue.Empty:
            return b""
        if len(frame) > limit:
            raise ValueError("Oversized USB protocol frame.")
        return frame

    def write(self, data):
        return self.link.write(data)


def display_timing(lines, edge):
    """Decode the initial REC screen for one physical GPIO edge."""
    matches = [m for line in lines if (m := DISPLAY.fullmatch(line.rstrip("\r\n"))) and int(m[1]) == edge]
    if not matches:
        return {"status": "missing"}
    if len(matches) != 1:
        raise ValueError("Multiple initial REC screens for the same GPIO edge.")
    match = matches[0]
    times = list(map(int, match.groups()[1:7]))
    mode, status = match[8], match[9]
    if status != "missing" and (mode == "none" or times != sorted(times)):
        raise ValueError("Inconsistent REC screen timing.")
    keys = ("request_ms", "draw_ms", "refresh_ms", "wave_ms", "wait_end_ms", "done_ms")
    result = dict(zip(keys, times), mode=mode, status=status)
    if status == "idle":
        result["panel_complete_ms"] = result["wait_end_ms"]
        result["wave_duration_ms"] = result["wait_end_ms"] - result["wave_ms"]
    return result


SCREEN_FIELDS = ("request", "draw", "ready", "claim", "upload", "wave_full", "wave_partial",
                 "busy", "idle", "timeout", "no_busy", "plane", "done", "cache", "fallback",
                 "setup_done")
TRACE_FIELDS = {
    "summary": ("software_restart", "injected_ms", "edge", "accepted", "pcm", "inbox",
                "note", "frames_in", "frames_out", "crc", "overflow", "saved"),
    "home": SCREEN_FIELDS,
    "rec": SCREEN_FIELDS,
    "storage": ("dequeue", "ready", "stat_wav", "recover", "allocate", "directory", "path_end",
                "serial", "open", "reserve", "seek", "header_write", "flush", "fsync",
                "stall_begin", "stall_end", "first_write", "first_write_error"),
}


AUDIO_FIELDS = ("observed", "dma_overruns", "dma_lost_bytes", "reconfig_reads",
                "read_errors", "max_read_gap_us", "ring_high_blocks")


RELEASE_FIELDS = ("button", "mic_stop", "queued", "saving_ready", "wave_full", "wave_partial",
                  "busy", "idle", "timeout", "no_busy", "committed", "superseded",
                  "saved_request", "saved_idle")


# The sdtrace1 wire format uses microseconds for all writer intervals. Subcall
# durations partition the open operation differently from the lower-layer buckets.
IO_BUCKETS = {"fat_lock_us": "fatfs_lock_block", "vfs_lock_us": "vfs_lock_block",
              "host_lock_us": "sdmmc_host_lock_block", "command_us": "sdmmc_command",
              "event_us": "sdmmc_event_queue_block", "other_us": "unresolved"}
IO_COUNTS = ("lock_calls", "lock_blocks", "commands", "events")
IO_PHASE_FIELDS = ("edge", "total_us", *IO_BUCKETS, *IO_COUNTS, "valid")
OPEN_SUBCALLS = ("path_us", "posix_open_us", "fdopen_us", "setvbuf_us", "close_us")
STORAGE_IO_PAGES = {
    "io_open": IO_PHASE_FIELDS,
    "io_header": IO_PHASE_FIELDS,
    "io_calls": ("edge", *OPEN_SUBCALLS, "open_rc", "open_errno", "header_rc", "header_errno"),
}
OPEN_OUTCOMES = ("unobserved", "success", "posix_open_failed", "fdopen_failed", "setvbuf_failed")
HEADER_OUTCOMES = ("unobserved", "success", "short_or_failed_fwrite")


def _read_trace_page(link, usb, page, fields):
    row = usb._exchange(link, "BENCH trace " + page)
    if (len(row) != 5 + len(fields) or row[:2] != ["BENCH", "TRACE1"] or row[4] != page
            or any(not re.fullmatch(r"[0-9]{1,10}", n) or int(n) > 0xffffffff
                   for n in row[2:4] + row[5:])):
        raise ValueError("Malformed retained display measurement: " + page)
    return tuple(map(int, row[2:4])), dict(zip(fields, map(int, row[5:])))


def read_display_trace(link, usb, include_audio=False, include_storage_io=False):
    """All requested pages must come from one boot and one unchanged measurement revision."""
    pages = dict(TRACE_FIELDS)
    if include_audio:
        pages.update(audio=AUDIO_FIELDS, release=RELEASE_FIELDS)
    if include_storage_io:
        pages.update(STORAGE_IO_PAGES)
    for _ in range(4):
        result = {}
        version = None
        for page, fields in pages.items():
            stamp, result[page] = _read_trace_page(link, usb, page, fields)
            if version is not None and version != stamp:
                break
            version = stamp
        else:
            result["boot"], result["revision"] = version
            return result
    raise ValueError("Display measurement changed during all readback attempts.")


class _TraceChanged(Exception):
    pass


MEMORY_FIELDS = ("slots", "frames_per_block", "blocks_per_slot", "pcm_bytes_per_slot",
                 "allocated_bytes_per_slot", "psram_total_bytes", "psram_free_bytes")
OVERLAP_FIELDS = ("edge", "prior_edge", "pending_at", "finished_at", "finish_ok", "visible_on", "visible_off")


def read_owner_history(link, usb, include_overlap=False):
    """Read the first two accepted edges of one boot as a single revision, including late archive IDs."""
    history_fields = ("seen", "first", "second", "injected_ms")
    take_pages = {name: fields for name, fields in TRACE_FIELDS.items() if name != "home"}
    take_pages.update(audio=AUDIO_FIELDS, release=RELEASE_FIELDS)
    take_pages.update(STORAGE_IO_PAGES)
    take_pages.update(OWNER_PAGES)
    if include_overlap:
        take_pages["overlap"] = OVERLAP_FIELDS
    for _ in range(4):
        stamp, index = _read_trace_page(link, usb, "history", history_fields)

        def same_revision(page, fields):
            current, row = _read_trace_page(link, usb, page, fields)
            if current != stamp:
                raise _TraceChanged
            return row

        try:
            image = same_revision("image", range(8))
            elf = "".join(f"{part:08x}" for part in image.values())
            home = same_revision("home", TRACE_FIELDS["home"])
            memory = same_revision("memory", MEMORY_FIELDS) if include_overlap else None
            seen = index["seen"]
            edges = (index["first"], index["second"])
            count = min(seen, 2)
            if not all(edges[:count]) or any(edges[count:]) or len(set(edges[:count])) != count:
                raise ValueError("Invalid pinned REC edges in history index.")
            takes = []
            for slot, edge in zip(("a_", "b_"), edges[:count]):
                take = {name: same_revision(slot + name, fields) for name, fields in take_pages.items()}
                if (take["summary"]["edge"] != edge or take["summary"]["injected_ms"] != index["injected_ms"]
                        or any(row["edge"] not in (0, edge) for row in take.values() if "edge" in row)):
                    raise ValueError("Pinned trace page does not match its REC edge or configuration.")
                take.update(home=home, boot=stamp[0], revision=stamp[1], elf_sha256=elf)
                takes.append(take)
            if same_revision("history", history_fields) != index:
                raise ValueError("Pinned REC index changed without a measurement revision.")
            return dict(boot=stamp[0], revision=stamp[1], seen=seen, injected_ms=index["injected_ms"],
                        elf_sha256=elf, home=home, takes=takes, **({"memory": memory} if include_overlap else {}))
        except _TraceChanged:
            continue
    raise ValueError("Pinned recordings changed during all readback attempts.")


def overlap_trial_result(history):
    """Measure whether the second take really captured PCM before the first save finished."""
    if len(history["takes"]) < 2:
        return {"status": "waiting_for_two_takes"}
    first, second = history["takes"][:2]
    if "overlap" not in second:
        return {"status": "unsupported"}
    previous = first["summary"]["edge"]
    s, p = second["summary"], second["overlap"]
    edge = s["edge"]
    if (first["boot"] != history["boot"] or second["boot"] != history["boot"]
            or p["edge"] != edge or edge == previous or p["prior_edge"] not in (0, previous)):
        raise ValueError("Overlap evidence belongs to a different boot or REC edge.")

    def elapsed(start, end):
        dt = (end - start) & 0xffffffff
        return dt if start and end and dt < 0x80000000 else None

    qualified = (edge + 320) & 0xffffffff
    active = p["prior_edge"] == previous and bool(p["pending_at"])
    finished = bool(p["finished_at"])
    before = elapsed(s["pcm"], p["finished_at"])
    if (p["finish_ok"] not in (0, 1) or (p["finish_ok"] and not finished)
            or (finished and (not active or elapsed(p["pending_at"], p["finished_at"]) is None))
            or (p["visible_on"] and (not active or elapsed(p["pending_at"], p["visible_on"]) is None))
            or (p["visible_off"] and (not p["visible_on"] or not finished
                                      or elapsed(p["visible_on"], p["visible_off"]) is None
                                      or elapsed(p["finished_at"], p["visible_off"]) is None))):
        raise ValueError("Inconsistent previous-save timestamps.")
    proven = None if not (finished and s["pcm"]) else before is not None and before > 0
    return {
        "status": "overlap" if active else "no_previous_save_at_acceptance",
        "first_edge": previous, "second_edge": edge,
        "first_release_to_second_press_ms": elapsed(first["release"]["button"], edge),
        "second_edge_to_accepted_ms": elapsed(edge, s["accepted"]),
        "second_edge_to_first_pcm_ms": elapsed(edge, s["pcm"]),
        "hold_qualified_to_accepted_ms": elapsed(qualified, s["accepted"]),
        "hold_qualified_to_first_pcm_ms": elapsed(qualified, s["pcm"]),
        "second_first_pcm_before_previous_save_end": proven,
        "second_pcm_lead_ms": before if proven else None,
        "previous_save_succeeded": bool(p["finish_ok"]) if finished else None,
        "pending_to_visible_ms": elapsed(p["pending_at"], p["visible_on"]),
        "finished_to_indicator_off_ms": elapsed(p["finished_at"], p["visible_off"]),
        "indicator_on_observed": bool(p["visible_on"]), "indicator_off_observed": bool(p["visible_off"]),
    }


def release_feedback(release, *, archived_inbox=False):
    """Report only events belonging to the retained take and its Saving frame."""
    button = release["button"]
    times = {k: (v - button) & 0xffffffff if button and v else None
             for k, v in release.items() if k != "button"}
    wave = times["wave_full"] if times["wave_full"] is not None else times["wave_partial"]
    ready, busy, idle = times["saving_ready"], times["busy"], times["idle"]
    coalesced = times["superseded"]
    saved_request, saved_idle = times["saved_request"], times["saved_idle"]
    # A finalized startup-inbox WAV can be acknowledged before catalog replay
    # assigns its note number. "committed" timestamps that later admission.
    catalogued, mic_stop = times["committed"], times["mic_stop"]
    inconsistent = (any(v is not None and v >= 0x80000000 for v in times.values())
                    or bool(release["wave_full"] and release["wave_partial"])
                    or (times["queued"] is not None and ready is not None and ready < times["queued"])
                    or (coalesced is not None and (wave is not None or times["queued"] is None
                                                   or coalesced < times["queued"]
                                                   or (ready is not None and coalesced < ready)))
                    or (wave is not None and (ready is None or wave < ready))
                    or (busy is not None and (wave is None or busy < wave))
                    or (idle is not None and (busy is None or idle < busy))
                    or (saved_idle is not None and (saved_request is None or saved_idle < saved_request))
                    or (mic_stop is not None and saved_request is not None and saved_request < mic_stop)
                    or (catalogued is not None and saved_request is not None
                        and not archived_inbox and saved_request < catalogued)
                    or (catalogued is not None and mic_stop is not None and catalogued < mic_stop))
    if not button:
        status = "no_release_observed"
    elif inconsistent:
        status = "inconsistent"
    elif release["timeout"] or release["no_busy"]:
        status = "panel_failed"
    elif coalesced is not None:
        status = "superseded_before_activation"
    elif wave is None:
        status = "no_activation_observed"
    elif idle is None:
        status = "activation_observed"
    else:
        status = "acknowledged"
    confirmed = []
    if status == "acknowledged":
        confirmed.append((idle, "saving"))
    if button and not inconsistent and saved_idle is not None:
        confirmed.append((saved_idle, "saved"))
    first, screen = min(confirmed) if confirmed else (None, None)
    return {"status": status, "from_button_ms": times,
            "first_visible_ms": first, "first_visible_screen": screen}


def storage_phase_result(trace, page, calls):
    """Interpret one completed writer scope only when the phase and call outcome share its REC edge."""
    result = {"status": "incomplete", "dominant": None}
    phase = trace.get(page)
    if calls is None:
        return dict(result, detail="Storage call outcomes are missing.")
    for row, fields in ((phase, IO_PHASE_FIELDS), (calls, STORAGE_IO_PAGES["io_calls"])):
        if row is None:
            continue
        if (not isinstance(row, dict) or row.keys() != set(fields)
                or any(type(n) is not int or not 0 <= n <= 0xffffffff for n in row.values())):
            return dict(result, status="inconsistent", detail="Invalid storage page fields.")
        if (row["edge"] and row["edge"] != trace["summary"]["edge"]) or (not row["edge"] and any(row.values())):
            return dict(result, status="inconsistent", detail="Probe data does not match the retained REC edge.")

    opened = page == "io_open"
    open_rc, header_rc = calls["open_rc"], calls["header_rc"]
    if open_rc >= len(OPEN_OUTCOMES) or header_rc >= len(HEADER_OUTCOMES):
        return dict(result, status="inconsistent", detail="Unknown storage call result.")
    if header_rc and open_rc not in (1, 4):
        return dict(result, status="inconsistent", detail="Header write recorded without an open stream.")
    rc = open_rc if opened else header_rc
    result["outcome"] = (OPEN_OUTCOMES if opened else HEADER_OUTCOMES)[rc]
    result["errno"] = calls["open_errno" if opened else "header_errno"] or None
    if phase is None:
        return dict(result, detail="Storage phase timing is missing.")
    if not phase["edge"] and not rc:
        return dict(result, status="unobserved", detail="No completed operation in this phase.")
    if not phase["edge"] or not rc:
        return dict(result, detail="Phase timing or call result is missing.")
    if phase["valid"] > 1:
        return dict(result, status="inconsistent", detail="Invalid scope validity flag.")
    if not phase["valid"]:
        return dict(result, detail="The firmware could not account for the complete scope.")

    buckets = {key: phase[key] for key in IO_BUCKETS}
    counts = {key: phase[key] for key in IO_COUNTS}
    total = phase["total_us"]
    lock_us = phase["fat_lock_us"] + phase["vfs_lock_us"] + phase["host_lock_us"]
    if (sum(buckets.values()) != total or (lock_us and not counts["lock_blocks"])
            or (counts["lock_blocks"] and not counts["lock_calls"])
            or (phase["command_us"] and not counts["commands"]) or (phase["event_us"] and not counts["events"])):
        return dict(result, status="inconsistent", detail="Duration, event counts and scope total disagree.")
    result.update(total_us=total, exclusive_us=buckets, counts=counts)
    if opened:
        subcalls = {key: calls[key] for key in OPEN_SUBCALLS}
        if sum(subcalls.values()) > total:
            return dict(result, status="inconsistent", detail="Open subcalls exceed the enclosing scope.")
        result["subcalls_us"] = subcalls
    if rc != 1:
        return dict(result, status="operation_failed", detail="The requested storage operation failed.")
    if not opened and open_rc != 1:
        return dict(result, detail="The unbuffered stream setup failed before the header write.")
    if not total:
        return dict(result, detail="No measurable elapsed time.")

    largest = max(buckets, key=buckets.get)
    dominant = IO_BUCKETS[largest] if 2 * buckets[largest] > total else "mixed"
    return dict(result, status="measured", dominant=dominant)


def storage_io_result(trace):
    """Partition elapsed writer time. Timing a host event cannot identify electrical card busy."""
    phases = (("wav_open", "io_open"), ("header_write", "io_header"))
    if not any(page in trace for page in STORAGE_IO_PAGES):
        return {name: {"status": "unsupported", "dominant": None,
                       "detail": "This trace has no storage probe pages."} for name, _ in phases}
    result = {name: storage_phase_result(trace, page, trace.get("io_calls")) for name, page in phases}
    result["interpretation"] = (
        "Each set of six exclusive buckets partitions writer wall time in microseconds. "
        "Lock time runs from the first kernel block marker until the acquisition returns. "
        "Event time runs from the first actual wait-list placement in a positive-timeout SDMMC "
        "event receive until that receive returns. Both intervals include time to run again. "
        "The block counts include repeated placements. Immediate event receives and zero-time "
        "polls within a command stay in command time. Electrical card busy requires a separate "
        "signal measurement, and writer CPU time requires task runtime data. Residual time "
        "includes uninstrumented work. A dominant bucket exceeds half its scope. "
        "Open subcall timings overlap the same six buckets. The initial 44-byte fwrite ends "
        "before flush, fsync and the injected delay.")
    return result


def display_trial_result(trace, previous_boot, wav=None):
    """Keep a missed edge, unacknowledged panel and missing SD readback distinguishable."""
    s, home, rec, storage = (trace[k] for k in ("summary", "home", "rec", "storage"))
    edge, pcm = s["edge"], s["pcm"]
    home_wave = home["wave_full"] or home["wave_partial"]
    rec_wave = rec["wave_partial"] or rec["wave_full"]
    def panel_acknowledged(screen, wave):
        if (bool(screen["wave_full"]) == bool(screen["wave_partial"]) or
                screen["timeout"] or screen["no_busy"] or not screen["upload"]):
            return False
        if not screen["upload"] <= wave <= screen["busy"] <= screen["idle"] <= screen["done"]:
            return False
        # A full base writes both planes before activation. A partial refresh
        # writes its previous-image plane after the physical BUSY interval.
        if screen["wave_full"]:
            return screen["upload"] <= screen["plane"] <= wave
        return screen["idle"] <= screen["plane"] <= screen["done"]
    home_ok = panel_acknowledged(home, home_wave)
    rec_ok = panel_acknowledged(rec, rec_wave)
    own_edge = bool(edge) and edge <= s["accepted"] <= pcm
    restarted = trace["boot"] != previous_boot and s["software_restart"] == 1
    edge_in_home = home_ok and home["busy"] <= edge < home["idle"]
    overlapped = (own_edge and home_ok and home["busy"] <= pcm <= rec["request"]
                  <= rec["ready"] < home["idle"])
    serialized = home_ok and rec_ok and rec["ready"] <= rec["upload"] <= rec_wave and home["done"] <= rec["upload"]
    stall_start, stall_end = storage["stall_begin"], storage["stall_end"]
    injected = (s["injected_ms"] == 6500 and 0 < stall_start <= stall_end <= storage["first_write"]
                and stall_end - stall_start >= 6500 and not storage["first_write_error"])
    activation_in_stall = rec_ok and injected and stall_start <= rec_wave < stall_end
    complete_audio = (s["frames_in"] > 0 and s["frames_in"] == s["frames_out"]
                      and s["overflow"] == 0 and s["saved"] == 1 and s["note"] > 0)
    pcm_matches = None if wav is None else (
        complete_audio and wav["note"] == s["note"] and wav["frames"] == s["frames_in"]
        and wav["crc"].lower() == f"{s['crc']:08x}")
    audio = trace.get("audio")
    upstream = None if audio is None else audio["observed"] == 1 and not any(
        audio[k] for k in ("dma_overruns", "dma_lost_bytes", "reconfig_reads", "read_errors"))
    feedback = None
    if "release" in trace:
        release = trace["release"]
        first_write, saved_request = storage["first_write"], release["saved_request"]
        archived_inbox = (s["inbox"] == 1 and s["saved"] == 1 and s["note"] > 0
                          and first_write != 0 and saved_request != 0
                          and ((saved_request - first_write) & 0xffffffff) < 0x80000000)
        feedback = release_feedback(release, archived_inbox=archived_inbox)
    return {
        **({"storage_owner": storage_owner_result(trace)} if "own_open" in trace else {}),
        "storage_io": storage_io_result(trace),
        "observed_upstream_intact": upstream,
        "release_feedback": feedback,
        "new_software_boot": restarted, "edge_during_home_busy": edge_in_home,
        "first_pcm_and_rec_ready_during_home_busy": overlapped,
        "inbox": bool(s["inbox"]), "rec_panel_acknowledged": rec_ok,
        "home_completed_before_rec_upload": serialized,
        "forced_wait_ms": stall_end - stall_start if stall_start and stall_end >= stall_start else None,
        "confirmed_6500_ms_injection": injected,
        "rec_activation_during_forced_wait": activation_in_stall,
        "rec_activation_before_forced_wait": bool(rec_ok and injected and rec_wave < stall_start),
        "rec_activation_before_first_write": bool(rec_ok and 0 < rec_wave < storage["first_write"]),
        "ms_after_button": {key: stamp - edge if edge and stamp >= edge else None
                            for key, stamp in (("first_pcm", pcm), ("request", rec["request"]),
                                               ("ready", rec["ready"]), ("activation", rec_wave),
                                               ("panel_idle", rec["idle"] if rec_ok else 0),
                                               ("first_write", storage["first_write"]))},
        "captured_frames": s["frames_in"], "microphone_crc32": f"{s['crc']:08x}",
        "wav_matches_microphone": pcm_matches,
        "passed": bool(restarted and edge_in_home and overlapped and serialized and s["inbox"] and
                       activation_in_stall and pcm_matches and upstream is not False),
    }


def check_target(response, required=None):
    builds = (required,) if isinstance(required, str) else required or (*DISPLAY_BUILDS, PREVIOUS_BUILD)
    if len(response) != 3 or response[:2] != ["BENCH", DEV] or response[2] not in builds:
        raise ValueError("Qualification build and authenticated development Note must both match.")


def created(path):
    fields = [line[12:] for line in path.read_text(encoding="utf-8").splitlines()
              if line.startswith("created_utc=")]
    if len(fields) != 1:
        raise ValueError(f"Missing or ambiguous capture UTC in {path.name}")
    return fields[0]


def snapshot(root):
    folders = [folder for name in ("notes", "recordings") if (folder := root / name).is_dir()]
    if not folders:
        raise ValueError("Select a Note card containing notes or recordings.")
    result = {}
    for folder in folders:
        for path in folder.rglob("note_*"):
            if not path.is_file():
                continue
            primary = path.with_suffix("") if path.suffix == ".bak" else path
            if path != primary and (primary.suffix not in (".meta", ".txt", ".delivery") or primary.is_file()):
                continue
            if primary.suffix not in (".wav", ".txt", ".meta", ".delivery", ".moments"):
                continue
            if primary.suffix == ".meta":
                value = created(path)
            else:
                with path.open("rb") as src:
                    value = hashlib.file_digest(src, "sha256").hexdigest()
            result[primary.relative_to(root).as_posix()] = value
    return result


def compare(before, after):
    changed = [path for path, value in before.items() if after.get(path) != value]
    if changed:
        raise ValueError("Original card evidence missing or changed: " + ", ".join(changed))


def verify_take(root, note, stamp, edge, trace):
    if not 0x40000400 <= note <= 0x7fffffff:
        raise ValueError("Expected a monthly recorder ID.")
    code = note & 1023
    if code > 960:
        raise ValueError("Unsupported capture month.")
    month = f"{2020 + (code - 1) // 12:04}{(code - 1) % 12 + 1:02}" if code else "000000"
    expected = "" if stamp == "unknown" else stamp
    if expected:
        date = datetime.strptime(expected, "%Y-%m-%dT%H:%M:%SZ")
        if date.strftime("%Y%m") != month:
            raise ValueError("Capture UTC disagrees with the month in the ID.")
    elif code:
        raise ValueError("An unknown capture must have month zero.")
    path = root / "recordings" / month / f"note_{note}.wav"
    meta = path.with_suffix(".wav.meta")
    if not meta.is_file():
        meta = meta.with_suffix(".meta.bak")
    if not meta.is_file() or created(meta) != expected:
        raise ValueError("Capture metadata differs from expected UTC.")
    with path.open("rb") as src:
        header = src.read(44)
        if len(header) != 44:
            raise ValueError("Incomplete WAV header.")
        riff, size, wav, fmt, n, codec, channels, hz, rate, align, bits, data, length = struct.unpack(
            "<4sI4s4sIHHIIHH4sI", header)
        if ((riff, wav, fmt, n, codec, channels, hz, rate, align, bits, data) !=
                (b"RIFF", b"WAVE", b"fmt ", 16, 1, 1, 16000, 32000, 2, 16, b"data")
                or size != 36 + length or length % 2 or not length):
            raise ValueError("Invalid PCM16 recorder WAV header.")
        count = crc = 0
        while block := src.read(65536):
            count += len(block)
            crc = zlib.crc32(block, crc)
    frames = count // 2
    checks, hashes = set(), set()
    for line in trace:
        line = line.rstrip("\r\n")
        for pattern in (CHECK, SHORT_CHECK):
            match = pattern.fullmatch(line)
            if match and int(match[1]) == edge:
                checks.add(tuple(map(int, match.groups()[1:])))
        match = CRC.fullmatch(line)
        if match and int(match[1]) == edge:
            hashes.add(int(match[2], 16))
    if count != length or checks != {(frames, frames, 0, 1)} or hashes != {crc}:
        raise ValueError("WAV length, successful microphone frame counts or microphone CRC do not match.")
    return {"note": note, "utc": expected, "edge": edge, "frames": frames, "pcm_crc32": f"{crc:08x}"}


def device(args):
    sys.path.insert(0, str(BRIDGE))
    from note_settings import EnrollmentStore
    from pair import STATE
    from serial.tools.list_ports import comports
    import usb_sync as usb

    record = EnrollmentStore(args.state or STATE, {}).devices().get(DEV)
    if not record:
        raise ValueError("Development Note pairing missing from this Bridge state.")
    identity, token = usb.paired_port(comports(), args.port, {DEV: record["token"]})
    if identity != DEV:
        raise ValueError("USB identity mismatch.")
    with usb._open_link(args.port, record["id"]) as link:
        token, host, peer = usb._paired_session(link, (token,))
        close_session = True
        try:
            op = args.action
            status = usb._exchange(link, "BENCH status")
            if op == "owner-trace":
                check_target(status, OWNER_BUILDS)
                return read_owner_history(link, usb, include_overlap=status[2] in OVERLAP_BUILDS)
            if op == "display-trace":
                check_target(status, DISPLAY_BUILDS)
                return read_display_trace(link, usb, include_audio=status[2] in AUDIO_BUILDS,
                                          include_storage_io=status[2] in STORAGE_BUILDS)
            check_target(status, MSC_BUILDS if op in USB_STORAGE_ACTIONS else None)
            if op == "usb-step":
                from bridge import Bridge
                from note_web import read_object
                state = args.state or STATE
                bridge = Bridge(state, read_object(state / "config.json"))
                with WorkLink(link, usb) as traced:
                    if not usb._recorder_protocol(traced) or not traced.recorder_work_plans:
                        raise ValueError("Expected recorder WORK2 on the development Note.")
                    changed, _ = usb._drain_recorder_work(traced, token, bridge, DEV, threading.Event(),
                                                          None, only_note=args.note, one=True)
                return {"note": args.note, "stage_completed": changed}
            if op in ("set-utc", "restore"):
                epoch = int(time.time()) if op == "restore" else int(
                    datetime.strptime(args.utc, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc).timestamp())
                if not 1704067200 <= epoch < 4102444800:
                    raise ValueError("RTC supports 2024 through 2099.")
                before = time.monotonic()
                proof = usb._proof(token, f"clock {epoch}", host, peer)
                reply = usb._exchange(link, f"CLOCK {epoch} {proof}")
                if (len(reply) != 2 or reply[0] != "CLOCK-SAVED" or not
                        hmac.compare_digest(reply[1], usb._proof(token, f"clock-saved {epoch}", host, peer))):
                    raise ValueError("Clock write was not acknowledged.")
            if op == "restore":
                usb._expect_ok(link, "BENCH clock-normal")
            if op in ("set-utc", "restore", "clock"):
                reply = usb._exchange(link, "CLOCK")
                if len(reply) != 3 or reply[0] != "CLOCK":
                    raise ValueError("Clock readback failed.")
                chip, system = map(int, reply[1:])
                if op != "clock" and any(abs(v - epoch - (time.monotonic() - before)) > 2 for v in (chip, system)):
                    raise ValueError("RTC or system UTC did not follow the requested clock.")
                return {"rtc": chip, "system": system, "host": int(time.time())}
            option = args.point if op == "arm" else op
            if op == "arm" and args.note is not None:
                option += f" {args.note}"
            if op == "usb-storage":
                try:
                    usb._expect_ok(link, "BENCH usb-storage")
                except (usb.UsbReplyTimeout, OSError) as error:
                    close_session = False
                    raise RuntimeError("USB storage acknowledgment was lost. Check disk enumeration "
                                       "before issuing another request.") from error
                # The recorder closes this session as it restarts into storage mode.
                close_session = False
            else:
                usb._expect_ok(link, "BENCH " + option)
            return {"applied": option, "serial": DEV}
        finally:
            if close_session:
                with suppress(OSError, usb.UsbSyncError):
                    usb._expect_ok(link, "CLOSE")


def provider(args):
    sys.path.insert(0, str(BRIDGE))
    from google_tasks import read_task_action
    from pala_google import _api, TASKS_SCOPE
    from pair import STATE

    digest = args.receipt.read_text(encoding="ascii")
    if not re.fullmatch("[0-9a-f]{64}", digest):
        raise ValueError("Expected the original 64-byte delivery receipt or claim.")
    # A single short general task has exactly one action at index zero.
    with _api(args.state or STATE, "tasks", "v1", TASKS_SCOPE, "google-tasks-token.json") as api:
        account = api.tasklists().get(tasklist="@default").execute().get("id")
        if account != args.account or not account or account == "@default":
            raise ValueError("The provider account differs from the expected test account.")
        row = read_task_action(api, args.list_id, f"work1-{digest}-0")
        if not row or row.get("title") != args.title:
            raise ValueError("No unique Google task with the expected marker and title.")
        return {"matches": 1, "task_id": row["id"], "receipt": digest}


def listen(args):
    sys.path.insert(0, str(BRIDGE))
    import serial
    from serial.tools.list_ports import comports
    import usb_sync as usb

    deadline = time.monotonic() + args.seconds
    count = 0
    while time.monotonic() < deadline:
        try:
            usb.paired_port(comports(), args.port, {DEV: None})
        except usb.UsbSyncError:
            time.sleep(.25)
            continue
        try:
            with serial.Serial(port=None, baudrate=115200, timeout=.2) as port:
                port.dtr = port.rts = False
                port.port = args.port
                port.open()
                line = bytearray()
                while time.monotonic() < deadline:
                    line.extend(port.read_until(b"\n", 1024))
                    if line.endswith(b"\n"):
                        event = event_line(line)
                        if event:
                            print(event, flush=True)
                            count += 1
                        line.clear()
                    elif len(line) > 2048:
                        line.clear()
        except serial.SerialException:
            time.sleep(.25)
    if not count:
        raise ValueError("No development Note recorder trace was captured.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    for name in ("clock", "restore", "set-utc", "clock-unknown", "clock-normal", "clock-fail-next-boot",
                 "arm", "trace", "usb-step", "display-trace", "owner-trace", *USB_STORAGE_ACTIONS):
        cmd = sub.add_parser(name)
        cmd.add_argument("--port", required=True)
        if name == "trace":
            cmd.add_argument("--seconds", type=int, default=90)
        else:
            cmd.add_argument("--state", type=Path)
        if name == "usb-step":
            cmd.add_argument("--note", type=int, required=True)
        if name == "set-utc":
            cmd.add_argument("utc", help="UTC such as 2026-11-30T23:59:45Z")
        if name == "arm":
            cmd.add_argument("point", choices=POINTS)
            cmd.add_argument("--note", type=int, help="Required for text, receipt and lose-receipt")
    report = sub.add_parser("owner-report", help="Resolve VFS owner evidence from a saved owner-trace snapshot")
    report.add_argument("history", type=Path)
    report.add_argument("--elf", type=Path, required=True)
    report.add_argument("--addr2line", type=Path, required=True)
    cloud = sub.add_parser("provider", help="Read back one short general test task from its Google list")
    cloud.add_argument("receipt", type=Path)
    cloud.add_argument("--account", required=True, help="Verified default Tasks list ID for the approved account")
    cloud.add_argument("--list-id", required=True)
    cloud.add_argument("--title", required=True)
    cloud.add_argument("--state", type=Path)
    for name in ("snapshot", "compare", "take"):
        cmd = sub.add_parser(name)
        cmd.add_argument("card", type=Path)
        if name == "compare":
            cmd.add_argument("baseline", type=Path)
        if name == "take":
            cmd.add_argument("note", type=int)
            cmd.add_argument("utc", help="Expected capture UTC or unknown")
            cmd.add_argument("edge", type=int)
            cmd.add_argument("trace", type=Path)
    args = parser.parse_args()
    if args.action == "usb-step" and not 0x40000400 <= args.note <= 0x7fffffff:
        parser.error("Select the intended monthly recording ID.")
    if args.action == "arm":
        if args.point in ("text", "receipt", "lose-receipt") and args.note is None:
            parser.error("This phase requires --note with the expected recording ID.")
        if args.note is not None and (args.point in ("off", "open", "pcm") or not 0x40000400 <= args.note <= 0x7fffffff):
            parser.error("Select a valid monthly ID for a note-specific phase.")
    if args.action == "trace":
        listen(args)
        return
    if args.action == "owner-report":
        history = json.loads(args.history.read_text(encoding="utf-8-sig"))
        result = []
        for take in history["takes"]:
            if any(take[k] != history[k] for k in ("boot", "revision", "elf_sha256")):
                raise ValueError("Take does not belong to this owner history.")
            result.append({"edge": take["summary"]["edge"], "note": take["summary"]["note"],
                           "owner": symbolize_owner_result(storage_owner_result(take), args.elf, args.addr2line)})
    elif args.action == "provider":
        result = provider(args)
    elif args.action == "snapshot":
        result = snapshot(args.card)
    elif args.action == "compare":
        before = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
        compare(before, snapshot(args.card))
        result = {"verified_originals": len(before)}
    elif args.action == "take":
        with args.trace.open(encoding="utf-8-sig") as trace:
            result = verify_take(args.card, args.note, args.utc, args.edge, trace)
    else:
        result = device(args)
    print(json.dumps(result, indent=2, ensure_ascii=True, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, RuntimeError) as error:
        sys.exit(str(error))
