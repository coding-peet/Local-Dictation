# Kubo Bridge routing rules and observed failures

## Find a decision

In Local Dictation, open Connections and select Note transcripts.
Choose the recording by its Note number and capture time. The original
transcript and Bridge decisions are available in the same window.

The decision log records the route, configured Codex model and reasoning
effort, destination resolution, calendar decision and delivery outcome.
It stores no transcript text, credentials or provider IDs. Older
transcripts can be present without a decision log.

## Routing rules

- Preserve the original transcript independently of classification,
  model output and destination availability.
- Resolve an explicit Google Tasks list against the connected account.
  A missing or ambiguous name stays pending.
- Use General for a note with no explicit destination. A shopping
  instruction uses the configured shopping list or a unique existing
  list named Nákup.
- For an explicit calendar instruction, preserve the original recording
  time and require a concrete future start time. The Note asks for
  confirmation before a calendar write.
- If the calendar instruction lacks a time, keep the original transcript
  visible. A message mentioning a date without requesting a calendar
  action remains a regular note.
- Use the selected Codex model and reasoning effort. High is the
  default for profiles without a saved choice.

## Observed failures

### Shopping list heading misheard

A shopping heading was transcribed as "nákupný soznam" and a list
command as "pridaj dozusnom Nákup". Both initially went to General.

Recognize these forms in explicit shopping prefixes. Check that an
ordinary note mentioning shopping still uses General. The recognition
cases are covered by test_pala_google.py.

### Calendar instruction with date only

Note #54 specified a day without a start time. Bridge kept the exact
transcript in its local journal but the user had no obvious way to find it.
The recording is now visible under Note transcripts. Its original text
is retained independently of any calendar action.

### Reasoning was fixed at Low

Bridge passed Low directly to Codex even when the desktop used a higher
setting. Local Dictation now saves the Bridge-specific model and reasoning
choices. Bridge reads the selection for the next request.

## Record a new case

Record the Note number and capture time, the command pattern with any
private details removed, the expected route, the observed decision and
the regression test. Keep credentials and transcript bodies in their
existing private stores.

## Compare transcription models

Keep selected sample audio and transcripts in the private benchmark folder.
For each case, keep the original WAV, the WAV passed to the recognizer,
the exact reference text, the raw model output and the Bridge decision.
Record the expected destination and content alongside the observed result.

Reuse the same input WAV and reference when comparing models. Check the
preserved content as well as the destination. Label synthetic audio and
recordings captured on Note separately. Include real recordings only when
the speaker selects them and verifies the reference. Use offline routing
for comparisons so a replay cannot create duplicate Google entries.
