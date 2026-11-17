# Input language and verification banner

## Approved behavior

Peter requested unobtrusive language information in the recording banner,
similar to SwiftKey. Display the actual model result as `Auto · SK` or
`Auto · EN`. Display a fixed language as `SK`, `EN`, or another supported code.
KInIT is Slovak-only and must display `SK`, even when Auto was requested.
Never infer the detected code from the transcript text.

Preferred Auto languages are removable tags in one row with a `+` button.
The add list comes from installed models' advertised capabilities. It is not
limited to three hardcoded languages. Initial preferences are SK and EN;
RU, SR and CS are absent. A model uses the supported intersection and rejects
an empty intersection. Excluding a language constrains the decoder; it does
not prove recognition accuracy or provide confidence.

Clicking the banner language opens an explicit language choice. It names any
required change from a Slovak-only model to an installed multilingual model.
During recording the choice queues a full retranscription after Finish;
capture continues until the usual Ctrl+Shift. A change during verification
cancels that pass and retranscribes the retained original audio. The replay
does not start the microphone. It uses the existing guarded destination and
Finish decision. Any intervening input/focus change still requires explicit
Insert. Retrying an already completed History item creates a separate Review
and never replaces previously inserted text automatically. Original text,
manual edits and audio remain in protected History. Retain audio even when
recognition returned no text.

With Verify enabled, the primary transcript is a read-only, bounded preview
in the nonactivating banner while verification runs. Withdraw only owned live
text through the existing validated path. Agreement inserts the final result
at the original guarded cursor and hides the banner. Differences open Review.
Verification must use the primary pass's resolved language.

## Implementation and validation

1. Carry supported language metadata and request-local Auto allowlists through
   engine, session and client. Keep model/global generation settings unchanged.
2. Carry actual language and mode through protected Review/History metadata.
3. Add capability-based language controls and queued same-audio replay through
   the existing session pipeline. Preserve cancellation and Finish guards.
4. Add synthetic engine/protocol, language policy and saved-WAV regressions.
5. Render the real banner and language controls offline in Light/Dark themes.
   Do not create native windows, inject input, record audio or play sound.
6. Run complete core/backend suites and Release build, then fresh code review.
7. Publish outside the checkout. Deploy only after normal app Exit, preserving
   settings, models and user data, with a verified payload backup.

Canonical checkout: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows. No worktrees, commits or pushes for this task.
