"""Publish transient, content-free activity for the Local Dictation card."""
from contextlib import contextmanager, nullcontext, suppress
from datetime import datetime, timezone
import json
import os
import threading


class BridgeActivity:
    def __init__(self, state, transport):
        self.transport = transport
        self.pid = os.getpid()
        self.path = state / f"bridge-activity-{transport}-{self.pid}.json"
        self.temporary = self.path.with_suffix(".tmp")
        self.active = {}
        self.lock = threading.Lock()

    def _publish(self):
        try:
            if not self.active:
                self.path.unlink(missing_ok=True)
                return
            phase, changed = self.active[next(reversed(self.active))]
            self.temporary.write_text(json.dumps({
                "Pid": self.pid,
                "Transport": self.transport,
                "Phase": phase,
                "UpdatedAt": changed,
            }), encoding="utf-8")
            self.temporary.replace(self.path)
        except OSError:
            # Status reporting must leave transcription and delivery independent.
            with suppress(OSError):
                self.temporary.unlink(missing_ok=True)

    @contextmanager
    def step(self, phase):
        operation = object()
        with self.lock:
            self.active[operation] = (phase, datetime.now(timezone.utc).isoformat())
            self._publish()
        try:
            yield
        finally:
            with self.lock:
                del self.active[operation]
                self._publish()


def activity_step(activity, phase):
    return activity.step(phase) if activity is not None else nullcontext()
