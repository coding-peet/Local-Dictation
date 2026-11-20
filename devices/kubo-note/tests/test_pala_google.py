from pathlib import Path
import sys
from contextlib import nullcontext, redirect_stdout
from io import StringIO
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from pala_google import DeliveryQueue, classify_transcript


class PalaGoogleTests(unittest.TestCase):
    def test_routing_requires_an_explicit_personal_intent(self):
        self.assertEqual(classify_transcript("Úloha, skontrolovať modrý zápisník"),
                         ("tasks", ["skontrolovať modrý zápisník"]))
        self.assertEqual(classify_transcript("Pridaj na nákupný zoznam maslo a rožky."),
                         ("shopping", ["maslo", "rožky"]))
        self.assertEqual(classify_transcript("Ulož do Google Docs nápad na výlet."),
                         ("docs", ["nápad na výlet."]))
        self.assertEqual(classify_transcript("Pridaj mi termín 11. decembra o tretej.")[0],
                         "calendar")
        self.assertEqual(classify_transcript("Dnes som sa vrátil neskoro."),
                         ("general", ["Dnes som sa vrátil neskoro."]))
        self.assertEqual(classify_transcript("vprité mlieko na nákupný zoznam"),
                         ("general", ["vprité mlieko na nákupný zoznam"]))

    def test_mixed_explicit_work_is_held_locally(self):
        self.assertEqual(classify_transcript(
            "Poznámka: osobný nápad. Pridaj mi pracovnú úlohu poslať ponuku.")[0], "work")
        self.assertEqual(classify_transcript("Ulož do OneNotu zápis z porady.")[0], "work")

    def test_named_destination_requires_an_exact_binding(self):
        self.assertEqual(classify_transcript("Zapíš do zoznamu Môj nákup: rožky."),
                         ("list:moj nakup", ["rožky."]))
        self.assertEqual(classify_transcript("Ulož do dokumentu \"Kubo nápady\" výlet do Žiliny."),
                         ("document:kubo napady", ["výlet do Žiliny."]))
        self.assertEqual(classify_transcript("Do zoznamu niekam niečo"), ("needs-target", ["Do zoznamu niekam niečo"]))
        send = Mock(return_value="verified")
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            action, = queue.stage("fixture", 23, None, "Do zoznamu Môj nákup: rožky.")
            general = {"id": "general", "title": "General"}
            self.assertEqual(queue.deliver(action, {"general": general}, send), "pending")
            send.assert_not_called()
            approved = {"id": "shopping", "title": "Môj nákup"}
            self.assertEqual(queue.deliver(action, {"general": general, "list:moj nakup": approved}, send), "done")
            send.assert_called_once_with("list:moj nakup", approved, None, "rožky.", action)

    def test_slovak_commands_can_arrive_without_accents(self):
        self.assertEqual(classify_transcript("Uloha, skontrolovať modrý zápisník."),
                         ("tasks", ["skontrolovať modrý zápisník"]))
        self.assertEqual(classify_transcript("Uloz do Google Docs výlet do Žiliny."),
                         ("docs", ["výlet do Žiliny."]))
        self.assertEqual(classify_transcript(
            "Poznamka: Osobný nápad. Pridaj mi pracovnu ulohu poslať ponuku.")[0], "work")
        self.assertEqual(classify_transcript("Pridaj mi termin zajtra o 10:00")[0],
                         "calendar")

    def test_same_saved_text_is_delivered_once_and_work_and_events_stay_local(self):
        send = Mock(return_value="google-id")
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            target = {"id": "tasks-list", "title": "Approved"}
            args = ("fixture-device", 20, "2026-11-19T23:15:00Z", "Úloha, skontrolovať zápisník")
            action, = queue.stage(*args)
            self.assertEqual(queue.stage(*args), [action])
            self.assertEqual(queue.deliver(action, {"tasks": target}, send), "done")
            self.assertEqual(queue.deliver(action, {"tasks": target}, send), "done")
            send.assert_called_once()
            for note, text, status in (
                (21, "Pridaj mi termín 11. decembra o tretej.", "needs-confirmation"),
                (22, "Pridaj mi pracovnú úlohu poslať ponuku.", "local"),
            ):
                key, = queue.stage("fixture-device", note, None, text)
                self.assertEqual(queue.deliver(key, {}, send), status)
            send.assert_called_once()

    def test_saved_task_can_be_renamed_or_deleted_without_redelivery(self):
        from pala_google import main

        api = Mock()
        tasklists = api.tasklists.return_value
        tasks = api.tasks.return_value
        target = {"id": "trial-list", "title": "Approved trial"}
        tasklists.list.return_value.execute.return_value = {"items": [target]}
        tasklists.get.return_value.execute.return_value = target
        provider = {}
        tasks.list.return_value.execute.side_effect = lambda: {"items": list(provider.values())}
        tasks.get.return_value.execute.side_effect = lambda: provider["task-1"]

        def insert(*, tasklist, body):
            self.assertEqual(tasklist, target["id"])
            def execute():
                provider["task-1"] = {"id": "task-1", "status": "needsAction", **body}
                return provider["task-1"]
            return Mock(execute=execute)

        tasks.insert.side_effect = insert
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            (state / "config.json").write_text(
                '{"device_tokens": {"fixture-device": "fixture-token-not-used"}}', encoding="utf-8")
            saved = state / "note_20.txt"
            saved.write_text("Uloha: skontrolovať modrý zápisník", encoding="utf-8")
            args = ["pala_google.py", "--transcript", str(saved), "--note", "20",
                    "--task-list", target["title"], "--apply"]
            output = StringIO()
            with (patch("pair.STATE", state), patch.object(sys, "argv", args),
                  patch("pala_google._api", side_effect=lambda *_args: nullcontext(api)),
                  patch("bridge.Bridge.transcribe_local", side_effect=AssertionError("ASR must not run")),
                  redirect_stdout(output)):
                main()
                self.assertEqual(provider["task-1"]["title"], "skontrolovať modrý zápisník")
                provider["task-1"]["title"] = "Edited by Peter"
                main()
                self.assertEqual(provider["task-1"]["title"], "Edited by Peter")
                provider.clear()
                main()

        self.assertEqual(output.getvalue().splitlines(), ["Note 20: tasks: done."] * 3)
        tasks.insert.assert_called_once_with(
            tasklist="trial-list", body={"title": "skontrolovať modrý zápisník"})
        tasks.get.assert_called_once_with(tasklist="trial-list", task="task-1")
        self.assertEqual(provider, {})

    def test_general_list_keeps_the_complete_text_as_one_entry(self):
        from pala_google import deliver_google
        from zoneinfo import ZoneInfo

        api = Mock()
        target = {"id": "general-list", "title": "General"}
        api.tasklists.return_value.get.return_value.execute.return_value = target
        tasks = api.tasks.return_value
        tasks.list.return_value.execute.return_value = {}
        stored = {}

        def insert(*, tasklist, body):
            self.assertEqual(tasklist, target["id"])
            stored.update(id="record-1", **body)
            return Mock(execute=Mock(return_value=stored))

        tasks.insert.side_effect = insert
        tasks.get.return_value.execute.side_effect = lambda: stored
        text = ("Toto si len poznamenám na neskôr.\n"
                "Výmena filtra je potrebná cez víkend. Výmena filtra počká do soboty.")
        with patch("pala_google._api", return_value=nullcontext(api)):
            actual = deliver_google(Path("unused"), ZoneInfo("Europe/Bratislava"),
                                    "general", target, None, text, "fixture-action")
        self.assertEqual(actual, "record-1")
        tasks.insert.assert_called_once()
        self.assertEqual(stored["title"], "Výmena filtra")
        self.assertEqual(stored["notes"], text)

    def test_title_is_a_whole_short_dictation_or_a_short_phrase(self):
        from pala_google import task_presentation

        self.assertEqual(task_presentation("Poliať akváriové rastliny."), ("Poliať akváriové rastliny.", ""))
        for body, expected in (
            ("No dobre, chcel som si len teraz niečo poznamenať. "
             "Krevetky a akváriové rastliny presuniem do novej nádrže. "
             "Akváriové rastliny skontrolujem večer.", "Akváriové rastliny"),
            ("No dobre, toto je ešte taká vec, ktorú si sem odložím. "
             "Kalibrácia teplomera bude cez víkend. "
             "Pred montážou je potrebná kalibrácia teplomera.", "Kalibrácia teplomera"),
        ):
            with self.subTest(expected=expected):
                title, details = task_presentation(body)
                self.assertEqual(title, expected)
                self.assertEqual(details, body)

    def test_long_topic_heading_does_not_copy_a_sentence_or_cut_a_word(self):
        from pala_google import task_presentation

        text = ("Na štvrtok si evidujem opravu akváriového osvetlenia. "
                "Osvetlenie bliká počas rannej skúšky a potom zhasne.")
        title, details = task_presentation(text)
        self.assertLessEqual(len(title), 34)
        self.assertLessEqual(len(title.split()), 3)
        self.assertNotIn("…", title)
        self.assertNotIn("evidujem", title.casefold())
        self.assertEqual(details, text)

    def test_dominant_keyword_keeps_a_second_topic_word_when_available(self):
        from pala_google import task_presentation

        text = ("Bluetooth sa po prebudení nespustí. "
                "Bluetooth po prebudení nereaguje. Bluetooth chcem skontrolovať.")
        title, details = task_presentation(text)
        self.assertIn("Bluetooth", title)
        self.assertIn("prebudení", title.casefold())
        self.assertLessEqual(len(title), 34)
        self.assertEqual(details, text)

    def test_plain_task_with_lost_response_is_not_inserted_again(self):
        from google_tasks import TaskWriteUncertain, insert_plain_task

        api = Mock()
        tasks = api.tasks.return_value
        tasks.insert.return_value.execute.side_effect = TimeoutError("Lost insert response")
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            action, = queue.stage("fixture-device", 20, None, "Poliať rastliny.")
            target = {"general": {"id": "general-list", "title": "General"}}

            def send(_kind, binding, _captured, body, _key):
                return insert_plain_task(api, binding["id"], body, "")

            with self.assertRaises(TaskWriteUncertain):
                queue.deliver(action, target, send)
            self.assertEqual(queue.deliver(action, target, send), "uncertain")
        tasks.insert.assert_called_once_with(tasklist="general-list", body={"title": "Poliať rastliny."})

    def test_usb_google_delivery_waits_for_save_acknowledgment(self):
        send = Mock(return_value="verified")
        target = {"tasks": {"id": "tasks-list", "title": "Approved"}}
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            args = ("fixture-device", 20, None, "Úloha: upozorniť")
            key, = queue.stage(*args, ready=False)
            self.assertEqual(queue.pending(), [])
            self.assertEqual(queue.deliver(key, target, send), "awaiting-usb")
            send.assert_not_called()
            self.assertEqual(queue.stage(*args), [key])
            self.assertEqual(queue.pending(), [key])
            self.assertEqual(queue.deliver(key, target, send), "done")
            send.assert_called_once()

    def test_unbound_destination_is_queued_and_uncertain_delivery_is_not_repeated(self):
        send = Mock(side_effect=RuntimeError("uncertain provider response"))
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            action, = queue.stage("fixture-device", 20, None, "Úloha: upozorniť")
            self.assertEqual(queue.deliver(action, {}, send), "pending")
            send.assert_not_called()
            target = {"tasks": {"id": "tasks-list", "title": "Approved"}}
            with self.assertRaisesRegex(RuntimeError, "uncertain"):
                queue.deliver(action, target, send)
            self.assertEqual(queue.deliver(action, target, send), "uncertain")
            send.assert_called_once()
            lookup = Mock(side_effect=[None, "verified-task"])
            self.assertEqual(queue.reconcile_task(action, target, lookup), "uncertain")
            self.assertEqual(queue.reconcile_task(action, target, lookup), "done")
            self.assertEqual(queue.deliver(action, target, send), "done")
            send.assert_called_once()


if __name__ == "__main__":
    unittest.main()
