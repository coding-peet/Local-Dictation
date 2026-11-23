from pathlib import Path
import sys
from datetime import datetime, timezone
import sqlite3
from contextlib import closing, nullcontext, redirect_stdout
from io import StringIO
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

from pala_google import DeliveryQueue, classify_transcript


class PalaGoogleTests(unittest.TestCase):
    def test_keeps_the_exact_original_transcript_without_any_google_target(self):
        from pala_google import UsbGoogleDelivery

        text = "Pridaj do kalendára, že zajtra treba zavolať."
        capture = "2026-11-23T00:00:00Z"
        with tempfile.TemporaryDirectory() as folder:
            router = UsbGoogleDelivery(Path(folder), {"processing_mode": "transcribe"})
            receipt = router.stager("fixture-device")(54, capture, text)
            saved = router.queue.note_transcripts(0)["records"]
            self.assertEqual([(row["note"], row["text"], row["onNote"]) for row in saved], [(54, text, False)])
            receipt()
            receipt()
            saved = router.queue.note_transcripts(0)["records"]
            self.assertEqual([(row["note"], row["text"], row["onNote"]) for row in saved], [(54, text, True)])
            self.assertEqual(router.queue.pending(), [])

    def test_decisions_follow_the_original_through_routing_and_delivery(self):
        from pala_google import UsbGoogleDelivery

        with tempfile.TemporaryDirectory() as folder:
            list_binding = {"id": "target-id", "title": "General"}
            router = UsbGoogleDelivery(Path(folder), {"destination": "google",
                                                      "google_routes": {"general": list_binding}})
            keys = []
            router.stager("fixture-device", keys)(54, None, "Pôvodný text.")()
            with patch.object(router, "_send", return_value="verified"):
                router.flush(keys)
            record, = router.queue.note_transcripts(0)["records"]
            line = "\n".join(record["decisions"])
            self.assertIn("Routing: general", line)
            self.assertIn("Google: confirmed delivery to General", line)
            self.assertNotIn("Pôvodný text", line)

    def test_legacy_calendar_transcript_remains_visible(self):
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            with queue.database() as db:
                db.execute("""INSERT INTO google_actions(id, kind, captured, body, status, device, recording_id)
                              VALUES('legacy','calendar','2026-11-23T00:00:00Z','Presný starší prepis','needs-confirmation',
                                     'fixture-device','54:2026-11-23T00:00:00+00:00')""")
            records = queue.note_transcripts(0)["records"]
            self.assertEqual([(row["note"], row["text"]) for row in records], [(54, "Presný starší prepis")])

    def test_codex_quota_prefers_five_hours_and_falls_back_to_weekly(self):
        from codex_usage import usage_snapshot

        now = 1_795_000_000
        five_hours = {"usedPercent": 68, "windowDurationMins": 300, "resetsAt": now + 200}
        weekly = {"usedPercent": 71, "windowDurationMins": 10_080, "resetsAt": now + 3000}
        live = {"rateLimitsByLimitId": {"codex": {"primary": weekly, "secondary": five_hours}}}
        snapshot = usage_snapshot(live, now)
        self.assertEqual(snapshot.five_hours, (32, now + 200))
        self.assertEqual(snapshot.weekly, (29, now + 3000))
        self.assertEqual(snapshot.legacy_window(), (32, now + 200))
        live["rateLimitsByLimitId"]["codex"]["secondary"] = {**five_hours, "resetsAt": now}
        self.assertEqual(usage_snapshot(live, now).legacy_window(), (29, now + 3000))
        legacy = {"rateLimits": {"primary": {**five_hours, "usedPercent": 105, "resetsAt": None}}}
        snapshot = usage_snapshot(legacy, now)
        self.assertEqual(snapshot.five_hours, (0, 0))
        self.assertEqual(snapshot.legacy_window(), (0, now + 900))
        self.assertIsNone(usage_snapshot({"rateLimitsByLimitId": {"another": {"primary": weekly}}}, now))
        self.assertIsNone(usage_snapshot({"rateLimits": {"primary": {**weekly, "usedPercent": None}}}, now))

    def test_home_dashboard_only_shares_codex_usage_with_compatible_kubo_processing(self):
        from codex_usage import UsageSnapshot, codex_usage
        from pala_google import home_dashboard_payload

        snapshot = UsageSnapshot(None, (29, 1795000200), 1795000100)
        with tempfile.TemporaryDirectory() as folder, patch.object(
                codex_usage, "get", return_value=snapshot) as read:
            state = Path(folder)
            self.assertEqual(home_dashboard_payload(state, {"processing_mode": "transcribe"}, "note",
                                                    version=4), "h2,-1,0,-")
            self.assertEqual(home_dashboard_payload(state, {"processing_mode": "kubo"}, "note"), "h2,-1,0,-")
            read.assert_not_called()
            self.assertEqual(home_dashboard_payload(state, {"processing_mode": "kubo"}, "note",
                                                    version=3), "h3,-1,0,-,29,1795000200")
            self.assertEqual(home_dashboard_payload(state, {"processing_mode": "kubo"}, "note",
                                                    version=4), "h4,-1,0,-,1795000100,-1,0,29,1795000200")
            self.assertEqual(read.call_count, 2)

    def test_home_counts_use_the_day_of_the_last_confirmed_action(self):
        def at(hour, minute):
            return datetime(2026, 11, 19, hour, minute, tzinfo=timezone.utc)

        targets = {
            "shopping": {"id": "shopping", "title": "Shopping"},
            "general": {"id": "general", "title": "General"},
            "docs": {"id": "docs", "title": "Notes"},
        }
        send = Mock(return_value="receipt")
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            shopping = ("PaLa", 1, None, "Nákup: mlieko a rožky.")
            first, second = queue.stage(*shopping)
            queue.stage("pala", 2, None, "Pracovná úloha poslať ponuku.")
            queue.stage("pala", 3, None, "Termín: zajtra o deviatej.")
            queue.stage("pala", 4, None, "Poznámka: krátky text", ready=False)
            queue.stage("other", 1, None, "Iné zariadenie.")
            queue.stage("pala", 5, None, "Do zoznamu neschválený: niečo.")
            doc, = queue.stage("pala", 6, None, "Ulož do Google Docs osobný nápad")
            with patch("pala_google.time.time", return_value=at(22, 59).timestamp()):
                self.assertEqual(queue.deliver(first, targets, send), "done")
            self.assertEqual(queue.home_counts("PALA", targets, now=at(22, 59)), ("2026-11-19", 0, 2))
            with patch("pala_google.time.time", return_value=at(23, 0).timestamp()):
                self.assertEqual(queue.deliver(second, targets, send), "done")
                self.assertEqual(queue.deliver(doc, targets, send), "done")
            self.assertEqual(queue.stage(*shopping), [first, second])
            self.assertEqual(queue.home_counts("pala", targets, now=at(23, 1)), ("2026-11-20", 2, 0))

    def test_home_counts_count_the_recording_once_if_its_transcript_changes(self):
        targets = {"general": {"id": "general", "title": "General"},
                   "tasks": {"id": "tasks", "title": "Tasks"}}
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            for captured, text in (("2026-11-20T08:10:00Z", "Modrý zápisník."),
                                   ("2026-11-20T09:10:00+01:00", "Úloha: skontrolovať modrý zápisník."),
                                   ("2026-11-20T09:11:00+01:00", "Nový diktát po zmazaní starého.")):
                key, = queue.stage("pala", 1, captured, text)
                now = datetime(2026, 11, 20, 12, tzinfo=timezone.utc)
                with patch("pala_google.time.time", return_value=now.timestamp()):
                    self.assertEqual(queue.deliver(key, targets, Mock(return_value="receipt")), "done")
            self.assertEqual(queue.home_counts("pala", targets, now=now)[1:], (2, 0))

    def test_home_counts_preserve_receipts_from_the_legacy_schema(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            with closing(sqlite3.connect(directory / "google-delivery.sqlite")) as db, db:
                db.execute("""CREATE TABLE google_actions (
                    id TEXT PRIMARY KEY, kind TEXT NOT NULL, captured TEXT, body TEXT NOT NULL,
                    status TEXT NOT NULL, target_id TEXT, provider_id TEXT
                )""")
            queue = DeliveryQueue(directory)
            args = ("pala", 1, None, "Úloha: overiť počítadlo")
            action, = queue.stage(*args)
            with queue.database() as db:
                db.execute("UPDATE google_actions SET status='done', provider_id='previous' WHERE id=?", (action,))
            queue = DeliveryQueue(directory)
            targets = {"tasks": {"id": "tasks", "title": "Tasks"}}
            self.assertEqual(queue.stage(*args), [action])
            self.assertEqual(queue.deliver(action, targets, self.fail), "done")
            self.assertEqual(queue.home_counts("pala", targets)[1:], (0, 0))

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

    def test_note_without_a_destination_uses_general_and_calendar_command_does_not(self):
        note = "Poznámka: vymeniť modrý obal."
        self.assertEqual(classify_transcript(note), ("general", [note]))
        for command in ("Zapíš do kalendára zajtra kontrolu o tretej.",
                        "Do Google Kalendára: kontrola 11. decembra o tretej."):
            with self.subTest(command=command):
                self.assertEqual(classify_transcript(command)[0], "calendar")

    def test_calendar_intent_handles_a_polite_command_but_not_a_calendar_mention(self):
        for text in ("Prosím, naplánuj mi termín zajtra o tretej.",
                     "Pridaj udalosť do Google kalendára zajtra o tretej.",
                     "Napíš mi do kalendára návštevu u veterinára o 16:00."):
            with self.subTest(text=text):
                self.assertEqual(classify_transcript(text)[0], "calendar")
        self.assertEqual(classify_transcript("Poznámka: zajtra si pozri kalendár.")[0], "general")

    def test_calendar_command_accepts_a_spoken_day_before_the_destination(self):
        for command in (
            "Daj mi na dnes do kalandára o 18.00 vymeniť filter.",
            "Daj mi na dnes do kalendára o 18.00 vymeniť filter.",
            "Daj mi do kalandára na dnes o 18.00 vymeniť filter.",
            "Zapíš mi, prosím, na piatok do Google kalendára údržbu o deviatej.",
        ):
            with self.subTest(command=command):
                self.assertEqual(classify_transcript(command), ("calendar", [command]))

        mention = "Daj mi vedieť, či si to už zapísal do kalendára."
        self.assertEqual(classify_transcript(mention), ("general", [mention]))

    def test_shopping_command_accepts_politeness_and_items_before_the_destination(self):
        for command in (
            "Daj mi, prosím, na nákup ovsené mlieko.",
            "Pridaj mi ovsené mlieko na nákupný zoznam, prosím.",
        ):
            with self.subTest(command=command):
                self.assertEqual(classify_transcript(command), ("shopping", ["ovsené mlieko"]))

        mention = "Daj mi vedieť, čo už máš na nákupnom zozname."
        self.assertEqual(classify_transcript(mention), ("general", [mention]))

    def test_personal_collection_defaults_work_topics_and_unbound_tasks_to_general(self):
        from pala_google import UsbGoogleDelivery

        config = {"google_routes": {"general": {"id": "list", "title": "General"}},
                  "personal_note_collection": True}
        with tempfile.TemporaryDirectory() as folder:
            delivery = UsbGoogleDelivery(Path(folder), config)
            stage = delivery.stager("note")
            stage(1, None, "Pracovná úloha skontrolovať návrh.")()
            stage(2, None, "Úloha: vymeniť obal.")()
            stage(3, None, "Zapíš do kalendára návštevu o tretej.")()
            with delivery.queue.database() as db:
                rows = db.execute("SELECT kind, status FROM google_actions ORDER BY rowid").fetchall()
            self.assertEqual(rows, [("general", "pending"), ("general", "pending"),
                                    ("calendar", "needs-confirmation")])

    def test_archive_keeps_full_original_text_and_reuses_the_recording_key(self):
        text = "Zapíš do kalendára stretnutie o tretej."
        with tempfile.TemporaryDirectory() as folder:
            queue = DeliveryQueue(Path(folder))
            key, = queue.stage("note", 12, None, text, archive=True)
            self.assertEqual(queue.stage("note", 12, None, text, archive=True), [key])
            send = Mock(return_value="readback")
            general = {"id": "list", "title": "General"}
            self.assertEqual(queue.deliver(key, {"general": general}, send), "done")
            send.assert_called_once_with("general", general, None, text, key)

    def test_mixed_explicit_work_is_held_locally(self):
        self.assertEqual(classify_transcript(
            "Poznámka: osobný nápad. Pridaj mi pracovnú úlohu poslať ponuku.")[0], "work")
        self.assertEqual(classify_transcript("Ulož do OneNotu zápis z porady.")[0], "work")

    def test_spoken_list_uses_unique_account_match_without_a_binding(self):
        from pala_google import UsbGoogleDelivery

        with tempfile.TemporaryDirectory() as folder:
            router = UsbGoogleDelivery(Path(folder), {"destination": "google"})
            title = "Nákup"
            existing = {"id": "shopping-fixture", "title": title}
            general = {"id": "default-fixture", "title": "General"}
            config = {"processing_mode": "kubo", "destination": "google",
                      "google_routes": {"general": general}}
            keys = []
            with patch.object(router, "active_config", return_value=config), \
                 patch("pala_google._api", return_value=nullcontext(Mock())), \
                 patch("pala_google.items", return_value=[general, existing]), \
                 patch.object(router, "_send", return_value="verified") as send:
                text = "Pridaj mi do zoznamu Nákup: ovsené mlieko."
                router.stager("fixture-device", keys)(17, None, text)()
                router.flush(keys)
                router.flush(keys)
            send.assert_called_once_with("list:nakup", existing, None, "ovsené mlieko.", keys[0])

    def test_shopping_intent_uses_nakup_instead_of_general(self):
        from pala_google import UsbGoogleDelivery

        with tempfile.TemporaryDirectory() as folder:
            shopping = {"id": "shopping-fixture", "title": "Nákup"}
            general = {"id": "default-fixture", "title": "General"}
            router = UsbGoogleDelivery(Path(folder), {"destination": "google", "personal_note_collection": True,
                                                      "google_routes": {"general": general}})
            keys = []
            with patch("pala_google._api", return_value=nullcontext(Mock())), \
                 patch("pala_google.items", return_value=[general, shopping]), \
                 patch.object(router, "_send", return_value="verified") as send:
                router.stager("fixture-device", keys)(19, None, "Pridaj na nákupný zoznam ovsené mlieko.")()
                router.flush(keys)
            send.assert_called_once_with("shopping", shopping, None, "ovsené mlieko", keys[0])

    def test_usb_and_paired_bridge_share_one_named_delivery(self):
        import json
        from bridge import Bridge
        from pala_google import UsbGoogleDelivery

        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder)
            config = {"destination": "google"}
            router = UsbGoogleDelivery(state, config)
            bridge = Bridge(state, config)
            text = "Pridaj mi do zoznamu Nákup: ovsené mlieko."
            capture = "2026-11-23T00:10:00Z"
            router.stager("fixture-device")(17, capture, text)()
            request = {"pala_note": 17, "pala_created": capture, "pala_transcript": text,
                       "text": {"format": {"name": "pala_memory", "schema": {}}}}
            with patch.object(bridge, "memories", return_value=("{}", [])):
                bridge.dispatch("/v1/responses", json.dumps(request), "application/json", device="fixture-device")
            self.assertEqual(len(router.queue.pending()), 1)
            with router.queue.database() as db:
                self.assertEqual(db.execute("SELECT kind, body FROM google_actions").fetchall(),
                                 [("list:nakup", "ovsené mlieko.")])

    def test_missing_or_ambiguous_list_does_not_use_general(self):
        from pala_google import UsbGoogleDelivery

        for available in ([], [{"id": "1", "title": "Nákup"}, {"id": "2", "title": "Nakup"}]):
            with self.subTest(available=available), tempfile.TemporaryDirectory() as folder:
                general = {"id": "default-fixture", "title": "General"}
                router = UsbGoogleDelivery(Path(folder), {"destination": "google",
                                                          "google_routes": {"general": general}})
                key, = router.queue.stage("fixture-device", 17, None, "Do zoznamu Nákup: ovsené mlieko.")
                with patch("pala_google._api", return_value=nullcontext(Mock())), \
                     patch("pala_google.items", return_value=available), \
                     patch.object(router, "_send") as send:
                    router.flush([key])
                send.assert_not_called()
                self.assertEqual(router.queue.pending(), [key])

    def test_shopping_command_survives_a_transcribed_list_word(self):
        for command in ("Pridaj mi do nákupného zoznamu ovsené mlieko.",
                        "Pridaj mi na nákup ovsené mlieko.",
                        "Pridaj dozusnom Nákup ovsené mlieko.",
                        "Nákupný soznam, ovsené mlieko.",
                        "Pridaj mi do zoznamu Nákup ovsené mlieko."):
            with self.subTest(command=command):
                self.assertEqual(classify_transcript(command), ("shopping", ["ovsené mlieko"]))
        self.assertEqual(classify_transcript("Spomenul som si na nákup ovseného mlieka.")[0], "general")

    def test_calendar_suffix_takes_precedence_and_plain_mentions_stay_notes(self):
        item = "Vo štvrtok mám o 14:30 zubára"
        for ending in (", daj mi to do kalendára.", " daj mi to do kalendára"):
            self.assertEqual(classify_transcript(item + ending)[0], "calendar")
        for plain in (item + ".", "Termín u zubára je vo štvrtok o 14:30."):
            self.assertEqual(classify_transcript(plain), ("general", [plain]))

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

class HomeDashboardTests(unittest.TestCase):
    def test_bluetooth_home_dashboard_waits_for_cold_provider_values(self):
        from codex_usage import UsageSnapshot
        from pala_google import home_dashboard_payload

        config = {"processing_mode": "kubo", "google_routes": {
            "general": {"id": "test-list", "title": "General"}}}
        queue = Mock()
        queue.home_counts.return_value = ("2026-11-22", 0, 0, None)
        quota = UsageSnapshot(None, (25, 1795000200), 1795000100)
        with patch("note_web.processing_config", return_value=config), \
             patch("pala_google.general_open_count", side_effect=[-1, 3]), \
             patch("pala_google.home_codex_usage", side_effect=[None, quota]), \
             patch("pala_google.time.sleep"):
            dashboard = home_dashboard_payload(Path("."), config, "note", queue=queue, version=4,
                                               wait_for_sources=4)
        self.assertEqual(dashboard, "h4,3,0,-,1795000100,-1,0,25,1795000200")

    def test_confirmed_general_delivery_invalidates_an_inflight_count(self):
        from pala_google import GeneralTaskCount

        state = Path(".")
        target = {"id": "test-list", "title": "General"}
        cache = GeneralTaskCount()
        with patch("pala_google.threading.Thread") as worker, \
             patch("pala_google.count_open_general_tasks", side_effect=[2, 3]):
            self.assertEqual(cache.get(state, target), -1)
            old = worker.call_args.kwargs
            cache.invalidate(state, target)
            old["target"](*old["args"])
            self.assertEqual(cache.get(state, target), -1)
            fresh = worker.call_args.kwargs
            fresh["target"](*fresh["args"])
            self.assertEqual(cache.get(state, target), 3)

    def test_count_requests_only_status_and_excludes_completed_tasks(self):
        from contextlib import nullcontext
        from unittest.mock import patch
        from pala_google import count_open_general_tasks

        target = {"id": "general", "title": "General"}
        api = unittest.mock.Mock()
        with patch("pala_google._api", return_value=nullcontext(api)), \
             patch("pala_google._verify_task_list") as verify, \
             patch("pala_google.items", return_value=[{"status": "needsAction"}, {"status": "completed"}]) as scan:
            self.assertEqual(count_open_general_tasks(Path("."), target), 1)
        verify.assert_called_once_with(api, target)
        self.assertFalse(scan.call_args.kwargs["showCompleted"])
        self.assertEqual(scan.call_args.kwargs["fields"], "nextPageToken,items(status)")

    def test_payload_contains_open_count_pending_count_and_local_clock(self):
        from unittest.mock import Mock, patch
        from pala_google import home_dashboard_payload

        queue = Mock()
        queue.home_counts.return_value = ("2026-11-21", 8, 2, 1795285920)
        config = {"processing_mode": "kubo", "google_routes": {}}
        with patch("note_web.processing_config", return_value=config), \
             patch("pala_google.general_open_count", return_value=17):
            self.assertRegex(home_dashboard_payload(Path("."), config, "note", queue=queue),
                             r"^h2,17,2,[0-9]{2}:[0-9]{2}$")
