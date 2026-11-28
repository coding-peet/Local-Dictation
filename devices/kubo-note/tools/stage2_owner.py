"""Decode first-two-take VFS owner measurements from the development recorder."""
from copy import deepcopy
import hashlib
from pathlib import Path
import subprocess


OWNER_BUILD = "2.0.0-recorder-vfsowner2-stall6500"
PHASES = {"wav_open": "own_open", "header_write": "own_header"}
COUNT_FIELDS = ("edge", "waits", "omitted", "context_drops")
WAIT_FIELDS = ("edge", "mutex", "holder", "waiter_pc", "fat_pc", "work", "fat_op",
               "opcode", "wait_us", "held_us", "host_us", "event_us", "flags", "delay_us",
               "name0", "name1", "name2", "name3")
OWNER_PAGES = {name: fields for base in PHASES.values() for name, fields in
               ((base, COUNT_FIELDS), (base + "0", WAIT_FIELDS), (base + "1", WAIT_FIELDS))}
WORK = ("unknown", "SD mount", "recording layout", "Home", "catalog", "recovery", "sync queue",
        "USB", "sync", "storage audit", "settings", "recorder spool", "archive", "boot",
        "weather forecast", "holiday calendar", "delivery status", "clock", "free space")
FAT = ("unknown", "f_open", "f_read", "f_write", "f_opendir", "f_readdir", "f_stat", "f_close",
       "f_closedir", "f_sync", "f_lseek", "f_unlink", "f_rename", "f_mkdir", "f_truncate", "f_getfree",
       "VFS open", "VFS read", "VFS write", "VFS close", "VFS stat", "VFS fstat",
       "VFS opendir", "VFS readdir", "VFS closedir", "fdopen", "setvbuf", "fwrite")
QUEUE, RELEASE, WORK_AT_BLOCK, FAT_AT_BLOCK = 1, 2, 4, 8
CMD_AT_BLOCK, EVENT_AT_BLOCK, WORK_CHANGED, FAT_CHANGED = 16, 32, 64, 128
OPCODE_CHANGED, UNCERTAIN, DELAY_AT_BLOCK, DELAY_OBSERVED = 256, 512, 1024, 2048
ALL_FLAGS = 4095


def _has_fields(row, fields):
    return (isinstance(row, dict) and row.keys() == set(fields)
            and all(type(n) is int and 0 <= n <= 0xffffffff for n in row.values()))


def _decode_wait(row):
    flags = row["flags"]
    matched = bool(flags & RELEASE)
    held, host, event, delay = (row[k] for k in ("held_us", "host_us", "event_us", "delay_us"))
    if (flags & ~ALL_FLAGS or bool(flags & QUEUE) != bool(row["mutex"])
            or (matched and (flags & UNCERTAIN or not row["mutex"] or not row["holder"]))
            or (not matched and any((held, host, event, delay)))
            or not event <= host <= held <= row["wait_us"] or delay > held
            or (delay and not flags & DELAY_OBSERVED)
            or (flags & DELAY_AT_BLOCK and not flags & DELAY_OBSERVED)
            or (flags & WORK_AT_BLOCK and not row["work"])
            or (flags & FAT_AT_BLOCK and not row["fat_op"])
            or (flags & CMD_AT_BLOCK and row["opcode"] == 0xffffffff)):
        raise ValueError("Owner duration or identity flags are inconsistent.")
    packed = b"".join(row["name" + str(i)].to_bytes(4, "big") for i in range(4))
    name = packed.partition(b"\x00")[0]
    if len(name) == 16 or any(not 32 <= b < 127 for b in name):
        raise ValueError("Invalid RTOS task name in retained owner data.")
    return {
        "task": name.decode("ascii") or None, "task_handle": row["holder"] or None,
        "mutex": row["mutex"] or None, "waiter_pc": row["waiter_pc"] or None,
        "file_caller_pc": row["fat_pc"] or None, "wait_us": row["wait_us"],
        "work": WORK[row["work"]] if row["work"] < len(WORK) else f"unknown({row['work']})",
        "file_operation": FAT[row["fat_op"]] if row["fat_op"] < len(FAT) else f"unknown({row['fat_op']})",
        "sd_opcode": None if row["opcode"] == 0xffffffff else row["opcode"],
        "observed_at_block": [label for bit, label in ((WORK_AT_BLOCK, "work"), (FAT_AT_BLOCK, "file_operation"),
                              (CMD_AT_BLOCK, "sd_command"), (EVENT_AT_BLOCK, "sd_event"),
                              (DELAY_AT_BLOCK, "task_delay")) if flags & bit],
        "changed_during_wait": [label for bit, label in ((WORK_CHANGED, "work"), (FAT_CHANGED, "file_operation"),
                                (OPCODE_CHANGED, "sd_opcode")) if flags & bit],
        "release": "matched" if matched else ("ambiguous" if flags & UNCERTAIN else "unobserved"),
        "block_to_give_us": held if matched else None,
        "holder_sd_host_us": host if matched else None,
        "holder_sd_event_us": event if matched else None,
        "holder_task_delay_us": delay if matched else None,
        "give_to_waiter_return_or_reblock_us": row["wait_us"] - held if matched else None,
    }


def _decode_phase(trace, page):
    edge = trace["summary"]["edge"]
    counts = trace.get(page)
    slots = [trace.get(page + str(index)) for index in range(2)]
    if not _has_fields(counts, COUNT_FIELDS) or not all(_has_fields(r, WAIT_FIELDS) for r in slots):
        raise ValueError("Incomplete owner pages.")
    io = trace.get(page.replace("own_", "io_"))
    if not io or counts["edge"] != io.get("edge") or counts["edge"] not in (0, edge):
        raise ValueError("Owner and writer phase have different REC edges.")
    waits, omitted = counts["waits"], counts["omitted"]
    if omitted != max(0, waits - 2) or any(r["edge"] != counts["edge"] for r in slots):
        raise ValueError("Owner count or slot identity is inconsistent.")
    if not counts["edge"] and (any(counts.values()) or any(any(r.values()) for r in slots)):
        raise ValueError("Owner data without a REC edge.")
    for unused in slots[min(2, waits):]:
        if any(v for k, v in unused.items() if k != "edge"):
            raise ValueError("Uncounted owner observation.")
    retained = [_decode_wait(r) for r in slots[:min(2, waits)]]
    if len(retained) == 2 and retained[0]["wait_us"] < retained[1]["wait_us"]:
        raise ValueError("Owner waits are out of order.")
    if not counts["edge"] or not io.get("valid"):
        status = "incomplete"
    elif waits > io["lock_blocks"]:
        raise ValueError("VFS owner waits exceed all measured writer mutex blocks.")
    elif waits:
        status = "observed"
    elif io["vfs_lock_us"]:
        status = "writer_vfs_wait_without_owner_observation"
    else:
        status = "no_vfs_mutex_block_observed"
    return {"status": status, "wait_count": waits, "omitted_shorter_waits": omitted,
            "context_drops": counts["context_drops"], "writer_phase_us": io.get("total_us"),
            "writer_vfs_lock_us": io.get("vfs_lock_us"), "longest_waits": retained}


def storage_owner_result(trace):
    result = {"elf_sha256": trace.get("elf_sha256")}
    for phase, base in PHASES.items():
        try:
            result[phase] = _decode_phase(trace, base)
        except ValueError as error:
            result[phase] = {"status": "inconsistent", "detail": str(error), "longest_waits": []}
    return result


def _xtensa_callsite(pc):
    # Windowed return addresses can carry the register-window increment in bits 31:30.
    # A caller's return address points at the instruction after the call.
    if not pc:
        return None
    address = ((pc & 0x3fffffff) | 0x40000000) - 1
    if 0x40000000 <= address < 0x40400000 or 0x42000000 <= address < 0x44000000:
        return address
    return None


def symbolize_owner_result(owner, elf_path, addr2line_path):
    """Resolve PCs only against the ELF identified by the running application descriptor."""
    elf = Path(elf_path)
    if hashlib.sha256(elf.read_bytes()).hexdigest() != owner.get("elf_sha256"):
        raise ValueError("Runtime ELF SHA-256 differs from the supplied ELF.")
    result = deepcopy(owner)
    waits = [wait for name in PHASES for wait in result[name]["longest_waits"]]
    keys = ("waiter_pc", "file_caller_pc")
    addresses = sorted({a for w in waits for k in keys if (a := _xtensa_callsite(w[k])) is not None})
    symbols = {}
    if addresses:
        lines = subprocess.run([str(addr2line_path), "-f", "-C", "-e", str(elf), *map(hex, addresses)],
                               capture_output=True, check=True, text=True, errors="replace",
                               timeout=30).stdout.splitlines()
        if len(lines) != 2 * len(addresses):
            raise ValueError("Unexpected addr2line response for retained PCs.")
        for address, function, source in zip(addresses, lines[::2], lines[1::2]):
            if function != "??" or not source.startswith("??"):
                symbols[address] = {"function": function, "source": source, "callsite": hex(address)}
    for wait in waits:
        for key in keys:
            wait[key + "_symbol"] = symbols.get(_xtensa_callsite(wait[key]))
    return result
