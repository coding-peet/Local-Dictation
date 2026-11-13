# Settings search, help and output mode validation

Date: 13 November 2026.

## Behavior

Search settings is embedded above the named navigation. Results replace only
the main page area, preserving the underlying page and its drafts. Search covers
names, categories, descriptions and keywords, ignores case/accents and requires
all terms. Exact/prefix labels rank first. Enter selects the highest-ranked
result after query edits. Ctrl+F focuses search; Escape or a named navigation
item clears it, including clicking the current page.

Results open and scroll to the real setting. Hidden dependencies lead to the
relevant parent without enabling it or rewriting saved values. Preferred-language
results move keyboard focus into their chip/Add controls.

Information icons replace occasional explanatory paragraphs beside fields and
switches. Hover and click show the explanation; keyboard activation and accessible
name/help text remain available. Clicking help never toggles the adjacent setting.
Saving failures and runtime warnings remain visible.

After recording has two choices. Verification remains independent:

| After recording | Verification off | Verification on |
| --- | --- | --- |
| Insert directly | Insert if the original field passes checks | Agreement inserts; differences open Review |
| Preview first | Open Review | Agreement and differences both open Review |

Preview first prevents all live/final automatic writes and leaves recognition
available in the banner. Live insertion is disabled with a visible explanation;
its saved preference returns with Insert directly. Changes apply to the next
recording. Unknown output selections are rejected instead of becoming direct
insertion; old settings without this property retain their previous direct mode.

Preview Reviews and further transcripts retain an explicit-insertion flag in
protected storage and language replay. Resolving the last phrase or Save text
does not insert them. Explicit Insert still captures a fresh selected cursor.

## Evidence

- 468/468 Core tests passed, zero skipped.
- 175/175 synthetic backend tests passed. Existing dependency deprecation and
  intentional failing-server-fixture output do not indicate a failed test.
- 115 windowless WPF/controller checks passed, including accessible help,
  search keyboard/mouse routing, hidden prerequisites, rejected unknown output,
  Preview live/Finish suppression, durable explicit insertion and existing Review.
- Release diagnostic build and candidate publish: zero warnings and errors.
- Light/Dark settings at 640/1040 DIP, search results and popovers were rendered
  and visually inspected. Material floating hints use a managed offscreen
  PresentationSource to settle their real visibility state. No HWND, native
  input sink or audio device is created by these renders.
- Independent review findings were corrected: live prerequisite routing,
  invalid-selection fallback, Preview instructions, focus into language chips
  and explicit policy retained through saved transcription/language replay.

## Reproduce

Run from C:\Git\Local-Dictation\repo. These commands write only build/test
artifacts outside the checkout; they do not install or start the client.

~~~powershell
& C:\AI\tools\dotnet-10.0.403\dotnet.exe test `
  tests\LocalDictation.Core.Tests\LocalDictation.Core.Tests.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-settings-output\core

& C:\AI\tools\dotnet-10.0.403\dotnet.exe build `
  diagnostics\VerificationBannerRender\VerificationBannerRender.csproj `
  -c Release --nologo `
  --artifacts-path C:\AI\build\Local-Dictation-settings-output\ui

& C:\AI\tools\dotnet-10.0.403\dotnet.exe `
  C:\AI\build\Local-Dictation-settings-output\ui\bin\VerificationBannerRender\release\VerificationBannerRender.dll `
  --output C:\AI\build\Local-Dictation-settings-output\final
~~~

## Candidate and deployment

Candidate: C:\Git\Local-Dictation\settings-output-candidate-20261113

Version: 0.1.0-settings.20261113.1, based on 1236dd8 plus reviewed uncommitted
source. It includes the earlier automatic clipboard return and Review changes.
No commit or push was requested. Artifacts and the 17-file SHA-256 manifest are
under C:\AI\build\Local-Dictation-settings-output.

Publication alone does not install it. The client must exit normally before
file replacement. Keep both APIs running and preserve protected data/settings.

## Installed update: 14 November 2026

After Peter confirmed normal Exit, the client was verified stopped. All 17
candidate files matched their manifest. The previous app was copied and all
17 backup files verified before replacement; all 17 installed files were then
verified against the candidate. No files or directories were deleted.

Backup:
C:\AI\build\Local-Dictation-settings-output\previous-app-before-settings-20261113-230152Z

The backup suffix uses UTC; this deployment occurred on 14 November in Bratislava.

The installed client was started normally with the existing child-only diagnostic
launcher. PID 2516 and the full settings build version were confirmed by the fresh
delivery-session.json marker. No --no-hotkey argument or test taskbar banner was
used. All 115 protected data/settings files remained byte-identical after startup.

API listeners retained their identities: session API 48125 / PID 34080 and
TypeWhisper API 8123 / PID 53440. Session health remained ready=true, busy=false.
Neither API was restarted; TypeWhisper health was not separately queried.

deployment.json and validation.json under the artifact directory record the
completed installation/startup and preservation checks. At deployment time, source
remained uncommitted on feature/native-windows at 1236dd8. A later review and
commit/push request is recorded in review-before-push-20261114.md.

## Remaining manual acceptance

No microphone, native insertion, physical audio, screen-reader session or native
high-contrast integration was exercised. Windowless checks do not prove real
Outlook/Codex delivery, native keyboard focus or popup behavior. The completion
decision matrix and live guards are tested separately; CompleteRun opening a
native Review was not exercised end to end. No model configuration or runtime
API was changed/restarted, and no ASR hallucination fix is claimed here.
