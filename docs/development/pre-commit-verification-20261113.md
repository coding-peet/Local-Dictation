# Pre-commit verification: 13 November 2026

## Scope

Capture the delivered cursor-dictation repairs, Material Design 3 client,
language metadata/preferences, same-audio correction, protected Review/history,
synthetic regressions and development records in the existing Windows branch.

## Final review corrections

- A replay could connect after transcript.final while the old primary model
  still owned the server. Before connecting or sending replay audio, wait for
  /health to report both ready and idle. The wait has an absolute 30-second
  deadline and responds to user cancellation. This releases the original
  owner's race; unrelated clients can still acquire admission independently.
- Keep captured duration visible alongside language during final processing
  and Verify. The first transcript remains the read-only preview.
- Update CompactFlowProbe and InlineReviewProbe delivery doubles to implement
  the explicit final/live intent and original-field checks. Do not hide absent
  behavior through interface defaults.
- Retire the synthetic initial placeholder after real text first appears.
  Restore it explicitly for fresh capture negative cases, preserving rejection
  of a falsely empty ValuePattern when real owned text remains after Delete.

Focused review of the corrections found no remaining actionable findings.
Source comments explain the API's null/unrestricted preference contract and
why replay must wait for actual server ownership release.

## Fresh validation

- 379 Core/worker tests passed. New HTTP-only cases verify ready-and-idle
  admission, cancellation and an absolute deadline for an unresponsive request.
- 132 backend tests passed using synthetic engines/audio. An intentionally
  malformed-result case logs an expected error; the suite completes successfully.
- 38 windowless WPF state/layout and controller-handler checks passed in
  Light/Dark themes. The duration assertion failed before the correction and
  passes afterward, including verification with a queued language correction.
- The Release app and renderer build passed with zero warnings/errors.
- CompactFlowProbe and InlineReviewProbe compile with zero warnings/errors.
  They were not launched; their older runtime/layout assumptions are not
  established as current application acceptance by compilation.
- The blocked DeliveryProbe payload was neither rebuilt nor executed. Its
  source-only fixture correction has not received a new native run.
- No desktop control, native paste, recording, playback, real inference or
  API restart was performed during this Git publication task.

Evidence: C:\AI\build\Local-Dictation-final-commit.
Core results: results\core-tests.trx.
Backend output: results\backend-tests.txt.
Windowless results and PNGs: renders\.
The duration regression's pre-fix result is in verify-duration-red\.

## Installed application versus source

The running installed build remains 0.1.0-banner-model.20261113.1, verified in
preferred-picker-model-20261113.md. This Git task did not replace its loaded
DLLs. The final review corrections above are validated source changes and
require a later normal client update to reach that running installation.

Complete live speech-pause and automatic recording-to-Outlook Finish acceptance
remain unconfirmed. See native-acceptance-20261113.md for the earlier successful
prepared-Review placement and the limits of that evidence.

## Git context

Canonical checkout: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows.
Starting HEAD: b494dca1845394af04d0d1e18eec06568ce1292a.
No worktree, merge, model/runtime conversion or private-data migration is part
of this publication. Stage only the reviewed text source/tests/docs; keep build
outputs, protected user data, recordings and model weights outside Git.
