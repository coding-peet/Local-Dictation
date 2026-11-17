# Native completion follow-up

Execute inline with superpowers:executing-plans. The approved native design and
user instruction to continue authorize this completion. Do not repeat discovery.

**Spec:** ../specs/2026-11-10-live-dictation-design.md and
../specs/2026-11-10-interactive-verification-review.md.

**Constraints:** English UI; original FP16 weights; unchanged production8123;
candidate48125 only; external EXE/source separation. Corporate Defender's
recorded EXE block remains unresolved. No native launch or policy workaround
until official execution approval is established.

## Task 1: Immutable model provenance

Files: application.py, sessions.py, comparison.py; ReviewDocument.cs;
AppController.cs; corresponding Python/C# tests.

Interfaces: session start snapshots model IDs/revisions and UTC start.
Comparison identity hashes exact immutable model metadata/text. Review persists
the snapshot; old files show unknown revisions rather than current registry data.

1. Write failing snapshot/content-identity and protected persistence tests.
2. Observe RED, implement event and persistence path.
3. Run relevant suites and commit.

## Task 2: Full originals and target-monitor banner

Files: ReviewWindow.cs, BannerWindow.cs, AppController.cs;
Core/BannerPlacement.cs, Windows monitor adapter and geometry tests.

Interfaces: Working, Dictation original and Verification original tabs share one
native window. Source Copy returns exact source without changing choices/draft.
Banner anchors to destination monitor/work area/DPI, supports negative coordinates
and never activates another window.

1. Test negative coordinates, taskbar bounds and scaled placement; observe RED.
2. Implement geometry/monitor adapter and native source/provenance views.
3. Compile WPF and run core tests. Native visual checks remain blocked pending approval.

## Task 3: Complete catalog installation

Files: catalog.py, imports.py, bootstrap.py, api_main.py, model_engine.py;
SettingsWindow.cs, ApiClient.cs and focused installation tests.

Interfaces: inspect missing catalog models at their pinned commit and install
into managed storage. Preserve healthy installations. Same-revision repair backs
up only the registered incomplete managed directory, then rolls back directory
and registry on failure. Persist managed overrides across restart. Download is
not runtime/quality validation. API preparation tolerates a missing default model
so the GUI can install it. Legacy batch defaults remain compatible.

1. Write failing pinned inspect/install/repair/rollback/restoration and absent
default-runtime tests with synthetic original-format fixtures.
2. Observe RED, implement transaction and native catalog controls.
3. Run full tests and inspect actual public metadata without duplicating large
installed model weights; commit.

## Task 4: Verification and publication

1. Run full Python/.NET suites and publish outside source.
2. Obtain one fresh-context independent review of this follow-up.
3. Fix Important/Critical findings with RED-to-GREEN regressions.
4. Update validation, push feature/native-windows; verify clean tree/remote HEAD.
5. Keep native acceptance and live-quality acceptance separately open if blocked.
