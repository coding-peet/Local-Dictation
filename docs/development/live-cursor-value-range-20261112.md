# Live cursor value and range repair

## Current result

The current implementation addresses two observed delivery failures. The first
deployed candidate did not solve Peter's acceptance test and is superseded.
Current native/application acceptance is recorded below separately from build
and unit-test results.

## Required user flow

Ctrl+Shift starts recording from the original editable cursor. Live transcription
updates that field automatically, including after pauses and corrections to
earlier wording. Ordinary recording needs no banner action. Ctrl+Shift finishes
recording and automatically applies the final text. Outlook retains live wording
in the banner and receives the complete final text only after Finish. Successful
insertion uses the existing four-second banner dismissal.

Optional verification differences still require the user's selected wording,
as specified before this repair. The repair does not change model selection,
inference, verification preferences, the backend or microphone capture.

## Observed failures

1. The captured ProseMirror TextPattern document sometimes contained one LF,
   and sometimes 16 placeholder-like UTF-16 units. After a complete native paste,
   only the actual payload remained, with its exact end caret. Full-document
   acknowledgement expected the old generated content and permanently stopped.
2. Another real attempt acknowledged its initial 28-unit draft, then rejected
   an 83-unit update at the owned-range phase before queuing replacement input.
   The existing range builder could not prove a suffix with provider character
   movement. This is distinct from the first-paste acknowledgement failure.

Only counts and technical stages are recorded here. Actual transcripts, window
names and clipboard contents remain outside the repository.

## Implementation

Capture retains the exact raw empty projection only when the original field is
an Edit control with a document-start caret and its own writable ValuePattern
independently reports an empty value. The delivery lease uses an empty baseline.
Subsequent inspections may interpret only that captured raw projection as empty,
again requiring caret zero and the same field's writable empty value. Raw changed
text remains visible to the guard even if a provider returns a stale empty Value.
No arbitrary trimming, placeholder wording or provider-class name grants ownership.

Owned-suffix construction retains its cached anchor and character-unit checks.
When character construction fails, a read-only backward FindText searches only
the original document prefix up to the validated caret. Acceptance requires exact
suffix text, its end at that caret, document bounds and the complete exact prefix.
Only then may the existing code select and replace the owned suffix.

Original-window association, exact field identity, input generation, editable
access, full document/caret acknowledgement and clipboard safeguards remain in
force. No unconfirmed paste is repeated. New diagnostic fields contain fixed
empty-value stages and OwnedRangeStrategy, without text values.

## Verification

- Current Core suite: 281 passed; zero failures or skips.
- Current Release client publish: succeeded.
- Current native DeliveryProbe source: built; zero warnings or errors.
- A native RED placeholder case ran against the preceding delivery code and
  failed before input because generated placeholder text became the baseline.
- New native cases cover placeholder and LF projections, pause/continue,
  earlier-word correction, withdrawal, final focus restoration, real words and
  whitespace, absent/read-only ValuePattern, contradictory empty values and a
  failed Delete that must not acknowledge retained owned text as withdrawn.
- A separate new case requires exact FindText fallback when character movement
  cannot construct the suffix and checks surrounding text preservation.
- Native GREEN for this revision has NOT run: Defender ASR event 1121 blocked
  the newly published DeliveryProbe.exe under rule
  01443614-CD74-433A-B99E-2ECDC07BFC25. The executable was not renamed, loaded
  through another host or given an exclusion. IT policy was not changed.
- Native Chrome control was also denied. The browser-only synthetic page showed
  generated placeholder children and an empty field value; this is not a native
  UIA or paste acceptance result.
- Earlier 298/31 results belong to the superseded provider-specific candidate
  and must not be used as current native/application acceptance.

## Deployment and acceptance

Candidate directory: C:\Git\Local-Dictation\continuation-candidate-2.
Build identity includes cursor-value-range-20261112.

The old client was exited normally by Peter. Standard main-product deployment
and real pause/continue plus Outlook acceptance are pending. The blocked test
executable will not be used through an alternate path or loader.

Work remains in C:\Git\Local-Dictation\repo on feature/native-windows,
starting from b494dca. No commit, push, merge or release was performed.

## Verified normal product update

Peter explicitly approved replacing the 14 application files with the prepared
repair and launching it. The previous client had exited normally. All 14 previous
files were backed up and hash-verified; all 14 installed files matched the Release
candidate SHA-256 manifest. The standard installed LocalDictation.exe launched
normally, with hotkeys and child-only diagnostic journaling.

Startup marker and actual process agree on PID 61744 and build:

    0.1.0+b494dca.cursor-value-range-20261112.b494dca1845394af04d0d1e18eec06568ce1292a

Application control showed Ready and Saved automatically. The main product's
normal update was allowed; the blocked DeliveryProbe was not used through a
renamed executable, alternate loader or security exclusion. Current native GREEN
remains unrun. Real live and Outlook acceptance is pending Peter's result.

The independent review's initial stale-empty-value finding was corrected by
binding empty interpretation to the lease's exact captured raw projection. The
follow-up review found no further concrete code blocker for a controlled update.


## Subsequent real acceptance failure

Peter's test of PID 61744 failed. Two initial ProseMirror pastes retained a
16-unit raw baseline in the expectation; observed complete payloads had exact
15/39-unit document lengths. Capture attempted empty-value checking but never
recorded empty-value-confirmed. Thus the assumed ValuePattern contract was not
established on the real provider. The pause exposed an already-invalidated lease.

An opt-in, capture-only empty-field-shape diagnostic now records pattern IDs,
document/caret lengths, ValuePattern/Legacy availability, read-only state and
value lengths/equality. It reads only the original field after native window
and focus proof, never walks other controls and never logs names, text, values,
audio, clipboard content or text hashes. It does not change delivery acceptance.
