# Local Dictation desktop design guide

This is the project's working UI guide, not a claim of official Material or
Windows certification. The client uses existing WPF and MaterialDesignThemes
controls. Preserve its waveform icon, palette and recording workflow.

## Workspace first

Keep Search at the upper left and the four named destinations below it. Do not
repeat the application name from the native title bar inside the client area.
Give History's transcript and recording list the space saved by removing chrome.

Keep selection, unsaved text and audio lifetime when resizing or switching
pages. A visual simplification must not silently change recording or delivery.

## Actions

Record/Stop stays above Help in the navigation footer on every page. Shortcut
dictation is the primary cursor workflow; this button is an alternate route
for recording from the app into Review. Keep its filled emphasis and accurate
tooltip. It must never become a navigation item or silently select a cursor.

Windows generally recommends upper command placement for discoverability on
large screens. Our footer placement is a deliberate tradeoff: it preserves a
stable global action without taking height from Search or the transcript.
A top toolbar and a Dictation-only button were considered; the former consumes
content space and the latter makes the action unavailable on other pages.

Keep Open Review/Copy next to the selected transcript. Put occasional export,
language change and deletion under More. Confirm destructive deletion with an
explicit action and safe Cancel; opening a dialog must not start dictation.

## Status and help

Put ordinary recording/connection state in a compact bottom status bar.
Routine saving belongs there only on relevant settings pages. Preserve saving
failures on all pages; let long messages wrap. Important global warnings remain
prominent above content, rather than being hidden in a transient notification.

Keep explanations next to their setting in the existing information helper,
available on hover, click, keyboard and accessibility. Help stays at the bottom
of navigation and is reachable with F1. Do not use status text as a second toolbar.

## Resizing and accessibility

The History divider has a transparent idle appearance, a 10-DIP resize target,
a size cursor, a hover cue and keyboard focus feedback. Keep keyboard resizing
and the list/detail minimum widths. Narrow windows stack the panes without
losing the preferred wide-layout width.

Retain 48-DIP action targets, visible focus, semantic selection foregrounds,
theme-aware resources and readable text wrapping. Avoid permanent decorative
lines where cursor/hover/focus affordances provide enough guidance.

## Authoritative references

- Material Design 3: https://m3.material.io/
- Windows design basics:
  https://learn.microsoft.com/en-us/windows/apps/design/basics/
- Command placement and consistent global actions:
  https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/command-bar
- Navigation/search/footer regions:
  https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview
- Desktop status area:
  https://learn.microsoft.com/en-us/windows/win32/controls/status-bar-reference
- Historical status guidance, useful principles but older visual examples:
  https://learn.microsoft.com/en-us/windows/win32/uxguide/ctrl-status-bars

Consult these alongside the current product requirements. Material defines
component roles and visual behavior; it does not mandate our Record location.
Verify the actual composed shell with realistic long content and narrow widths.
Windowless renders do not prove native focus, insertion or physical playback.
