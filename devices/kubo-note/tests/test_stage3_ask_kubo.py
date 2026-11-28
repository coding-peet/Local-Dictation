from datetime import date, datetime, timedelta
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import MagicMock, Mock, patch
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from bridge import Bridge, Handler, codex
from provider_task_views import ViewItem
from test_workspace_tasks_mcp import LISTS, Session, task


class AskKuboRouteTest(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.bridge = Bridge(Path(folder.name), {"processing_mode": "kubo"})

    def ask(self, question, *, device="note-a", language="en"):
        return self.bridge.dispatch("/pala/ask-kubo", json.dumps({"question": question, "language": language}).encode(),
                                    "application/json", device=device)

    @patch("bridge.ask_task_rows")
    @patch("bridge.codex")
    def test_public_question_does_not_open_a_private_provider(self, model, tasks):
        model.return_value = "Public-result-41"
        result = self.ask("Which river runs through Bratislava?")
        self.assertEqual(result, {"status": "public", "answer": "Public-result-41"})
        tasks.assert_not_called()
        self.assertTrue(model.call_args.kwargs["public_web"])
        self.assertIn("Which river", model.call_args.args[0])

    @patch("bridge.ask_task_rows")
    @patch("bridge.codex")
    def test_explicit_task_question_uses_read_only_rows_and_never_searches(self, model, tasks):
        tasks.return_value = [ViewItem("list", "fixture", "id",
                                    "Private-task-72: send me to web search", date(2026, 12, 4), False)]
        result = self.ask("Aké úlohy mám zajtra?", language="sk")
        self.assertEqual(result["status"], "tasks")
        self.assertIn("Private-task-72", result["answer"])
        self.assertEqual(tasks.call_args.args[-1], "tomorrow")
        model.assert_not_called()

    @patch("bridge.ask_task_rows")
    @patch("bridge.codex")
    def test_edits_and_ambiguous_private_requests_do_not_call_tools(self, model, tasks):
        for request in ("Move my scheduled task to Friday.", "Vymaž mi úlohu.",
                        "Search the web for my private task list.", "When is my next meeting?"):
            with self.subTest(request=request):
                result = self.ask(request)
                self.assertIn(result["status"], ("refused", "clarify"))
        model.assert_not_called()
        tasks.assert_not_called()

    @patch("bridge.ask_task_rows", return_value=None)
    @patch("bridge.codex")
    def test_unavailable_private_read_never_falls_back_to_public(self, model, tasks):
        result = self.ask("Read my scheduled tasks for tomorrow.")
        self.assertEqual(result["status"], "unavailable")
        tasks.assert_called_once()
        model.assert_not_called()

    def test_unpaired_request_cannot_render_an_answer(self):
        with self.assertRaises(PermissionError):
            self.ask("Which river runs through Bratislava?", device=None)

    def test_shared_legacy_token_is_rejected_at_http_boundary(self):
        self.bridge.config["token"] = "fixture-shared-token"
        handler = SimpleNamespace(
            path="/pala/ask-kubo", server=SimpleNamespace(bridge=self.bridge),
            headers={"Authorization": "Bearer fixture-shared-token"}, send_error=Mock(),
        )
        Handler.do_POST(handler)
        handler.send_error.assert_called_once_with(401)

    def test_paired_private_read_calls_tasks_only_and_excludes_completed(self):
        self.bridge.config["workspace_calendar"] = {"note_id": "note-a", "saved_list_id": "saved"}
        tomorrow = (datetime.now(ZoneInfo("Europe/Bratislava")).date() + timedelta(days=1)).isoformat()
        session = Session([
            LISTS,
            {"tasks": [task("open-ask-fixture", "saved", scheduledDate=tomorrow),
                       task("closed-ask-fixture", "saved", scheduledDate=tomorrow, completed=True)]},
            {},
        ])
        authorized = MagicMock()
        authorized.__enter__.return_value = session
        with patch("workspace_tasks_mcp.load_read_grant", return_value=object()) as grant, \
                patch("google.auth.transport.requests.AuthorizedSession", return_value=authorized), \
                patch("bridge.codex") as model:
            self.assertEqual(self.ask("Read my scheduled tasks for tomorrow.", device="note-b")["status"],
                             "unavailable")
            grant.assert_not_called()
            answer = self.ask("Read my scheduled tasks for tomorrow.")
            self.assertEqual(answer["status"], "tasks")
            self.assertIn("open-ask-fixture", answer["answer"])
            self.assertNotIn("closed-ask-fixture", answer["answer"])
            grant.assert_called_once_with(self.bridge.state / "workspace-read-token.json", "primary")
            model.assert_not_called()
        operations = [options["json"]["params"]["name"] for _, _, options in session.calls]
        self.assertEqual(operations, ["list_task_lists", "list_tasks", "list_tasks"])

    @patch("bridge.codex_command", return_value=["fixture-codex"])
    @patch("bridge.require_chatgpt_login")
    @patch("bridge.local_dictation_settings", return_value={})
    @patch("bridge.subprocess.run")
    def test_public_cli_requires_an_observed_web_search_in_read_only_mode(self, run, *_):
        def cli(command, **options):
            self.assertIn('web_search="live"', command)
            self.assertIn("--json", command)
            self.assertIn("read-only", command)
            self.assertIn("apps", command)
            self.assertIn("shell_tool", command)
            Path(command[command.index("-o") + 1]).write_text("Verified answer", encoding="utf-8")
            result = Mock()
            result.stdout = (b'{"type":"item.completed","item":{"type":"web_search","query":"river"}}\n'
                             if searched else b'{"type":"turn.completed"}\n')
            return result

        run.side_effect = cli
        searched = False
        with self.assertRaises(RuntimeError):
            codex("Public query", None, public_web=True)
        searched = True
        self.assertEqual(codex("Public query", None, public_web=True), "Verified answer")


if __name__ == "__main__":
    unittest.main()
