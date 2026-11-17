# Preferred correction languages and model caption: 13 November 2026

## Requested behavior

Limit the banner's language picker to configured preferred languages. Offer
all supported languages when no preferences are configured. Show the selected
model in small unobtrusive text in the banner.

## Implemented

- The correction picker uses preferred tags in their chosen order, intersected
  with supported languages of installed compatible models. Auto remains a mode.
  An unavailable nonempty preference set does not fall back to all languages.
- The active recording's settings snapshot supplies the preference list. Changes
  made in Settings during a recording still apply to the next recording.
- All tags can be removed. No preferences means unrestricted supported Auto and
  correction choices. Initial SK/EN defaults are unchanged. At the API boundary,
  an empty client list is serialized as null, preserving the existing backend's
  unrestricted contract rather than sending an invalid empty allowlist.
- Verifier eligibility covers all effective candidates in unrestricted mode.
  Nonempty preferences with no supported intersection remain invalid.
- The banner shows a subdued 11-DIP model name beside the phase. It identifies
  the Dictation model normally and the active verifier during Verify. Built-in
  models use short unambiguous names; hover exposes full identity and role.
  Long custom names can ellipsize, with their full name retained on hover.
- The banner remains 392 x 88 DIP normally and 456 x 176 during Verify. The
  separate time/language line stays fully readable. No native delivery,
  cancellation, microphone-start or clipboard code was changed.

Candidate: C:\Git\Local-Dictation\banner-model-candidate-20261113.
Version: 0.1.0-banner-model.20261113.1, plus source revision suffix.
Deployed and started after confirmed normal Exit. Verified client PID: 57004.
All 17 installed files and their backups were hash-verified; saved settings are
byte-identical. Both existing API processes were preserved. Runtime evidence:
C:\AI\build\Local-Dictation-banner-model\runtime-verification.json.

## Validation

- 376 Core/worker tests passed, including preference filtering, unavailable
  preference sets, empty/unrestricted mode, verifier coverage and actual
  session-start JSON for constrained versus unrestricted requests.
- 36 silent WPF layout/lifecycle checks passed in Light/Dark themes. The real
  picker is checked with SK/EN preferences, no preferences and unsupported tags.
  Banner renders cover primary/verifier labels, KInIT and a long custom name.
  Built-in names and elapsed/language text fit their actual allocated width.
- The existing windowless fixture creates no native HWND, does not invoke
  product startup and does not perform native input, recording or playback.
- Release build/publish passed with zero warnings/errors; scoped diff check passed.
- The backend source and runtime are unchanged. Its existing null preference
  contract was inspected directly; no model inference or server restart is needed.

Focused read-only code review completed with no actionable findings.
Real speech, cursor delivery and Outlook were not retested during this update.
Earlier acceptance boundaries remain in native-acceptance-20261113.md.

## Source and evidence

Source: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows.
Base HEAD: b494dca1845394af04d0d1e18eec06568ce1292a.
At deployment time, existing dirty/untracked work was preserved and publication
had not been requested. The subsequent Git publication is documented in
pre-commit-verification-20261113.md.

Evidence: C:\AI\build\Local-Dictation-banner-model.
Windowless results: renders\results.json and adjacent PNG files.
Pre-change source snapshots: before\.
