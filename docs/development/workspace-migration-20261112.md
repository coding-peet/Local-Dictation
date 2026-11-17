# Canonical working-folder migration - 12 November 2026

## Result

Development now uses C:\Git\Local-Dictation\repo on
feature/native-windows. Use ordinary branches in that folder; no additional
worktrees. Models, private data, environments, and SDKs remain under C:\AI.

## State preserved before switching

The requested repo.worktrees/native-windows path did not exist. Git registered
the original source at repo/.worktrees/native-windows instead.

Both checkouts had no staged, unstaged, or ordinary untracked changes.
The main folder was on main at d13d39f884fd7e59b9d3428fbbfb9786693addbb.
The source was on feature/native-windows at
9e30d8edd3c99e77d7be23e49348edf5a4ae4d0e. Both matched their upstreams.

A verified all-ref Git bundle, both physical indexes, empty staged/unstaged
patches, and complete file inventories were saved outside the checkout.
Physical file archives are encrypted for the current Windows user with DPAPI;
they include original ACL metadata. They are not portable to another account.

Backup directory:

    C:\AI\build\Local-Dictation-workspace-migration-20261112T124106Z

## Transfer and verification

- Detached the original worktree at its existing commit, freeing the branch.
- Switched the canonical folder to feature/native-windows at the same commit.
- Preserved all 243 original source files, including ignored local state.
- Verified every copied file by byte length and SHA-256 against the inventory.
- Verified the original source files remained byte-identical.
- Preserved all six pre-existing nontracked main-folder files. The main-folder
  backup contains all 27 original physical files.
- Preserved the main branch reference and the source's logical index state.
- Confirmed empty staged/unstaged/untracked status before documentation changes.

Byte-preserving copies initially appeared modified because the new checkout's
index recorded different newline/stat metadata. Filtered content matched every
index blob. A scratch copy of the original source index refreshed cleanly;
restoring that index resolved status without changing file bytes or staging
content. No line-ending normalization or reset was used.

The external transfer-verification.json records the final checks. The old
worktree remains detached and preserved; do not use it for further development
or remove it while an existing candidate service imports its source.

## Continuation

Build from the canonical folder to a separate candidate output. Verify the
published assembly's LocalDictation.SourceRoot points to the canonical folder.
A source-folder migration does not prove Outlook delivery compatibility or
replace the installed client. Use a controlled client/API handover that
preserves active dictation, pending Review, clipboard recovery, and the separate
TypeWhisper production API on 8123.

## Validation from the canonical folder

The new candidate was built with scripts/Build.ps1, using:

    BuildRoot: C:\AI\build\Local-Dictation-main-checkout-20261112
    AppDirectory: C:\Git\Local-Dictation\candidate-main-checkout-20261112

- Core tests: 84 passed, zero failed/skipped.
- Backend synthetic tests: 116 passed in 18.708 seconds.
- Read the actual published DLL metadata: LocalDictation.SourceRoot is
  C:\Git\Local-Dictation\repo.
- Published Help.html SHA-256 matches the source Help.

The backend tests did not load models or restart either API. The running
installed client was preserved. The new candidate is not an installed update;
Outlook's complete-text acknowledgement still needs its attributed native test.
