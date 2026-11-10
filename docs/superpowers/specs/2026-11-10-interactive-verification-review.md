# Interactive verification review

Date: 2026-11-10

Status: requested interaction recorded; the visual arrangement is a proposal.

This is an amendment to the main Local Dictation design. Peter prefers an
interactive output where he can decide which model transcribed each disputed
section correctly.


## Approach

Use one working transcript with highlighted differences and a phrase picker.
Word-level alignment determines differences, then adjacent edits are grouped
into readable phrases. A changed word can stand alone; additions, omissions,
or reworded expressions can occupy a short block.

Two complete static transcripts remain source material, but the main
interaction is choosing wording inside a single resulting text.

Keep the model roles generic: Dictation and Verification, with the actual
selected model names alongside them. Large-v3 is sample/default registry
data, not a condition in the review workflow.


## Interaction

- Start from the primary transcript. Highlight unresolved regions.
- Clicking a region shows its Dictation and Verification wording.
- Keep dictation, Use verification, or Use my wording resolves that region.
- Empty candidates are explicit choices for inserted or omitted words.
  Display placeholders such as No text in this version; never put those
  placeholders into the resulting transcript.
- Preserve exact selected spelling, diacritics, numbers, punctuation, and
  whitespace. Region IDs do not change when a selected phrase changes length.
- Preserve unapplied custom wording when navigating to another region.
- Keep remaining dictation explicitly resolves the remaining regions using
  the primary version. It does not replace already selected custom wording.
- Undo choice reverses the last decision, including a bulk keep operation.
- Copy reviewed text becomes available after every region is resolved.
  The user can resolve the remainder in one action if they want the original.
- Show the assembled plain text for inspection. Copying is explicit and
  does not dismiss or delete the Review item.

Do not imply that the verification model's wording is automatically correct.
Both models can be wrong. A custom wording option must always be available.


## Native application boundary

The annotations and phrase picker live in Local Dictation's own Review
window. A generic destination field such as the Codex composer receives
plain text; this feature does not add controls inside that application.

A successful live dictation may already exist in the destination. Completing
verification must not automatically replace it or manual edits in that field.
For the currently designed recovery workflow, the user copies the reviewed
text and deliberately pastes it where needed.

Keep both immutable model transcripts, model/revision identities, region
decisions, and the working Review draft separate. Persist pending Review
state using the existing protected, atomic local store. No general history
or audio archive is added.

The existing final Review editor may still support whole-text manual edits.
Such an edit is an authoritative separate draft: changing comparison choices
must not silently rebuild over that manual draft. An explicit rebuild action
must preserve the previous edited draft until the user dismisses it. The
concept below demonstrates per-region custom wording, not that later editor
round trip.


## Comparison data and grouping

A completed verification result needs:

- Immutable primary and verification text plus their model/revision IDs.
- A comparison snapshot identity.
- Ordered, stable region IDs and exact source spans in both transcripts.
- Replacement, insertion, and deletion candidates, including empty strings.
- The selected source/custom text and unreviewed state for each region.

The shared backend should own deterministic comparison data so future clients
can use the same interpretation. The Windows client owns the user's choices.
Detailed transport schema and alignment implementation belong to the
implementation plan.

Do not build regions by character positions in a working string that changes
as choices are applied. Reassemble from immutable spans and stable IDs.

Tokenization/alignment must preserve Unicode text. Do not erase Slovak
diacritics or silently normalize meaningful number/unit differences.
When repeated words, reordering, or segmentation make a narrow alignment
ambiguous, prefer a larger reviewable region over misleading word matches.
A group is a comparison aid, not proof of which text was spoken.


## Audio policy

Keep the previously selected policy: only pending Review text persists;
audio is temporary.

This interaction does not require retaining audio after the required model
passes finish. No replay button or invented word timestamps are added.
If sentence playback is requested later, its temporary-RAM lifetime and
actual alignment/timestamp capability need an explicit design amendment.


## Concept and checks

Prototype: docs/prototypes/verification-review.html.

The self-contained browser page uses synthetic text and pre-aligned sample
regions. It does not call models, infer correctness, capture a microphone,
or type into a destination. It has no text-alignment algorithm.

Copy reviewed text can write the sample result to the clipboard only after
the user clicks it. It never reads clipboard content. If clipboard writing
is unavailable, show/select the resulting text for manual copying.

Prototype state stays in page memory. Refreshing/closing it does not test
the native pending-Review persistence feature.

Verify at least:

1. Mixed primary/verification/custom selections produce the exact expected
   plain text without markers or duplicated source phrases.
2. Different phrase lengths, Unicode/diacritics, numbers, insertions, and
   omissions preserve correct output boundaries.
3. Empty custom text removes a phrase only after an explicit selection.
4. Unapplied custom wording survives navigation; model originals stay intact.
5. Undo and bulk keep preserve prior custom decisions.
6. Unreviewed text is not silently marked as accepted.
7. Copy success and copy failure keep all Review decisions and text.
8. Role labels can change without model-specific control logic.

These are concept checks. They do not prove native editor behavior, real
transcript alignment, microphone latency, or ASR quality.


## Concept verification on 2026-11-10

The concept passed 21 state/DOM-simulated checks, including exact mixed
output, insertions/deletions, preserved custom drafts, undo, and copy failure.
The secondary label color was adjusted to meet a 4.5:1 text contrast floor.
The desktop layout was observed in Chrome. Real ASR alignment, narrow-layout
rendering, native editor behavior, and Review persistence remain unverified.

The fresh builder check found no important unanswered planning questions
for this amendment. The current browser concept remains a proposal for
Peter to react to.
