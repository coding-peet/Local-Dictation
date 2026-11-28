"""Build and verify a disposable Pala Note Stage 3 SD card fixture on a PC."""

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys
import wave
from zipfile import ZIP_DEFLATED, ZipFile
import zlib

RATE = 16000
TARGET = "202611"
NEIGHBOR = "202612"
CASES = (
    ("2026-11-10T09:30:00Z", 34, "audio"),
    ("2026-11-11T09:30:00Z", 35, "audio"),
    ("2026-11-12T09:30:00Z", 36, "delivery"),
    ("2026-11-13T09:30:00Z", 37, "delivered"),
    ("2026-12-01T08:00:00Z", 5, "delivered"),
)
PHASES = ("before", "after-delete")


def fingerprint(path):
    with path.open("rb") as stream:
        return {"bytes": path.stat().st_size, "sha256": hashlib.file_digest(stream, "sha256").hexdigest()}


def inventory(root):
    return {
        path.relative_to(root).as_posix(): fingerprint(path)
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def seed_note(root, ordinal, stamp, seconds, state):
    year, month = map(int, stamp[:7].split("-"))
    note = (1 << 30) | (ordinal << 10) | ((year - 2020) * 12 + month)
    folder = root / "recordings" / f"{year:04d}{month:02d}"
    folder.mkdir(parents=True, exist_ok=True)
    base = folder / f"note_{note}"
    audio = base.with_suffix(".wav")
    second = b"".join(struct.pack("<h", (index * (31 + ordinal * 2)) % 8192 - 4096)
                      for index in range(RATE))
    pcm = second * seconds
    with wave.open(str(audio), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(pcm)
    base.with_suffix(".wav.meta").write_bytes(
        f"created_utc={stamp}\ntag=Inbox\nsynced={int(state != 'audio')}\n".encode("ascii"))
    if state != "audio":
        base.with_suffix(".txt").write_bytes(
            f"Stage 3 synthetic transcript {ordinal}.\nSkúška kontrolných súčtov.\n".encode("utf-8"))
    if state == "delivered":
        base.with_suffix(".delivery").write_bytes(
            hashlib.sha256(f"fixture only, note {note}".encode("ascii")).hexdigest().encode("ascii"))
    else:
        (root / ".pala-work" / f"{note}.job").write_bytes(struct.pack("<i4s21s", note, b"UTC1", stamp.encode("ascii")))
    return {
        "note": note, "captured_utc": stamp, "state": state, "wav": audio.relative_to(root).as_posix(),
        "samples": RATE * seconds, "pcm_crc32": f"{zlib.crc32(pcm):08x}",
    }


def build(destination):
    """Create a new bundle. Existing paths, including an SD root, are refused."""
    destination.mkdir()
    root = destination / "card-root"
    (root / ".pala-work").mkdir(parents=True)
    (root / ".pala-recordings-v2").write_bytes(struct.pack("<I", 1))
    notes = [seed_note(root, index, *case) for index, case in enumerate(CASES, 1)]
    (root / "recordings" / TARGET / "stage3-fixture.log").write_bytes(
        "Generated on PC for Stage 3.\nSkúška: <script>read as plain text</script>\n".encode("utf-8"))

    before = inventory(root)
    removed = sorted(
        [name for name in before if name.startswith(f"recordings/{TARGET}/")] +
        [f".pala-work/{row['note']}.job" for row in notes if row["state"] != "delivered"])
    after = {path: item for path, item in before.items() if path not in removed}
    manifest = {
        "card_fixture_version": 1,
        "target_month": TARGET, "preserved_month": NEIGHBOR,
        "pending": {"audio": 2, "delivery": 1},
        "logical_bytes_to_remove": sum(before[name]["bytes"] for name in removed),
        "notes": notes, "before": before, "after-delete": after, "removed": removed,
    }
    (destination / "manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    for phase in PHASES:
        (destination / f"SHA256-{phase}.txt").write_text(
            "".join(f"{item['sha256']}  {name}\n" for name, item in manifest[phase].items()),
            encoding="ascii", newline="\n")
    with ZipFile(destination / "stage3-card-fixture.zip", "w", ZIP_DEFLATED) as archive:
        for name in before:
            archive.write(root / name, "card-root/" + name)
        for name in ("manifest.json", "SHA256-before.txt", "SHA256-after-delete.txt"):
            archive.write(destination / name, name)
    return manifest


def check(manifest, root, phase, allow_other_files=False):
    if not root.is_dir():
        raise ValueError(f"Card root is not a directory: {root}")
    expected = manifest[phase]
    missing = sorted(name for name in expected if not (root / name).is_file())
    changed = sorted(name for name, item in expected.items()
                     if name not in missing and fingerprint(root / name) != item)
    forbidden = {name for name in manifest["removed"] if (root / name).exists()} if phase == "after-delete" else set()
    if not allow_other_files:
        forbidden.update(path.relative_to(root).as_posix() for path in root.rglob("*")
                         if path.is_file() and path.relative_to(root).as_posix() not in expected)
    return {"missing": missing, "changed": changed, "unexpected": sorted(forbidden)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    generator = commands.add_parser("build", help="create a new bundle directory")
    generator.add_argument("output", type=Path)
    verifier = commands.add_parser("check", help="verify a card directory without writing to it")
    verifier.add_argument("manifest", type=Path)
    verifier.add_argument("root", type=Path)
    verifier.add_argument("--phase", choices=PHASES, required=True)
    verifier.add_argument("--allow-other-files", action="store_true",
                          help="allow files created by the device while still checking every fixture file")
    args = parser.parse_args()
    try:
        if args.command == "build":
            manifest = build(args.output)
            print(f"Built {len(manifest['before'])} fixture files and both checksum lists in {args.output}")
            return 0
        manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
        issues = check(manifest, args.root, args.phase, args.allow_other_files)
    except (OSError, ValueError) as exc:
        print(exc, file=sys.stderr)
        return 2
    if any(issues.values()):
        print(json.dumps(issues, indent=2), file=sys.stderr)
        return 1
    print(f"{args.phase}: verified {len(manifest[args.phase])} fixture files in {args.root}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
