# Compact Review resize repair

Approved request: increasing Review height must add usable transcript space.
Use the existing checkout on codex/review-window-resize, not another worktree.

1. Reproduce the capped reader/editor viewport using detached WPF layout at
   two heights; preserve original text and verify no native window is created.
2. Give the compact window a bounded resizable grid. Allocate extra height to
   the reader/editor; retain visible main actions and scroll secondary controls
   when the window is small. Preserve styling, focus and insertion semantics.
3. Run the new regression, full detached WPF checks, Core/backend tests and
   candidate publish. Inspect synthetic renders. Never touch running APIs.
4. Update Help and current requirements. Prepare a client-only candidate;
   replacement waits until no client instance remains. Commit and push were
   subsequently requested after deployment; merge remains outside scope.

Dictionary discussion remains a proposal: exact replacement hit counters,
explicit deletion rules and recognition hints have separate semantics. Keep
activation off pending paired evaluation and preserve legitimately spoken words.
No dictionary behavior is changed by this layout repair.
