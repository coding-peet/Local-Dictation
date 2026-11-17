# General navigation and Windows startup

Date: 2026-11-17
Status: implementation authorized by Peter on 2026-11-17.
Use his corrected navigation order General, Models, Dictation, History.
Baseline: 586ed67, feature/native-windows, with the existing uncommitted
import-error and private-transcript selection changes preserved.

## Request and intent

Add Start with Windows and a top-level General section above Dictation.
Reassess the placement of existing features so app-wide preferences and
transcription behavior have clear homes.

## Approaches

1. Add General for startup and appearance, retain Settings for the other
   existing preferences. This changes fewer routes, but leaves two similarly
   broad destinations and makes feature placement harder to predict.
2. Replace Settings with General at the top; move transcription-specific
   dictionary and compatibility controls to Dictation. This requires updating
   navigation/search routes and detached checks, but keeps four destinations
   with a clearer division. Selected approach, approved by Peter, with his corrected section order.

## Proposed arrangement

Navigation order confirmed by Peter: General, Models, Dictation, History.

| Destination | Content |
| --- | --- |
| General / Startup | Start with Windows |
| General / Appearance | Existing System, Light and Dark theme |
| General / Keyboard shortcut | Enable, key combination, Press/Hold behavior |
| General / History | Existing audio retention and preservation warning, as explicitly approved |
| General / Advanced | Existing local service address, Reconnect and Start local API actions; collapsed by default |
| Dictation | Existing microphone, models, language, output, live, private and verification controls |
| Dictation / Advanced / Personal dictionary | Existing hints and corrections; activation remains gated |
| Dictation / Advanced / Application compatibility | Existing remembered-app status and reset action |
| Models | Install, inspect, test and remove available model files; active dictation and verification roles remain selected in Dictation |
| History | Existing saved transcript, search/filter, import and playback workspace |

Remove the redundant Settings navigation item. Preserve current card styling,
semantic theme colors, native templates and the compact rightmost help column.
General is first in navigation; ordinary manual launch still opens Dictation.
This keeps routine users at their next recording rather than general setup.
Search results must use the new category, expand the necessary options and
reveal the correct control without changing any setting. Record/Stop, Help and
the bottom status remain unchanged.

## Workflow rationale

Keep four main destinations. General configures the app, Models manages its
recognition resources, Dictation prepares the next recording and History works
with existing results or imported files. Adding a separate Settings destination
would overlap General. Additional Dictionary, Privacy or Advanced navigation
items do not yet support an independent workflow large enough to justify them.

The global shortcut stays in General alongside background startup: it configures
how users invoke the app from other applications and is changed infrequently.
The active primary/verification model selectors stay beside language and output
in Dictation; users should not visit model installation to prepare a recording.

The later approval explicitly named retention in General. Keep that approved
placement in this implementation. The agent's intervening History-options idea
remains a future alternative, not an approved requirement. Retention stays in
its own History card, separate from view filters, with existing warnings intact.

Keep everyday Dictation choices visible. The two large dictionary editors and
compatibility recovery controls belong in collapsed Advanced content to avoid
lengthening the routine path. Unsaved/error states must remain discoverable
when this area is collapsed. Private-next remains beside the recording controls;
its memory-only selector must not appear among persisted History entries.

This uses the general/task-specific distinction in Apple's Settings guidance
as a cross-platform design principle, with the existing Windows WPF controls:
https://developer.apple.com/design/human-interface-guidelines/settings.md

## Start with Windows

The switch is opt-in. Merely installing, starting, refreshing or testing the
client must not enable Windows startup.

Use a single app-owned current-user Run entry pointing to the current installed
EXE with a dedicated --startup argument. Quote the executable path. No admin,
scheduled task, service or additional dependency is needed. Do not register a
build/test host. Read actual registration instead of mirroring a separate JSON
boolean. Modify only the app-owned value after an explicit switch action and
report write failures inline with the previous state retained.

On automatic launch, create the existing tray client without opening the main
window or recording. Normal manual launch keeps the main window. Reuse the
single-instance guard. Do not alter Windows' StartupApproved state or bypass a
Windows/user/organization startup restriction; explain an external Windows
startup block where it can be established reliably.

Microsoft reference:
https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys

## Verification and delivery

Use injected startup storage/launch policy and synthetic documents. Test quoted
paths, enable/disable/readback failures, no mutation on refresh, startup versus
manual launch and navigation/search destinations. Do not change the real Run
entry merely to test it. Render General and changed Dictation pages in Light
and Dark at compact and regular widths; retain keyboard and accessibility names.

No recording, playback, voice/inference tests or API restart/reconfiguration.
Keep the running APIs and user data intact. Package in a separate candidate
folder; replace the installed client only after verified normal tray Exit.
Commit and push only when separately requested.
