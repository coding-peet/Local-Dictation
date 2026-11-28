from contextlib import contextmanager
from copy import deepcopy
from dataclasses import replace
import json
import os
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from bridge import Bridge
from stage3_mcp_write_cases import CAPTURED, Delivery, END, Operation, Receipt, START, ZONE
from test_workspace_tasks_mcp import Response


WRITES = {"create_task", "create_event", "update_doc"}


class FakeProvider:
    """A shared provider store used across separate, freshly opened Bridge profiles."""

    def __init__(self, operation):
        self.operation = operation
        self.attempts = []
        self.remote = []
        self.fault = None
        self.visible = True
        self.revision = "revision-1"
        self.text = "\n"
        self.ranges = {}
        self.stale_revision = False
        self.bad_readback = False
        self.reply_with_other_id = False

    def connect(self, owner):
        def handle(service, packet):
            assert packet["method"] == "tools/call" and packet["jsonrpc"] == "2.0"
            assert service == self.operation.provider
            request = packet["params"]
            name = request["name"]
            arguments = request["arguments"]
            self.attempts.append((owner, name, deepcopy(arguments)))
            if name in WRITES:
                if self.fault == "approval":
                    return Response({"jsonrpc": "2.0", "method": "elicitation/create", "id": "approval",
                                     "params": {"message": "Fixture requires approval"}}, stream=True)
                if self.fault == "before":
                    raise TimeoutError("Connection lost before the fixture committed.")
                if self.fault == "ack-only":
                    data = {"id": "ack-without-readback"}
                else:
                    data = self.write(name, arguments)
                if self.fault == "after":
                    raise TimeoutError("Connection lost after the fixture committed.")
                if self.reply_with_other_id:
                    return Response({"jsonrpc": "2.0", "id": packet["id"] + 1,
                                     "result": {"structuredContent": data}})
            else:
                data = self.read(name, arguments)
            return Response({"jsonrpc": "2.0", "id": packet["id"], "result": {"structuredContent": data}},
                            stream=owner == "Bridge B")
        return handle

    def write(self, name, args):
        plan = self.operation
        if name == "create_task":
            assert args == {"taskListId": plan.target, "title": plan.title, "description": plan.description}
            row = {"id": f"mcp-task-{len(self.remote) + 1}", **args, "completed": False}
        elif name == "create_event":
            assert args == {"calendarId": plan.target, "summary": plan.title, "description": plan.description,
                            "startTime": START, "endTime": END, "timeZone": ZONE,
                            "notificationLevel": "NONE", "useDefaultReminders": False}
            row = {"id": f"mcp-event-{len(self.remote) + 1}", "summary": args["summary"],
                   "description": args["description"], "status": "confirmed",
                   "start": {"dateTime": "2026-12-04T08:30:00Z"},
                   "end": {"dateTime": "2026-12-04T09:00:00Z"}}
        else:
            assert name == "update_doc" and args["documentId"] == plan.target
            assert set(args) == {"documentId", "writeControl", "requests"}
            if self.stale_revision or args["writeControl"] != {"requiredRevisionId": self.revision}:
                raise RuntimeError("The synthetic revision changed before commit.")
            first, second = args["requests"]
            assert list(first) == ["insertText"] and list(second) == ["createNamedRange"]
            insert = first["insertText"]
            marker = second["createNamedRange"]
            assert insert["endOfSegmentLocation"] == {"tabId": "fixture-tab"}
            assert insert["text"] == plan.entry + "\n" and marker["name"] == plan.marker
            end = 1 + len(plan.entry.encode("utf-16-le")) // 2
            assert marker["range"] == {"startIndex": 1, "endIndex": end, "tabId": "fixture-tab"}
            self.text = insert["text"] + self.text
            self.ranges[marker["name"]] = {"namedRanges": [
                {"namedRangeId": "fixture-range-id", "ranges": [marker["range"]]},
            ]}
            self.revision = "revision-2"
            row = {"replies": [{}, {"createNamedRange": {"namedRangeId": "fixture-range-id"}}],
                   "writeControl": {"requiredRevisionId": self.revision}}
        self.remote.append(deepcopy(row))
        return row

    def read(self, name, args):
        plan = self.operation
        if name == "read_doc":
            assert args == {"documentId": plan.target}
            text = self.text.replace("Fixture", "Tampered", 1) if self.bad_readback else self.text
            if not self.visible:
                text = "\n"
            end = 1 + len(text.encode("utf-16-le")) // 2
            return {"content": {
                "documentId": plan.target, "title": plan.title, "revisionId": self.revision,
                "tabs": [{"tabProperties": {"tabId": "fixture-tab"}, "documentTab": {
                    "body": {"content": [{"endIndex": end, "paragraph": {"elements": [
                        {"startIndex": 1, "endIndex": end, "textRun": {"content": text}},
                    ]}}]}, "namedRanges": deepcopy(self.ranges) if self.visible else {},
                }}],
            }}
        if name in ("list_events", "list_tasks"):
            if name == "list_tasks":
                key = "tasks"
                assert args["filter"] == {"taskListId": plan.target, "state": "ALL"}
            else:
                key = "events"
                assert args["calendarId"] == plan.target and args["timeZone"] == ZONE
                assert args["startTime"] == START and args["endTime"] == END
            # A same-title unrelated item occurs on the first page.
            if "pageToken" not in args:
                return {key: [{"id": "other", "title": plan.title, "summary": plan.title,
                               "description": "Manual entry"}], "nextPageToken": "second-page"}
            assert args["pageToken"] == "second-page"
            return {key: deepcopy(self.remote) if self.visible else []}
        if name == "get_task":
            assert list(args) == ["taskId"]
        else:
            assert name == "get_event" and args["calendarId"] == plan.target
        row = next(row for row in self.remote if row["id"] == args.get("taskId", args.get("eventId")))
        if self.bad_readback:
            return {**row, "description": "Wrong content with the same resource identifier."}
        return deepcopy(row)

    def writes(self):
        return [(owner, tool) for owner, tool, _ in self.attempts if tool in WRITES]


class McpWriteContractTest(unittest.TestCase):
    def setUp(self):
        folder = TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.root = Path(folder.name)
        self.path_a = self.root / "bridge-a"
        self.path_b = self.root / "bridge-b"
        self.path_a.mkdir()
        self.path_b.mkdir()
        self.claim_path = self.root / "note.claim"
        self.receipt_path = self.root / "note.delivery"

    def claim(self, digest):
        helper = os.environ.get("PALA_TEST_MCP_NOTE_HELPER")
        if helper:
            return subprocess.run([helper, "claim", str(self.claim_path), digest],
                                  check=True, capture_output=True, text=True).stdout
        try:
            with self.claim_path.open("x", encoding="ascii") as stream:
                stream.write(digest)
            return "write"
        except FileExistsError:
            return "reconcile" if self.claim_path.read_text(encoding="ascii") == digest else "conflict"

    def complete(self, digest):
        helper = os.environ.get("PALA_TEST_MCP_NOTE_HELPER")
        if helper:
            subprocess.run([helper, "complete", str(self.claim_path), str(self.receipt_path), digest], check=True)
        else:
            self.assertEqual(self.claim_path.read_text(encoding="ascii"), digest)
            self.receipt_path.write_text(digest, encoding="ascii")

    def exchange(self, state, operation, provider, mode, note_digest=None):
        bridge = Bridge(state, {})
        peer = "Bridge A" if state == self.path_a else "Bridge B"
        delivery = Delivery(operation, provider.connect(peer))

        @contextmanager
        def prepare(folder, _, device, note, captured, body, **kwargs):
            self.assertEqual((folder, device, note, captured, body),
                             (state, "fixture-note", operation.note, CAPTURED, operation.body))
            yield delivery

        record = {"note": operation.note, "captured": CAPTURED, "text": operation.body}
        if mode:
            record.update(digest=note_digest, mode=mode)
        endpoint = "/pala/recorder-apply" if mode else "/pala/recorder-plan"
        with patch("recorder_delivery.prepare_delivery", prepare):
            result = bridge.dispatch(endpoint, json.dumps(record).encode(), "application/json",
                                     device="fixture-note")
        return result, delivery.receipt

    def case(self, service):
        for path in (self.claim_path, self.receipt_path):
            path.unlink(missing_ok=True)
        operation = Operation(service, "fixture-" + service)
        provider = FakeProvider(operation)
        plan, receipt = self.exchange(self.path_a, operation, provider, None)
        self.assertEqual(plan, {"digest": operation.digest})
        self.assertIsNone(receipt)
        return operation, provider

    def test_normal_write_verifies_online_readback(self):
        for service in ("tasks", "calendar", "docs"):
            with self.subTest(service=service):
                plan, provider = self.case(service)
                reply, receipt = self.exchange(self.path_a, plan, provider, self.claim(plan.digest), plan.digest)
                self.assertTrue(reply["confirmed"])
                self.assertIsInstance(receipt, Receipt)
                self.assertEqual((receipt.operation_digest, receipt.target), (plan.digest, plan.target))
                self.complete(plan.digest)
                self.assertEqual(self.receipt_path.read_text(encoding="ascii"), plan.digest)
                self.assertEqual(len(provider.remote), 1)

    def test_lost_reply_after_commit_second_bridge_reconciles_without_a_second_write(self):
        for service in ("tasks", "calendar", "docs"):
            with self.subTest(service=service):
                plan, provider = self.case(service)
                provider.fault = "after"
                reply, receipt = self.exchange(self.path_a, plan, provider, self.claim(plan.digest), plan.digest)
                self.assertFalse(reply["confirmed"])
                self.assertIsNone(receipt)
                self.assertFalse(self.receipt_path.exists())
                provider.visible = False
                reply, receipt = self.exchange(self.path_b, plan, provider, self.claim(plan.digest), plan.digest)
                self.assertFalse(reply["confirmed"])
                self.assertIsNone(receipt)
                provider.visible = True
                reply, receipt = self.exchange(self.path_b, plan, provider, self.claim(plan.digest), plan.digest)
                self.assertTrue(reply["confirmed"])
                self.assertEqual(receipt, Receipt(service, plan.account, plan.target, plan.digest,
                    {"tasks": "mcp-task-1", "calendar": "mcp-event-1", "docs": "fixture-range-id"}[service]))
                self.complete(plan.digest)
                self.assertEqual(len(provider.writes()), 1)
                self.assertEqual(len(provider.remote), 1)

    def test_lost_before_commit_and_unverified_ack_remain_pending(self):
        for service in ("tasks", "calendar", "docs"):
            for fault in ("before", "ack-only", "approval"):
                with self.subTest(service=service, fault=fault):
                    plan, provider = self.case(service)
                    provider.fault = fault
                    for bridge in (self.path_a, self.path_b):
                        reply, receipt = self.exchange(bridge, plan, provider, self.claim(plan.digest), plan.digest)
                        self.assertFalse(reply["confirmed"])
                        self.assertIsNone(receipt)
                    self.assertFalse(self.receipt_path.exists())
                    self.assertEqual(len(provider.writes()), 1)
                    self.assertFalse(provider.remote)

    def test_another_account_target_or_changed_content_cannot_take_over_a_claim(self):
        plan, provider = self.case("tasks")
        self.assertEqual(self.claim(plan.digest), "write")
        for changed in (replace(plan, account="fixture-other-owner"), replace(plan, target="fixture-other-list"),
                        replace(plan, body="Different synthetic text")):
            plan_b, _ = self.exchange(self.path_b, changed, provider, None)
            self.assertEqual(self.claim(plan_b["digest"]), "conflict")
            result, receipt = self.exchange(self.path_b, changed, provider, "reconcile", plan.digest)
            self.assertFalse(result["confirmed"])
            self.assertIsNone(receipt)
        self.assertEqual(provider.attempts, [])

    def test_marked_but_different_readback_is_not_a_receipt(self):
        for service in ("tasks", "calendar", "docs"):
            with self.subTest(service=service):
                plan, provider = self.case(service)
                provider.bad_readback = True
                self.assertFalse(self.exchange(self.path_a, plan, provider, self.claim(plan.digest), plan.digest)[0]["confirmed"])
                self.assertFalse(self.exchange(self.path_b, plan, provider, self.claim(plan.digest), plan.digest)[0]["confirmed"])
                self.assertEqual(len(provider.writes()), 1)

    def test_duplicate_markers_keep_the_claim_pending(self):
        for service in ("tasks", "calendar", "docs"):
            with self.subTest(service=service):
                plan, provider = self.case(service)
                provider.fault = "after"
                result, _ = self.exchange(self.path_a, plan, provider, self.claim(plan.digest), plan.digest)
                self.assertFalse(result["confirmed"])
                if service == "docs":
                    ranges = provider.ranges[plan.marker]["namedRanges"]
                    ranges.append({**ranges[0], "namedRangeId": "duplicate-range"})
                else:
                    provider.remote.append({**provider.remote[0], "id": "duplicate-item"})
                result, receipt = self.exchange(self.path_b, plan, provider, self.claim(plan.digest), plan.digest)
                self.assertFalse(result["confirmed"])
                self.assertIsNone(receipt)
                self.assertFalse(self.receipt_path.exists())
                self.assertEqual(len(provider.writes()), 1)

    def test_docs_stale_required_revision_leaves_the_document_unchanged(self):
        plan, provider = self.case("docs")
        provider.stale_revision = True
        for bridge in (self.path_a, self.path_b):
            self.assertFalse(self.exchange(bridge, plan, provider, self.claim(plan.digest), plan.digest)[0]["confirmed"])
        self.assertEqual(provider.text, "\n")
        self.assertEqual(provider.ranges, {})
        self.assertEqual(len(provider.writes()), 1)

    def test_wrong_jsonrpc_reply_id_prevents_acknowledgement(self):
        plan, provider = self.case("calendar")
        provider.reply_with_other_id = True
        self.assertFalse(self.exchange(self.path_a, plan, provider, self.claim(plan.digest), plan.digest)[0]["confirmed"])
        self.assertTrue(self.exchange(self.path_b, plan, provider, self.claim(plan.digest), plan.digest)[0]["confirmed"])
        self.assertEqual(len(provider.writes()), 1)


if __name__ == "__main__":
    unittest.main()
