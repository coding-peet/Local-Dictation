# Repository continuity and publication plan

Goal: a new agent can recover current requirements and rebuild the Windows
client from a fresh checkout without the original chat or build output.

User authorization: update repository documentation and Help, comment, commit,
and push the existing branch. Preserve unrelated work and external runtime data.

- [ ] Consolidate current user input in docs/PRODUCT_REQUIREMENTS.md.
- [ ] Link requirements, build/recovery instructions, and status from AGENTS.md
  and README; identify superseded historical reports.
- [ ] Update offline Help against the current implementation.
- [ ] Remove the launcher dependency on a specifically named source worktree.
- [ ] Audit all addable files for source/assets versus private/generated data.
- [ ] Build a new checkout from the exact staged tree with separate build/output
  directories; verify Help/icon/dependencies and source-root metadata.
- [ ] Run relevant Core/backend/packaging and controller/input checks, preserving
  the native acceptance limitation from the interrupted review probe.
- [ ] Commit only reviewed paths, push non-force to feature/native-windows,
  and verify remote HEAD and clean worktree.

No old checkout will be deleted to perform this check. A separate clone will
exercise the same committed source and external documented dependencies.
