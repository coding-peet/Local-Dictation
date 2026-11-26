import asyncio
import json
from contextlib import contextmanager
import sys
import tempfile
import threading
import unittest
from unittest.mock import Mock, patch
from pathlib import Path

BRIDGE = Path(__file__).resolve().parents[1] / "mac-bridge"
sys.path.insert(0, str(BRIDGE))

from bridge import Bridge
from recorder_delivery import plan_digest, settle_task_actions
from test_sync_replay import InterruptedNote, DEVICE
from test_usb_sync import PREFIX, TOKEN
import usb_sync


class RecorderDeliveryTest(unittest.TestCase):
    def test_spoken_shopping_normalizes_ham_and_preserves_distinct_variants(self):
        from recorder_delivery import shopping_products
        self.assertEqual(shopping_products(["šunka", "šunku", "ŠUNKA", "údená šunka", "slanina"]),
                         ["šunka", "údená šunka", "slanina"])

    def test_long_caption_uses_configured_model_and_the_entire_note(self):
        from recorder_delivery import note_title
        details = "Dlhý úvod bez predmetu poznámky. Hlavná požiadavka je opraviť názov v zozname."
        with patch("bridge.codex", return_value='{"title":"Oprava názvu poznámky v zozname"}') as model:
            self.assertEqual(note_title(details), "Oprava názvu poznámky v zozname")
            self.assertIn(details, model.call_args.args[0])

    def test_reopened_plan_reuses_ai_caption_from_other_bridge(self):
        from recorder_delivery import prepare_delivery
        api = Mock()
        api.tasklists.return_value.get.return_value.execute.return_value = {"id": "account"}
        cfg = {"processing_mode": "kubo", "google_routes": {"general": {"id": "list", "title": "General"}}}
        body = "Toto je dlhý úvod bez predmetu poznámky. Hlavná požiadavka je opraviť názov v zozname."
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second, \
                patch("recorder_delivery._api") as connect, patch("recorder_delivery._verify_task_list"), \
                patch("recorder_delivery.note_title", return_value="Oprava názvu poznámky v zozname") as caption:
            connect.return_value.__enter__.return_value = api
            with prepare_delivery(Path(first), cfg, "note", 42, None, body, plan="") as plan:
                self.assertEqual(plan.title, caption.return_value)
                saved, digest = plan.plan, plan.digest
            with prepare_delivery(Path(second), cfg, "note", 42, None, body, plan=saved) as resumed:
                self.assertEqual((resumed.title, resumed.digest), (caption.return_value, digest))
                self.assertEqual(resumed.actions[0][1], body)
            caption.assert_called_once_with(body)

    def test_other_account_list_or_text_cannot_reuse_a_claim(self):
        def digest(account="account", target="target", text="dictation"):
            return plan_digest("MAC", 1073743000, "2026-11-25T10:00:00Z", text, account, target, "general")
        self.assertEqual(digest(), digest())
        self.assertEqual(len(digest()), 64)
        self.assertNotEqual(digest(), digest(account="different account"))
        self.assertNotEqual(digest(), digest(target="different list"))
        self.assertNotEqual(digest(), digest(text="different text"))

    def test_other_bridge_reads_after_the_first_provider_reply_was_lost(self):
        remote = {}
        writes = []
        actions = [("action", "dictation")]
        def write(key, text):
            writes.append(key)
            remote[key] = text
            raise TimeoutError("reply lost after commit")
        with self.assertRaises(TimeoutError):
            settle_task_actions(actions, "write", write, remote.get)
        self.assertTrue(settle_task_actions(actions, "reconcile", write, remote.get))
        self.assertEqual(writes, ["action"])

    def test_absent_provider_marker_after_uncertain_write_is_pending(self):
        writes = []
        self.assertFalse(settle_task_actions([("action", "dictation")], "reconcile",
                                           lambda k, v: writes.append(k), lambda k: None))
        self.assertEqual(writes, [])


class RecorderNote(InterruptedNote):
    def __init__(self, cut, *, modern=False):
        super().__init__(cut)
        self.modern = modern
        self.plans = {}
        self.claimed = {}
        self.delivered = []

    def write(self, raw):
        if self.offline:
            raise OSError("link gone")
        command, *args = raw[len(PREFIX):].decode().strip().split()
        if command == "WORK-PROTOCOL":
            self.reply("WORK2" if self.modern else "WORK1")
        elif command == "WORK-PLAN":
            note = int(args[0])
            if len(args) == 2:
                self.plans.setdefault(note, args[1])
            self.reply("PLAN " + self.plans.get(note, "-"))
        elif command == "WORK-NEXT":
            note = next((n for n in self.queue if n > int(args[0]) and n not in self.delivered), None)
            if note is None:
                self.reply("EMPTY RECHECK")
            else:
                self.reply(f"WORK {note} {'TEXT' if note in self.files else 'AUDIO'}")
        elif command == "WORK-CLAIM":
            note, digest = int(args[0]), args[1]
            assert note in self.files
            if note not in self.claimed:
                self.claimed[note] = digest
                self.reply("CLAIMED")
            else:
                self.reply("RECONCILE" if self.claimed[note] == digest else "CONFLICT")
        elif command == "WORK-DONE":
            note, digest = int(args[0]), args[1]
            assert self.claimed[note] == digest
            if note not in self.delivered:
                self.delivered.append(note)
            if self.cut == "DONE":
                self.cut = None
                raise OSError("Note committed delivery but its response was lost")
            self.reply(f"DELIVERED {note}")
        else:
            return super().write(raw)
        return len(raw)


class RecorderWifiTest(unittest.TestCase):
    def test_lost_wifi_provider_reply_reconciles_without_another_insert(self):
        delivered = set()
        modes = []
        digest = "c" * 64

        @contextmanager
        def plan(state, config, device, note, captured, text):
            self.assertEqual(device, DEVICE)
            self.assertEqual((note, captured, text), (42, None, "test note"))
            class Route:
                def settle(self, mode):
                    modes.append(mode)
                    if mode == "write":
                        assert not delivered
                        delivered.add(note)
                        raise TimeoutError("provider response lost after writing")
                    return note in delivered
            result = Route()
            result.digest = digest
            yield result

        doc = {"note": 42, "text": "test note"}
        with tempfile.TemporaryDirectory() as folder, patch("recorder_delivery.prepare_delivery", plan):
            root = Path(folder)
            before, after = root / "pc-a", root / "pc-b"
            before.mkdir()
            after.mkdir()
            first, second = Bridge(before, {}), Bridge(after, {})
            def post(bridge, stage, payload, device=DEVICE):
                return bridge.dispatch("/pala/recorder-" + stage, json.dumps(payload).encode(),
                                       "application/json", device=device)
            self.assertEqual(post(first, "plan", doc), {"digest": digest})
            with self.assertRaises(TimeoutError):
                post(first, "apply", {**doc, "digest": digest, "mode": "write"})
            self.assertEqual(post(second, "apply", {**doc, "digest": "d" * 64, "mode": "write"}),
                             {"confirmed": False})
            self.assertEqual(post(second, "apply", {**doc, "digest": digest, "mode": "reconcile"}),
                             {"confirmed": True, "digest": digest})
            with self.assertRaises(ValueError):
                post(second, "apply", {**doc, "digest": digest, "mode": "write"}, None)
        self.assertEqual(modes, ["write", "reconcile"])


class RecorderWorkerTest(unittest.IsolatedAsyncioTestCase):
    async def run_cut(self, transport, cut, state):
        from test_bluetooth_sync import GattPeer, BleLink

        card = RecorderNote(cut)
        stop = threading.Event()
        recognized = []
        writes = []
        remote = set()

        def recognize(audio):
            n = int.from_bytes(audio[-2:], "little")
            recognized.append(n)
            return {"text": f"Text from recording {n}"}

        @contextmanager
        def adapter(state, config, device, note, captured, text):
            assert text == f"Text from recording {note}"
            class Plan:
                digest = plan_digest(device, note, captured, text, "same-account", "same-list", "general")

                def settle(self, mode):
                    if mode == "write":
                        assert note not in remote
                        writes.append(note)
                        remote.add(note)
                        if cut == "PROVIDER" and note == 42:
                            card.queue.append(43)
                            card.offline = True
                            raise OSError("provider committed, bearer disconnected")
                    return note in remote

            yield Plan()

        async def connect(folder):
            card.offline = False
            bridge = Bridge(folder, {"processing_mode": "transcribe"})
            bridge.local_transcription_ready = lambda: True
            bridge.transcribe_local = recognize
            if transport == "USB":
                return await asyncio.to_thread(usb_sync._drain_link, card, TOKEN, bridge, stop, device=DEVICE)

            def exchange(frame):
                card.write(frame)
                card.read_until()
                return card.read_until()
            link = BleLink(GattPeer(TOKEN, b"B" * 16, mtu=247, command=exchange), asyncio.get_running_loop())
            await link.initialize(TOKEN)
            return await asyncio.to_thread(usb_sync.drain_bluetooth_backlog, link, TOKEN, bridge, DEVICE, stop)

        with patch("recorder_delivery.prepare_delivery", adapter):
            with self.assertRaises(OSError):
                await connect(state)
            self.assertEqual(recognized, [42])
            # A second PC has no ASR cache. Transfer to it only after text is on SD.
            if cut != "TEXT":
                state = state / "second"
                state.mkdir()
            await connect(state)
            await connect(state)
        self.assertEqual(card.commits, [42, 43])
        self.assertEqual(card.delivered, [42, 43])
        self.assertEqual(recognized, [42, 43])
        self.assertEqual(writes, [42, 43])

    async def test_reconnect_commits_a_then_b_over_usb_and_ble(self):
        for transport in ("USB", "BLE"):
            for cut in ("TEXT", "COMMIT", "PROVIDER"):
                with self.subTest(transport=transport, cut=cut), tempfile.TemporaryDirectory() as tmp:
                    await self.run_cut(transport, cut, Path(tmp))
