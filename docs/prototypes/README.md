# UI concepts

## Recording banner and recovery Review

Open recording-review.html in a browser to explore recording, finalization,
target changes, an unavailable API, and the settings concept.

The page uses sample text and device names. It does not capture a microphone,
contact the ASR API, modify the clipboard, or type into another application.

The settings concept includes Verify after recording and a separate
Verification model dropdown. Its current single entry is sample data; the
native client will obtain choices from the API registry.


## Interactive verification Review

Open verification-review.html in a browser.

Click highlighted phrases, then choose Keep dictation, Use verification,
or Use my wording. The sample includes replacements of different lengths,
number formatting, a Slovak diacritic difference, an omission, and an addition.

Unreviewed phrases initially use the dictation version. Keep remaining
dictation resolves those regions explicitly without replacing prior choices.
Undo choice reverses a decision or bulk keep. Custom drafts survive
navigation between phrases.

Copy reviewed text is enabled after every region is resolved. It writes
the assembled sample text to the clipboard only when clicked; it never
reads the clipboard. If writing is unavailable, the resulting text is
selected for manual copying. Copying keeps the review choices.

This page uses synthetic, pre-aligned regions. It does not call a model,
compute transcript alignment, record audio, or modify a destination field.
Choices stay in page memory; closing or refreshing this concept does not
exercise native Review persistence.

See [the interaction design](../superpowers/specs/2026-11-10-interactive-verification-review.md)
for the intended native behavior and source/draft ownership.


## Verification limits

DOM-simulated checks exercise concept state and interaction logic.
They do not establish native Windows focus/insertion behavior, real
transcript alignment, microphone latency, or model accuracy.

The interactive verification page passed 21 state/DOM-simulated checks and
its desktop layout was observed in Chrome on 2026-11-10. No screenshot with
personal browser chrome is included in this repository. Narrow layouts
and native Windows behavior have not received application-level acceptance.
The earlier recording-banner rendering check had an app-approval timeout;
that historical limit does not apply to the observed interactive-review page.
