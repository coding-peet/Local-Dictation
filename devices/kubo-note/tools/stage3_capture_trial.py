"""Collect and verify the Stage 3 dev Note trials. The watch command only observes."""
import argparse
import json
from pathlib import Path
import re
import struct
import sys
import time
import wave
import zlib

import stage2_bench as bench
from stage3_quiet_voice import words
from recording_parts import PART_BYTES, pack_wav

BUILD = "2.0.0-stage3-trial1"
PHRASE = "Na námestí stojí modrý bicykel."
INTERRUPT_PHRASE = "Modrý bicykel stojí pri bráne."
PATTERNS = {
    "pcm": r"\[S3\] pcm edge=(\d+) at=(\d+) now=(\d+)",
    "microphone": r"\[S3\] microphone edge=(\d+) intact=(\d+)",
    "lock": r"\[S3\] lock edge=(\d+) locked=(\d+) released=(\d+) stop=(\d+)",
    "archive": r"\[S3\] archive edge=(\d+) note=(\d+)",
    "counts": r"\[RecCheck\] e=(\d+) in=(\d+) out=(\d+) ov=(\d+) ok=(\d+)",
    "crc": r"\[RecCRC\] e=(\d+) crc=([0-9a-fA-F]{8})",
    "speech-start": r"\[S3\] speech-start at=(\d+)",
    "speech-end": r"\[S3\] speech-end at=(\d+) cancelled=(\d+) failed=(\d+)",
}


def parse(line):
    for kind, pattern in PATTERNS.items():
        match = re.fullmatch(pattern, line)
        if match:
            return kind, [int(v, 16 if kind == "crc" and i == 1 else 10) for i, v in enumerate(match.groups())]
    return None


def elapsed(start, end):
    return (end - start) & 0xffffffff


def require(value, explanation):
    if not value:
        raise ValueError(explanation)


def one(events, kind):
    values = {tuple(row[1]) for row in events if row[0] == kind}
    require(len(values) == 1, f"Expected one distinct {kind} event, got {len(values)}.")
    return values.pop()


def timing(events, case):
    edge, pcm, sent = one(events, "pcm")
    capture, note = one(events, "archive")
    counted, frames, written, overflow, ok = one(events, "counts")
    hashed, crc = one(events, "crc")
    require(one(events, "microphone") == (edge, 1), "Microphone DMA or read integrity failed.")
    require(edge == capture == counted == hashed and note > 0, "Capture IDs or button edges differ.")
    require(frames == written and frames > 0 and not overflow and ok, "Incomplete capture.")
    result = dict(note=note, frames=frames, crc32=f"{crc:08x}", edge_to_pcm_ms=elapsed(edge, pcm))
    require(result["edge_to_pcm_ms"] < 30000, "Invalid REC and microphone chronology.")
    if case == "interrupt":
        starts = [r for r in events if r[0] == "speech-start"]
        ends = [r for r in events if r[0] == "speech-end"]
        require(len(starts) == len(ends) == 1, "Missing, repeated or restarted speech.")
        start, = one(events, "speech-start")
        end, cancelled, failed = one(events, "speech-end")
        require(cancelled and not failed and elapsed(start, end) < 600000 and
                elapsed(start, edge) <= elapsed(start, end) and elapsed(edge, end) <= elapsed(edge, pcm),
                "The REC edge must interrupt active speech and PCM must follow playback shutdown.")
        result.update(edge_to_output_stop_ms=elapsed(edge, end), output_stop_to_pcm_ms=elapsed(end, pcm))
    elif case in ("long", "short"):
        locked_edge, locked, released, stop = one(events, "lock")
        require(locked_edge == edge and locked and released and 5000 <= elapsed(edge, locked) < 60000
                and elapsed(locked, released) < 60000, "LOCK and release of both buttons not observed.")
        require(stop == (2 if case == "short" else 1), "Unexpected STOP button.")
        if case == "long":
            require(305 * 16000 <= frames <= 330 * 16000, "Long take outside 5:05 to 5:30.")
        result.update(locked_ms=elapsed(edge, locked), released_ms=elapsed(edge, released), stop_button=stop)
    return result


def watch(args):
    from serial.tools.list_ports import comports
    import usb_sync as usb
    from note_settings import EnrollmentStore
    devices = [p.device for p in comports() if (p.vid, p.pid) == (0x303a, 0x1001)
               and usb.usb_identity(p.serial_number) == bench.DEV]
    require(len(devices) == 1, "Expected the development Note recorder USB identity.")
    peer = EnrollmentStore(args.state, {}).devices().get(bench.DEV)
    require(peer, "Pairing for the dev Note is missing.")
    with usb._open_link(devices[0], peer["id"]) as link:
        usb._paired_session(link, (peer["token"],))
        bench.check_target(usb._exchange(link, "BENCH status"), BUILD)
        usb._expect_ok(link, "CLOSE")
        with args.trace.open("x", encoding="ascii", buffering=1) as log:
            def emit(row):
                print(json.dumps(row), file=log, flush=True)
            emit(["trial", args.case, bench.DEV, BUILD])
            print("USB READY. " + ("Ask Kubo for a long spoken answer with a lower hold."
                  if args.case == "interrupt" else "Start one short REC during deletion." if args.case == "during-delete"
                  else "Hold upper REC. At LOCK press lower; release both at ZAMKNUTE."), flush=True)
            pending = b""
            scheduled = []
            events = []
            captured = None
            finish_at = None
            deadline = time.monotonic() + (470 if args.case == "long" else 160)
            while time.monotonic() < deadline:
                chunk = link.read_until(b"\n", 4096)
                pending += chunk
                require(len(pending) < 8192, "Overlong USB line.")
                now = time.monotonic()
                if pending.endswith(b"\n"):
                    line = pending.strip().decode("ascii", errors="replace")
                    pending = b""
                    require(not line.startswith(("=== Pala Note", "ESP-ROM:")), "Note restarted during collection.")
                    event = parse(line)
                    if event:
                        emit(event)
                        events.append(event)
                        kind, vals = event
                        if kind == "speech-start" and args.case == "interrupt":
                            print("REC NOW during the spoken answer. Say: " + INTERRUPT_PHRASE + " Then release.", flush=True)
                        if kind == "pcm":
                            require(captured is None, "More than one take in the same trace.")
                            captured = vals[0]
                            origin = now - elapsed(vals[1], vals[2]) / 1000
                            if args.case == "long":
                                scheduled = [(origin + 290, "Sentence in nine seconds. Use the next two cues."),
                                             (origin + 299, "SPEAK: Na námestí"),
                                             (origin + 300.5, "CONTINUE: stojí modrý bicykel."),
                                             (origin + 315, "STOP now with a fresh upper press.")]
                            elif args.case == "short":
                                scheduled = [(origin + 12, "STOP with a fresh lower press after releasing both.")]
                while scheduled and scheduled[0][0] <= now:
                    due, cue = scheduled.pop(0)
                    emit(["cue", cue, round((now - due) * 1000)])
                    print(cue, flush=True)
                if {"archive", "counts", "crc"} <= {e[0] for e in events}:
                    if finish_at is None:
                        finish_at = now + (8 if args.case == "interrupt" else 2)
                    if now >= finish_at:
                        result = timing(events, args.case)
                        emit(["finished", result])
                        print(json.dumps(result), flush=True)
                        return
            raise ValueError("Timed out. Partial trace retained.")


def pcm_bytes(path):
    with wave.open(str(path)) as f:
        require((f.getnchannels(), f.getsampwidth(), f.getframerate(), f.getcomptype()) ==
                (1, 2, 16000, "NONE"), "Unexpected WAV format.")
        pcm = f.readframes(f.getnframes())
        require(len(pcm) == f.getnframes() * 2 and path.stat().st_size == 44 + len(pcm), "Truncated or padded WAV.")
        return pcm


def verify(args):
    rows = [json.loads(line) for line in args.trace.read_text(encoding="ascii").splitlines()]
    case = rows[0][1]
    require(rows[0] == ["trial", case, bench.DEV, BUILD] and case in ("long", "short", "interrupt", "during-delete"), "Wrong trace.")
    result = timing(rows, case)
    require(rows[-1] == ["finished", result], "Trace did not finish.")
    base = f"note_{result['note']}.wav"
    paths = list((args.card / "recordings").glob("*/" + base))
    require(len(paths) == 1, "Expected one archived base WAV for this note.")
    wav = paths[0]
    pcm = pcm_bytes(wav)
    extras = sorted(p.name for p in wav.parent.glob(base + ".p*") if p.name != base + ".parts")
    if case == "long":
        require(len(pcm) == PART_BYTES and extras == [base + ".p1"], "Expected exactly two five-minute-format parts.")
        last = pcm_bytes(wav.with_name(base + ".p1"))
        records = [(i, len(part), zlib.crc32(part)) for i, part in enumerate((pcm, last))]
        records.append((2, 0, 0))
        manifest = b"".join(struct.pack("<4I", i, size, crc, 0x50415254 ^ i ^ size ^ crc) for i, size, crc in records)
        require(wav.with_name(base + ".parts").read_bytes() == manifest, "Missing or different sealed part manifest.")
        pcm += last
    else:
        require(not extras and not wav.with_name(base + ".parts").exists(), "Unexpected extra part.")
    require(len(pcm) == result["frames"] * 2 and f"{zlib.crc32(pcm):08x}" == result["crc32"],
            "Card PCM differs from microphone.")
    if case == "long":
        require(args.download is not None, "Supply the WAV actually downloaded from Web Companion.")
        require(pcm_bytes(args.download) == pcm, "Web Companion WAV differs from the two card parts.")
        require(args.listing is not None, "Supply the Web Companion month listing containing this take.")
        hrefs = re.findall(r"""href=['"]/wav\?num=(\d+)['"]""", args.listing.read_text(encoding="utf8"))
        require(hrefs.count(str(result["note"])) == 1, "Expected one download link for this note on its archive page.")
        transcript = wav.with_suffix(".txt").read_text(encoding="utf8")
        require(words(transcript) == words(PHRASE), "Missing, added or reordered words in the saved transcript.")
        if getattr(args, "model_root", None):
            import os
            os.environ.update(LOCAL_DICTATION_MODEL_ROOT=str(args.model_root), HF_HUB_OFFLINE="1",
                              TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1")
            from model_engine import WhisperEngine, FAST_MODEL_ID
            engine = WhisperEngine(threads=3)
            halves = [pcm[PART_BYTES-96000:PART_BYTES], pcm[PART_BYTES:PART_BYTES+96000]]
            heard = [engine.transcribe(pack_wav(half), "", FAST_MODEL_ID, "sk")["text"] for half in halves]
            result["boundary_asr"] = heard
            require([words(text) for text in heard] == [words("Na námestí"), words("stojí modrý bicykel")],
                    "The two independently transcribed sides did not confirm the split: " + repr(heard))
        if args.boundary:
            require(not args.boundary.resolve().is_relative_to(args.card.resolve()), "Save the excerpt on the PC.")
            with args.boundary.open("xb") as output:
                output.write(pack_wav(pcm[PART_BYTES-96000:PART_BYTES+96000]))
    if case == "interrupt":
        text = wav.with_suffix(".txt").read_text(encoding="utf8")
        require(words(text) == words(INTERRUPT_PHRASE), "Interrupted REC transcript differs, including first word.")
    print(json.dumps(result, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest="command", required=True)
    capture = subs.add_parser("watch")
    capture.add_argument("case", choices=("long", "short", "interrupt", "during-delete"))
    capture.add_argument("state", type=Path, help="paired dev Bridge state")
    capture.add_argument("trace", type=Path, help="new JSONL evidence file")
    check = subs.add_parser("verify")
    check.add_argument("trace", type=Path)
    check.add_argument("card", type=Path)
    check.add_argument("--download", type=Path)
    check.add_argument("--listing", type=Path)
    check.add_argument("--model-root", type=Path, help="independently transcribe three seconds on each side of the rollover")
    check.add_argument("--boundary", type=Path, help="write a new six-second excerpt for listening")
    args = parser.parse_args()
    (watch if args.command == "watch" else verify)(args)


if __name__ == "__main__":
    main()
