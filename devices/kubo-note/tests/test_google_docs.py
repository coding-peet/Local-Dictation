from datetime import date
from hashlib import sha256
from pathlib import Path
from types import SimpleNamespace
import sys
import unittest
from unittest.mock import Mock


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
import google_docs


DOCUMENT_ID = "doc-kubo-notes"
TITLE = "Kubo notes general"
ACTION_KEY = "pala-42:personal-note"
MARKER = "kubo-note-" + sha256(ACTION_KEY.encode()).hexdigest()
CAPTURED_DATE = "2026-11-20"
NOTE = "Modrý zápisník 📘\nDruhý riadok."
ENTRY = CAPTURED_DATE + "\n" + NOTE


def units(text):
    return len(text.encode("utf-16-le")) // 2


def tab(body, tab_id="first-tab", marked_text=None, marker=MARKER):
    paragraphs = []
    start = 1
    for line in body.split("\n")[:-1]:
        elements = []
        end = start
        for text in (line[:2], line[2:] + "\n"):
            if text:
                stop = end + units(text)
                elements.append({"startIndex": end, "endIndex": stop, "textRun": {"content": text}})
                end = stop
        paragraphs.append({"startIndex": start, "endIndex": end, "paragraph": {"elements": elements}})
        start = end

    document_tab = {"body": {"content": paragraphs}}
    if marked_text is not None:
        index = body.rindex(marked_text)
        start = 1 + units(body[:index])
        document_tab["namedRanges"] = {marker: {"name": marker, "namedRanges": [{
            "name": marker,
            "namedRangeId": f"range-{marker}",
            "ranges": [{"tabId": tab_id, "startIndex": start, "endIndex": start + units(marked_text)}],
        }]}}

    return {"tabProperties": {"tabId": tab_id}, "documentTab": document_tab}


def document(body="\n", *, revision="r1", marked_text=None):
    return {
        "documentId": DOCUMENT_ID,
        "title": TITLE,
        "revisionId": revision,
        "tabs": [tab(body, marked_text=marked_text)],
    }


class BadRevisionError(Exception):
    resp = SimpleNamespace(status=400)


class GoogleDocsTests(unittest.TestCase):
    def setUp(self):
        self.api = Mock()
        self.documents = self.api.documents.return_value
        self.read = self.documents.get.return_value.execute
        self.write = self.documents.batchUpdate.return_value.execute

    def append(self, *, captured_date=CAPTURED_DATE, text=NOTE, action_key=ACTION_KEY):
        return google_docs.append_note(self.api, DOCUMENT_ID, TITLE, captured_date, text, action_key)

    def test_append_uses_first_tab_and_revision_guarded_utf16_range(self):
        existing = "Predošlá 📂 poznámka"
        before = document(existing + "\n")
        before["tabs"].append(tab("Another tab\n", "other-tab"))
        after = document(existing + "\n\n" + ENTRY + "\n\n", revision="r2", marked_text=ENTRY)
        after["tabs"].append(tab("Another tab\n", "other-tab"))
        self.read.side_effect = [before, after]

        self.assertTrue(self.append(captured_date=date(2026, 11, 20)))

        start = 1 + units(existing + "\n\n")
        self.documents.get.assert_called_with(
            documentId=DOCUMENT_ID, includeTabsContent=True, suggestionsViewMode="SUGGESTIONS_INLINE",
        )
        self.assertEqual(self.documents.get.call_count, 2)
        self.documents.batchUpdate.assert_called_once_with(documentId=DOCUMENT_ID, body={
            "writeControl": {"requiredRevisionId": "r1"},
            "requests": [
                {"insertText": {"text": "\n\n" + ENTRY + "\n", "endOfSegmentLocation": {"tabId": "first-tab"}}},
                {"createNamedRange": {"name": MARKER, "range": {
                    "startIndex": start, "endIndex": start + units(ENTRY), "tabId": "first-tab",
                }}},
            ],
        })

    def test_empty_document_starts_directly_with_date(self):
        self.read.side_effect = [document(), document(ENTRY + "\n\n", revision="r2", marked_text=ENTRY)]
        self.assertTrue(self.append())
        requests = self.documents.batchUpdate.call_args.kwargs["body"]["requests"]
        self.assertEqual(requests[0]["insertText"]["text"], ENTRY + "\n")
        self.assertEqual(requests[1]["createNamedRange"]["range"]["startIndex"], 1)

    def test_later_append_inserts_after_previous_marker_end(self):
        second_key = "pala-43:personal-note"
        second_marker = "kubo-note-" + sha256(second_key.encode()).hexdigest()
        second_note = "Druhá poznámka."
        second_entry = CAPTURED_DATE + "\n" + second_note
        initial_text = ENTRY + "\n\n"
        final_text = ENTRY + "\n\n" + second_entry + "\n\n"
        before = document(initial_text, marked_text=ENTRY)
        after = document(final_text, revision="r2", marked_text=ENTRY)
        after["tabs"][0]["documentTab"]["namedRanges"].update(
            tab(final_text, marked_text=second_entry, marker=second_marker)["documentTab"]["namedRanges"],
        )
        self.read.side_effect = [before, after, after]

        self.assertTrue(self.append(text=second_note, action_key=second_key))
        self.assertFalse(self.append())
        self.documents.batchUpdate.assert_called_once()
        requests = self.documents.batchUpdate.call_args.kwargs["body"]["requests"]
        self.assertEqual(requests[0]["insertText"]["text"], "\n" + second_entry + "\n")
        start = requests[1]["createNamedRange"]["range"]["startIndex"]
        self.assertEqual(start, 1 + units(initial_text))

    def test_document_id_and_title_must_both_match(self):
        for field, value in (("documentId", "another-doc"), ("title", "Renamed notes")):
            with self.subTest(field=field):
                self.read.return_value = document() | {field: value}
                with self.assertRaisesRegex(RuntimeError, "destination"):
                    self.append()
        self.documents.batchUpdate.assert_not_called()

    def test_missing_revision_blocks_write(self):
        self.read.return_value = document(revision=None)
        with self.assertRaisesRegex(RuntimeError, "revision"):
            self.append()
        self.documents.batchUpdate.assert_not_called()

    def test_retry_recognizes_same_action_after_tab_order_and_range_offset_change(self):
        saved = document()
        saved["tabs"].insert(0, tab("New first tab\n", "new-tab"))
        saved["tabs"][1] = tab("A manual edit\n" + ENTRY + "\n", marked_text=ENTRY)
        self.read.return_value = saved

        self.assertFalse(self.append())
        self.documents.batchUpdate.assert_not_called()

    def test_matching_text_without_marker_does_not_stand_in_for_the_action(self):
        self.read.side_effect = [
            document(ENTRY + "\n"),
            document(ENTRY + "\n\n" + ENTRY + "\n\n", revision="r2", marked_text=ENTRY),
        ]

        self.assertTrue(self.append())
        self.documents.batchUpdate.assert_called_once()

    def test_existing_marker_with_different_text_blocks_write(self):
        existing = CAPTURED_DATE + "\nA manually edited note"
        self.read.return_value = document(existing + "\n", marked_text=existing)

        with self.assertRaisesRegex(RuntimeError, "read back"):
            self.append()

        self.documents.batchUpdate.assert_not_called()

    def test_duplicate_markers_block_write(self):
        saved = document(ENTRY + "\n", marked_text=ENTRY)
        saved["tabs"].append(tab(ENTRY + "\n", "another-tab", marked_text=ENTRY))
        self.read.return_value = saved

        with self.assertRaisesRegex(RuntimeError, "multiple"):
            self.append()

        self.documents.batchUpdate.assert_not_called()

    def test_lost_response_reconciles_saved_action_without_a_second_write(self):
        saved = document(ENTRY + "\n\n", revision="r2", marked_text=ENTRY)
        self.write.side_effect = TimeoutError("response lost")
        self.read.side_effect = [document(), saved, saved]

        self.assertFalse(self.append())
        self.assertFalse(self.append())
        self.documents.batchUpdate.assert_called_once()

    def test_lost_response_without_marker_keeps_action_unverified(self):
        self.write.side_effect = TimeoutError("response lost")
        self.read.side_effect = [document(), document(revision="r2")]

        with self.assertRaisesRegex(RuntimeError, "could not be verified"):
            self.append()

        self.documents.batchUpdate.assert_called_once()

    def test_concurrent_manual_edit_refreshes_revision_and_append_index(self):
        existing = "Ručná úprava 🔧"
        self.write.side_effect = [BadRevisionError(), {}]
        self.read.side_effect = [
            document(), document(existing + "\n", revision="r2"),
            document(existing + "\n\n" + ENTRY + "\n\n", revision="r3", marked_text=ENTRY),
        ]

        self.assertTrue(self.append())

        first, second = [call.kwargs["body"] for call in self.documents.batchUpdate.call_args_list]
        self.assertEqual(first["writeControl"], {"requiredRevisionId": "r1"})
        self.assertEqual(second["writeControl"], {"requiredRevisionId": "r2"})
        self.assertEqual(second["requests"][1]["createNamedRange"]["range"]["startIndex"],
                         1 + units(existing + "\n\n"))

    def test_successful_response_without_action_readback_reports_failure(self):
        self.read.side_effect = [document(), document(ENTRY + "\n\n", revision="r2")]

        with self.assertRaisesRegex(RuntimeError, "read back"):
            self.append()

        self.documents.batchUpdate.assert_called_once()

    def test_invalid_date_blank_note_or_stripped_characters_fail_before_io(self):
        for kwargs in (
            {"captured_date": "2026-02-30"},
            {"text": "  \n"},
            {"text": "Note with \x00 stripped content"},
            {"text": "Note with \ue000 a private-use glyph"},
            {"action_key": ""},
        ):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                self.append(**kwargs)
        self.api.documents.assert_not_called()
