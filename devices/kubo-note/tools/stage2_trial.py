"""Collect Stage 2 restart traces and verify individual acceptance trials."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timedelta
import json
from pathlib import Path
import re
import sys
import time
from types import SimpleNamespace

import stage2_bench as bench


BOOT = re.compile(r"=== Pala Note v?[A-Za-z0-9._-]{1,64} ===")
TRANSPORTS = ("usb", "ble", "wifi")
LINK = re.compile(r"\[BenchLink\] link=(usb|ble|wifi) phase=(transcribed|write|reconcile|confirmed|delivered) note=(\d+)")
CUT_POINTS = ("open", "pcm", "publish", "text", "receipt")
CUT = re.compile(r"\[BenchCut\] phase=(open|pcm|publish|text|receipt) note=(-?\d+) edge=(\d+) window_ms=10000")
REPLY_PREFIXES = (b"KUBO_USB_V1 ", b"PALA_CONFIG_V1 ")
MAX_FRAME = 4096
TIMING = re.compile(r"\[RecTiming\] edge=(\d+) stage=([a-z_]{1,24}) ms=(\d+) elapsed=(\d+)")
BOOT_MARK = re.compile(r"\[RecBoot\] edge=(\d+) stage=([a-z_]{1,24}) boot_ms=(\d+)")


def matching_banner(line, build):
    return line in (f"=== Pala Note {build} ===", f"=== Pala Note v{build} ===")


class TraceLink:
    """A single reader delivers protocol replies and logs allowlisted diagnostics."""

    def __init__(self, raw, report):
        self.raw = raw
        self.peer_id = raw.peer_id
        self.report = report
        self.pending = bytearray()
        self.discard = False

    def write(self, wire):
        return self.raw.write(wire)

    def read_until(self, terminator, limit):
        chunk = self.raw.read_until(terminator, MAX_FRAME)
        if not self.discard:
            self.pending.extend(chunk)
            if len(self.pending) > MAX_FRAME:
                self.pending.clear()
                self.discard = True
        if not chunk.endswith(terminator):
            return b""
        frame = bytes(self.pending)
        self.pending.clear()
        self.discard = False
        if frame.startswith(REPLY_PREFIXES):
            if len(frame) > limit:
                raise ValueError("Oversized USB protocol reply.")
            return frame
        banner = frame.rstrip(b"\r\n").decode("ascii", errors="replace")
        event = banner if BOOT.fullmatch(banner) else bench.event_line(frame)
        if event:
            self.report(event)
        return b""


def collect_restart(connect, request, build, report, seconds, reply_errors, *, now=time.monotonic, pause=time.sleep):
    """Keep reading on the same handle during setup and resume by USB identity after a disconnect."""
    deadline = now() + seconds
    result = {"boot_banners": 0, "restart_ticket": None}
    attached = False
    attempted = request is None

    def event(line):
        result["boot_banners"] += matching_banner(line, build)
        report(line)

    while now() < deadline:
        try:
            with connect() as raw:
                link = TraceLink(raw, event)
                attached = True
                report("[Stage2] usb_attached")
                if not attempted:
                    attempted = True
                    try:
                        result["restart_ticket"] = request(link)
                    except reply_errors:
                        report("[Stage2] restart_reply_unavailable")
                    else:
                        report(f"[Stage2] restart_ticket={result['restart_ticket']}")
                while now() < deadline:
                    link.read_until(b"\n", MAX_FRAME)
        except OSError:
            if attached:
                report("[Stage2] usb_detached")
                attached = False
            pause(.25)
    report("[Stage2] trace_finished")
    return result


def capture(args):
    sys.path.insert(0, str(bench.BRIDGE))
    from note_settings import EnrollmentStore, SettingsError
    from serial.tools.list_ports import comports
    import usb_sync as usb

    record = EnrollmentStore(args.state, {}).devices().get(bench.DEV)
    if not record:
        raise ValueError("Development Note pairing is missing from the selected Bridge state.")
    opened = False

    @contextmanager
    def connect():
        nonlocal opened
        names = [port.device for port in comports() if (port.vid, port.pid) == (0x303a, 0x1001)
                 and usb.usb_identity(port.serial_number) == bench.DEV]
        if len(names) != 1 or (not opened and args.port and names[0].casefold() != args.port.casefold()):
            raise OSError("Development Note unavailable on the expected USB attachment.")
        with usb._open_link(names[0], record["id"]) as raw:
            opened = True
            yield raw

    def request(link):
        usb._paired_session(link, (record["token"],))
        bench.check_target(usb._exchange(link, "BENCH status"), args.build)
        reply = usb.config_exchange(link, {"op": "restart.request", "mode": "restart"})
        ticket = reply.get("ticket")
        if reply.get("ok") is not True or reply.get("pending") is not True or type(ticket) is not int or ticket <= 0:
            raise ValueError("The development Note did not accept a restart confirmation request.")
        return ticket

    with args.log.open("x", encoding="ascii", buffering=1) as trace:
        def report(line):
            print(line, file=trace, flush=True)
            print(line, flush=True)
            if line.startswith("[Stage2] restart_ticket="):
                print("Confirm the restart on the Note and press REC at the intended point during boot.", flush=True)
            elif line == "[Stage2] restart_reply_unavailable":
                print("Restart reply unavailable. Capture is active. Check the Note for the prompt.", flush=True)

        report(f"[Stage2] device={bench.DEV} expected_build={args.build}")
        result = collect_restart(connect, request if args.action == "restart-trace" else None,
                                 args.build, report, args.seconds, (usb.UsbSyncError, SettingsError))
    if not opened:
        raise ValueError("The development Note never opened. Check the selected port and which program owns it.")
    if args.action == "restart-trace" and not result["boot_banners"]:
        raise ValueError("The log was saved, but no matching boot banner was observed.")
    return result


def archive_stem(card, note):
    code = note & 1023
    if not 0x40000400 <= note <= 0x7fffffff or code > 960:
        raise ValueError("Expected a valid monthly recorder ID.")
    month = f"{2020 + (code - 1) // 12:04}{(code - 1) % 12 + 1:02}" if code else "000000"
    return card / "recordings" / month / f"note_{note}"


def verify_recording(card, note, utc, edge, lines, *, build=None, boot=None, software_restart=False, crosses=None):
    banners = [i for i, line in enumerate(lines) if BOOT.fullmatch(line)]
    if boot is not None:
        if boot < 1 or boot > len(banners):
            raise ValueError("The selected boot banner is missing from the trace.")
        start = banners[boot - 1]
        if not matching_banner(lines[start], build):
            raise ValueError("The measured boot does not match the expected firmware build.")
        lines = lines[start + 1:banners[boot] if boot < len(banners) else len(lines)]
    elif banners:
        raise ValueError("Select the measured boot with --boot and --build.")
    if "[Stage2] usb_detached" in lines:
        # A missing banner after detach could otherwise merge two resets.
        lines = lines[:lines.index("[Stage2] usb_detached")]
    edges = {int(match[1]) for line in lines if (match := bench.CRC.fullmatch(line))}
    if edges != {edge}:
        raise ValueError("Use a complete trace with exactly one measured recording in the selected boot.")
    if software_restart and f"[RecBoot] edge={edge} software_reset=1" not in lines:
        raise ValueError("The selected REC edge has no software-restart evidence.")

    relative = "/" + archive_stem(card, note).with_suffix(".wav").relative_to(card).as_posix()
    markers = {"[Rec] " + relative, "[Rec] /sdcard" + relative, "[Rec] archived /sdcard" + relative}
    if not markers.intersection(lines):
        raise ValueError("The selected WAV ID is missing from the recording trace.")
    result = bench.verify_take(card, note, utc, edge, lines)
    result["duration_s"] = result["frames"] / 16000
    timings = {}
    boot_marks = {}
    for row in lines:
        if (m := TIMING.fullmatch(row)) and int(m[1]) == edge:
            value = {"stage_ms": int(m[3]), "since_rec_ms": int(m[4])}
            if m[2] in timings and timings[m[2]] != value:
                raise ValueError("Conflicting stage timings for the selected REC edge.")
            timings[m[2]] = value
        if (m := BOOT_MARK.fullmatch(row)) and int(m[1]) == edge:
            if m[2] in boot_marks and boot_marks[m[2]] != int(m[3]):
                raise ValueError("Conflicting boot timings for the selected REC edge.")
            boot_marks[m[2]] = int(m[3])
    for key, pattern, stage in (
        ("first_pcm_ms", rf"\[Rec\] edge={edge} first_pcm=\d+ latency_ms=(\d+)", "pcm"),
        ("first_write_ms", r"\[Rec\] first_write=\d+ write_latency_ms=(\d+)", "first_write"),
    ):
        elapsed = {int(m[1]) for row in lines if (m := re.fullmatch(pattern, row))}
        if stage in timings:
            elapsed.add(timings[stage]["since_rec_ms"])
        if len(elapsed) > 1:
            raise ValueError("Conflicting values for " + key + ".")
        if elapsed:
            result[key] = elapsed.pop()
    if timings:
        result["timings"] = timings
    if boot_marks:
        result["boot_marks_ms"] = boot_marks
    if crosses:
        started = datetime.strptime(utc, "%Y-%m-%dT%H:%M:%SZ")
        boundary = datetime.strptime(crosses, "%Y-%m-%dT%H:%M:%SZ")
        if not started < boundary < started + timedelta(seconds=result["duration_s"]):
            raise ValueError("The recorded PCM duration does not span the requested UTC boundary.")
        result["crossed_utc"] = True
    display = bench.display_timing(lines, edge)
    if display["status"] != "missing":
        result["display"] = display
    return result


def verify_delivery(card, note, expected_text, lines, transport, read_provider, *, previous=None):
    stem = archive_stem(card, note)
    receipt = stem.with_suffix(".delivery")
    if not expected_text.strip() or stem.with_suffix(".txt").read_bytes() != expected_text:
        raise ValueError("The SD transcript differs from the expected Bridge transcript.")
    digest = receipt.read_text(encoding="ascii")
    if not re.fullmatch(r"[a-f0-9]{64}", digest):
        raise ValueError("The selected note has no valid committed delivery receipt.")

    def phases(rows):
        return [(i, m[1], m[2]) for i, row in enumerate(rows)
                if (m := LINK.fullmatch(row)) and int(m[3]) == note]

    completion = "confirmed" if transport == "wifi" else "delivered"
    if previous is None:
        expected = (["transcribed"] if transport == "wifi" else []) + ["write", completion]
    else:
        a = phases(previous)
        grants = [i for i, _, phase in a if phase == "write"]
        lost = f"[BenchCut] phase=lose-receipt note={note}"
        if len(grants) != 1 or lost not in previous[grants[0] + 1:] or any(
                phase in ("reconcile", "delivered") for _, _, phase in a):
            raise ValueError("Bridge A lacks a single write grant followed by the withheld receipt.")
        expected = ["reconcile", completion]
    if [(link, phase) for _, link, phase in phases(lines)] != [(transport, phase) for phase in expected]:
        raise ValueError(f"The trace does not show the expected {transport} phases: {expected}.")
    provider = read_provider(receipt)
    if provider.get("matches") != 1 or provider.get("receipt") != digest:
        raise ValueError("Independent Google readback did not confirm exactly one task for this receipt.")
    return {"note": note, "transport": transport, "path": "write" if previous is None else "reconcile",
            "transcript_match": True, "provider": provider}


def verify_recovery(before, after, after_second_boot, lines, phase, marker):
    indexes = [i for i, line in enumerate(lines) if (m := CUT.fullmatch(line)) and m[1] == phase
               and int(m[3] if phase in ("open", "pcm") else m[2]) == marker]
    if len(indexes) != 1:
        raise ValueError("Expected one matching cut window for the selected trial.")
    later = lines[indexes[0] + 1:]
    boot = next((i for i, row in enumerate(later) if BOOT.fullmatch(row)), None)
    if boot is None or "[Stage2] usb_detached" not in later[:boot] or f"[BenchCut] phase={phase} expired" in later[:boot]:
        raise ValueError("The trace does not show a detach and reboot before the cut window expired.")
    if not all(isinstance(entry, dict) and entry for entry in (before, after, after_second_boot)):
        raise ValueError("Card preservation checks require nonempty baseline and recovery snapshots.")
    bench.compare(before, after)
    if after != after_second_boot:
        raise ValueError("Archive and receipt state changed on the second recovery boot.")
    return {"phase": phase, "marker": marker, "originals_preserved": len(before),
            "new_artifacts": sorted(set(after) - set(before)), "archive_stable_after_second_boot": True}


def text_lines(path):
    return path.read_text(encoding="utf-8-sig").splitlines()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    for action in ("trace", "restart-trace"):
        cmd = commands.add_parser(action)
        cmd.add_argument("--state", type=Path, required=True)
        cmd.add_argument("--log", type=Path, required=True)
        cmd.add_argument("--build", required=True)
        cmd.add_argument("--port")
        cmd.add_argument("--seconds", type=int, default=90)
    rec = commands.add_parser("recording")
    rec.add_argument("card", type=Path)
    rec.add_argument("note", type=int)
    rec.add_argument("utc")
    rec.add_argument("edge", type=int)
    rec.add_argument("trace", type=Path)
    rec.add_argument("--boot", type=int, help="One-based index of the boot banner in the trace")
    rec.add_argument("--build")
    rec.add_argument("--software-restart", action="store_true")
    rec.add_argument("--crosses", help="Required UTC boundary")
    delivery = commands.add_parser("delivery")
    delivery.add_argument("card", type=Path)
    delivery.add_argument("note", type=int)
    delivery.add_argument("trace", type=Path)
    delivery.add_argument("--transport", choices=TRANSPORTS, required=True)
    delivery.add_argument("--previous", type=Path, help="Bridge A trace with the lost receipt")
    delivery.add_argument("--transcript", type=Path, required=True, help="Original Bridge transcript")
    for option in ("account", "list-id", "title"):
        delivery.add_argument("--" + option, required=True)
    delivery.add_argument("--state", type=Path, required=True)
    recovery = commands.add_parser("recovery")
    for file in ("baseline", "after", "after_second_boot", "trace"):
        recovery.add_argument(file, type=Path)
    recovery.add_argument("phase", choices=CUT_POINTS)
    recovery.add_argument("marker", type=int, help="REC edge for open or pcm, note ID for other phases")
    args = parser.parse_args()

    if args.action in ("trace", "restart-trace"):
        if not 1 <= args.seconds <= 3600 or not re.fullmatch(r"[A-Za-z0-9._-]{1,64}", args.build):
            parser.error("Supply a duration of 1â€“3600 seconds and the exact firmware build.")
        result = capture(args)
    elif args.action == "recording":
        if (args.boot is None) != (args.build is None):
            parser.error("--boot and --build must be used together.")
        result = verify_recording(args.card, args.note, args.utc, args.edge, text_lines(args.trace),
                                  build=args.build, boot=args.boot, software_restart=args.software_restart,
                                  crosses=args.crosses)
    elif args.action == "delivery":
        def read_provider(receipt):
            return bench.provider(SimpleNamespace(receipt=receipt, account=args.account, list_id=args.list_id,
                                                  title=args.title, state=args.state))
        result = verify_delivery(args.card, args.note, args.transcript.read_bytes(), text_lines(args.trace),
                                 args.transport, read_provider, previous=text_lines(args.previous) if args.previous else None)
    else:
        snapshots = [json.loads(path.read_text(encoding="utf-8-sig"))
                     for path in (args.baseline, args.after, args.after_second_boot)]
        result = verify_recovery(*snapshots, text_lines(args.trace), args.phase, args.marker)
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, RuntimeError) as error:
        sys.exit(str(error))
