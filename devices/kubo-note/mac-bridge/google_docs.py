"""Append dated personal notes to one explicitly selected Google Docs document."""

from datetime import date
from hashlib import sha256
import re


DOCUMENTS_SCOPE = "https://www.googleapis.com/auth/documents"

_DOCUMENT_ID = re.compile(r"[A-Za-z0-9_-]+")
# Docs strips these controls and private-use characters during text insertion.
_UNSUPPORTED_TEXT = re.compile(r"[\x00-\x08\x0c-\x1f\ud800-\udfff\ue000-\uf8ff]")


def _document_tabs(tabs):
    for tab in tabs:
        tab_id = tab.get("tabProperties", {}).get("tabId")
        content = tab.get("documentTab")
        if not tab_id or not isinstance(content, dict):
            raise RuntimeError("Google Docs returned an incomplete document tab.")
        yield tab_id, content
        yield from _document_tabs(tab.get("childTabs", []))


def _read_document(documents, document_id, expected_title):
    document = documents.get(
        documentId=document_id, includeTabsContent=True, suggestionsViewMode="SUGGESTIONS_INLINE",
    ).execute()
    if document.get("documentId") != document_id or document.get("title") != expected_title:
        raise RuntimeError("The Google Docs destination ID or title does not match.")
    tabs = list(_document_tabs(document.get("tabs", [])))
    if not tabs:
        raise RuntimeError("Google Docs returned no document tabs.")
    return document.get("revisionId"), tabs


def _range_matches(tab_id, tab, named_range, entry):
    ranges = named_range.get("ranges", [])
    if not named_range.get("namedRangeId") or len(ranges) != 1:
        return False

    span = ranges[0]
    start, end = span.get("startIndex"), span.get("endIndex")
    if span.get("segmentId") or span.get("tabId", tab_id) != tab_id:
        return False
    if not isinstance(start, int) or not isinstance(end, int) or not 1 <= start < end:
        return False

    expected = entry.encode("utf-16-le")
    if (end - start) * 2 != len(expected):
        return False

    parts = bytearray()
    cursor = start
    for element in tab.get("body", {}).get("content", []):
        if element.get("endIndex", 0) <= cursor:
            continue
        for run in element.get("paragraph", {}).get("elements", []):
            run_start, run_end = run.get("startIndex", 0), run.get("endIndex", 0)
            if run_end <= cursor:
                continue
            text = run.get("textRun", {}).get("content")
            if run_start > cursor or text is None:
                return False
            content = text.encode("utf-16-le")
            if len(content) != (run_end - run_start) * 2:
                return False
            stop = min(run_end, end)
            parts.extend(content[(cursor - run_start) * 2:(stop - run_start) * 2])
            cursor = stop
            if cursor == end:
                return parts == expected
    return False


def _has_verified_entry(tabs, marker, entry):
    matches = []
    for tab_id, tab in tabs:
        named_ranges = tab.get("namedRanges", {})
        if marker in named_ranges:
            ranges = named_ranges[marker].get("namedRanges", [])
            if not ranges:
                raise RuntimeError("Could not read back the marked Google Docs note.")
            matches.extend((tab_id, tab, named_range) for named_range in ranges)
    if not matches:
        return False
    if len(matches) != 1:
        raise RuntimeError("Google Docs contains multiple markers for this note.")
    if not _range_matches(*matches[0], entry):
        raise RuntimeError("Could not read back the marked Google Docs note.")
    return True


def _append_batch(revision, first_tab, marker, entry):
    if not revision:
        raise RuntimeError("The Google Docs destination has no editable revision.")

    tab_id, tab = first_tab
    content = tab.get("body", {}).get("content", [])
    final_element = content[-1] if content else {}
    end = final_element.get("endIndex")
    if "paragraph" not in final_element or not isinstance(end, int) or end < 2:
        raise RuntimeError("Could not locate the end of the selected Google Docs tab.")

    # Each note ends with an unmarked newline. Future inserts then start beyond
    # the end of its named range, even when the note is last in the document.
    separator = ""
    if end > 2:
        separator = "\n" if final_element.get("startIndex") == end - 1 else "\n\n"
    start = end - 1 + len(separator)
    return {
        "writeControl": {"requiredRevisionId": revision},
        "requests": [
            {"insertText": {"text": separator + entry + "\n", "endOfSegmentLocation": {"tabId": tab_id}}},
            {"createNamedRange": {"name": marker, "range": {
                "tabId": tab_id, "startIndex": start,
                "endIndex": start + len(entry.encode("utf-16-le")) // 2,
            }}},
        ],
    }


def append_note(api, document_id, expected_title, captured_date, text, action_key) -> bool:
    """Return True after an append and readback, or False for a verified replay."""
    if not isinstance(document_id, str) or not _DOCUMENT_ID.fullmatch(document_id):
        raise ValueError("A Google Docs document ID is required.")
    if not isinstance(expected_title, str) or not expected_title.strip():
        raise ValueError("An expected Google Docs title is required.")
    if not isinstance(action_key, str) or not action_key.strip():
        raise ValueError("A stable action key is required.")
    if not isinstance(text, str) or not text.strip():
        raise ValueError("A Google Docs note must contain text.")
    captured_date = str(captured_date)
    try:
        if date.fromisoformat(captured_date).isoformat() != captured_date:
            raise ValueError
    except ValueError:
        raise ValueError("The note requires a valid recording date in YYYY-MM-DD format.") from None
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    if _UNSUPPORTED_TEXT.search(text):
        raise ValueError("The note contains characters that Google Docs cannot insert.")

    entry = f"{captured_date}\n{text}"
    marker = "kubo-note-" + sha256(action_key.encode("utf-8")).hexdigest()

    documents = api.documents()
    revision, tabs = _read_document(documents, document_id, expected_title)
    if _has_verified_entry(tabs, marker, entry):
        return False

    for attempt in range(2):
        batch = _append_batch(revision, tabs[0], marker, entry)
        try:
            documents.batchUpdate(documentId=document_id, body=batch).execute()
        except Exception as error:
            fresh_revision, tabs = _read_document(documents, document_id, expected_title)
            if _has_verified_entry(tabs, marker, entry):
                return False
            # A stale requiredRevisionId is rejected with HTTP 400 before applying
            # the batch. Retry once on a fresh revision. An uncertain write gets
            # read back above but never causes another write within this call.
            status = getattr(getattr(error, "resp", None), "status", None)
            if attempt == 0 and status == 400 and fresh_revision and fresh_revision != revision:
                revision = fresh_revision
                continue
            raise RuntimeError("The Google Docs append could not be verified.") from None

        _, saved_tabs = _read_document(documents, document_id, expected_title)
        if not _has_verified_entry(saved_tabs, marker, entry):
            raise RuntimeError("Could not read back the marked Google Docs note.")
        return True
