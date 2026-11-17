# Material usability audit and intuitive controls

Historical checkpoint: this records the state at the version/date below.
Later requirements and layouts supersede its present-tense UI descriptions.
For current behavior use docs/PRODUCT_REQUIREMENTS.md, docs/UI_DESIGN.md and
docs/development/search-first-shell-20261114.md from the repository root.

Date: 2026-11-14
Checkout: C:\Git\Local-Dictation\repo
Branch: feature/native-windows
Base commit: 07a9fe6f843db9b2bf2d62ef409cec01bd113528

## Review scope

Reviewed the shell/navigation/search, Dictation settings, Models, banner, compact
Review/editor/further-transcription picker, Details, History and player. Used
current source, synthetic controllers and offline renders; no desktop control,
microphone capture, playback or real-record deletion.

## Component decisions

The installed MaterialDesignInXaml WPF controls remain the UI toolkit. Material 3
supplies component semantics; desktop focus, input and geometry use the existing
application profile. This is not a claim of complete Compose/Material parity.

| Need | Applied control |
| --- | --- |
| Enable a recording shortcut | Compact header switch |
| Exactly one recording gesture | Native Material radio group: Press / Hold |
| Show a key binding | Separate keycaps with a trailing edit icon |
| Assign a new binding | Explicit Listening state; live keycaps; focus ownership |
| Delete one History row | Small icon, usable target, timestamped No-default confirmation |
| Free transcript space | Count in existing detail header; single-row 60-DIP player |
| Open a further-transcription choice | Inline disclosure with owned focus/local Escape |

The choice of these controls is an application design decision informed by the
following references, rather than a verbatim requirement to copy another UI:

- Material component semantics:
  https://material-web.dev/components/
- WPF toolkit component catalogue:
  https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/wiki/ControlStyleList
- WPF keyboard versus logical focus:
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/focus-overview
- Keyboard-based shortcut assignment reference:
  https://learn.microsoft.com/en-us/windows/powertoys/keyboard-manager

## Concrete audit findings

- Dictation exposed uninstalled catalogue models and then rejected their save.
  Its choice now contains installed models; missing saved selections keep their
  existing warning instead of silently selecting another model.
- The inline model picker left focus on its trigger and Escape closed Review.
  It now sets model logical focus, requests visible keyboard focus, and handles
  the first Escape locally before restoring trigger focus. Review data is kept.
- Empty History asked for an impossible selection. It now explains that the
  first completed recording will appear there.
- The Models URL floating label appeared overlapped only in detached renders.
  The fixture now uses the established visibility/idle settling lifecycle;
  production field semantics were retained.
- Models Live status and idle progress received targeted regressions: the status
  must fit its cell and progress must represent active work, preserving errors.

## Hold safety review

Hold recording owns its exact Run. Release finishes only that run, even if a new
operation has replaced it. Configuration changes are deferred until release or
cancellation, and shortcut editing cannot consume keys during a busy run.

Independent review found an Escape path with no later Stop to apply deferred
configuration. The fix applies the saved configuration after Escape relinquishes
its hold, retaining consumed Escape repeats/key-up and the original gesture's
lockout. Button cancellation while physically held still waits for release.

## Verification ledger

- Root full Core suite: 531 passed, 0 failed.
- Root synthetic backend suite: 175 passed, 0 failed. The expected failure-fixture
  traceback and existing Starlette deprecation warning did not fail the suite.
- Integrated WPF suite passed 144 checks before final review corrections.
- Final WPF/controller suite: 153 passed, with no temporary extra test gates.
- Final Release diagnostic build: 0 warnings, 0 errors.
- Live status wraps within its existing 120-DIP column/48-DIP row; glyph bounds
  fit the cell. Idle/finished progress is collapsed, active progress visible.
- Candidate publish, installed Help/source hash comparison and git diff --check passed.

Native hold-key behavior, microphone capture, actual audio playback/seek, Windows
high-contrast integration and Codex/Outlook insertion remain manual acceptance.
The prior Review selected-cursor insertion investigation remains separate; these
usability changes do not prove that native scenario fixed.

## Deployment

Version: 0.1.0-ux.20261114.1+07a9fe6f843db9b2bf2d62ef409cec01bd113528
Installed client: C:\Git\Local-Dictation\app\LocalDictation.exe
Client PID: 39844

Peter confirmed normal Exit of the previous client. All 17 installed files and
all 17 backup files matched their SHA-256 manifests. All 117 protected data files,
including settings and recordings, remained byte-identical through replacement
and startup. Existing data was not deleted during verification or deployment.

Both API processes were preserved: session PID 4364 (48125), ready=true/busy=false;
existing batch PID 22352 (8123). No duplicate session API was started.

Candidate: C:\Git\Local-Dictation\ux-candidate-20261114
Artifacts: C:\AI\build\Local-Dictation-ux
Backup: C:\AI\build\Local-Dictation-ux\previous-app-20261114-192429Z

The artifact directory contains candidate-manifest.json, previous-app-manifest.json
and deployment.json. The candidate includes the preserved prior diagnostic,
automatic-start and shortcut patches. No new commit or push was made in this task.