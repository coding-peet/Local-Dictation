# Outlook and Codex manual delivery observations — 2026-11-12

## Scope

These observations concern the installed client build
`0.1.0+5eb991597f0bdaa84c719f22a29927f0b5ed44d4`, process 52496. They do not
establish compatibility with every field or future app version. No transcript,
signature, clipboard content, or audio is included in this report.

## Outlook: one confirmed insertion

Peter reported that a dictation worked in Outlook. Capture metadata associated
the field with the separate Outlook compose window. The subsequent final
clipboard paste queued four input events and readback matched the expected full
text and caret exactly. The clipboard transaction reported restored. The user
reported success; independent visual verification of the signature was not
performed. One successful trial does not establish a general fix.

## Codex: partial live insertion, confirmation failed

In a Codex question response field, a live clipboard paste queued four input
events. The field's observed text length was 18 UTF-16 units against 25
expected, with the first mismatch at offset 18; the observed caret matched.
The screenshot showed the beginning of the dictation in the field and the full
text in the recovery banner. The app stopped further delivery and recorded a
pending clipboard recovery. These observations do not establish whether the
seven missing units were real pre-existing text or provider projection, nor do
they establish the root cause. Do not weaken ownership checks based on length
alone or replay an unconfirmed paste.

## Subsequent Outlook attempt and Insert report

A later final Outlook delivery queued four input events, but the one-second
readback remained identical to its previous baseline; the expected added text
and caret were not confirmed. A clipboard recovery was pending in that event.
This does not prove whether the destination could process an earlier paste later.
Peter also reported that Insert appeared to do nothing and expressed the need
to insert at a deliberately selected cursor. The relationship between his
button report and this specific logged attempt has not been established.

The current implementation retries the remembered validated destination on
Insert; Choose destination arms a separate selection flow. While clipboard
recovery is pending, the review insertion path refuses another attempt. This
guard may explain an inactive-looking retry but does not prove which control was
clicked in Peter's report. The reported desired
experience and the nonresponsive Insert require investigation and manual
acceptance. Preserve destination validation and avoid duplicate insertion after
an unconfirmed paste. This report does not claim a code fix.

At observation, a Restore previous clipboard confirmation was open. Its dialog
requires the destination to be inspected and any delayed paste stopped before
confirmation. The application must remain open while its in-memory backup is
needed. The final recovery outcome has not been confirmed.
