# Review transcription and save validation

Date: 13 November 2026.

## Behavior

Transcribe again replaces the former Second transcript action. Model selection,
Transcribe confirmation, Cancel and errors are embedded in Review, with no
additional picker Window. In compact Review, choosing a model temporarily
replaces the transcript panel and then restores it, including at 400 DIP width.
The same compact Review instance stays open during saved-recording transcription.
The original protected recording is reused; the microphone is not opened.

Use this transcript selects and saves the complete alternative, resolves the
comparison and updates an active manual editor. It does not insert externally.
Original model outputs/provenance remain unchanged; overridden manual text is
archived and can be restored through Details. Original-source pages in Details
also offer the whole-source action.

Save text persists and approves the complete edited draft without insertion or
closing Review. Insert remains separate. Saved/Not saved states distinguish
success from storage failure, and failure retains editable text for retry.
Typing still preserves a draft automatically. Done editing leaves that draft
intact. Normal final phrase-choice auto-insertion is unchanged.

## Evidence

- 447/447 Core tests passed, zero skipped.
- 96 silent WPF/controller checks passed: same-window picker at compact width,
  selection errors in place, whole-alternative selection while editing, archived
  prior draft, saved text reopened from its protected store, no insertion from
  Save, and failure preserving the editable draft.
- Release build and separate candidate publish passed, zero warnings/errors.
- The missing whole-source action, missing explicit Save action and old popup
  wording were observed in failing regressions before the fixes.
- Independent review checked provenance/edits, save failures and asynchronous
  Review lifetime. Its save-failure/status/label findings were corrected.
- Offline renders use the Canvas background that a detached Window content tree
  otherwise loses. Actual compact Review, saved alternative and inline picker
  were visually inspected. No native UI control or audio was used.

The user-requested matching history item was found read-only. Its numeric
enumeration was already present in the primary ASR output; alternative decodes
differed. The manual correction was already persisted. Identical protected audio
was retained across those comparisons. No private transcript, recording or
decrypted export was added to this repository.

## Reproduce checks

Run from C:\Git\Local-Dictation\repo. Output stays outside the checkout.

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe test `
  tests\LocalDictation.Core.Tests\LocalDictation.Core.Tests.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-review-save\core

& C:\AI\tools\dotnet-10.0.403\dotnet.exe build `
  diagnostics\VerificationBannerRender\VerificationBannerRender.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-review-save\ui

& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
  C:\AI\build\Local-Dictation-review-save\ui\bin\VerificationBannerRender\release\VerificationBannerRender.dll `
  --output C:\AI\build\Local-Dictation-review-save\green
~~~

## Candidate and remaining verification

Candidate: C:\Git\Local-Dictation\review-save-candidate-20261113

Version: 0.1.0-review.20261113.1, based on 1236dd8 plus the uncommitted reviewed
patch. This candidate also includes automatic clipboard return. Neither a commit
nor push was requested. Publication alone does not install or launch it.

Artifacts: C:\AI\build\Local-Dictation-review-save

Native focus/insertion, real ASR retranscription, Windows clipboard and physical
audio are not verified by these checks. No model configuration or backend service
was changed/restarted. ASR hallucination remains a separate quality issue; Review
changes do not claim to fix it. Original-FP16 models and user data were preserved.

The older InlineReviewProbe Use edited text assertions describe the previous
combined approve/insert action and are superseded by Save text checks above.
Earlier validation reports remain historical where their wording differs from
current requirements.

## Installed update

After Peter confirmed normal tray Exit, the old PID 13816 was absent. All 17
previous application files were backed up and their hashes verified before
replacement. All 17 installed files then matched the candidate manifest.

Backup:

    C:\AI\build\Local-Dictation-review-save\previous-app-before-review-20261113-221444

The installed client started normally as PID 12200, without --no-hotkey or a
test taskbar banner. The startup marker verified the actual process and version
0.1.0-review.20261113.1+1236dd8dcf06fcfa9bae5289b7c2e2da996db078.
The process remained responsive. No recording, playback or native insertion was
performed during deployment verification.

All 114 existing protected data/settings files retained their hashes after
startup; none were missing. The session API remained PID 34080 on 48125 and
reported ready=true, busy=false. TypeWhisper's API remained PID 53440 on 8123.
Neither API was restarted. Models and runtime environments were not part of the
copy. deployment.json and validation.json record the completed installation.

The TypeWhisper-inspired Home, settings search, information buttons and explicit
After recording mode were researched only; this installed update does not add
those features or change model decoding settings.
