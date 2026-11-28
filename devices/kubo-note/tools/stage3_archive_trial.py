"""Take paired Settings and read-only FAT32 snapshots for the disposable month trial."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import re
import subprocess

import zlib
import stage2_bench as bench
from stage3_card_fixture import check, fingerprint
from stage3_capture_trial import BUILD, pcm_bytes, require, timing

SETTINGS = ("sdTotalMiB", "sdUsedMiB", "pendingAudio", "pendingDelivery")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, obj):
    with path.open("x", encoding="utf8") as f:
        json.dump(obj, f, indent=2)


def mount(args):
    from serial.tools.list_ports import comports
    import usb_sync as usb
    from note_settings import EnrollmentStore
    record = EnrollmentStore(args.state, {}).devices().get(bench.DEV)
    require(record, "Pair dev Note first.")
    ports = [p.device for p in comports() if (p.vid, p.pid) == (0x303a, 0x1001)
             and usb.usb_identity(p.serial_number) == bench.DEV]
    require(len(ports) == 1, "Dev Note recorder interface unavailable.")
    with usb._open_link(ports[0], record["id"]) as link:
        usb._paired_session(link, (record["token"],))
        bench.check_target(usb._exchange(link, "BENCH status"), BUILD)
        response = usb._exchange(link, "BENCH trial-card")
        require(len(response) == 6 and response[:2] == ["BENCH", "CARD"] and
                all(v.isdecimal() for v in response[2:]), "Wait for Settings and SD accounting.")
        snapshot = dict(zip(SETTINGS, map(int, response[2:])))
        save(args.settings, dict(device=bench.DEV, build=BUILD, **snapshot))
        try:
            usb._expect_ok(link, "BENCH usb-storage")
        except usb.UsbReplyTimeout:
            print("Recorder detached before acknowledgement. Verify the MSC identity below.")
    print("Wait for Pala Note SD. Eject its volume after taking the snapshot.")


def volume(root):
    require(re.fullmatch(r"[A-Za-z]:[\\/]", str(root.resolve())), "Use the mounted dev Note volume root.")
    drive = root.resolve().drive
    command = """
$ErrorActionPreference='Stop'
$part=Get-Partition -DriveLetter 'DRIVE'
$disk=Get-CimInstance Win32_DiskDrive -Filter ("Index="+$part.DiskNumber)
$parent=(Get-PnpDeviceProperty -InstanceId $disk.PNPDeviceID -KeyName DEVPKEY_Device_Parent).Data
$volume=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='DRIVE:'"
[pscustomobject]@{instance=$disk.PNPDeviceID;parent=$parent;serial=$volume.VolumeSerialNumber;
format=$volume.FileSystem;free=[int64]$volume.FreeSpace;total=[int64]$volume.Size}|ConvertTo-Json -Compress
""".replace("DRIVE", drive[0])
    obj = json.loads(subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", command], text=True))
    require(obj["parent"].upper() == r"USB\VID_303A&PID_0002\70041DD816A8" and
            obj["instance"].upper().startswith(r"USBSTOR\DISK&VEN_PALA&PROD_PALA_NOTE_SD") and
            obj["format"] == "FAT32" and obj["serial"] and 0 < obj["total"] < 2500000000, "Unexpected card or hardware.")
    return obj


def snapshot(manifest, card, phase, stats, disk, baseline=None, confirmation=None, survivor=None):
    target = manifest["target_month"]
    require(stats["build"] == BUILD and stats["device"] == bench.DEV, "Unexpected Settings source.")
    issues = check(manifest, card, phase, allow_other_files=True)
    require(not any(issues.values()), f"Card manifest mismatch: {issues}")
    index = {p.relative_to(card).as_posix(): fingerprint(p) for folder in ("recordings", ".pala-work")
             for p in (card / folder).rglob("*") if p.is_file()}
    target_prefix = f"recordings/{target}/"
    expected = manifest[phase]
    survivor_files = set()
    if survivor is not None:
        note = survivor["note"]
        matches = list((card / "recordings").glob(f"*/note_{note}.wav"))
        require(len(matches) == 1, "The recording made during deletion must survive once.")
        audio = pcm_bytes(matches[0])
        require(len(audio) == survivor["frames"] * 2 and f"{zlib.crc32(audio):08x}" == survivor["crc32"],
                "Surviving capture differs from microphone.")
        require((card / ".pala-work" / f"{note}.job").is_file(), "Missing pending work for surviving REC.")
        survivor_files = {p.relative_to(card).as_posix() for p in matches[0].parent.glob(f"note_{note}.*")}
    unexpected = [p for p in index if p.startswith(target_prefix) and p not in expected and p not in survivor_files]
    require(not unexpected, "Unrelated files in the disposable month. Do not approve deletion.")
    pending = manifest["pending"]
    if phase == "before":
        require(stats["pendingAudio"] == pending["audio"] and stats["pendingDelivery"] == pending["delivery"],
                "Displayed device queue differs from the fixture.")
        html = confirmation or ""
        for field in (f"Delete {target[:4]}-{target[4:]}?", f"Pending audio: {pending['audio']}</p>",
                      f"Pending delivery: {pending['delivery']}</p>", f"name='month' value='{target}'",
                      "name='legacy' value='0'"):
            require(field in html, "Wrong or incomplete confirmation page.")
    else:
        require(baseline and baseline["phase"] == "before" and baseline["target"] == target, "Wrong baseline.")
        require(baseline["disk"]["serial"] == disk["serial"] and baseline["disk"]["total"] == disk["total"],
                "FAT32 volume identity changed.")
        require(stats["pendingAudio"] == int(survivor is not None) and stats["pendingDelivery"] == 0,
                "Queue still includes deleted work or is missing the new REC.")
        previous = baseline["index"]
        removed = set(previous) - set(index)
        require(removed == set(manifest["removed"]), "Unrelated deletion or surviving selected file.")
        require(all(index[k] == previous[k] for k in previous if k not in removed), "A preserved file changed.")
        require(sum(previous[k]["bytes"] for k in removed) == manifest["logical_bytes_to_remove"], "Byte delta mismatch.")
        require(stats["sdTotalMiB"] == baseline["settings"]["sdTotalMiB"], "Settings card capacity changed.")
        require(stats["sdUsedMiB"] < baseline["settings"]["sdUsedMiB"] and disk["free"] > baseline["disk"]["free"],
                "Expected positive free-space gain in Settings and on FAT32.")
    return dict(target=target, phase=phase, disk=disk, settings=stats, index=index,
                free_gain_bytes=disk["free"] - baseline["disk"]["free"] if baseline else None,
                logical_bytes=manifest["logical_bytes_to_remove"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest="command", required=True)
    switch = subs.add_parser("mount")
    switch.add_argument("state", type=Path)
    switch.add_argument("settings", type=Path, help="new local settings snapshot")
    for phase in ("before", "after-delete"):
        p = subs.add_parser(phase)
        for arg in ("card", "manifest", "settings", "output"):
            p.add_argument(arg, type=Path)
        p.add_argument("confirmation" if phase == "before" else "baseline", type=Path)
        if phase == "after-delete":
            p.add_argument("--survivor-trace", type=Path, help="short REC made during deletion")
    args = parser.parse_args()
    if args.command == "mount":
        return mount(args)
    require(not args.output.resolve().is_relative_to(args.card.resolve()), "Save the report on the PC.")
    manifest = read_json(args.manifest)
    digest = sha256(args.manifest.read_bytes()).hexdigest()
    baseline = read_json(args.baseline) if args.command == "after-delete" else None
    if baseline:
        require(baseline["manifest_sha256"] == digest, "Different fixture.")
    html = args.confirmation.read_text(encoding="utf8") if args.command == "before" else None
    survivor = None
    if getattr(args, "survivor_trace", None):
        rows = [json.loads(line) for line in args.survivor_trace.read_text(encoding="ascii").splitlines()]
        require(rows[0] == ["trial", "during-delete", bench.DEV, BUILD], "Wrong survivor trace.")
        survivor = timing(rows, "during-delete")
        require(rows[-1] == ["finished", survivor], "Incomplete survivor trace.")
    result = snapshot(manifest, args.card, args.command, read_json(args.settings), volume(args.card), baseline, html, survivor)
    result["manifest_sha256"] = digest
    save(args.output, result)
    print(json.dumps({k: v for k, v in result.items() if k not in ("index", "disk")}, indent=2))


if __name__ == "__main__":
    main()
