# Local Dictation engineering instructions

## Scope and language

This repository owns the Windows client, shared ASR server source, tests,
design documents, prototypes, and build/deployment scripts.

Use English for application copy, documentation, and commit messages.
Use py instead of python in commands on this Windows machine.

Start by reading docs/PRODUCT_REQUIREMENTS.md (current user input) and
docs/development/rebuild-and-recovery.md (fresh-checkout build and continuity).
Then read the relevant design in docs/superpowers/specs. Older plans and
validation reports are historical where they conflict with current requirements.

## Working folder and branches

On this PC, perform all source edits, tests, builds, commits, and pushes from:

    C:\Git\Local-Dictation\repo

Use ordinary Git branches in this one folder. Do not create additional
worktrees, even when an agent workflow normally recommends worktree isolation.
The active implementation branch is feature/native-windows; inspect the actual
branch and local changes before switching. Preserve unrelated work.

The former .worktrees/native-windows folder is a detached preservation copy.
Do not edit, build, test, commit, or push from it. Do not remove it incidentally:
a candidate service may still import its source until a controlled handover.
Keep published app files outside the checkout, and models/user data in C:\AI.
At handoff, report the working folder, active branch, and latest commit.

## Agent continuity

Inspect the actual checkout, branch, status, and existing changes before editing.
Do not depend on an old chat, a scratch mirror, an untracked asset, or a fixed
worktree name. Add required source/assets/tests/docs to the reviewed Git scope.

Keep responsibilities separate:

- docs/PRODUCT_REQUIREMENTS.md records current user requirements and changes.
- AGENTS.md records agent workflow and engineering constraints.
- docs/development/rebuild-and-recovery.md records reproducible setup/recovery.
- src/LocalDictation.App/Help/index.html explains current user-visible behavior.

When behavior changes, update requirements and Help where relevant. Keep test
results and remaining manual checks explicit. The review candidate is not the
installed app; do not assume a publish command deployed it.

When asked to comment, commit, push: review and stage only intended paths,
write a meaningful English commit, push the existing branch without force,
then verify remote HEAD and clean status. Do not merge or delete worktrees
unless requested. Preserve models, private data, and runtime environments.


## Source and runtime separation

Canonical checkout: C:\Git\Local-Dictation\repo.

Published client: C:\Git\Local-Dictation\app.
Client data: C:\AI\LocalDictation\data.
Client logs: C:\AI\LocalDictation\logs.
Build output: C:\AI\build\Local-Dictation.
Existing backend runtime and models: C:\AI\PalaNote-ASR.

Keep generated binaries and real user data outside the checkout. Never
commit recordings, transcripts, pending reviews, logs, credentials,
downloaded model weights, virtual environments, or SDK installations.

Edit source in the repository. Treat C:\AI\PalaNote-ASR as a deployed
runtime; do not edit or restart it as incidental setup work. Use a separate
candidate listener and shared read-only model files during development.


## Product boundaries

- All recording and transcription are local.
- Preserve original model weights and FP16 behavior; no conversion or quantization.
- Verification is a generic role with a registry-backed model dropdown.
  Large-v3 is the initial selection, not a hardcoded workflow.
- Keep protected text history until explicit deletion. Audio retention defaults
  to 7 days and is configurable; expiry removes audio only. Pending Review text
  remains independent. Do not restore the obsolete count20 transcript eviction.
- Never submit the text inserted into another app or synthesize a send shortcut.
- Stop automatic delivery when the owned destination cannot be validated.
- Codex insertion requires Peter's manual application acceptance test.
- Preserve the working TypeWhisper setup until controlled handover.


## Verification

Run the existing backend tests with the candidate API environment. It contains
the session API dependencies; the deployed batch environment does not include
all of them. This command runs tests with synthetic data and does not restart
either running API or load model weights:

~~~powershell
$env:VIRTUAL_ENV = 'C:\AI\build\Local-Dictation\server-env'
py -B -m unittest discover -s server -p 'test_*.py' -v
~~~

These are unit tests, not proof of live microphone latency, native insertion,
or quality in a particular application. Keep those claims separate.

Write readable engineering documentation with headings, short paragraphs,
and separate command blocks. Explain what setup commands change.
