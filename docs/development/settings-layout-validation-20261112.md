# Settings layout simplification - 12 November 2026

## Requested behavior

Reduce reading and repeated headings on the Dictation setup page. Keep the
recording controls together. Place live insertion immediately after live
transcription. Show the optional verifier controls only when verification is on.

## Changes

- Keep Local Dictation and Record in banner in one aligned title row.
- Put connection and automatic saving feedback in one compact status row.
- Remove the Recording heading, its introductory line, Text and verification
  heading, and the long delivery explanation from the setup page.
- Group microphone, dictation model/language, live transcription, and live
  insertion. Keep actionable model/latency notes concise; omit redundant healthy
  model descriptions.
- Separate Verify after recording with a simple rule. Indent and collapse its
  model/details while off, keeping the eligible selection and existing autosave.
- Keep rejected verification feedback in the footer if no second model is
  eligible. Clear only the warning owned by that condition when it recovers.
- Update offline Help and product requirements.

Existing WPF colors, control sizing, keyboard controls, and recording behavior
are preserved. No inference, cursor-delivery, or clipboard policy was changed.

## Verification

All work ran from C:\Git\Local-Dictation\repo on feature/native-windows.
No new worktree was created. Output stays outside the checkout.

- SettingsProbe: 25/25 passed, using isolated synthetic settings/models.
- Two new disclosure cases failed on the old layout, then passed after the
  change: hide/re-enable without losing the verifier, and open with verification
  off without expanding controls or rewriting settings.
- The existing no-eligible-verifier case now verifies the explanation remains
  outside collapsed controls, a rejected enabling action is explicit, and the
  warning clears after recovery.
- Core tests: 84/84 passed; Release client publish succeeded.
- Static Help checks: 20 unique IDs, 12 valid internal links, no scripts, and
  restrictive default CSP. Published Help must match source at final publish.
- Reviewed actual WPF content renders in dark/light appearance, verification
  on/off, and a narrower/shorter viewport. The short viewport scrolls; footer
  actions stay separate. This is application-rendered visual verification, not
  physical DPI, screen-reader, microphone, or native delivery acceptance.
- Independent source review found no actionable issue in the grouping,
  disclosure, autosave, and model-eligibility changes.

Evidence:

    C:\AI\build\Local-Dictation-settings-simplified\red.json
    C:\AI\build\Local-Dictation-settings-simplified\green.json
    C:\AI\build\Local-Dictation-settings-simplified\settings-dictation.png
    C:\AI\build\Local-Dictation-settings-simplified\settings-dictation-simple.png
    C:\AI\build\Local-Dictation-settings-simplified\settings-dictation-light.png
    C:\AI\build\Local-Dictation-settings-simplified\settings-dictation-compact.png

Candidate output:

    C:\Git\Local-Dictation\candidate-settings-simplified

The installed running client was preserved. This candidate still requires a
controlled handover after that client exits normally and any clipboard recovery
is resolved. A successful UI rebuild does not establish Outlook delivery.
