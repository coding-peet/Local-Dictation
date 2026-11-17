# Input language and verification preview: 13 November 2026

## Result

The reviewed source adds an actual language indicator, preferred Auto language
tags with a `+` picker, and same-audio language correction. It also keeps the
first primary transcript visible in a bounded read-only banner during Verify.

Candidate: C:\Git\Local-Dictation\language-candidate-20261113.
Version: 0.1.0-language.20261113.1, plus source revision suffix.
Deployed and started after Peter confirmed normal Exit. Verified client PID: 33668.
Candidate API PID: 19864, ready and idle. All 17 installed files
and their backups were hash-verified; saved settings remain byte-identical.
Production API PID 39448 remains unchanged. Runtime evidence: runtime-verification.json.

Canonical source: C:\Git\Local-Dictation\repo.
Branch: feature/native-windows.
Base HEAD: b494dca1845394af04d0d1e18eec06568ce1292a.
The existing dirty/untracked work is preserved. No commit or push was requested.

## Behavior and limits

- `Auto · SK` and similar labels come from the detector result, not text guessing.
  KInIT remains Slovak-only and displays `SK`. Explicit languages display their code.
- Available add choices come from installed model metadata. Original OpenAI
  Large-v3/Turbo expose 100 language codes. KInIT is not promoted to multilingual
  merely because its generation configuration contains other language tokens.
- Initial Auto preferences are SK and EN. Remove/add tags to change them.
  Empty supported intersections and incompatible verification configurations
  are rejected before autosave or microphone startup.
- Detection filters only a request-local copied language token map. Generation
  keeps the full original map. The verifier uses the primary resolved language.
  A restricted detector still chooses one allowed language; it has no confidence
  or abstention and does not establish recognition accuracy.
- Choosing another language while recording queues a complete pass after Finish,
  while capture continues. Choosing during verification cancels that pass first.
  A replay sends the same original PCM through a fresh normal session and does
  not call microphone Start. It retains the original destination and Finish gate.
- An explicit language window opened after Finish can invalidate automatic
  delivery through the usual focus/input guard. The result then waits for Insert.
  A History retry always creates a separate Review and never automatically
  overwrites an earlier insertion. Original text and edits remain in History.
- Audio is retained even when transcription is empty. History still keeps its
  latest 20 entries; a retranscription is a new entry linked to the original.
- Escape clears a queued retry. A new Review owns the delivery target exclusively,
  including delayed final receipts; the parent has no mutable target alias.
- Verify preview is read-only, scrollable and nonactivating. It shows the primary
  text only. Existing guarded withdrawal, agreement insertion and Review flow
  remain responsible for native delivery and differences.

## Verification performed

- 373 Core/worker tests passed.
- 132 backend tests passed with synthetic audio/engines. No real ASR inference
  or model loading was used by these tests.
- 26 windowless WPF state/layout and controller-handler checks passed.
  The renderer asserts native HWND zero and bypasses product constructors that
  register input hooks, audio devices or stores. It never shows a window, sends
  native input, accesses the clipboard, records or plays sound.
- Light/Dark renders cover recording, final primary processing, short/long Verify
  preview, Verify off, empty text, Auto waiting/detected, forced ES, language tags
  and the correction picker. Tags keep 48-DIP interaction targets.
- Actual Cancel-handler regression reproduced a queued replay surviving Escape
  before the fix. It passes after the fix.
- Actual controller receipt-transfer/drain checks pass for delayed final receipts
  whose parent/child text is either equal or different.
- Actual SaveSettings regression confirms invalid Auto preferences do not replace
  the last valid settings object or write a settings file.
- Backend registry-reload regression reproduced managed multilingual models losing
  language capabilities on a cache hit, then passed after preserving cached metadata.
- Release build and publish succeeded with zero errors and warnings.
- Fresh review found cancellation, receipt ownership, verifier eligibility,
  preference validation and catalog-cache issues. All were fixed and re-reviewed;
  no actionable findings remain in those paths.

Evidence directory: C:\AI\build\Local-Dictation-language.
Offline results/renders: renders\results.json and adjacent PNGs.
The earlier cancellation RED result is in cancel-red\results.json.

## Remaining application acceptance

No desktop control, speech recording or playback was used for this change.
Real recognition accuracy, post-pause cursor updates and the complete automatic
recording-to-Outlook Finish flow were not re-tested here. Earlier native evidence
and its limits are in native-acceptance-20261113.md. Source/synthetic checks and
offline layout validation do not establish native editor compatibility.

Production TypeWhisper API 8123, original model weights and private data are
outside this source update. The candidate API was restarted in a verified idle state to load the new Python
source. The production API on 8123 was not restarted.

## Reproduce the silent layout checks

Run from C:\Git\Local-Dictation\repo:

~~~powershell
& 'C:\AI\tools\dotnet-10.0.403\dotnet.exe' build `
  diagnostics\VerificationBannerRender\VerificationBannerRender.csproj `
  -c Release --artifacts-path C:\AI\build\Local-Dictation-language\dotnet

& 'C:\AI\tools\dotnet-10.0.403\dotnet.exe' `
  C:\AI\build\Local-Dictation-language\dotnet\bin\VerificationBannerRender\release\VerificationBannerRender.dll `
  --output C:\AI\build\Local-Dictation-language\renders
~~~

These commands build source and write synthetic protected test artifacts and
PNG renders under the named output directory. They do not launch the normal app,
create desktop windows, record, play audio or perform a native paste.
