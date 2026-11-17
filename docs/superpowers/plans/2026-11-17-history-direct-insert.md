# Direct History insertion implementation plan

> Execute inline in the canonical checkout using superpowers:executing-plans.

Goal: History menu inserts the highlighted text or whole transcript directly
into the last external application's current caret, without an intermediate UI.

Architecture: native foreground observer stores window/process metadata only;
controller restores and freshly validates the field, then reuses protected
selected-cursor delivery with an independent memory-only copy.

Tech stack: existing .NET 10/WPF client, Win32 foreground events, existing UIA
and clipboard transaction guards. Continue codex/review-window-resize in the
canonical repository; do not create a worktree.

- [x] Add failing synthetic controller and detached menu/selection checks.
- [x] Add isolated native foreground observer and guarded activation helper.
- [x] Add memory-only History-copy lifecycle, cancellation and concurrency gates
      while preserving original Review/recording delivery behavior.
- [x] Add History Insert menu action, snapshot selection before menu closure,
      and show result inline. No intermediate window.
- [x] Run focused regressions, review changes, then full relevant checks/build.
- [x] Update requirements and Help with direct workflow and failure behavior.
- [x] Prepare a client-only candidate; verify normal Exit before replacement.

Keep original transcripts, private audio/data and API processes untouched.


Client deployment is verified in docs/development/history-direct-insert-20261118.md.
Native paste acceptance remains manual; no voice test was run.
