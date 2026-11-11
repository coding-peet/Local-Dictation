# Repository continuity and publication plan

Goal: a new agent can recover current requirements and rebuild the Windows
client from a fresh checkout without the original chat or build output.

User authorization: update repository documentation and Help, comment, commit,
and push the existing branch. Preserve unrelated work and external runtime data.

- [x] Consolidate current user input in docs/PRODUCT_REQUIREMENTS.md.
- [x] Link requirements, build/recovery instructions, and status from AGENTS.md
  and README; identify superseded historical reports.
- [x] Update offline Help against the current implementation.
- [x] Remove the launcher dependency on a specifically named source worktree.
- [x] Audit all addable files for source/assets versus private/generated data.
- [x] Build a new checkout from the committed implementation tree with separate build/output
  directories; verify Help/icon/dependencies and source-root metadata.
- [x] Run relevant Core/backend/packaging and controller/input checks, preserving
  the native acceptance limitation from the interrupted review probe.
- [x] Commit only reviewed paths; publication is complete only after non-force push to feature/native-windows
  and verification of remote HEAD and clean worktree. This final gate is checked in the publishing turn.

No old checkout will be deleted to perform this check. A separate clone will
exercise the same committed source and external documented dependencies.

Implementation/build evidence: docs/development/checkout-verification-20261111.md.
