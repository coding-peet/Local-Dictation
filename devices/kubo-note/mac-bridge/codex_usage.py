"""Read the signed-in Codex account's usage without starting a model session."""
from contextlib import suppress
from dataclasses import dataclass
import json
import os
from queue import Empty, Queue
import subprocess
import threading
import time


@dataclass(frozen=True)
class UsageSnapshot:
    five_hours: tuple[int, int] | None
    weekly: tuple[int, int] | None
    sample_expires: int

    def legacy_window(self):
        window = self.five_hours or self.weekly
        if window:
            remaining, reset = window
            return remaining, reset or self.sample_expires
        return None


def _usage_window(bucket, duration, now):
    for key in ("primary", "secondary"):
        window = bucket.get(key)
        if not isinstance(window, dict) or window.get("windowDurationMins") != duration:
            continue
        used, reset = window.get("usedPercent"), window.get("resetsAt")
        if type(used) is not int:
            continue
        if reset is None:
            reset = 0
        if type(reset) is int and (reset == 0 or now < reset <= 0xffffffff):
            return max(0, min(100, 100 - used)), reset
    return None


def usage_snapshot(result, now):
    buckets = result.get("rateLimitsByLimitId")
    if isinstance(buckets, dict) and buckets:
        bucket = buckets.get("codex")
    else:
        bucket = result.get("rateLimits")
    if not isinstance(bucket, dict) or bucket.get("limitId", "codex") != "codex":
        return None
    five_hours = _usage_window(bucket, 300, now)
    weekly = _usage_window(bucket, 10_080, now)
    return UsageSnapshot(five_hours, weekly, now + 900) if five_hours or weekly else None


def read_codex_rate_limits():
    from bridge import codex_command

    env = {key: value for key, value in os.environ.items()
           if key not in ("OPENAI_API_KEY", "CODEX_API_KEY", "OPENAI_BASE_URL")}
    args = codex_command() + ["app-server", "--listen", "stdio://"]
    with subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL, text=True, encoding="utf-8", errors="replace",
                          env=env, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)) as process:
        messages = Queue()

        def read_messages():
            for line in process.stdout:
                with suppress(ValueError):
                    message = json.loads(line)
                    if isinstance(message, dict) and message.get("id") in (1, 2):
                        messages.put(message)
            messages.put(None)

        threading.Thread(target=read_messages, daemon=True).start()
        deadline = time.monotonic() + 15

        def send(message):
            process.stdin.write(json.dumps(message) + "\n")
            process.stdin.flush()

        def receive(request_id):
            reply = messages.get(timeout=max(0, deadline - time.monotonic()))
            if reply is None or reply.get("id") != request_id or not isinstance(reply.get("result"), dict):
                raise RuntimeError("Codex account usage is unavailable.")
            return reply["result"]

        try:
            send({"id": 1, "method": "initialize",
                  "params": {"clientInfo": {"name": "pala_note", "title": "Pala Note", "version": "1.0.0"}}})
            receive(1)
            send({"method": "initialized"})
            send({"id": 2, "method": "account/rateLimits/read"})
            return receive(2)
        finally:
            with suppress(OSError):
                process.stdin.close()
            try:
                process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                process.kill()


class CodexUsage:
    def __init__(self):
        self.lock = threading.Lock()
        self.snapshot = None
        self.sampled = 0
        self.next_check = 0
        self.refreshing = False

    def get(self, *, force=False):
        now = time.monotonic()
        with self.lock:
            if not self.refreshing and (force or now >= self.next_check):
                self.next_check = now + 300
                self.refreshing = True
                threading.Thread(target=self._refresh, daemon=True).start()
            if self.snapshot and now - self.sampled <= 900:
                utc = time.time()

                def live(window):
                    return window if window and (window[1] == 0 or window[1] > utc) else None

                five_hours = live(self.snapshot.five_hours)
                weekly = live(self.snapshot.weekly)
                if five_hours or weekly:
                    return UsageSnapshot(five_hours, weekly, self.snapshot.sample_expires)
        return None

    def _refresh(self):
        try:
            result = read_codex_rate_limits()
            snapshot = usage_snapshot(result, int(time.time()))
            with self.lock:
                self.snapshot = snapshot
                self.sampled = time.monotonic()
        except (OSError, ValueError, RuntimeError, Empty, subprocess.SubprocessError):
            # A missing sign-in or status outage must leave Note sync operational.
            pass
        finally:
            with self.lock:
                self.refreshing = False


codex_usage = CodexUsage()
